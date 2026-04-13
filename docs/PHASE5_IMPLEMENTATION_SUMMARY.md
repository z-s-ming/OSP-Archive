# Phase 5: Local Safe Target Selection 说明文档

## 1. 模块目标
Phase 5 的目标是：在每一帧为每个用户从其 Voronoi 分区内部选出一个“局部安全目标点”，并将该点注入重定向器，使用户更平滑地朝安全、可行方向移动。

核心特点：
- 在用户所属 cell 内选点，不跨区。
- 考虑前向可行性（扇区）、边界安全距离、与他人占据带距离。
- 采样密度随区域面积自适应，兼顾质量与开销。
- 失败时有确定性回退目标，保证系统连续运行。

## 2. 代码位置
- `Assets/RDW/02 Script/GlobalCoordination/LocalSafeTarget/LocalSafeTargetSelector.cs`
- `Assets/RDW/02 Script/GlobalCoordination/LocalSafeTarget/LocalSafeTargetTypes.cs`
- `Assets/RDW/02 Script/GlobalCoordination/LocalSafeTarget/LocalSafeTargetVisualizer.cs`
- `Assets/RDW/02 Script/GlobalCoordination/LocalSafeTarget/LocalSafeTargetLogger.cs`
- `Assets/RDW/02 Script/GlobalCoordination/GlobalCoordinationManager.cs`
- `Assets/RDW/02 Script/RDW_Scripts/Redirector/S2CRedirector.cs`

## 3. 类职责与接口

### 3.1 LocalSafeTargetSelector
职责：执行“候选区域构建 -> 采样 -> 打分 -> 选优 -> 回退”的完整逻辑。

主要公开接口：
```csharp
public LocalTargetResult SelectTargetForUser(
    int userId,
    Vector2 userPosition,
    Vector2 userHeading,
    List<Vector2> cellVertices,
    Vector2 cellCentroid,
    List<Vector2> allUserPositions,
    List<PredictedOccupancyBand> occupancyBands,
    PartitionRiskFrame riskFrame)
```

输入：
- `userId`: 用户索引。
- `userPosition`: 用户当前平面位置（x,z）。
- `userHeading`: 用户前向单位向量（x,z）。
- `cellVertices`: 该用户 Voronoi 区域顶点。
- `cellCentroid`: 区域质心（当前实现未直接用于评分）。
- `allUserPositions`: 所有用户位置（兼容参数，当前实现未直接用于评分）。
- `occupancyBands`: 预测占据带列表（用于与他人距离项）。
- `riskFrame`: 风险帧（当前仅提取对应用户指标，未直接参与打分）。

输出：
- `LocalTargetResult.targetPoint`: 最终目标点。
- `LocalTargetResult.totalScore`: 最优样本分数。
- `LocalTargetResult.sampleCount`: 实际候选样本数。
- `LocalTargetResult.fallbackUsed`: 是否走回退逻辑。

补充接口：
```csharp
public LocalSafeTargetResult SelectTarget(...)
public void UpdateConfig(LocalSafeTargetConfig newConfig)
```

### 3.2 LocalSafeTargetTypes
职责：定义配置与结果数据结构。

结构体：
- `LocalSafeTargetConfig`: 几何参数、采样参数、权重参数。
- `LocalSafeTargetResult`: 算法内部结果（含组件字段）。
- `LocalTargetResult`: 对外帧级结果，含兼容属性：`HasValidTarget`、`TargetPosition`、`BestScore`。

默认参数（`GetDefaults`）：
- 扇区半角：55°
- 搜索半径：1.5m ~ 2.0m
- 边界内缩：0.3m ~ 0.5m
- 权重：`η1=1.0`, `η2=1.5`, `η3=0.4`
- 采样密度：`ρ=55 pts/m²`
- 采样上下界：`24 ~ 120`

### 3.3 LocalSafeTargetVisualizer
职责：场景中绘制目标点、用户点、连线箭头、cell 轮廓。

主要接口：
```csharp
public void Initialize(StateCollector collector)
public void UpdateTargets(Dictionary<int, LocalTargetResult> targets)
public void UpdateCellVertices(Dictionary<int, List<Vector2>> cellVertices)
public void Draw()
```

输入：
- 最新目标字典。
- 用户 cell 顶点缓存。

输出：
- Unity Gizmos 可视化（仅显示，不改变算法结果）。

### 3.4 LocalSafeTargetLogger
职责：按帧写入 CSV，便于离线分析。

主要接口：
```csharp
public LocalSafeTargetLogger(string experimentFolder, string filename = "local_safe_targets.csv")
public void LogFrame(int frameIndex, Dictionary<int, LocalTargetResult> targets, float timestamp)
public void Flush()
public void Clear()
```

CSV 字段：
- `Frame,UserId,TargetX,TargetY,TotalScore,SampleCount,FallbackUsed,Timestamp`

### 3.5 GlobalCoordinationManager（Phase 5 集成）
职责：在主流程中调用选点，并将结果下发给重定向器。

关键方法：
- `InitializeLocalSafeTargetSelection()`
- `TrySelectLocalSafeTargets(FrameState, PartitionResult, PartitionRiskFrame, PredictedOccupancyFrame)`
- `ApplyLocalTargetsToRedirectors()`

调用时机：`ProcessStep()` 内，在分区与风险更新后执行。

### 3.6 S2CRedirector（目标注入端）
职责：优先使用外部目标点；无外部目标时退回原始 S2C 逻辑。

新增接口：
```csharp
public void SetExternalSafeTarget(Vector2 target)
public void ClearExternalSafeTarget()
```

行为优先级：
1. `externalSafeTarget` 有值 -> 直接用该值作为 steering target。
2. 否则 -> 执行 S2C 原中心点导向策略。

## 4. 算法流程
每个用户每帧执行一次：

1. 几何检查：`cellVertices.Count < 3` 时直接回退。
2. 构建候选区域：`Inset(cell) + ForwardFan` 约束。
3. 计算候选区域面积并自适应采样。
4. 对每个候选点计算总分。
5. 选择最高分样本作为目标点。
6. 若无有效样本，执行回退目标。

## 5. 计算公式

### 5.1 候选区域
理想表达：
$$
\Omega_i = C_i^{\text{inset}} \cap \text{Fan}(p_i, h_i, \theta, R)
$$

说明：当前实现是近似求交，不是严格多边形布尔求交。实现方式为“保留内缩多边形中位于扇区内的顶点；若为空则退回整个内缩多边形”。

### 5.2 采样数
$$
n = \text{clamp}(\rho \cdot A_{\text{eff}},\ n_{\min},\ n_{\max})
$$

其中：
- $A_{\text{eff}}$ 为候选区域面积。
- $\rho = 55$。
- $n_{\min}=24,\ n_{\max}=120$。

网格步长：
$$
\Delta = \sqrt{\frac{A_{\text{eff}}}{n}}
$$

### 5.3 目标点评分
$$
	ext{LocalScore}(q) = \eta_1\,d_{\text{boundary}}(q) + \eta_2\,d_{\text{occ}}(q) - \eta_3\,d_{\text{user}}(q)
$$

定义：
$$
d_{\text{boundary}}(q)=\min_{e\in \partial C_i}\text{dist}(q,e)
$$
$$
d_{\text{occ}}(q)=\min_{j\neq i}\text{dist}(q,O_j)
$$
$$
d_{\text{user}}(q)=\|q-p_i\|
$$

默认权重：
- $\eta_1=1.0$
- $\eta_2=1.5$
- $\eta_3=0.4$

### 5.4 点到线段距离
$$
t = \text{clamp}\left(\frac{(p-a)\cdot(b-a)}{\|b-a\|^2},\ 0,\ 1\right)
$$
$$
p_{\text{closest}} = a + t(b-a),\quad d=\|p-p_{\text{closest}}\|
$$

### 5.5 占据带距离修正
单段胶囊模型：
$$
d_{\text{band-seg}} = \max\left(0,\ d_{\text{point-seg}} - \frac{r_s+r_e}{2}\right)
$$

对同一用户占据带所有段取最小值。

### 5.6 多边形面积
Shoelace 公式：
$$
A = \frac{1}{2}\left|\sum_{k=1}^{m}(x_k y_{k+1} - x_{k+1} y_k)\right|
$$

### 5.7 回退目标
$$
q_{\text{fallback}} = p_i + \hat{h}_i \cdot 1.0
$$

## 6. 输入输出汇总

### 6.1 算法输入（单用户）
- 用户位置与朝向。
- 用户对应 Voronoi cell 顶点。
- 其他用户预测占据带。
- 可选风险帧与上下文参数。

### 6.2 算法输出（单用户）
- 最终目标点 `Vector2`。
- 目标点分数 `float`。
- 候选样本数 `int`。
- 是否回退 `bool`。

### 6.3 系统级输出
- 下发到 `S2CRedirector` 的 `externalSafeTarget`。
- 可视化渲染数据（Gizmos）。
- CSV 帧级日志。

## 7. 时序与数据流
```text
ProcessStep
  -> Build partition
  -> Evaluate risk / occupancy
  -> TrySelectLocalSafeTargets
     -> LocalSafeTargetSelector.SelectTargetForUser (per user)
     -> latestLocalTargets[userId] = result
  -> ApplyLocalTargetsToRedirectors
     -> S2CRedirector.SetExternalSafeTarget
  -> RDW simulation tick
```

## 8. 复杂度与工程注意事项
- 粗略复杂度：`O(U * N * (E + B))`
  - `U`: 用户数
  - `N`: 每用户样本数
  - `E`: cell 边数
  - `B`: 其他用户占据带段数
- 当前候选区域求交为近似方案，极端几何情况下可能引入额外无效采样。
- `LocalSafeTargetResult` 的分项字段目前未在最终结果中完整回填，可作为后续分析增强点。

## 9. 可配置参数建议
- 多用户拥挤场景：提高 `WeightOccupancyDist`，适当提高 `MinSamplesPerUser`。
- 边界贴行问题：提高 `BoundaryBufferMax` 或 `WeightBoundaryDist`。
- 目标跳动明显：适度降低 `SearchRadiusMax`，并结合上层做时间平滑。
- 算力受限：降低 `SampleDensityPerM2` 或收紧 `MaxSamplesPerUser`。

## 10. 验证清单
- [ ] 开启/关闭 `enableLocalSafeTargetSelection`，确认行为可切换。
- [ ] 检查目标点是否始终落在用户有效活动区域附近。
- [ ] 检查 `fallbackUsed` 比例是否异常升高。
- [ ] 检查 CSV 记录与可视化显示是否一致。
- [ ] 多用户场景下确认目标点不会系统性趋同到同一区域。

---

状态：Phase 5 已集成并可运行。  
文档更新时间：2026-04-12
