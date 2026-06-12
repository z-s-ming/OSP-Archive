# OSP2 文档目录

本目录包含OSP2项目的所有文档，按功能分类组织。

## 目录结构

### 📐 architecture/ (架构设计文档)
系统架构、设计方案和技术规范文档。

- **live-vr-multiplayer-user-experiment.md** - Live VR多人真人实验重构方案（核心架构文档，35KB）
- **experiment-log-system.md** - 实验日志系统设计
- **interface-map-t1.md** - 接口映射文档
- **phase3-risk-and-module-organization.md** - Phase 3：风险评估与模块组织
- **phase4-dynamic-partition-optimization.md** - Phase 4：动态分区优化
- **PHASE5_IMPLEMENTATION_SUMMARY.md** - Phase 5：实现总结
- **PHASE5_VISUALIZATION_GUIDE.md** - Phase 5：可视化指南

### 📖 guides/ (使用指南)
功能使用指南和操作说明文档。

- **LOCAL_SAFE_CURVATURE_REDIRECTOR_GUIDE.md** - 局部安全曲率重定向器使用指南
- **bidirectional-recoverability-reset.md** - 双向可恢复重置
- **proactive-reset-log-postprocess-guide.md** - 主动重置日志后处理指南
- **voronoi-boundary-proactive-reset-trigger.md** - Voronoi边界主动重置触发器

### 🐛 issues/ (问题修复与故障排查)
已知问题的诊断分析和修复方案。

- **issue-restart-lifecycle-sync-fix.md** - 用户实验重启生命周期同步问题修复方案
  - 问题：Soft Restart/Recalibrate Restart后重置无法触发、位置同步异常、增益注入失败
  - 根因：生命周期版本（restartEpoch/runId）更新时序错误
  - 状态：待修复

### 🔬 research/ (研究论文)
学术研究相关的论文、草稿和研究笔记。

- **主动重置_桌面文档修订版.md** - 主动重置研究文档（修订版）
- **主动重置论文研究思路_打磨版.md** - 论文研究思路
- **ICXR2026_主动重置论文中文工作稿.docx** - ICXR 2026会议论文工作稿
- **ICXR2026_主动重置论文中文工作稿_扩写版.docx** - 扩写版论文
- **期末论文_多人RDW主动重置仲裁方法.docx** - 期末论文：多人RDW主动重置仲裁方法

## 文档命名规范

- **英文文档**：使用小写字母和连字符（kebab-case），如 `live-vr-multiplayer-user-experiment.md`
- **中文文档**：使用下划线分隔，如 `主动重置_桌面文档修订版.md`
- **阶段性文档**：使用大写前缀，如 `PHASE5_IMPLEMENTATION_SUMMARY.md`
- **问题文档**：使用 `issue-` 前缀，如 `issue-restart-lifecycle-sync-fix.md`

## 快速导航

### 新手入门
1. 先阅读 [Live VR多人实验架构](architecture/live-vr-multiplayer-user-experiment.md) 了解系统整体设计
2. 查看 [实验日志系统](architecture/experiment-log-system.md) 了解数据记录机制
3. 根据需要参考 [guides/](guides/) 目录下的使用指南

### 问题排查
1. 查看 [issues/](issues/) 目录下是否有相关问题的修复方案
2. 参考架构文档理解系统设计原理
3. 查看日志和调试信息定位问题

### 研究开发
1. 参考 [research/](research/) 目录下的论文和研究笔记
2. 查看各Phase文档了解系统演进历史
3. 参考架构文档进行新功能设计

## 文档维护

- **添加新文档**：根据文档类型放入相应子目录
- **更新README**：添加新文档后记得更新本README的文件列表
- **问题文档**：解决问题后在文档开头标注解决状态和日期
- **过时文档**：不要删除，在文档开头标注"已过时"并说明替代文档

## 相关目录

- `../Assets/RDW/02 Script/` - 源代码
- `../Assets/StreamingAssets/` - 运行时资源
- `../Experiment Results/` - 实验数据输出

---

最后更新：2026-06-10
