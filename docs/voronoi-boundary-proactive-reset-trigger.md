# VoronoiBoundary 主动重置触发器说明

本文档说明 `ProactiveUserResetJudgeMode.VoronoiBoundary` 的设计目的、调用链、触发条件、实验参数、日志字段，以及本次新增/修改的代码位置。

## 1. 目的

`VoronoiBoundary` 是一个主动用户重置的早期触发器。它不是替代原有的 `TTC` 或 `Recoverability`，而是新增一种判断入口：

```text
当一对相邻用户正在相向接近，并且其中某个用户已经靠近两者的 Voronoi 冲突边界，同时该用户运动反方向的真实物理边界余量不足时，提前触发主动 USER_RESET 候选。
```

它要解决的问题是：原有触发器通常要等用户间碰撞风险已经比较明显才触发，容易滞后，导致部分场景中用户发生 USER_RESET 后又很快 WALL_RESET，形成短时间连续重置。

## 2. 调用链

运行时调用链如下：

```text
GlobalCoordinationManager.ProcessStep()
  -> TryEvaluateBidirectionalRecoverabilityCandidates(partitionResult)
    -> ProactiveResetPipeline.Evaluate(context)
      -> ProactiveResetPairFilter.TryBuildPairContext(...)
      -> ProactiveResetTriggerDetectorFactory.Create(judgeMode)
      -> VoronoiBoundaryProactiveResetTriggerDetector.TryCreateTrigger(...)
      -> CandidateSelector
      -> CooldownPolicy
      -> SafetyPolicy
      -> ProactiveResetIntentDispatcher.Dispatch(...)
      -> RedirectedUnit.SetProactiveUserResetIntent(...)
      -> RedirectedUnit.CheckCurrentStatus(...)
      -> USER_RESET
```

`VoronoiBoundary` 只负责触发，不负责选人、不负责最终安全检查、不直接执行 reset。执行仍然经过原有 candidate selection、cooldown、in-place safety check 和 intent dispatch。

## 3. 开启方式

在 Inspector 中：

```text
Proactive User Reset
  Enable Strategy: true
  Judge Mode: VoronoiBoundary
  User Selection Mode: Arbitration
  Enable In Place Safety Check: true
```

对应枚举：

```csharp
public enum ProactiveUserResetJudgeMode
{
    Recoverability = 0,
    Simple = 1,
    TTC = 2,
    VoronoiBoundary = 3
}
```

## 4. 触发条件

一次 VoronoiBoundary 触发必须同时满足以下条件。

### 4.1 先通过旧的 pair 前置过滤

`VoronoiBoundary` 现在也走 `ProactiveResetPairFilter` 的旧过滤条件：

```text
1. 两个用户都存在有效运动方向。
2. 两个用户速度方向整体相向，即 velocityA dot velocityB < 0。
3. pair closing speed > 0.05。
4. 排除 single-side collision candidate。
```

这一步用于防止仅因为 Voronoi cell 边界接近就误触发。也就是说，第三张图那类缺少相向运动趋势的情况应被前置过滤挡掉。

### 4.2 同一个用户同时满足边界距离和反向物理边界压力

对相邻 pair `(A, B)`：

```text
dConflictA = A 到 A/B Voronoi 冲突边界的距离
dConflictB = B 到 A/B Voronoi 冲突边界的距离

reverseWallA = A 沿当前运动方向反方向到真实物理边界/障碍的距离
reverseWallB = B 沿当前运动方向反方向到真实物理边界/障碍的距离
```

触发要求同一个用户满足：

```text
(dConflictA <= voronoiBoundaryDistanceThreshold
 and reverseWallA <= voronoiBoundaryReverseWallDistanceThreshold)

or

(dConflictB <= voronoiBoundaryDistanceThreshold
 and reverseWallB <= voronoiBoundaryReverseWallDistanceThreshold)
```

默认参数：

```text
voronoiBoundaryDistanceThreshold = 1.25m
voronoiBoundaryReverseWallDistanceThreshold = 2.0m
```

注意：这里不是 `max(reverseWallA, reverseWallB) >= 2m`。当前语义是更严格的“某一用户靠近冲突边界，并且该用户运动反方向真实空间不足 2m”。

### 4.3 冲突边界距离持续下降

为了避免静态靠近边界导致误触发，触发器维护 pair 级别的趋势窗口：

```text
voronoiBoundaryTrendWindowFrames = 5
voronoiBoundaryTrendRequiredFrames = 4
```

即最近 5 帧中至少 4 次 `dConflictPair` 下降，才认为该 pair 正在持续压向冲突边界。

`dConflictPair` 定义为：

```text
dConflictPair = min(dConflictA, dConflictB)
```

如果某 pair 中断一帧以上没有被评估，趋势状态会重置，避免旧状态污染新触发。

## 5. 冲突边界与反向距离计算

### 5.1 Voronoi 冲突边界

当前实现使用 `PartitionResult.SeedPoints` 中 pair 两个 seed 的垂直平分线近似共享冲突边界：

```text
boundaryNormal = normalize(seedB - seedA)
boundaryPoint = (seedA + seedB) / 2
dConflict = abs(dot(userPosition - boundaryPoint, boundaryNormal))
```

这样不依赖共享边界线段顶点匹配，避免 Voronoi 多边形顶点浮点误差导致边界查找不稳定。

### 5.2 运动反方向真实边界距离

对每个用户：

```text
reverseDirection = -normalize(lastMovementDirection)
reverseWallDistance = raycast(realUserPosition, reverseDirection, realSpaceBoundary/obstacles)
```

该距离用于判断用户在“当前运动趋势的反方向”是否已经没有足够真实空间。默认阈值是 `2m`。

## 6. Cooldown 与重复触发

为避免同一 pair 短时间内 `(4,3)` 和 `(3,4)` 轮流重复主动触发，新增 pair-level cooldown：

```text
pairExecutionCooldownSeconds = 3.0s
```

执行主动 reset 后：

```text
RegisterProactiveUserResetExecution(runtimeUserId, otherRuntimeUserId)
  -> runtime id 转换为 units[] index
  -> RegisterExecution(selectedIndex, ...)
  -> RegisterPairExecution(selectedIndex, otherIndex, ...)
```

这里特别注意：`RedirectedUnit.GetID()` 是 runtime id，而 pipeline/cooldown 内部使用的是 `units[]` index。本次实现已在注册 cooldown 前完成转换，否则 cooldown 会失效或错位。

## 7. 日志字段

`proactive_trigger_frame.csv` 追加了 VoronoiBoundary 相关字段：

```csv
conflictBoundaryDistanceA,
conflictBoundaryDistanceB,
conflictBoundaryDistancePair,
reverseWallDistanceA,
reverseWallDistanceB,
conflictBoundaryTrendHitCount,
conflictBoundaryTrendWindowFrames
```

当 `judgeMode=VoronoiBoundary` 时：

```text
triggerDistance = conflictBoundaryDistancePair
triggerClosingSpeed = pair closing speed
```

其它 judge mode 下，这些新增字段可能为默认值，后处理脚本应根据 `judgeMode` 判断是否使用。

## 8. Manifest 参数

实验 manifest 中新增记录：

```json
"proactiveUserReset": {
  "judgeMode": "VoronoiBoundary",
  "pairExecutionCooldownSeconds": "...",
  "voronoiBoundaryDistanceThreshold": "...",
  "voronoiBoundaryReverseWallDistanceThreshold": "...",
  "voronoiBoundaryTrendWindowFrames": "...",
  "voronoiBoundaryTrendRequiredFrames": "..."
}
```

这些字段用于保证后处理和复现实验时能还原触发器参数。

## 9. 新增/修改文件

### 9.1 设置与参数

```text
Assets/RDW/02 Script/RDW_Scripts/Setting/SimulationSetting.cs
```

修改内容：

```text
1. 新增 ProactiveUserResetJudgeMode.VoronoiBoundary。
2. 新增 VoronoiBoundary 触发阈值参数。
3. 新增 pairExecutionCooldownSeconds。
```

### 9.2 Pipeline 上下文

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Core/ProactiveResetModels.cs
```

修改内容：

```text
1. ProactiveResetFrameContext 增加 PartitionResult。
2. ProactiveResetTriggerEvent 增加 VoronoiBoundary 日志字段。
```

### 9.3 Pair 前置过滤

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Core/ProactiveResetPairFilter.cs
```

修改内容：

```text
VoronoiBoundary 也使用旧的相向运动、closing-speed、single-side 过滤。
```

### 9.4 触发器实现

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Trigger/ProactiveResetTriggerDetectors.cs
```

修改内容：

```text
1. Factory 支持 VoronoiBoundary。
2. 新增 VoronoiBoundaryProactiveResetTriggerDetector。
3. 实现冲突边界距离、运动反方向真实边界距离、趋势窗口判断。
4. 提供 ResetTemporalState()，在 episode reset 时清空状态。
```

### 9.5 调用接入

```text
Assets/RDW/02 Script/GlobalCoordination/GlobalCoordinationManager.cs
```

修改内容：

```text
1. 构造 ProactiveResetFrameContext 时传入 PartitionResult。
2. episode reset 时清空 VoronoiBoundary temporal state。
3. trigger log 写入 VoronoiBoundary 字段。
4. 主动 reset 执行后注册 user cooldown 和 pair cooldown。
5. runtime id 转 units[] index 后再注册 cooldown。
```

### 9.6 Cooldown

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Core/ProactiveResetStrategyInterfaces.cs
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Execution/PerUserProactiveResetCooldown.cs
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Execution/ProactiveResetIntentDispatcher.cs
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Core/ProactiveResetPipeline.cs
```

修改内容：

```text
1. 新增 pair cooldown 接口。
2. candidate 阶段和 dispatch 阶段都检查 pair cooldown。
3. 执行主动 reset 后注册 pair cooldown。
```

### 9.7 日志与 manifest

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Logging/ProactiveTriggerWindowLogger.cs
Assets/RDW/02 Script/GM_DataRecord.cs
```

修改内容：

```text
1. trigger frame log 增加 VoronoiBoundary 指标列。
2. manifest 增加 VoronoiBoundary 参数和 pair cooldown 参数。
```

### 9.8 执行日志调用

```text
Assets/RDW/02 Script/RDW_Scripts/Unit/RedirectedUnit.cs
```

修改内容：

```text
主动 USER_RESET 执行时，把 selected runtime id 和 other runtime id 一起传给 GlobalCoordinationManager 注册 cooldown。
```

## 10. 与原有策略的关系

```text
Recoverability:
  使用最大曲率可恢复性判断 pair 是否已经难以通过重定向自然恢复。

TTC:
  使用未来时间到碰撞判断直接几何碰撞风险。

VoronoiBoundary:
  使用 Voronoi 冲突边界接近、运动反方向真实空间不足、持续下降趋势作为早期干预信号。
```

三者通过 `ProactiveUserResetJudgeMode` 切换，互不改变彼此的触发语义。

`Enable In Place Safety Check` 仍保持原语义：选中用户原地 reset 时，检查另一用户用最大曲率左右规避是否仍不可避免发生 pair 间用户碰撞。

## 11. 实验注意事项

1. 对比实验必须记录 `judgeMode` 和所有 VoronoiBoundary 参数。
2. 如果观察到重复 pair 触发，优先检查 `pairExecutionCooldownSeconds` 是否写入 manifest，以及 cooldown 注册是否拿到了正确的 `otherRuntimeUserId`。
3. 如果观察到静态近边界误触发，应检查 `conflictBoundaryTrendHitCount` 是否真的达到阈值。
4. 如果观察到非相向用户触发，应检查 `ProactiveResetPairFilter` 的 closing-speed 和 velocity dot 日志或断点。
5. 后处理时不要把 `trigger` 等同于 `executed reset`；真正执行仍以 `inter_reset_distance.csv` 中的 `resetEventType=PROACTIVE_USER_RESET` 为准。
