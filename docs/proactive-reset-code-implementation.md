# Proactive Reset Code Implementation

本文档说明当前仓库中主动用户重置（proactive user reset）的代码实现。它关注运行时代码结构、调用链、关键类职责、配置项、日志和后处理口径，不讨论论文研究动机。

## 1. 文档定位

`docs/主动重置_桌面文档修订版.md` 是论文方法和研究逻辑文档。本文档是代码实现说明，回答：

- 主动 reset 代码在哪些文件里。
- 每帧如何从候选 pair 走到触发、选人、安全检查、冷却、下发和执行。
- `PROACTIVE_USER_RESET` 如何进入普通 `USER_RESET` 执行流程。
- 日志字段从哪里写出，后处理时应该看哪些文件。

已有相关文档：

- `docs/voronoi-boundary-proactive-reset-trigger.md`：只说明 `VoronoiBoundary` 触发器。
- `docs/proactive-reset-log-postprocess-guide.md`：只说明主动 reset 日志后处理。
- 本文档：主动 reset 运行时代码总览。

## 2. 代码目录

主动 reset 主要代码位于：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/
```

目录职责如下：

| 目录 | 作用 |
| --- | --- |
| `Core/` | 数据模型、pair 前置过滤、pipeline、接口、事件 ID。 |
| `Trigger/` | 主动 reset 触发器，包括 `Recoverability`、`Simple`、`TTC`、`VoronoiBoundary`、`RecoveryMarginTrend`。 |
| `Selection/` | 选择由哪名用户执行主动 reset。当前主要是 arbitration。 |
| `Safety/` | 候选动作安全检查，当前支持原地安全检查。 |
| `Execution/` | 冷却策略和 intent 下发。 |
| `Logging/` | trigger 和 candidate 阶段日志。 |

主动 reset 还接入以下外部文件：

| 文件 | 作用 |
| --- | --- |
| `Assets/RDW/02 Script/GlobalCoordination/GlobalCoordinationManager.cs` | 每帧构造上下文、调用 pipeline、写 trigger log、下发 intent、注册 cooldown。 |
| `Assets/RDW/02 Script/RDW_Scripts/Unit/RedirectedUnit.cs` | 接收 proactive intent，并在 `CheckCurrentStatus()` 中转为 `PROACTIVE_USER_RESET` 执行。 |
| `Assets/RDW/02 Script/RDW_Scripts/Setting/SimulationSetting.cs` | 主动 reset 的 Inspector 配置和枚举。 |
| `Assets/RDW/02 Script/GM_DataRecord.cs` | episode summary、inter-reset detail 和 manifest 日志。 |

## 3. 总体调用链

运行时调用链如下：

```text
GlobalCoordinationManager.ProcessStep()
  -> TryEvaluateBidirectionalRecoverabilityCandidates(partitionResult)
    -> ProactiveResetPipeline.Evaluate(context)
      -> ProactiveResetPairFilter.TryBuildPairContext(...)
      -> ProactiveResetTriggerDetectorFactory.Create(judgeMode)
      -> triggerDetector.TryCreateTrigger(...)
      -> ProactiveResetCandidateSelectorFactory.Create(userSelectionMode)
      -> candidateSelector.TrySelectCandidate(...)
      -> cooldownPolicy.IsPairCoolingDown(...)
      -> cooldownPolicy.IsCoolingDown(...)
      -> safetyPolicy.IsCandidateSafe(...)
      -> result.Intents.Add(...)
    -> LogProactiveResetTriggers(result.Triggers)
    -> ProactiveResetIntentDispatcher.Dispatch(result.Intents, units, frame)
      -> RedirectedUnit.SetProactiveUserResetIntent(...)
  -> RDWSimulationManager.SimulateRDW()
    -> RedirectedUnit.Simulate(...)
      -> RedirectedUnit.CheckCurrentStatus(...)
        -> TryConsumeProactiveUserResetIntent(...)
        -> status = "USER_RESET"
        -> LogInterResetDistance(..., "PROACTIVE_USER_RESET", ...)
        -> TryBeginExternalReset(ResetPlanType.ProactiveUserReset)
```

注意：主动 reset 并不直接绕过原有 resetter。它先在 `RedirectedUnit` 上写入一个 pending intent，然后由 `CheckCurrentStatus()` 把该 intent 消费成一次 `USER_RESET` 状态，只是日志类型标记为 `PROACTIVE_USER_RESET`。

## 4. 每帧入口

入口在：

```text
GlobalCoordinationManager.TryEvaluateBidirectionalRecoverabilityCandidates(...)
```

该函数每帧做以下事情：

1. 从 `RDWSimulationManager.instance.simulationSetting.proactiveUserReset` 读取配置。
2. 判断主动策略是否启用：

```text
proactiveEnabled = proactiveSettings.enableStrategy && bAllowUserReset
```

3. 判断是否需要运行 precheck：

```text
shouldRunPrecheck =
    proactiveEnabled
    or enableProactiveResetPairDistanceLogging
    or debugVisualizationEnabled
```

4. 要求 `partitionResult.CellAdjacency` 存在，因为主动 reset 只遍历相邻 cell 的用户 pair。
5. 主动策略启用时，先清空每个 unit 的上一帧 pending proactive intent。
6. 构造 `ProactiveResetFrameContext`。
7. 调用 `ProactiveResetPipeline.Evaluate(context)`。
8. 写 trigger log。
9. 下发 proactive reset intents。

## 5. FrameContext 与结果模型

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Core/ProactiveResetModels.cs
```

关键模型：

| 类型 | 作用 |
| --- | --- |
| `ProactiveResetFrameContext` | 每帧输入，包括 units、partition、adjacency、settings、frame/time、是否启用 proactive。 |
| `ProactiveResetPairContext` | 单个相邻用户 pair 的运动学上下文，包括 offset、velocity、closing speed、预测窗口。 |
| `ProactiveResetTriggerEvent` | 触发器输出，用于 trigger log。 |
| `ProactiveResetCandidate` | 候选选择输出，包括选中用户、另一用户、reset 方向、评分和 reject reason。 |
| `ProactiveResetIntent` | 最终下发给 `RedirectedUnit` 的执行意图。 |
| `ProactiveResetFrameResult` | 一帧内所有 triggers、candidates、intents、rejections。 |

## 6. Pair 前置过滤

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Core/ProactiveResetPairFilter.cs
```

`ProactiveResetPairFilter.TryBuildPairContext()` 只允许满足以下条件的用户 pair 进入触发器：

1. pair 来自 `partitionResult.CellAdjacency`，且 `unitBId > unitAId`，避免重复评估 `(A,B)` 和 `(B,A)`。
2. 两个用户和真实用户对象都存在。
3. 两个用户都有有效 `lastMovementDirection`。
4. 根据 movement direction 和 speed 计算 velocity。
5. `Vector2.Dot(velocityA, velocityB) < 0`，即整体上是相向运动。
6. closing speed 大于 `0.05`。
7. 排除 single-side collision candidate，即只有一方朝向另一方移动的情况。

这一步是主动 reset 的第一道门，目的是避免仅因空间邻近就触发主动 reset。

## 7. Trigger Detectors

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Trigger/ProactiveResetTriggerDetectors.cs
```

触发器由 `ProactiveResetTriggerDetectorFactory.Create(judgeMode)` 创建。当前支持：

| `judgeMode` | 类 | 触发逻辑 |
| --- | --- | --- |
| `Recoverability` | `RecoverabilityProactiveResetTriggerDetector` | 调用 `BidirectionalCollisionRecoverabilityEvaluator.Evaluate()`，当 `RiskConfirmed` 为真时触发。 |
| `Simple` | `SimpleDistanceProactiveResetTriggerDetector` | pair 距离小于 `simpleTriggerDistanceMeters`，且 closing speed 大于阈值。 |
| `TTC` | `TtcProactiveResetTriggerDetector` | 在预测窗口内采样两用户位置，若进入碰撞距离且满足最小 lead time 则触发。 |
| `VoronoiBoundary` | `VoronoiBoundaryProactiveResetTriggerDetector` | 接近 Voronoi 冲突边界、反向真实空间不足、冲突边界距离持续下降时触发。 |
| `RecoveryMarginTrend` | `RecoveryMarginTrendProactiveResetTriggerDetector` | 恢复余量仍为正、进入主动窗口、持续下降且用户仍在接近时触发。 |

`RecoveryMarginTrend` 当前最接近论文中的主动干预窗口思想：

```text
assessment.MaxSeparationMargin > 0
assessment.MaxSeparationMargin <= proactiveMarginThreshold
assessment.IsApproachingCandidate
margin trend confirmed
```

其中：

```text
rawThreshold = assessment.ClosingSpeedNow * recoveryMarginLeadTimeSeconds
             + recoveryMarginBufferMeters

proactiveMarginThreshold =
    clamp(rawThreshold,
          recoveryMarginMinThresholdMeters,
          recoveryMarginMaxThresholdMeters)
```

## 8. Candidate Selection

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Selection/ProactiveResetCandidateSelectors.cs
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Selection/ProactiveUserResetArbitrationService.cs
```

`ProactiveResetCandidateSelectorFactory` 根据 `userSelectionMode` 创建选择器：

| `userSelectionMode` | 行为 |
| --- | --- |
| `None` | 不选择用户，返回 `SelectionDisabled`。 |
| `Arbitration` | 使用 `ProactiveUserResetArbitrationService.TryArbitratePair()`。 |

Arbitration 的核心计算：

1. 对用户 A/B 分别计算 keep 方向和 reset 方向。
2. 当前 reset 方向是“远离 pair 中另一名用户”：

```text
resetDirectionA = normalize(positionA - positionB)
resetDirectionB = normalize(positionB - positionA)
```

3. 沿方向 raycast 计算真实空间剩余距离：

```text
keepDistanceA, keepDistanceB
resetDistanceA, resetDistanceB
```

4. 比较主动 reset 后 pair 中较差一侧的可行走距离：

```text
keepWorstDistance = min(keepDistanceA, keepDistanceB)
resetAWorstDistance = min(resetDistanceA, keepDistanceB)
resetBWorstDistance = min(resetDistanceB, keepDistanceA)
```

5. 选择 `resetAWorstDistance` 与 `resetBWorstDistance` 更大的动作。
6. 若二者接近，用 `cSelf` 较小的一方打破平局。
7. 若仍然接近，用 index 小的一方打破平局。

当 `judgeMode == RecoveryMarginTrend` 时，还会检查最小预期收益：

```text
selectedM >= keepMargin + proactiveMinExpectedImprovementMeters
```

否则拒绝，reason 为 `InsufficientExpectedImprovement`。

## 9. Cooldown

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Execution/PerUserProactiveResetCooldown.cs
```

该类维护两类 cooldown：

| cooldown | 字段 | 用途 |
| --- | --- | --- |
| 用户级 | `cooldownUntilFrameByUser` | 防止同一用户短时间内反复主动 reset。 |
| pair 级 | `cooldownUntilFrameByPair` | 防止同一 pair 短时间内轮流重复触发。 |

Pipeline 在 candidate 接受前检查：

```text
IsPairCoolingDown(...)
IsCoolingDown(...)
```

真正执行 proactive reset 后，`GlobalCoordinationManager.RegisterProactiveUserResetExecution()` 注册：

```text
executionCooldownSeconds
pairExecutionCooldownSeconds
```

## 10. Safety Policy

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Safety/ProactiveResetSafetyPolicies.cs
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Safety/ProactiveUserResetSafetyValidator.cs
```

`ProactiveResetSafetyPolicyFactory` 根据配置选择：

| 配置 | Policy |
| --- | --- |
| `enableInPlaceSafetyCheck = false` | `NoProactiveResetSafetyPolicy`，候选直接通过。 |
| `enableInPlaceSafetyCheck = true` | `InPlaceProactiveResetSafetyPolicy`。 |

`InPlaceProactiveResetSafetyPolicy` 调用：

```text
ProactiveUserResetSafetyValidator.IsSafeInPlaceReset(...)
```

如果检查失败，候选被拒绝，reason 为 `InPlaceSafetyCheck`。

## 11. Pipeline 决策顺序

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Core/ProactiveResetPipeline.cs
```

每个 pair 的决策顺序为：

```text
pair filter
  -> debug recoverability evaluate, optional
  -> trigger detector
  -> assign triggerId
  -> if proactive disabled: only trigger/debug path, no candidate
  -> candidate selector
  -> pair cooldown check
  -> user cooldown check
  -> safety policy
  -> accept candidate
  -> candidate log
  -> per-selected-user conflict resolution
  -> output intent
```

同一帧内，一个用户可能被多个 pair 选中。Pipeline 用 `selectedCandidateByUser` 对同一用户保留一个候选。替换规则在 `ShouldReplaceCandidate()` 中：

1. `KeepMargin` 更小者优先。
2. 若接近，`SelectedM` 更大者优先。
3. 若接近，`SelectedCSelf` 更小者优先。
4. 若仍接近，`OtherUserId` 更小者优先。

## 12. Intent Dispatch

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Execution/ProactiveResetIntentDispatcher.cs
```

Dispatcher 对每个 intent 做最终下发检查：

1. selected/other index 合法。
2. selected unit 和 other unit 存在。
3. selected unit 当前状态必须是 `IDLE`。
4. selected user 未处于 user cooldown。
5. selected pair 未处于 pair cooldown。

通过后调用：

```text
selectedUnit.SetProactiveUserResetIntent(...)
```

这一步只是写入 pending intent，还没有真正执行 reset。

## 13. RedirectedUnit 执行

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/Unit/RedirectedUnit.cs
```

执行分两步。

第一步，Dispatcher 写入 pending intent：

```text
SetProactiveUserResetIntent(
    otherUser,
    resetDirection,
    bidirectionalResetEvent,
    userId,
    otherUserId,
    originTriggerId,
    originCandidateId,
    decisionId)
```

该函数保存：

```text
pendingProactiveOtherUser
pendingProactiveResetDirection
pendingProactiveUserId
pendingProactiveOtherUserId
pendingProactiveOriginTriggerId
pendingProactiveOriginCandidateId
pendingProactiveDecisionId
hasPendingProactiveUserResetIntent = true
```

第二步，`RedirectedUnit.CheckCurrentStatus()` 在 `IDLE` 分支中优先消费 proactive intent：

```text
TryConsumeProactiveUserResetIntent(...)
  -> status = "USER_RESET"
  -> resultData.AddUserReset()
  -> RegisterUserResetEvent(..., proactive=true)
  -> LogInterResetDistance(..., "PROACTIVE_USER_RESET", ...)
  -> TryBeginExternalReset(ResetPlanType.ProactiveUserReset)
```

普通 user reset 逻辑仍在后面：

```text
resetter.NeedUserReset(...)
  -> LogInterResetDistance(..., "USER_RESET", ...)
  -> TryBeginExternalReset(ResetPlanType.UserReset)
```

因此主动 reset 的执行优先于普通被动 user reset，但最终都走 `USER_RESET` 状态和 resetter 执行流程。

## 14. 日志

主动 reset 运行时主要写三类日志。

### 14.1 Trigger log

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Logging/ProactiveTriggerWindowLogger.cs
```

输出：

```text
CGnA_DataLog/runs/<run>/raw/proactive_trigger_frame.csv
```

用途：记录触发器认为某 pair 满足主动 reset 风险条件的帧。

关键字段：

```text
triggerId
episodeObjectId
triggerFrame
triggerTime
pairMinUserId
pairMaxUserId
userId
otherUserId
judgeMode
horizonSeconds
triggerDistance
triggerClosingSpeed
predictedPairType
conflictBoundaryDistanceA/B/Pair
reverseWallDistanceA/B
conflictBoundaryTrendHitCount
```

### 14.2 Candidate log

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/Logging/ProactiveCandidateFrameLogger.cs
```

输出：

```text
CGnA_DataLog/runs/<run>/raw/proactive_candidate_frame.csv
```

用途：记录 trigger 后的候选选择、接受或拒绝原因。

关键字段：

```text
originTriggerId
candidateId
decisionId
candidateStatus
accepted
executed
resetDirectionX/Y
keepMargin
selectedM
selectedCSelf
rejectReason
```

常见 `rejectReason`：

```text
SelectionDisabled
ArbitrationFailed
InsufficientExpectedImprovement
PairCooldown
Cooldown
InPlaceSafetyCheck
```

### 14.3 Inter-reset detail

写入点在：

```text
RedirectedUnit.CheckCurrentStatus()
```

主动 reset 执行时写：

```text
resetEventType = "PROACTIVE_USER_RESET"
```

普通 user reset 执行时写：

```text
resetEventType = "USER_RESET"
```

后处理判断“是否真正执行主动 reset”时，应以 `inter_reset_distance.csv` 中的 `PROACTIVE_USER_RESET` 为准，而不是以 trigger 或 candidate 为准。

## 15. 配置项

文件：

```text
Assets/RDW/02 Script/RDW_Scripts/Setting/SimulationSetting.cs
```

主要配置在：

```text
SimulationSetting.proactiveUserReset
```

关键字段：

| 字段 | 作用 |
| --- | --- |
| `enableStrategy` | 主动 reset 总开关。 |
| `judgeMode` | 触发器类型。 |
| `userSelectionMode` | 用户选择方式，当前支持 `Arbitration` 和 `None`。 |
| `predictionHorizonSeconds` | 预测窗口秒数。 |
| `predictionSampleCount` | 预测采样数。 |
| `simpleTriggerDistanceMeters` | Simple 触发距离。 |
| `simpleClosingSpeedThreshold` | Simple closing speed 阈值。 |
| `ttcCollisionDistanceMeters` | TTC 碰撞距离阈值。 |
| `ttcMinTimeToHitSeconds` | TTC 最小提前时间。 |
| `arbitrationMEpsilon` | arbitration 中 worst-distance 比较容差。 |
| `arbitrationCEpsilon` | arbitration 中 self-cost 比较容差。 |
| `enableInPlaceSafetyCheck` | 是否启用主动 reset 原地安全检查。 |
| `executionCooldownSeconds` | 用户级 cooldown。 |
| `pairExecutionCooldownSeconds` | pair 级 cooldown。 |
| `recoveryMarginLeadTimeSeconds` | RecoveryMarginTrend 主动窗口 lead time。 |
| `recoveryMarginBufferMeters` | RecoveryMarginTrend 不确定性缓冲。 |
| `recoveryMarginMinThresholdMeters` | RecoveryMarginTrend 最小主动窗口阈值。 |
| `recoveryMarginMaxThresholdMeters` | RecoveryMarginTrend 最大主动窗口阈值。 |
| `recoveryMarginTrendWindowFrames` | 恢复余量趋势窗口长度。 |
| `recoveryMarginTrendRequiredFrames` | 窗口内需要多少帧下降。 |
| `proactiveMinExpectedImprovementMeters` | RecoveryMarginTrend 下候选动作最小预期收益。 |

## 16. Episode reset 时清理的状态

在 `GlobalCoordinationManager` 的 episode reset 逻辑中会清理：

```text
BidirectionalCollisionRecoverabilityEvaluator.ResetTemporalState()
VoronoiBoundaryProactiveResetTriggerDetector.ResetTemporalState()
RecoveryMarginTrendProactiveResetTriggerDetector.ResetTemporalState()
ProactiveResetEventIdTracker.ResetSession()
ProactiveTriggerWindowLogger.ResetSession()
ProactiveCandidateFrameLogger.ResetSession()
proactiveResetCooldown.Clear()
```

这一步很重要：趋势窗口、event id、logger session 和 cooldown 都不能跨 episode 污染。

## 17. 关键注意事项

1. `trigger` 不等于执行。真正执行以 `inter_reset_distance.csv` 中的 `PROACTIVE_USER_RESET` 行为准。
2. `candidateStatus=ACCEPTED` 也不等于执行。Dispatcher 或 `RedirectedUnit` 状态仍可能阻止执行。
3. 主动 reset 写入的是 pending intent，真正执行发生在 `RedirectedUnit.CheckCurrentStatus()`。
4. 主动 reset 执行后状态仍是 `USER_RESET`，但日志类型为 `PROACTIVE_USER_RESET`。
5. `ProactiveResetPairFilter` 依赖相邻 cell 和相向运动，非相向用户一般不会进入触发器。
6. `RecoveryMarginTrend` 的最小收益门槛只在该 judge mode 下启用。
7. 同一帧同一 selected user 可能来自多个 pair，Pipeline 会保留一个候选。
8. cooldown 同时有用户级和 pair 级，调实验时要同时记录二者参数。
9. `D_next_min` 和 `I_short_event_τ` 这类论文指标不是运行时代码直接写出的字段，通常由后处理脚本从 `inter_reset_distance.csv` 派生。

## 18. 与其它文档的关系

- 想理解代码运行时结构：读本文档。
- 想理解 `VoronoiBoundary` 触发器细节：读 `docs/voronoi-boundary-proactive-reset-trigger.md`。
- 想写后处理脚本和实验指标：读 `docs/proactive-reset-log-postprocess-guide.md`。
- 想写论文研究动机和指标论证：读 `docs/主动重置_桌面文档修订版.md`。
