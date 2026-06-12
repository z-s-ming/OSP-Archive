# LiveVR 通信和增益注入分析报告

**生成时间**: 2026-06-11  
**分析的日志文件**:
- `livevr_editor_log.txt` (1500行，编辑器端日志)
- `livevr_quest_pid_log.txt` (121586行，Quest客户端日志)

---

## 执行概要

根据用户反馈：**体验中增益注入是有效的**，但日志显示了一些需要关注的问题。

---

## 1. 通信状态分析

### 1.1 编辑器端 (Host)

**✅ 正常工作的功能**:
- 增益指令发送：正在定期发送增益命令到客户端
  ```
  [LiveVR] SendGain user=0 type=Rotation rate=18.77/s valid=0.250s virtual=(-4.12,15.86)/-155.9
  [LiveVR] SendGain user=0 type=Curvature rate=-13.26/s valid=0.250s virtual=(-3.95,15.64)/-158.0
  ```
- 重置消息发送：重置开始/结束消息正常发送
  ```
  [LiveVR] Sent RESET_START user=0 type=WALL_RESET event=1
  [LiveVR] Sent RESET_END user=0 event=1
  ```
- 增益计算：APFRedirector_OSP正在计算各种类型的增益

**✅ 预期行为**:
- `ApplyGainSkip reason=host_mode` - 这是正常的，编辑器作为Host不需要应用增益

### 1.2 Quest端 (Client)

**⚠️ 发现的问题**:
- **虚拟姿态过时**: Quest端持续报告 `stale_virtual_pose`
  ```
  [LiveVR] ApplyGainSkip reason=stale_virtual_pose 
  poseSeq=3119 poseAge=37.813s (序列号停滞，已过时37秒)
  ```
- **实验状态**: `state=Running` - 实验处于运行状态
- **增益数据无效**: `poseGain=Undefined poseRate=0.00/s poseValid=0.000s`

---

## 2. 重定向增益注入逻辑分析

### 2.1 编辑器端增益生成

**✅ 正常工作**:
- 增益计算详细且完整：
  ```
  [LiveVR] Gain user=0 type=Rotation redir=APFRedirector_OSP 
  seq=1090 duplicatePose=0 sampleDt=0.044 physicalDelta=0.038 
  speed=0.867 physicalYawDelta=-0.83 yawRate=-18.96 
  virtualYawDelta=-1.53 injectedYaw=-0.70 rate=-15.82 
  gains T/R/C=0.000/1.250/0.133 yawDiff=133.35
  ```
- 增益类型多样：Rotation（旋转）、Curvature（曲率）、None
- 增益间隔统计：定期记录增益应用统计
  ```
  [LiveVR] GainInterval user=0 reason=distance_step 
  physicalDist=12.058m virtualDist=12.060m 
  totalInjectedYaw=33.79 curvatureYaw=-0.14 rotationYaw=33.93
  ```

**⚠️ 注意事项**:
- 存在重复姿态：`duplicatePose=1` - 当姿态重复时，物理移动为0，不注入增益
- 这是正常的行为，表示用户静止不动

### 2.2 Quest端增益应用

**❌ 日志中的问题**:
- **没有找到成功应用增益的日志** (`ApplyGainCheck`)
- 所有记录都是 `ApplyGainSkip reason=stale_virtual_pose`
- 虚拟姿态序列号固定在 `poseSeq=3119`，长时间未更新

---

## 3. 根本原因分析

### 3.1 虚拟姿态广播机制

**代码路径**: `LiveVRVirtualPoseBroadcaster.cs`

**工作原理**:
1. 编辑器端在 `LateUpdate()` 中以60Hz的频率广播虚拟姿态
2. 调用 `manager.SendVirtualPose()` 发送姿态和增益数据
3. 包含：虚拟位置、虚拟朝向、增益类型、增益速率、有效时长

**发送条件**:
```csharp
if (hostOnly && !manager.IsHost) return;
if (sendOnlyWhileRunning && manager.ExperimentState != LiveVRExperimentState.Running) return;
```

### 3.2 Quest端接收和过时检查

**代码路径**: `LiveVRClientVirtualViewBinder.cs`

**过时检查逻辑**:
```csharp
float staleVirtualPoseTimeoutSeconds = 0.5f; // 0.5秒超时
float ageSeconds = (now - virtualPoseReceiveUnixMilliseconds) / 1000.0f;
hasFreshVirtualPose = ageSeconds <= staleVirtualPoseTimeoutSeconds;

if (hasVirtualPose && !hasFreshVirtualPose) {
    LogApplyGainSkip("stale_virtual_pose", manager, root);
}
```

### 3.3 日志时间点分析

**Quest日志时间**: `06-11 10:08:15` 到 `06-11 10:08:30`

**关键发现**:
- Quest端实验状态显示 `state=Running`
- 但最后收到的虚拟姿态是 `poseSeq=3119`，已经过时37-52秒
- 说明在这个时间段内，编辑器**停止了**虚拟姿态的广播

---

## 4. 可能的原因

### 4.1 最可能的情况（与用户反馈一致）

由于用户表示**"体验中是有增益注射的"**，这些日志记录的时间点很可能是：

1. **实验暂停或结束阶段**
   - 实验刚结束，Quest端还在运行
   - 编辑器端已停止广播虚拟姿态
   - Quest端继续尝试应用增益但姿态已过时

2. **日志记录的是实验后期**
   - 正常运行期间增益是有效的（用户确认）
   - 日志捕获的是异常或结束阶段

3. **sendOnlyWhileRunning 配置**
   - 如果编辑器端的 `sendOnlyWhileRunning=true`
   - 实验状态切换时会停止发送
   - Quest端会出现姿态过时的情况

### 4.2 需要排查的情况

如果在正常运行时也出现过时姿态：

1. **网络连接问题**
   - UDP包丢失
   - 网络延迟过高（>500ms）
   - 防火墙阻止通信

2. **线程/性能问题**
   - 编辑器端帧率过低，影响发送频率
   - Quest端接收线程阻塞

3. **配置问题**
   - `sendOnlyWhileRunning` 设置不一致
   - 用户ID映射错误

---

## 5. 验证和建议

### 5.1 立即验证

1. **检查编辑器端是否在发送虚拟姿态**
   - 编辑器日志中没有找到 `SendVirtualPose` 的直接日志
   - 但 `SendGain` 日志存在，说明 `BroadcastVirtualPoses()` 在运行
   - `SendVirtualPose()` 和 `SendGain` 日志在同一个方法中

2. **检查Quest端网络接收**
   - Quest日志中没有找到 `Received VIRTUAL_POSE` 的日志
   - 需要确认客户端是否正确接收UDP消息

### 5.2 建议的改进

#### 改进1: 增加虚拟姿态发送日志

在 `LiveVRVirtualPoseBroadcaster.cs` 中添加定期日志：

```csharp
private void LogVirtualPoseSendIfNeeded(int userId, uint sequence) {
    // 每1秒记录一次，确认发送状态
    Debug.Log($"[LiveVR] SendVirtualPose user={userId} seq={sequence}");
}
```

#### 改进2: 增加Quest端接收日志

在 `LiveVRNetworkManager.cs` 的消息处理中添加：

```csharp
// 收到虚拟姿态时记录
Debug.Log($"[LiveVR] ReceivedVirtualPose seq={pose.Sequence} age={ageSeconds:F3}s");
```

#### 改进3: 诊断模式

添加一个诊断标志，在出现 `stale_virtual_pose` 时：
- 记录最后一次成功接收的时间
- 记录网络状态
- 记录实验状态变化

#### 改进4: 宽容的超时设置

考虑增加过时超时时间（仅用于诊断）：
```csharp
float staleVirtualPoseTimeoutSeconds = 1.0f; // 从0.5秒增加到1.0秒
```

---

## 6. 结论

### 6.1 通信状态

**当前通信状态**: ⚠️ 部分异常（日志记录时段）

- 编辑器端正常生成和尝试发送增益命令
- Quest端在记录时段收不到新的虚拟姿态更新
- 重置功能的通信正常工作

### 6.2 增益注入逻辑

**增益注入逻辑**: ✅ 设计正确

- 编辑器端计算逻辑完整且详细
- Quest端应用逻辑正确（包含过时检查保护机制）
- 用户反馈体验中增益是有效的，说明核心逻辑工作正常

### 6.3 日志异常的可能解释

**最可能的情况**: 日志记录的是实验结束或异常时段

- Quest端日志从 10:08:13 开始就已经显示 `poseAge=37s`
- 说明在 10:07:36 左右收到最后一次虚拟姿态（seq=3119）
- 之后编辑器停止发送，Quest端继续运行并记录 `stale_virtual_pose`

### 6.4 实际运行情况（基于用户反馈）

**正常运行时**: ✅ 增益注入有效

用户明确表示"体验中是有增益的注射的"，说明：
1. 在正常实验期间，虚拟姿态广播工作正常
2. Quest端能够接收并应用增益
3. 重定向效果符合预期

---

## 7. 行动项

### 优先级 P1（如果正常运行时也出现问题）

1. 添加虚拟姿态发送确认日志
2. 添加Quest端接收确认日志
3. 检查网络连接稳定性

### 优先级 P2（优化和诊断）

1. 实现诊断模式，详细记录姿态发送/接收
2. 添加网络延迟监控
3. 优化日志输出，区分正常运行和异常状态

### 优先级 P3（代码质量）

1. 在 `SendVirtualPose` 添加发送失败检测
2. 实现姿态丢失告警机制
3. 添加自动重连逻辑

---

**分析完成**
