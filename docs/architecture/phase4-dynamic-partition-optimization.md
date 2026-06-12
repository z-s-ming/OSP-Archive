# Phase 4 动态分区优化说明（Phase 4-lite）

## 1. 范围与目标

Phase 4-lite 的定位是：

- 在共享空间中执行事件触发的局部分区修正
- 修复 cell 失配和邻域拥挤问题
- 通过小步 seed 更新提升分区稳定性

Phase 4-lite 不负责：

- 物理边界紧急救援
- APF / gain / reset 主控制逻辑重写
- 全局连续优化搜索

## 2. 本轮改动摘要（稳定性增强）

本轮在现有实现上做最小侵入增强，完成以下能力：

1. 接受策略升级为 epsilon 加权改善约束
2. 触发器升级为 enter/exit 双阈值滞回
3. candidate 评估流程整理为显式事务式提交/回滚
4. seed 更新加入 anchor 回归项，抑制长期漂移
5. CSV 日志增强，补充几何量与触发上下文
6. moving 判定改为 EMA 速度门控
7. 可视化增加 rejected/cooldown/proposal invalid 区分

## 3. 关键文件与职责

- `Assets/RDW/02 Script/GlobalCoordination/GlobalCoordinationManager.cs`
  - 统一编排 BeginAttempt -> ApplyCandidate -> EvaluateCandidate -> Commit/Rollback
  - 注入新的触发、接受策略和日志字段
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdate/PartitionUpdateTriggerEvaluator.cs`
  - 滞回状态机（enter/exit）
  - persistence + cooldown
  - EMA 速度门控
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdate/RiskDrivenSeedUpdater.cs`
  - 趋势项 + 邻居排斥项 + anchor 项合成更新方向
  - 保留 per-update shift 与 max offset 裁剪
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdate/PartitionUpdateAcceptancePolicy.cs`
  - 接受策略独立封装
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdate/PartitionUpdateAttemptContext.cs`
  - attempt 事务上下文与回滚关联数据
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdate/RiskDrivenSeedUpdateState.cs`
  - per-user trigger state（armed、persist、speedEma）
  - attempt 扩展字段与 reject reason 枚举
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdate/PartitionUpdateLogger.cs`
  - 稳定表头 CSV 输出与增强字段落盘
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdate/PartitionUpdateVisualizer.cs`
  - accepted/rejected/cooldown/proposal invalid 的差异化可视化

## 4. 配置项（新增/调整）

### 4.1 接受策略

- `partitionAcceptEpsilon = 0.03f`
- `partitionCellRiskWeight = 0.5f`
- `partitionNeighborRiskWeight = 0.5f`
- `partitionMaxAllowedCellRiskWorsen = 0.02f`
- `partitionMaxAllowedNeighborRiskWorsen = 0.02f`

### 4.2 触发滞回

- `tauCellRiskEnter = 0.60f`
- `tauCellRiskExit = 0.50f`
- `tauNeighborRiskEnter = 0.60f`
- `tauNeighborRiskExit = 0.50f`
- `persistFramesCell = 3`
- `persistFramesNeighbor = 3`
- `seedUpdateCooldown = 0.75f`

### 4.3 运动判定

- `seedTriggerMoveThreshold = 0.12f`
- 触发器内部使用 `SpeedEma` 而非瞬时 speed

### 4.4 seed 更新权重

- `seedTrendWeight = 0.55f`
- `seedNeighborWeight = 0.35f`
- `seedAnchorWeight = 0.10f`

## 5. 接受策略定义

设：

- `cellImprovement = cellBefore - cellAfter`
- `neighborImprovement = neighborBefore - neighborAfter`
- `weightedImprovement = wc * cellImprovement + wn * neighborImprovement`

candidate 接受条件：

1. `weightedImprovement > partitionAcceptEpsilon`
2. `cellAfter <= cellBefore + partitionMaxAllowedCellRiskWorsen`
3. `neighborAfter <= neighborBefore + partitionMaxAllowedNeighborRiskWorsen`

任一失败则拒绝，并记录 `rejectReason`。

## 6. 触发器滞回逻辑

每用户维护两个 armed 状态：

- `isCellRiskArmed`
- `isNeighborRiskArmed`

规则：

- 未 armed 时，风险超过 enter 阈值才进入 armed
- 已 armed 时，风险低于 exit 阈值才退出 armed
- 仅在 armed 状态下累计 persistence count
- 退出 armed 时对应计数清零
- 触发时仍受 cooldown 限制

## 7. 事务式流程

一次局部更新按四阶段执行：

1. BeginAttempt
   - 保存原 seeds
   - 保存 risk temporal snapshot
   - 记录旧 partition/risk 引用与 attempt 元数据
2. ApplyCandidate
   - 写入 proposed seed
   - rebuild candidate partition
   - 评估 candidate risk
3. EvaluateCandidate
   - 使用 acceptance policy 判定
4. CommitAttempt / RollbackAttempt
   - 接受：提交新 partition/risk/seed 状态
   - 拒绝：恢复 seeds、risk snapshot、partition/risk 引用，并回滚 trigger 状态

目标是确保 rejected candidate 不污染运行时状态。

## 8. 日志字段（CSV）

增强后输出字段包括：

- `weightedImprovement`
- `cellImprovement`
- `neighborImprovement`
- `cellRiskBefore` / `cellRiskAfter`
- `neighborRiskBefore` / `neighborRiskAfter`
- `minCellClearanceBefore` / `minCellClearanceAfter`
- `minNeighborSeparationBefore` / `minNeighborSeparationAfter`
- `speedAtTrigger`
- `cellPersistCount`
- `neighborPersistCount`
- `cooldownRemaining`
- `accepted`
- `rejectReason`

`rejectReason` 枚举：

- `None`
- `InsufficientImprovement`
- `CellRiskWorsened`
- `NeighborRiskWorsened`
- `ProposalInvalid`
- `CooldownBlocked`

## 9. 可视化约定

- accepted：绿色更新箭头
- rejected：短箭头并标记 `R`
- cooldown blocked：淡黄色标记 `CD`
- proposal invalid：灰色标记 `X`

## 10. 验收建议（最小集）

1. 触发稳定性
   - 在阈值边缘来回波动输入下，触发频率应明显低于单阈值实现
2. 接受质量
   - 被接受样本中，`weightedImprovement` 应大于 epsilon
   - 单项风险恶化不应超过配置上限
3. 回滚正确性
   - 人工构造拒绝样本后，后续帧 seeds/risk 不出现 candidate 残留
4. 漂移抑制
   - 长时运行中，seed 与 user 的偏移均值应受 anchor 项抑制
5. 诊断可用性
   - CSV 中可通过 `rejectReason + cooldownRemaining + persistCount` 解释“为何未更新”

## 11. 兼容性说明

本次改动保持了以下兼容约束：

- 不修改 APF、reset 和 RDW 主控制策略
- 保留 Phase 4-lite 的局部更新框架
- 仅增强稳定性与可分析性

根据脚本代码，我详细查证了这三个关键问题：

## 一、触发分区更新的条件

**多层嵌套触发机制**（PartitionUpdateTriggerEvaluator）：

1. **双阈值滞回**（进/退 armed 状态）
   - cellRisk 超过 `cellRiskEnterThreshold` (0.60) → 进入 armed
   - cellRisk 低于 `cellRiskExitThreshold` (0.50) → 退出 armed
   - userRisk 同理（邻域风险）

2. **运动门控**
   - 必须满足: `speedEma > seedTriggerMoveThreshold` (0.12 m/s)
   - speedEma 使用 EMA 平滑计算，不用瞬时速度（抗抖动）

3. **持久化计数**
   - armed 且运动 → 累计计数
   - 达到 `persistFramesCell` (3) 或 `persistFramesNeighbor` (3) → **最终触发**
   - 离开 armed → 计数复位

4. **冷却限制**  
   - 同一用户两次更新间隔 ≥ `seedUpdateCooldown` (0.75s) 才能再次触发

**结果**：当 cellRisk 或 userRisk 在阈值附近波动时，滞回设计大幅降低了误触频率。

---

## 二、候选种子点如何生成（取样方法）

**三向加权合成**（RiskDrivenSeedUpdater.ProposeSeed）：

```
proposedSeed = currentSeed + (trendDir·w₁ + neighborDir·w₂ + anchorDir·w₃) · step
```

其中三个方向是：

| 方向 | 来源 | 权重 | 作用 |
|------|------|------|------|
| **trendDirection** | predictedCenter - currentUserPosition | 0.55 | 跟随占用带预测中心 |
| **neighborDirection** | Σ(repulsion from high-risk neighbors) | 0.35 | 邻域排斥（基于 pairwiseRisk）|
| **anchorDirection** | currentUserPosition - currentSeed | 0.10 | 回归锚点，防长期漂移 |

**步长动态选择**（RiskDrivenSeedUpdater.cs）：

```csharp
severity = Max(metrics.CellBoundaryRisk, metrics.UserRisk)
if severity >= 0.85 → step = seedStepHigh
else if severity >= 0.70 → step = seedStepMedium
else → step = seedStepLow
```

**最终约束**：
- `maxSeedShiftPerUpdate` 限制单帧最大移动距离
- `maxSeedOffsetFromUser` 限制种子与用户位置的最大偏移
- 室内边界约束（留 0.1m 安全距离）

---

## 三、根据什么选择种子点

种子点是**通过上述加权方向和risk-driven步长直接生成的**，不是从候选池中"选择"的，而是**单次参数化生成**：

```
选择过程 = 综合评分 + 方向合成 + 步长自适应
```

关键参数：
- **需要改进的risk类型**（cell 还是 neighbor）决定方向权重偏好
- **风险严重程度**决定移动步长
- **邻居冲突强度**（高 pairwiseRisk）自动加重排斥方向

---

## 四、触发时是否只动当前用户、其他用户固定？

**是的，严格的单用户更新**。

PartitionUpdateCoordinator.cs 的流程：

```csharp
1. 保存原始全局 seeds
2. For each triggered user:
     _voronoiPartitioner.SetSeedPoint(context.UserId, proposedSeed)
     // 只改变这个用户的种子，其他保持原值
3. candidatePartition = Rebuild(所有种子)
   // 重新计算Voronoi，但其他用户种子不变
4. Evaluate candidate risk
5. Accept / Rollback
```

关键代码PartitionUpdateCoordinator.cs：

```csharp
_voronoiPartitioner.SetSeedPoints(transaction.OriginalSeeds);  // 重置到全部原点
for (int i = 0; i < contexts.Count; i++)
{
    PartitionUpdateAttemptContext context = contexts[i];
    _voronoiPartitioner.SetSeedPoint(context.UserId, context.ProposedSeed);  // 只改当前用户
}
```

**影响范围**：
- 该用户的 cell 重新计算（独立变化）
- 邻近用户的 cell 边界可能变化，但**邻近用户的种子不动**
- 若candidate被接受，只有该用户的种子入库；若被拒绝，全局状态回滚到 transaction 快照

这确保了**原子性**：事件要么成功全部提交，要么完全回滚。