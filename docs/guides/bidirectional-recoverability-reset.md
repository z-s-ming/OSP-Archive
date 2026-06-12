# Bidirectional Recoverability Reset

## Purpose

This reset module is designed for the bidirectional user-reset problem.
It is implemented as a new reset type instead of a risk module because its role is to decide whether a bilateral user collision should enter the reset pipeline.

The new reset type is:

- `ResetType.APF_R_Turn_OSP_BiRecoverability`

It keeps the existing `APF_R_Turn_OSP` wall-reset behavior and adds a recoverability gate in the bilateral `USER_RESET` trigger path.

## Core Idea

When two users collide while facing opposite directions, the system treats this as a bilateral-reset candidate.
Before triggering `USER_RESET`, the new resetter evaluates whether the collision is still avoidable by continuous maximum-curvature turning.

The evaluator checks the four extreme bilateral responses:

- `(+1, +1)`
- `(+1, -1)`
- `(-1, +1)`
- `(-1, -1)`

where:

- `+1` means maximum left turn
- `-1` means maximum right turn

For each pair, it computes the minimum short-horizon separation margin:

`m_sep^(sigmaA, sigmaB) = min_t ( d^(sigmaA, sigmaB)(t) - d_safe )`

and then takes:

`M_sep = max over all four pairs`

Decision rule:

- if `M_sep >= 0`, the bilateral collision is considered recoverable
- if `M_sep < 0`, the bilateral collision is considered unrecoverable

## Behavior in This Project

The new resetter changes only bilateral `USER_RESET` triggering:

- unilateral user reset stays unchanged
- wall reset stays the same as `APF_R_Turn_OSP`

When a bilateral collision candidate is detected:

1. Build both users' current states from the live `RedirectedUnit`
2. Estimate each user's speed from its resetter translation speed
3. Estimate each user's maximum curvature as:

`kappa_max = omega_max / v`

where:

- `omega_max` is the configured resetter rotation speed in radians per second
- `v` is the configured resetter translation speed

4. Predict both users under four maximum-curvature response pairs over a short horizon
5. Compute `M_sep`
6. Gate the reset:

- `Recoverable == true`:
  do not trigger bilateral `USER_RESET`
- `Recoverable == false`:
  keep the original bilateral reset flow

## Files

The implementation is located under the reset subsystem:

- `Assets/RDW/02 Script/RDW_Scripts/Resetter/APF_R_BiRecoverability_Resetter_OSP.cs`
- `Assets/RDW/02 Script/RDW_Scripts/Resetter/BidirectionalCollisionRecoverabilityEvaluator.cs`
- `Assets/RDW/02 Script/RDW_Scripts/Resetter/BidirectionalCollisionRecoverabilityAssessment.cs`

The reset enum mapping is updated in:

- `Assets/RDW/02 Script/RDW_Scripts/Setting/UnitSetting.cs`

The base reset hooks were opened for extension in:

- `Assets/RDW/02 Script/RDW_Scripts/Resetter/Resetter.cs`

## Current Parameters

Current built-in parameters in the new resetter:

- horizon: `1.5` seconds
- sample count: `60`
- safety buffer: `0.1`

Safe distance is computed as:

- `radiusA + radiusB + safetyBuffer`

If a user is not represented as `Circle2D`, the evaluator falls back to a radius of `0.5`.

## Notes

- This module is a reset-oriented gate, not a motion planner.
- It does not yet apply the best turn pair back into user control.
- Its current role is only to decide whether bilateral reset is necessary.
- If later needed, the same evaluator can be extended to output the best turn pair for a recovery controller.



下面按列顺序逐个说明（含“怎么来”）：

| 字段 | 含义 | 来源/计算 |
|---|---|---|
| `frame` | 帧号 | `Time.frameCount` |
| `time` | 仿真时间（秒） | `Time.time` |
| `pairMinId` | 用户对中较小ID | `min(unitA.GetID(), unitB.GetID())` |
| `pairMaxId` | 用户对中较大ID | `max(unitA.GetID(), unitB.GetID())` |
| `unitAId` | A用户ID | `unitA.GetID()` |
| `unitBId` | B用户ID | `unitB.GetID()` |
| `triggerTag` | 触发标签 | `TryLog` 参数 `triggerTag`（仅做CSV逗号替换） |
| `unitAStatus` | A当前状态 | `unitA.GetStatus()` |
| `unitBStatus` | B当前状态 | `unitB.GetStatus()` |
| `collisionType` | 碰撞类型文本 | `ResolveCollisionType(unitAStatus, unitBStatus)` 推导 |
| `unitAUsedRotationGain` | A上次移动是否用旋转增益 | `unitA.UsedRotationGainInLastMove()`，写成 `1/0` |
| `unitBUsedRotationGain` | B上次移动是否用旋转增益 | `unitB.UsedRotationGainInLastMove()`，写成 `1/0` |
| `distanceNow` | 当前两人距离 | `|(posB - posA)|` |
| `predictedMinDistance` | 预测时域内最小距离 | `assessment.PredictedMinDistance` |
| `currentDistance` | 评估器中的当前距离 | `assessment.CurrentDistance` |
| `safeDistance` | 安全距离阈值 | `TryLog` 参数 `safeDistance` |
| `isIntersectNow` | 当前是否重叠/相交 | `userA.IsIntersect(userB)`，写 `1/0` |
| `forwardDot` | 朝向点积 | `dot(normalized(forwardA), normalized(forwardB))` |
| `movementDot` | 运动方向点积 | `dot(normalized(moveA), normalized(moveB))` |
| `closingSpeed` | 由相对速度计算的接近速度 | `ResolveClosingSpeed(offsetAB, velA, velB)` |
| `closingSpeedNow` | 评估器当前接近速度 | `assessment.ClosingSpeedNow` |
| `isAdjacentCellCandidate` | 是否邻接cell候选 | `assessment.IsAdjacentCellCandidate`，写 `1/0` |
| `isApproachingCandidate` | 是否接近候选 | `assessment.IsApproachingCandidate`，写 `1/0` |
| `irrecoverableStreak` | 不可恢复连续计数 | `assessment.IrrecoverableStreak` |
| `persistentStreak` | 持续风险连续计数 | `assessment.PersistentStreak` |
| `isIrrecoverable` | 当前是否不可恢复 | `assessment.IsIrrecoverable`，写 `1/0` |
| `isApproaching` | 当前是否接近 | `assessment.IsApproaching`，写 `1/0` |
| `isPersistent` | 当前是否持续风险 | `assessment.IsPersistent`，写 `1/0` |
| `riskConfirmed` | 风险是否确认 | `assessment.RiskConfirmed`，写 `1/0` |
| `recoverable` | 是否可恢复 | `assessment.Recoverable`，写 `1/0` |
| `maxSeparationMargin` | 最大分离裕度 | `assessment.MaxSeparationMargin` |
| `marginLL` | 组合LL的分离裕度 | `assessment.MarginLL` |
| `marginLR` | 组合LR的分离裕度 | `assessment.MarginLR` |
| `marginRL` | 组合RL的分离裕度 | `assessment.MarginRL` |
| `marginRR` | 组合RR的分离裕度 | `assessment.MarginRR` |
| `bestSigmaA` | A最优sigma策略索引/值 | `assessment.BestSigmaA` |
| `bestSigmaB` | B最优sigma策略索引/值 | `assessment.BestSigmaB` |
| `worstTimeOnBestPair` | 最优组合下最危险时刻 | `assessment.WorstTimeOnBestPair` |
| `horizonSeconds` | 预测时域长度（秒） | `TryLog` 参数 `horizonSeconds` |
| `sampleCount` | 采样点数 | `TryLog` 参数 `sampleCount` |

补充规则：

1. 所有 `bool` 最终写入 CSV 时都转成 `1/0`。  
2. 浮点通过 `ToInvariant(..., "F6")` 写固定小数位。  
3. `assessment.*` 字段都不是本文件内部再计算，而是上游评估器传进来的结果。