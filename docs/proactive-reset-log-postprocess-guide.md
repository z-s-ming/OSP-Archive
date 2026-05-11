# Proactive Reset Log Postprocess Guide

本文档用于指导主动 reset / 普通 reset 日志的后处理脚本编写。目标不是要求运行时日志继续膨胀字段，而是明确现有字段的真实语义，并从现有 CSV 中派生稳定的实验统计口径。

## 1. Log Sources

后处理脚本至少读取三类文件；若分析主动 reset 的阶段效果，建议同时读取 candidate 层日志：

| 文件 | 典型路径 | 用途 |
| --- | --- | --- |
| Episode summary | `CGnA_DataLog/runs/<run>/raw/episode_summary.csv` | 每个 experiment episode 的汇总 reset 计数。 |
| Inter-reset detail | `CGnA_DataLog/runs/<run>/raw/inter_reset_distance.csv` | 每次 wall/user/proactive reset 事件明细，以及 reset 间距离。 |
| Proactive trigger frame log | `CGnA_DataLog/runs/<run>/raw/proactive_trigger_frame.csv` | 主动 reset 的风险触发帧。 |
| Proactive candidate frame log | `CGnA_DataLog/runs/<run>/raw/proactive_candidate_frame.csv` | trigger 后候选选择、冷却、安全检查的阶段结果。 |

脚本不要依赖 Excel 打开的显示值。`Date` 可能被 Excel 显示成科学计数法，必须按字符串读取。

## 2. Column Semantics

### 2.1 Episode Summary

当前 summary header：

```csv
Date,Timestamp,totalResetCountPerEpisode,userResetCountVariancePerEpisode,boundaryCollisionCountPerEpisode,proactiveUserResetActionCountPerEpisode,userInterResetSingleActionCountPerEpisode,userInterResetBothActionCountPerEpisode,user00ResetCountPerEpisode,...
```

建议解释：

| 字段 | 语义 |
| --- | --- |
| `Date` | 写日志时的墙钟时间字符串，不参与实验时间计算。 |
| `Timestamp` | 从 `GM_DataRecord.Start()` 起算的 Unity 时间。 |
| `totalResetCountPerEpisode` | 当前 experiment episode 内所有 unit 的 reset 总数。 |
| `boundaryCollisionCountPerEpisode` | wall + shutter reset 总数。当前 OSP flow 中 shutter 常为 0，但脚本不要假设。 |
| `proactiveUserResetActionCountPerEpisode` | 主动 USER_RESET 执行动作数，不等于 trigger 数。 |
| `userInterResetSingleActionCountPerEpisode` | 普通 user-user 单边 reset action 数。 |
| `userInterResetBothActionCountPerEpisode` | 普通双边 user reset 的 pair event 数，不是两个人各加一次的 action 数。 |
| `userXXResetCountPerEpisode` | 第 XX 个 logical user 在该 experiment episode 内的 total reset 数。 |

`userXXResetCountPerEpisode` 的列数量可作为 `logicalUserCount` 的可靠来源。

### 2.2 Inter-Reset Detail

新 header：

```csv
Date,Timestamp,episodeObjectId,frame,simTime,runtimeUnitId,otherRuntimeUnitId,runtimePairMinId,runtimePairMaxId,resetEventType,isBidirectionalUserPair,interResetDistance,cumulativeDistance
```

旧 header 兼容映射：

| 旧字段 | 新字段 | 说明 |
| --- | --- | --- |
| `episodeId` | `episodeObjectId` | 这是 `Episode.getID()`，不是 experiment episode index。 |
| `unitId` | `runtimeUnitId` | 这是 `RedirectedUnit.GetID()`，不是稳定 logical user index。 |
| `otherUnitId` | `otherRuntimeUnitId` | pair 另一方 runtime id；wall reset 时为 -1。 |
| `pairMinId` | `runtimePairMinId` | runtime pair id 的较小值。 |
| `pairMaxId` | `runtimePairMaxId` | runtime pair id 的较大值。 |
| `resetType` | `resetEventType` | `WALL_RESET` / `SHUTTER_RESET` / `USER_RESET` / `PROACTIVE_USER_RESET`。 |
| `isBidirectional` | `isBidirectionalUserPair` | 表示 user-user 风险/碰撞 pair 是否为双边语义，不表示一定双人都执行了 reset。 |

关键注意：

- `episodeObjectId` 不能直接用于 experiment episode 分组。
- `runtimeUnitId` 跨 episode 会递增，不能直接当 `user00/user01/...`。
- `runtimePairMinId/runtimePairMaxId` 可用于同一个 runtime episode 内的 pair join。
- `interResetDistance` 是该 runtime unit 自上次 reset 以来的物理距离。

### 2.3 Proactive Trigger Frame Log

新 header：

```csv
triggerIdInLog,episodeObjectId,triggerFrame,triggerTime,runtimePairMinId,runtimePairMaxId,runtimeUnitAId,runtimeUnitBId,judgeMode,horizonSeconds,triggerDistance,triggerClosingSpeed,unitMinStatus,unitMaxStatus,predictedPairType
```

旧 header 兼容映射：

| 旧字段 | 新字段 | 说明 |
| --- | --- | --- |
| `triggerId` | `triggerIdInLog` | 只在单个 trigger log 文件内唯一。 |
| `episodeId` | `episodeObjectId` | `Episode.getID()`。 |
| `pairMinId` | `runtimePairMinId` | runtime pair id 较小值。 |
| `pairMaxId` | `runtimePairMaxId` | runtime pair id 较大值。 |
| `unitAId` | `runtimeUnitAId` | 触发时传入的第一个 runtime unit。 |
| `unitBId` | `runtimeUnitBId` | 触发时传入的第二个 runtime unit。 |
| `collisionType` | `predictedPairType` | 预测/风险 pair 类型，不是已经发生的实际 collision。 |

关键注意：

- `triggerIdInLog` 在不同 CSV 文件之间会重复。脚本应构造 `triggerGlobalKey = fileName + ":" + triggerIdInLog`。
- `predictedPairType=BIDIRECTIONAL_USER_PAIR` 表示触发时根据朝向/closing 判断出的风险类型，不等于实际发生了双边 reset。

### 2.4 Proactive Candidate Frame Log

当前 header：

```csv
Date,Timestamp,episodeObjectId,frame,simTime,runtimePairMinId,runtimePairMaxId,runtimeUnitAId,runtimeUnitBId,judgeMode,selectionMode,candidateStatus,selectedRuntimeUnitId,otherRuntimeUnitId,resetDirectionX,resetDirectionY,keepMargin,selectedM,selectedCSelf,rejectReason
```

该表是阶段日志，不是 reset 执行日志：

| 字段 | 语义 |
| --- | --- |
| `candidateStatus` | `ACCEPTED` 表示产生可下发候选；`REJECTED` 表示候选阶段被拒绝；`SELECTION_DISABLED` 表示 trigger 成立但配置为不选择执行用户。 |
| `rejectReason` | `NONE`、`SelectionDisabled`、`ArbitrationFailed`、`Cooldown`、`InPlaceSafetyCheck` 等阶段原因。 |
| `selectedRuntimeUnitId` | 被选中执行主动 reset 的 runtime unit；未选中时可能为 `-1`。 |
| `otherRuntimeUnitId` | 候选对应的另一 runtime unit；选择失败时可能为 `-1`。 |
| `keepMargin/selectedM/selectedCSelf` | arbitration 候选评分，用于比较不同 selector 的行为。 |

关键注意：

- `ACCEPTED` 不等于真正执行 `PROACTIVE_USER_RESET`。真正执行仍以 `inter_reset_distance.csv` 中 `resetEventType=PROACTIVE_USER_RESET` 为准。
- candidate 日志按 pair 的 trigger window 去重，适合解释“为什么 trigger 没有变成主动 reset”，不适合当逐帧 trace 使用。

## 3. Normalization Layer

脚本第一步应做字段归一化，允许读取旧日志和新日志。

建议把每个表统一成以下字段：

```text
episodeObjectId
frame
simTime
runtimeUnitId
otherRuntimeUnitId
runtimePairMinId
runtimePairMaxId
resetEventType
isBidirectionalUserPair
triggerIdInLog
triggerTime
horizonSeconds
predictedPairType
candidateStatus
selectedRuntimeUnitId
rejectReason
sourceFile
```

读取 CSV 时要跳过重复 header 行。当前批量保存逻辑可能在 append 到同一文件时再次写 header，脚本应过滤 `Date == "Date"` 或 `frame == "frame"` 的行。

## 4. Derived Stable Keys

### 4.1 logicalUserCount

从 episode summary 中统计 `user\d\dResetCountPerEpisode` 列数：

```text
logicalUserCount = count(columns matching /^user\d+ResetCountPerEpisode$/)
```

如果没有 summary 文件，可退化为从 inter-reset 中每个 runtime episode block 的 runtime unit 数推断，但这比 summary 不稳。

### 4.2 runtimeEpisodeIndex

不要使用 `episodeObjectId`。推荐从 runtime unit id block 推导：

```text
minRuntimeUnitId = minimum runtimeUnitId in current inter-reset file
runtimeEpisodeIndex = floor((runtimeUnitId - minRuntimeUnitId) / logicalUserCount)
```

对 pair 事件：

```text
pairEpisodeIndexA = floor((runtimePairMinId - minRuntimeUnitId) / logicalUserCount)
pairEpisodeIndexB = floor((runtimePairMaxId - minRuntimeUnitId) / logicalUserCount)
```

校验要求：

- 对 user-user reset，`pairEpisodeIndexA == pairEpisodeIndexB == runtimeEpisodeIndex`。
- 对 wall/shutter reset，`otherRuntimeUnitId == -1`，只使用 `runtimeUnitId` 推导。

如果同一个 CSV 混入了多个 Unity Play session，`minRuntimeUnitId` 可能仍可用，但脚本应额外用 `Date/Timestamp/simTime` 是否回跳来切分 `playSessionIndex`。

### 4.3 logicalUserIndex

```text
logicalUserIndex = (runtimeUnitId - minRuntimeUnitId) % logicalUserCount
```

pair 两端：

```text
logicalPairMinIndex = min(logicalUserIndexA, logicalUserIndexB)
logicalPairMaxIndex = max(logicalUserIndexA, logicalUserIndexB)
```

不要用 `runtimePairMinId % logicalUserCount` 直接当 logical pair，因为 runtime id 可能不是从 0 开始。

### 4.4 triggerGlobalKey

```text
triggerGlobalKey = triggerSourceFileName + ":" + triggerIdInLog
```

`triggerIdInLog` 只在单个 trigger CSV 内可靠。

## 5. Trigger-Reset Matching

### 5.1 Matching Window

对每个 trigger row，定义窗口：

```text
windowStartTime = triggerTime
windowEndTime = triggerTime + horizonSeconds
windowStartFrame = triggerFrame
```

候选 reset rows：

```text
same runtime pair:
  runtimePairMinId == trigger.runtimePairMinId
  runtimePairMaxId == trigger.runtimePairMaxId
  simTime >= triggerTime
  simTime <= triggerTime + horizonSeconds

wall/shutter involving either user:
  resetEventType in {WALL_RESET, SHUTTER_RESET}
  runtimeUnitId in {trigger.runtimePairMinId, trigger.runtimePairMaxId}
  simTime within trigger window
```

frame 也可以作为辅助校验：

```text
frame >= triggerFrame
```

以 `simTime` 为主，因为不同 log 文件的写入时间不一定同步；以 `frame` 校验排序。

### 5.2 Outcome Classification

推荐按以下优先级分类：

| outcome | 判定 |
| --- | --- |
| `PROACTIVE_EXECUTED_ONLY` | window 内有 `PROACTIVE_USER_RESET`，且没有后续同 pair `USER_RESET`。 |
| `PROACTIVE_EXECUTED_THEN_USER_RESET` | window 内先有 `PROACTIVE_USER_RESET`，之后同 pair 又有 `USER_RESET`。 |
| `PASSIVE_USER_RESET_INSTEAD` | window 内没有 `PROACTIVE_USER_RESET`，但有同 pair `USER_RESET`。 |
| `BOUNDARY_RESET_DURING_WINDOW` | window 内 pair 任一用户发生 wall/shutter reset。可作为 secondary flag，不建议覆盖 user-pair outcome。 |
| `NO_RESET_WITHIN_HORIZON` | window 内没有 proactive/user/wall/shutter reset。 |

建议输出两个字段：

```text
primaryOutcome
boundaryResetDuringWindow
```

这样不会把 user-user 主效果和 wall reset 干扰混在一起。

### 5.3 Same-Frame Execution

主动 reset 常见模式是 trigger 与 `PROACTIVE_USER_RESET` 同 frame：

```text
proactiveExecutedSameFrame =
  exists reset row where
    resetEventType == PROACTIVE_USER_RESET
    frame == triggerFrame
    runtimePairMinId == trigger.runtimePairMinId
    runtimePairMaxId == trigger.runtimePairMaxId
```

这个字段适合检查主动 reset dispatch 是否按预期进入 `RedirectedUnit`。

## 6. Recommended Derived CSV

建议后处理脚本输出一份 derived trigger outcome 表：

```csv
triggerGlobalKey,sourceFile,runtimeEpisodeIndex,logicalPairMinIndex,logicalPairMaxIndex,triggerFrame,triggerTime,horizonSeconds,judgeMode,predictedPairType,triggerDistance,triggerClosingSpeed,proactiveExecutedSameFrame,firstProactiveFrame,firstUserResetFrame,primaryOutcome,boundaryResetDuringWindow
```

还可输出一份 normalized reset event 表：

```csv
runtimeEpisodeIndex,logicalUserIndex,logicalOtherUserIndex,logicalPairMinIndex,logicalPairMaxIndex,frame,simTime,resetEventType,isBidirectionalUserPair,interResetDistance,cumulativeDistance,sourceFile
```

## 7. Metrics To Report

每个 experiment episode 建议统计：

| metric | 来源 |
| --- | --- |
| `triggerCount` | trigger outcome 表行数。 |
| `proactiveExecutedCount` | `primaryOutcome` 包含 proactive executed 的行数，或 reset detail 中 `PROACTIVE_USER_RESET` 行数。 |
| `passiveResetInsteadCount` | `primaryOutcome == PASSIVE_USER_RESET_INSTEAD`。 |
| `proactiveThenUserResetCount` | `primaryOutcome == PROACTIVE_EXECUTED_THEN_USER_RESET`。 |
| `noResetWithinHorizonCount` | `primaryOutcome == NO_RESET_WITHIN_HORIZON`。 |
| `boundaryDuringTriggerWindowCount` | `boundaryResetDuringWindow == true`。 |
| `wallResetCount` | reset detail 中 `WALL_RESET` 行数。 |
| `ordinaryUserResetCount` | reset detail 中 `USER_RESET` 行数。 |
| `proactiveUserResetCount` | reset detail 中 `PROACTIVE_USER_RESET` 行数。 |

与 summary 对账：

```text
sum(reset detail rows by runtimeEpisodeIndex) should equal totalResetCountPerEpisode
count(PROACTIVE_USER_RESET) should equal proactiveUserResetActionCountPerEpisode
count(WALL_RESET + SHUTTER_RESET) should equal boundaryCollisionCountPerEpisode
```

普通 single/both 的对账要注意：

- `USER_RESET` 行数是 action rows。
- `userInterResetBothActionCountPerEpisode` 当前更接近 pair event count。
- 双边 reset 可能两名用户各有一行明细，但 summary 只计一次 pair event。

## 8. Sanity Checks

脚本应输出 warning，而不是静默吞掉：

1. `episodeObjectId` 不连续或从 0 开始：这是正常现象，但不应用于实验分组。
2. 同一 `runtimePairMinId/runtimePairMaxId` 的两端推导出不同 `runtimeEpisodeIndex`：这是异常。
3. trigger window 内同一 pair 同时出现多个 proactive reset：需要人工检查 cooldown 或 active session 去重。
4. trigger log 中 `triggerIdInLog` 重复但来自不同文件：正常；来自同文件则异常。
5. summary 行数与 inter-reset 推导出的 `runtimeEpisodeIndex` 数量不一致：可能 CSV 混入多个 play session 或 summary/inter-reset 文件未配对。
6. `Date` 被读成数字或科学计数法：说明 CSV parser/Excel 导出破坏了字段，应重新从原始 CSV 读取。

## 9. Minimal Python Processing Shape

伪代码：

```python
summary = read_summary(summary_path)
logical_user_count = count_user_columns(summary.columns)

reset = normalize_reset_csv(inter_reset_path)
reset = drop_repeated_headers(reset)
min_runtime_id = reset["runtimeUnitId"].min()
reset["runtimeEpisodeIndex"] = (reset["runtimeUnitId"] - min_runtime_id) // logical_user_count
reset["logicalUserIndex"] = (reset["runtimeUnitId"] - min_runtime_id) % logical_user_count

triggers = concat([
    normalize_trigger_csv(path).assign(sourceFile=basename(path))
    for path in trigger_paths
])
triggers["triggerGlobalKey"] = triggers["sourceFile"] + ":" + triggers["triggerIdInLog"].astype(str)
triggers["runtimeEpisodeIndex"] = (triggers["runtimePairMinId"] - min_runtime_id) // logical_user_count
triggers["logicalPairMinIndex"] = (triggers["runtimePairMinId"] - min_runtime_id) % logical_user_count
triggers["logicalPairMaxIndex"] = (triggers["runtimePairMaxId"] - min_runtime_id) % logical_user_count

outcomes = []
for trigger in triggers:
    window = reset[
        (reset["simTime"] >= trigger.triggerTime) &
        (reset["simTime"] <= trigger.triggerTime + trigger.horizonSeconds)
    ]

    pair_events = window[
        (window["runtimePairMinId"] == trigger.runtimePairMinId) &
        (window["runtimePairMaxId"] == trigger.runtimePairMaxId)
    ]

    boundary_events = window[
        window["resetEventType"].isin(["WALL_RESET", "SHUTTER_RESET"]) &
        window["runtimeUnitId"].isin([trigger.runtimePairMinId, trigger.runtimePairMaxId])
    ]

    outcomes.append(classify(trigger, pair_events, boundary_events))
```

`classify()` 使用第 5.2 节的优先级。

## 10. Interpretation Rules

- `PROACTIVE_USER_RESET` 是执行动作，不是 trigger。
- trigger 没有对应 proactive reset 时，不一定是 bug；可能被冷却、状态非 IDLE、安全检查、或普通 collision reset 抢先。
- `isBidirectionalUserPair=1` 不等于双方都执行 reset。
- `predictedPairType` 是触发时的预测类别，不是实际碰撞结果。
- `episodeObjectId` 和 `runtimeUnitId` 是运行时对象 ID；稳定实验口径应使用派生出的 `runtimeEpisodeIndex` 和 `logicalUserIndex`。

## 11. Runtime Module Layout

主动 reset 运行时代码按阶段放在 `Assets/RDW/02 Script/RDW_Scripts/ProactiveUserReset/` 下：

```text
Core/       context、pair filter、pipeline、strategy interfaces
Trigger/    Simple / TTC / Recoverability trigger detectors and recoverability evaluator
Selection/  candidate selector and arbitration service
Safety/     safety policies and in-place safety validator
Execution/  cooldown policy and intent dispatcher
Logging/    trigger and candidate frame loggers
```

GCM 只负责每帧构造 context、调用 pipeline、调用日志输出和 dispatcher；具体触发、候选、检查、冷却、下发细节不应再回流到 GCM。
