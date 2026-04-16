# Phase 3 变更记录：风险评估层与目录整理

## 1. 变更目的

本次变更包含两部分：

1. 实现风险评估层（只评估、不改控制逻辑）。
2. 对 GlobalCoordination 目录按模块分层，降低后续维护复杂度。

目标是让系统先具备稳定的风险感知能力，再在后续阶段（例如 T6 安全滤波）复用统一风险接口。

## 2. 模块级改动说明

### 2.1 协调主流程（GlobalCoordinationManager）

改动文件：

- Assets/RDW/02 Script/GlobalCoordination/GlobalCoordinationManager.cs

改动内容：

1. 新增风险层开关与参数（阈值、权重、日志频率、可视化开关）。
2. 初始化风险层组件：
   - PartitionRiskEvaluator
   - PartitionRiskLogger
   - PartitionRiskVisualizer
3. 在每帧分区结果产生后执行风险评估和日志输出。
4. 在 OnDrawGizmos 中追加风险可视化绘制。
5. 在 ResetEpisode 中重置风险层时序状态。

设计约束：

- 不改 redirect/reset/controller 的控制逻辑。
- 风险层作为只读分支接入。

### 2.2 风险评估模块（Risk）

新模块目录：

- Assets/RDW/02 Script/GlobalCoordination/Risk/

文件：

- PartitionRiskEvaluator.cs
- PartitionRiskLogger.cs
- PartitionRiskVisualizer.cs

能力说明：

1. 风险指标拆分：
   - cellBoundaryRisk
   - physicalBoundaryRisk
   - userRisk
   - smoothRisk（由 seedMotionPenalty + cellShapePenaltyReserved 构成）
2. dominantRiskType：
   - None / Wall / User / Smooth / Mixed
3. 用户间占用带风险：
   - 接口使用 minOccupancySeparation 命名
   - 当前实现为 same-horizon approximation，并保留后续升级空间
4. 风险输出：
   - 向量形式（均值）：[cell, user, smooth]，并保留 physical 观测
   - 标量形式：weightedTotalRisk（用于排序/汇总）
5. 日志原始几何量：
   - minCellClearance
   - minPhysicalClearance
   - minPairSeparation
   - minOccupancySeparation
   - seedDelta

### 2.3 预测占用模块（Prediction）

新模块目录：

- Assets/RDW/02 Script/GlobalCoordination/Prediction/

文件：

- PredictiveOccupancyModel.cs
- PredictiveOccupancyVisualizer.cs
- PredictionHorizonSelector.cs
- VelocityPredictor.cs
- PredictionEvaluator.cs

作用：

1. 统一预测占用带的数据表达（band/sample/segment）。
2. 提供未来占用可视化。
3. 提供基于误差统计的 horizon 选择工具。

## 3. 目录整理（文件迁移映射）

### 3.1 从 GlobalCoordination 根目录迁移到 Prediction

1. PredictiveOccupancyModel.cs -> Prediction/PredictiveOccupancyModel.cs
2. PredictiveOccupancyVisualizer.cs -> Prediction/PredictiveOccupancyVisualizer.cs
3. PredictionHorizonSelector.cs -> Prediction/PredictionHorizonSelector.cs
4. VelocityPredictor.cs -> Prediction/VelocityPredictor.cs
5. PredictionEvaluator.cs -> Prediction/PredictionEvaluator.cs

### 3.2 从 GlobalCoordination 根目录迁移到 Risk

1. PartitionRiskEvaluator.cs -> Risk/PartitionRiskEvaluator.cs
2. PartitionRiskLogger.cs -> Risk/PartitionRiskLogger.cs
3. PartitionRiskVisualizer.cs -> Risk/PartitionRiskVisualizer.cs

说明：

- 对应 .meta 文件已随迁移同步处理，保持 Unity 资产 GUID 连续性。

## 4. 工程配置同步

改动文件：

- Assembly-CSharp.csproj

改动内容：

1. 更新 Prediction 相关 Compile Include 路径。
2. 更新 Risk 相关 Compile Include 路径。

说明：

- 该同步用于保证当前 IDE 工程引用和目录结构一致。

## 5. 风险数据输出约定

日志目录：

- CGnA_DataLog/riskEvaluate/

日志文件：

- risk_frame_log_yyyyMMdd_HHmmss.csv

输出频率：

- 每 N 帧（由 riskOutputEveryNFrames 控制）。

## 6. 后续维护建议

1. 后续新增预测相关文件统一放入 Prediction 子目录。
2. 后续新增风险相关文件统一放入 Risk 子目录。
3. 若引入 cellShapePenalty 实现，保持现有日志字段不删，仅补充计算逻辑。
4. 若升级 user risk 为全带精确最小距离，复用 minOccupancySeparation 接口名，不改对外字段。

## 7. 风险可视化判读指南

本节用于回答“这些值是什么意思，如何判断是否正常”。

### 7.1 屏幕标签字段含义

当前可视化标签格式为：

- Ux: 用户编号。
- dom: 主导风险类型（None/Wall/User/Smooth/Mixed）。
- cell: cellBoundaryRisk，表示预测占用带对 Voronoi cell 边界的风险。
- phy: physicalBoundaryRisk，表示预测占用带对物理房间边界的风险。
- user: userRisk，表示该用户与其他用户的交互风险均值。
- smooth: smoothRisk，当前主要来自 seedMotionPenalty（cellShapePenaltyReserved 目前为 0）。
- vec:[cell,user,smooth]: 风险向量（用于解释而非单一打分）。
- tot: weightedTotalRisk，加权总分（默认权重 0.4/0.4/0.2）。

### 7.2 风险值范围与直觉

所有 risk 值都在 [0, 1] 区间内：

1. 0.00 到 0.20：低风险，通常表示有充足余量。
2. 0.20 到 0.50：中风险，表示开始接近安全阈值。
3. 0.50 到 0.80：高风险，建议关注是否持续升高。
4. 0.80 到 1.00：极高风险，通常意味着已接近或越过阈值。

说明：

1. cell 和 phy 的风险由“净空余量”映射得到，余量越小风险越大。
2. user 风险由最小占用带分离度映射得到，分离度越小风险越大。
3. smooth 风险由 seedDelta 相对阈值映射得到，seed 抖动越大风险越大。

### 7.3 关键原始几何量如何解读

日志中的原始量比 risk 值更适合解释“为什么高风险”：

1. minCellClearance：预测占用带到 cell 边界的最小净空。
2. minPhysicalClearance：预测占用带到物理房间边界的最小净空。
3. minPairSeparation 或 minOccupancySeparation：用户间最小占用带分离度。
4. seedDelta：当前帧与上一帧 seed 的位移量。

判读原则：

1. 当净空为负值时，表示预测占用已“越界”到对应边界之外，风险应接近 1。
2. 当分离度为负值时，表示预测占用带重叠，user 风险应接近 1。
3. 当 seedDelta 明显增大且持续时，smooth 风险应同步升高。

### 7.4 快速验收场景（建议按顺序）

1. 低拥挤居中行走：
预期 cell、phy、user 都偏低，dom 多为 None。

2. 向房间边界靠近：
预期 phy 先上升；若同时贴近 cell 边，cell 也上升。

3. 两用户相向或并行逼近：
预期 user 上升，连线增多且颜色从绿向红变化。

4. 快速转向或分区突变：
预期 smooth 上升，seedDelta 变大。

### 7.5 当前默认阈值（用于判断“是否符合预期”）

来自 GlobalCoordinationManager 的默认配置：

1. riskCellBoundarySafeClearance = 0.35
2. riskPhysicalBoundarySafeClearance = 0.50
3. riskPairSafeSeparation = 0.40
4. riskSeedDeltaReference = 0.25
5. riskAdjacencyThreshold = 0.35

经验判断：

1. 如果明显靠边而 phy 仍长期低于 0.2，阈值可能偏宽或几何输入异常。
2. 如果用户明显近距离交互但 user 长期低于 0.2，pairSafeSeparation 可能偏小。
3. 如果画面平稳但 smooth 长期高于 0.6，seedDeltaReference 可能偏小。

### 7.6 如何区分“符合预期”和“异常”

1. 符合预期：风险随场景变化方向正确，且和原始几何量趋势一致。
2. 可疑异常：风险曲线与几何量趋势长期背离（例如净空变小但 risk 不升）。
3. 明显异常：风险长期固定在接近 0 或接近 1，且与行为状态不匹配。


## Risk值的归一化实现

**核心方法：** PartitionRiskEvaluator.cs 和 PartitionRiskEvaluator.cs

**归一化公式：**
```
Risk = Clamp01( (SafeThreshold - ActualValue) / SafeThreshold )
```

**工作原理：**
- 当 `ActualValue >= SafeThreshold` 时，分子为负或零 → Risk = 0（安全）
- 当 `ActualValue = 0` 时，分子最大 → Risk 接近 1（危险）
- 当 `ActualValue` 在 0 到 SafeThreshold 之间时，Risk 线性插值
- `Clamp01()` 确保最终结果始终在 [0, 1]

**具体应用：**

| 风险类型 | 调用点 | 参数 |
|---------|------|------|
| **CellBoundaryRisk** | PartitionRiskEvaluator.cs | `MinCellClearance`, `CellBoundarySafeClearance` (0.35) |
| **PhysicalBoundaryRisk** | PartitionRiskEvaluator.cs | `MinPhysicalClearance`, `PhysicalBoundarySafeClearance` (0.50) |
| **UserRisk** | PartitionRiskEvaluator.cs | 取所有用户间分离度的平均 |
| **SmoothRisk** | PartitionRiskEvaluator.cs | `SeedMotionPenalty` = Clamp01(SeedDelta / SeedDeltaReference) |

---

## 主导风险类型（DominantRiskType）的变化逻辑

**实现方法：** PartitionRiskEvaluator.cs

**枚举类型（5种）：**
- `None` - 所有风险都很低
- `Wall` - 接近 cell 或物理边界
- `User` - 用户间冲突
- `Smooth` - 种子点抖动异常
- `Mixed` - 多个风险同时升高

**变化判断流程：**

```csharp
1. 找最大风险值
   maxValue = Max(wallRisk, userRisk, smoothRisk)
   
2. 若最大值 < DominantRiskNoneThreshold (0.10)
   → 返回 None
   
3. 找第二大风险值
   
4. 若 (maxValue - secondValue) <= DominantRiskMixedGap (0.08)
   → 返回 Mixed
   
5. 否则返回最大值对应的类型
```

**变化时机：**

| 情景 | 变化 |
|------|------|
| 所有风险都 < 0.10 | → **None** |
| wallRisk 最高且领先 > 0.08 | → **Wall** |
| userRisk 最高且领先 > 0.08 | → **User** |
| smoothRisk 最高且领先 > 0.08 | → **Smooth** |
| 两个或多个风险值接近（差距 ≤ 0.08） | → **Mixed** |

**实例：**
- 用户离 cell 边界还有 0.2m，但离其他用户只有 0.1m：userRisk 通常更高 → **User**
- 用户同时接近边界（risk 0.6）且有用户冲突（risk 0.55）：差距只有 0.05 < 0.08 → **Mixed**
- 用户走得很平稳，所有风险都 < 0.08 → **None**

这个设计让系统能**动态识别当前主要问题**，供后续决策使用。