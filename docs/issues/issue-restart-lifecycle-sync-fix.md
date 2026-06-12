# 用户实验重启生命周期同步问题诊断与修复方案

## 问题描述

在Live VR多人实验系统中，当执行"软重新开始"（Soft Restart）或"重新开始实验并校准"（Recalibrate Restart）后，出现以下问题：

1. **客户端能够重新开始运行**，但实验的生命周期数据不同步
2. **重置（Reset）功能无法触发** - 用户物理空间中无法正常执行重置操作
3. **用户位置同步异常** - 位置数据没有正确同步或同步了错误的Host物理空间数据
4. **重定向增益无法注入** - RDW（Redirected Walking）增益计算和应用失败

## 根本原因分析

### 问题1：生命周期版本更新顺序错误

在`LiveVRHostExperimentController.cs`的`SoftRestartRun()`和`RecalibrateAndRestart()`方法中：

```csharp
// 当前代码（第216-239行）
public void SoftRestartRun()
{
    StopRdwRuntime();
    ClearLiveResetRuntime(true);
    manager.BeginRestartEpoch();              // ❌ 问题：先增加restartEpoch
    BeginNewRunSession("soft_restart");       // ❌ 这里生成新的hostRunId
    manager.SetHostRunId(hostRunId);          // ❌ 然后设置新的runId
    manager.BroadcastClientResetClear("soft_restart");  // ❌ 广播时带的是新版本号
    ClearLivePoseCache();
    ResetRdwRuntimeForNextRun();
    // ...
}
```

**问题所在**：

1. `BeginRestartEpoch()` 将 `restartEpoch` 从 N 增加到 N+1
2. `BeginNewRunSession()` 生成新的 `hostRunId`（例如从 "20260610_143022" 变为 "20260610_143055"）
3. `SetHostRunId()` 更新 `protocolContext` 中的 `hostRunId` 和 `runId`
4. `BroadcastClientResetClear()` 发送 `CLIENT_RESET_CLEAR` 消息，消息中包含：
   - `RestartEpoch = N+1`（新值）
   - 但是通过可靠控制服务发送时，envelope中的`HostRunId`和`RunId`也是新值

**结果**：Client端收到消息时，由于自己还持有旧的`hostRunId`（N）和`restartEpoch`（N），当收到新的`CLIENT_RESET_CLEAR`消息时：
- 如果Client的`protocolContext`还未更新到新的`hostRunId`，可能会拒绝该消息
- 或者Client更新了epoch但没有正确清理本地状态

### 问题2：Client端接收控制消息的版本过滤

在`LiveVRProtocolVersionContext.cs`的`Accepts()`方法中（第49-71行）：

```csharp
public bool Accepts(LiveVRControlEnvelope envelope)
{
    bool isNewerRestartEpoch = envelope.RestartEpoch > restartEpoch;
    
    // 如果hostRunId不匹配，且不是更新的restartEpoch，拒绝
    if (!string.IsNullOrEmpty(hostRunId) &&
        !string.IsNullOrEmpty(envelope.HostRunId) &&
        !string.Equals(hostRunId, envelope.HostRunId, StringComparison.Ordinal) &&
        !isNewerRestartEpoch)
    {
        return false;  // ❌ 可能在这里拒绝了CLIENT_RESET_CLEAR消息
    }

    // 如果restartEpoch更旧，拒绝
    if (envelope.RestartEpoch < restartEpoch)
        return false;

    // 校准版本不匹配时拒绝
    if (envelope.CalibrationVersion > 0 &&
        calibrationVersion > 0 &&
        envelope.CalibrationVersion != calibrationVersion)
    {
        return false;
    }

    return true;
}
```

**问题场景**：
1. Client当前状态：`hostRunId="20260610_143022"`, `restartEpoch=0`
2. Host发送：`CLIENT_RESET_CLEAR`，envelope中`hostRunId="20260610_143055"`, `restartEpoch=1`
3. Client判断：`hostRunId`不匹配，但`isNewerRestartEpoch=true`，所以应该接受
4. **但是**：如果Client在接受消息后没有正确更新自己的`protocolContext`，后续的`RESET_START`、`VIRTUAL_POSE`等消息都可能被拒绝

### 问题3：HMD Movement Controller的Previous Pose Cache未正确清理

在`LiveVRHmdMovementController.cs`中（第73-85行）：

```csharp
LiveVRPoseSample previousSample;
if (!previousPoseByUserId.TryGetValue(userId, out previousSample))
    previousSample = sample;  // ❌ 首次使用当前sample作为previous

// 使用previousSample计算delta
LiveVRGainDebugSample gainDebug = ResolveLiveWalkingStepper()
    .StepLiveWalking(unit, sample, previousSample, units);

previousPoseByUserId[userId] = sample;  // 更新cache
```

虽然`SoftRestartRun()`调用了`ClearLivePoseCache()`（第228行），但时序问题可能导致：
- Cache清理后，第一帧的`previousSample`会被设置为当前`sample`
- 如果此时用户已经移动，第一帧的delta为0
- 或者如果Client还在发送旧session的pose数据，会产生错误的delta

### 问题4：CLIENT_RESET_CLEAR消息处理不完整

`CLIENT_RESET_CLEAR`消息的处理链路：

1. **Host发送**（`LiveVRNetworkManager.cs:430-448`）：
   ```csharp
   public void BroadcastClientResetClear(string reason)
   {
       LiveVRClientResetClearMessage clear = new LiveVRClientResetClearMessage
       {
           UserId = -1,
           RestartEpoch = protocolContext.RestartEpoch,  // 新的epoch
           Reason = reason,
           HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
       };

       for (int userId = 0; userId < expectedUserCountForAssignment; userId++)
       {
           if (IsUserConnected(userId, 10.0f))
               SendReliableToUser(userId, "CLIENT_RESET_CLEAR", clear.ToNetworkMessage());
       }
   }
   ```

2. **Client接收** - 代码中没有直接搜索到CLIENT_RESET_CLEAR的接收处理器，这意味着：
   - 消息可能通过可靠控制服务的通用handler接收
   - 但可能缺少将`RestartEpoch`更新到Client端`protocolContext`的逻辑

## 修复方案

### 修复1：调整Restart流程中的版本更新顺序

修改`LiveVRHostExperimentController.cs`的`SoftRestartRun()`方法：

```csharp
public void SoftRestartRun()
{
    LiveVRNetworkManager manager = ResolveNetworkManager();
    if (manager == null || !manager.IsHost)
        return;

    // 步骤1：停止RDW运行时
    StopRdwRuntime();
    
    // 步骤2：清除Live Reset运行时状态
    ClearLiveResetRuntime(true);
    
    // 步骤3：生成新的Run Session ID（但先不广播）
    BeginNewRunSession("soft_restart");
    
    // 步骤4：先广播CLIENT_RESET_CLEAR，使用*当前*的restartEpoch
    //        让Client在旧版本上下文中清理状态
    manager.BroadcastClientResetClear("soft_restart_prepare");
    
    // 步骤5：增加restartEpoch（这会影响后续所有消息）
    manager.BeginRestartEpoch();
    
    // 步骤6：更新Host的runId到protocolContext
    manager.SetHostRunId(hostRunId);
    
    // 步骤7：广播新的STATE消息，包含新的restartEpoch和runId
    //        Client通过STATE消息中更高的restartEpoch知道要更新版本上下文
    manager.BroadcastStateWithNewEpoch();
    
    // 步骤8：清理本地pose cache
    ClearLivePoseCache();
    
    // 步骤9：重置RDW运行时
    ResetRdwRuntimeForNextRun();
    
    // 步骤10：清除运行时模拟fallback
    manager.ClearRuntimeSimulatedFallbacks();

    // 步骤11：确定下一状态
    LiveVRExperimentState nextState = manager.HasPendingReliableControlType("CLIENT_RESET_CLEAR")
        ? LiveVRExperimentState.WaitingForUsers
        : AreExpectedUsersReady()
        ? LiveVRExperimentState.Ready
        : LiveVRExperimentState.WaitingForUsers;
    
    manager.SetExperimentState(nextState);
    
    Debug.Log(string.Format("[LiveVR] Soft Restart Run complete. state={0} runId={1} restartEpoch={2}", 
        nextState, GetHostRunIdLabel(), manager.RestartEpoch));
}
```

同样修改`RecalibrateAndRestart()`方法。

### 修复2：在LiveVRNetworkManager中添加新方法

在`LiveVRNetworkManager.cs`中添加：

```csharp
/// <summary>
/// 广播STATE消息，并包含完整的版本上下文，用于Restart后同步
/// </summary>
public void BroadcastStateWithNewEpoch()
{
    if (!IsHost)
        return;

    LiveVRStateMessage stateMessage = new LiveVRStateMessage
    {
        ExperimentState = experimentState,
        HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    };
    
    // 通过可靠控制服务发送，会自动包含完整的protocolContext
    for (int userId = 0; userId < expectedUserCountForAssignment; userId++)
    {
        if (IsUserConnected(userId, 10.0f))
            SendReliableToUser(userId, "STATE", stateMessage.ToNetworkMessage());
    }
    
    Debug.Log(string.Format("[LiveVR] Broadcasted STATE with new epoch. restartEpoch={0} runId={1}", 
        protocolContext.RestartEpoch, protocolContext.RunId));
}
```

### 修复3：增强Client端对CLIENT_RESET_CLEAR的处理

需要确保Client端在接收到`CLIENT_RESET_CLEAR`后：

```csharp
// 在Client接收线程中处理CONTROL envelope
void HandleControlEnvelope(LiveVRControlEnvelope envelope)
{
    // 特殊处理：CLIENT_RESET_CLEAR允许推进到更高的restartEpoch
    if (envelope.MessageType == "CLIENT_RESET_CLEAR")
    {
        LiveVRClientResetClearMessage clear;
        if (LiveVRClientResetClearMessage.TryParse(envelope.Payload, out clear))
        {
            // 如果收到更高的restartEpoch，允许更新本地版本上下文
            if (envelope.RestartEpoch > protocolContext.RestartEpoch)
            {
                Debug.Log(string.Format(
                    "[LiveVR Client] CLIENT_RESET_CLEAR推进版本上下文: restartEpoch {0}->{1}, runId {2}->{3}",
                    protocolContext.RestartEpoch, envelope.RestartEpoch,
                    protocolContext.RunId, envelope.RunId));
                
                protocolContext.RestartEpoch = envelope.RestartEpoch;
                protocolContext.HostRunId = envelope.HostRunId;
                protocolContext.RunId = envelope.RunId;
            }
            
            // 调用清理方法
            if (clientPresentationState != null)
                clientPresentationState.ClearForRestart(clear.RestartEpoch, clear.Reason);
            
            // 发送ACK
            SendControlAck(envelope.MessageId, envelope.MessageType, true, "reset_cleared");
        }
    }
    else if (!protocolContext.Accepts(envelope))
    {
        // 其他消息：如果版本不匹配，拒绝
        Debug.LogWarning(string.Format(
            "[LiveVR Client] 拒绝控制消息: type={0}, envelope.restartEpoch={1}, local.restartEpoch={2}",
            envelope.MessageType, envelope.RestartEpoch, protocolContext.RestartEpoch));
        return;
    }
    
    // 处理其他控制消息...
    switch (envelope.MessageType)
    {
        case "STATE":
            HandleStateMessage(envelope);
            break;
        case "RESET_START":
            HandleResetStartMessage(envelope);
            break;
        // ... 其他消息类型
    }
}
```

### 修复4：增强HMD Movement Controller的Pose Anchor机制

在`LiveVRHmdMovementController.cs`中添加运行状态检查：

```csharp
public void Step(RDWSimulationManager simulationManager, RedirectedUnit[] units)
{
    LiveVRNetworkManager manager = ResolveNetworkManager();
    if (manager == null || units == null)
        return;

    if (hostOnly && !manager.IsHost)
        return;

    // ✅ 添加：只在Running状态下驱动RDW
    if (evaluateRdwCoreWhileRunning && 
        manager.ExperimentState != LiveVRExperimentState.Running)
    {
        // 非Running状态：清理previous pose cache，避免积累错误的delta
        if (previousPoseByUserId.Count > 0)
        {
            Debug.Log("[LiveVR] Experiment not running, clearing previous pose cache to prevent stale deltas");
            previousPoseByUserId.Clear();
        }
        return;
    }

    unitsWithFreshPose.Clear();
    unitsDrivenBySimulation.Clear();
    
    for (int unitIndex = 0; unitIndex < units.Length; unitIndex++)
    {
        RedirectedUnit unit = units[unitIndex];
        if (unit == null)
            continue;

        int userId = unitIndex + unitIndexToUserIdOffset;
        LiveVRUserSource source = ResolveConfiguredUserSource(manager, userId);
        
        if (manager.ShouldUseSimulatedUser(userId, stalePoseTimeoutSeconds, requireCalibratedPose))
        {
            unitsDrivenBySimulation.Add(unit);
            continue;
        }

        LiveVRPoseSample sample;
        bool hasLivePose = TryGetValidPose(manager, userId, source != LiveVRUserSource.SimulatedOnly, out sample);
        if (!hasLivePose)
            continue;

        LiveVRPoseSample previousSample;
        bool hasPrevious = previousPoseByUserId.TryGetValue(userId, out previousSample);
        
        // ✅ 改进：第一帧使用当前sample作为anchor，避免产生delta
        if (!hasPrevious)
        {
            previousSample = sample;
            Debug.Log(string.Format("[LiveVR] User {0} first pose anchor: pos=({1:F2},{2:F2}) yaw={3:F1}",
                userId, sample.ExperimentPosition.x, sample.ExperimentPosition.y, sample.YawDegrees));
        }

        defaultLiveWalkingStepper.DriveVirtualUserFromHmdDelta = driveVirtualUserFromHmdDelta;
        LiveVRGainDebugSample gainDebug = ResolveLiveWalkingStepper()
            .StepLiveWalking(unit, sample, previousSample, units);
        
        if (gainDebug.IsValid)
        {
            LiveVRGainCommandService.UpdateFromRdwSample(userId, gainDebug);
            LiveVRGainDebugState.Record(userId, gainDebug);
        }
        
        previousPoseByUserId[userId] = sample;
        unitsWithFreshPose.Add(unit);
    }

    if (!evaluateRdwCoreWhileRunning || simulationManager == null || !simulationManager.BStart)
        return;

    for (int i = 0; i < unitsDrivenBySimulation.Count; i++)
        unitsDrivenBySimulation[i].Simulate(units);

    for (int i = 0; i < unitsWithFreshPose.Count; i++)
    {
        unitsWithFreshPose[i].CheckCurrentStatus(units);
    }
}
```

### 修复5：添加版本同步日志

在关键位置添加日志，便于调试：

```csharp
// 在LiveVRProtocolVersionContext.Accepts()中
public bool Accepts(LiveVRControlEnvelope envelope)
{
    bool isNewerRestartEpoch = envelope.RestartEpoch > restartEpoch;
    
    if (!string.IsNullOrEmpty(hostRunId) &&
        !string.IsNullOrEmpty(envelope.HostRunId) &&
        !string.Equals(hostRunId, envelope.HostRunId, StringComparison.Ordinal) &&
        !isNewerRestartEpoch)
    {
        Debug.LogWarning(string.Format(
            "[LiveVR] Rejected control message: hostRunId mismatch. " +
            "local={0}, envelope={1}, restartEpoch local={2} envelope={3}, messageType={4}",
            hostRunId, envelope.HostRunId, restartEpoch, envelope.RestartEpoch, envelope.MessageType));
        return false;
    }

    if (envelope.RestartEpoch < restartEpoch)
    {
        Debug.LogWarning(string.Format(
            "[LiveVR] Rejected control message: stale restartEpoch. " +
            "local={0}, envelope={1}, messageType={2}",
            restartEpoch, envelope.RestartEpoch, envelope.MessageType));
        return false;
    }

    if (envelope.CalibrationVersion > 0 &&
        calibrationVersion > 0 &&
        envelope.CalibrationVersion != calibrationVersion)
    {
        Debug.LogWarning(string.Format(
            "[LiveVR] Rejected control message: calibrationVersion mismatch. " +
            "local={0}, envelope={1}, messageType={2}",
            calibrationVersion, envelope.CalibrationVersion, envelope.MessageType));
        return false;
    }

    return true;
}
```

## 实施步骤

1. **立即修复**：修改`SoftRestartRun()`和`RecalibrateAndRestart()`的版本更新顺序
2. **验证消息流**：添加详细日志，确认Client接收到并正确处理`CLIENT_RESET_CLEAR`
3. **增强Client处理**：实现`CLIENT_RESET_CLEAR`的特殊处理逻辑，允许版本推进
4. **测试场景**：
   - 单用户Soft Restart后立即开始实验
   - 多用户Soft Restart后所有用户能正常Reset
   - Recalibrate Restart后重新校准并能触发增益
   - 网络延迟情况下的版本同步

## 预期效果

修复后应实现：

1. ✅ Soft Restart后，Client正确接收并处理`CLIENT_RESET_CLEAR`
2. ✅ Client的`protocolContext`与Host同步到新的`restartEpoch`和`runId`
3. ✅ 后续的`RESET_START`、`VIRTUAL_POSE`等消息不再被版本过滤拒绝
4. ✅ HMD Movement Controller正确清理pose cache，第一帧不产生错误delta
5. ✅ 重置功能正常触发，用户能在物理空间中执行reset
6. ✅ RDW增益正确计算并注入到Client视角

## 相关文件

- `OSP-Archive/Assets/RDW/02 Script/LiveVRMultiplayer/Host/LiveVRHostExperimentController.cs`
- `OSP-Archive/Assets/RDW/02 Script/LiveVRMultiplayer/Core/LiveVRProtocolVersionContext.cs`
- `OSP-Archive/Assets/RDW/02 Script/LiveVRMultiplayer/Network/LiveVRNetworkManager.cs`
- `OSP-Archive/Assets/RDW/02 Script/LiveVRMultiplayer/RDW/LiveVRHmdMovementController.cs`
- `OSP-Archive/Assets/RDW/02 Script/LiveVRMultiplayer/Client/LiveVRClientPresentationState.cs`
- `OSP-Archive/docs/live-vr-multiplayer-user-experiment.md` (设计文档)
