# Phase 5: Local Safe Target Selection - 可视化指南

## 启用可视化

### 方法 1：通过Inspector面板启用

1. 在Unity编辑器中，选择GlobalCoordinationManager所在的GameObject
2. 在Inspector中找到 **Phase 5: Local Safe Target Selection** 部分
3. 勾选 **Enable Local Safe Target Visualization** 选项

### 方法 2：运行时启用

在代码中：
```csharp
globalCoordinationManager.enableLocalSafeTargetVisualization = true;
```

## 可视化元素说明

当启用可视化后，在Scene视图中会显示以下元素（仅在Gizmos启用时可见）：

### 1. 用户当前位置（绿色小球）
- **颜色**: 绿色
- **大小**: 半径 0.15m
- **含义**: 用户在物理空间（世界坐标）的当前位置

### 2. 目标点（金色/黄色球体）⭐ 主要指标
- **颜色**: 金色 (RGB: 1, 0.84, 0)
- **大小**: 半径 0.25m (比用户位置更大，易于识别)
- **含义**: Phase 5为该用户选择的最优安全目标点
- **十字标记**: 在目标点中心绘制±形标记，增强可见性

### 3. 连接线（青色箭头）
- **颜色**: 青色
- **方向**: 从用户位置指向目标点
- **箭头**: 指向目标点的方向箭头
- **含义**: 推荐的行走方向和距离

### 4. 评分信息（仅在编辑器中显示）
- **位置**: 目标点上方 0.5m 处
- **文本**: `Score: X.XX`
- **含义**: 该目标点的总体评分（越高越好）

## 使用场景

### 场景 A：调试单个用户的目标选择
1. 在Scene视图启用Gizmos
2. 在运行模式下选择GlobalCoordinationManager
3. 观察特定用户的目标点变化

### 场景 B：多用户多目标可视化
- 每个用户显示独立的目标点和连接线
- 不同用户的目标点用颜色区分（根据用户ID）
- 可观察目标点的聚集或分散模式

### 场景 C：验证目标点合理性
- 目标点应该在用户的Voronoi单元内
- 目标点应该远离单元边界（buffer内侧）
- 目标点应该避开其他用户的预测占用区域
- 目标点应在用户前向方向的扇形区域内

## Gizmos控制

### 启用Gizmos显示
在Scene视图右上角，点击 **Gizmos** 按钮，确保处于启用状态：
```
[Gizmos] 3D [Icons] ...
```

### 调整Gizmos性能
如果目标点显示过多导致性能下降，可以：
1. 减少场景中的用户数量
2. 临时禁用其他可视化（Risk、Partition等）
3. 切换到Game视图（不显示Gizmos）

## 常见问题

### Q: 为什么看不到目标点？

**A:** 检查以下几点：
1. ✅ `Enable Local Safe Target Visualization` 已勾选？
2. ✅ Scene视图中Gizmos已启用？
3. ✅ 游戏正在运行模式（Play）？
4. ✅ GlobalCoordinationManager已初始化完成？
5. ✅ 至少一个用户的 `HasValidTarget` 为 true？

### Q: 目标点位置不合理？

**A:** 检查以下参数是否合理：
- `localTargetFanHalfAngleDeg`: 前向扇形角度（默认55°）
- `localTargetSearchRadiusMax`: 搜索半径（默认2.0m）
- `localTargetBoundaryBufferMax`: 边界缓冲距离（默认0.5m）
- `localTargetWeightBoundaryDist`, `localTargetWeightOccupancyDist`: 权重系数

### Q: 如何导出可视化的截图？

**A:** 在Scene视图：
1. 调整视角和缩放以显示所有用户
2. 截图（Print Screen或Shift+F12）
3. 或使用 `Shift+F1` 在Editor中截图

## 性能影响

- **渲染开销**: 最小 (~0.1ms 每帧用于10用户场景)
- **内存开销**: 可忽略 (仅存储目标点数据)
- **Editor vs Runtime**: 
  - Editor中显示额外的文本信息 (Score)
  - Runtime中仅显示几何元素

## 进阶配置

### 自定义可视化颜色

编辑代码中的以下常量：
```csharp
// 在 DrawLocalSafeTargetGizmos() 中
Gizmos.color = new Color(1f, 0.84f, 0f, 1f); // 改为所需颜色

// 用户位置
Gizmos.color = Color.green;  // 或 Color.red, Color.blue 等

// 连接线
Gizmos.color = Color.cyan;   // 或其他颜色
```

### 调整可视化大小

```csharp
const float targetPointRadius = 0.25f;  // 目标点球体半径
// 改为想要的大小
```

## 相关文档

- [Phase 5 实现总结](PHASE5_IMPLEMENTATION_SUMMARY.md)
- [多用户RDW原理](docs/multi-user-rdw.md)
- [Voronoi分区说明](docs/voronoi-partitioning.md)

---

**更新时间**: 2026年4月12日
**版本**: Phase 5 - Local Safe Target Selection v1.0
