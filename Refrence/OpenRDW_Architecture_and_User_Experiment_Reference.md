# OpenRDW 架构与用户实验设计参考

本文基于 `Refrence/OpenRDW/` 中的 OpenRDW 项目源码与数据目录整理，重点关注它如何组织 RDW 框架、算法模块、实验批处理、真实用户路径回放与结果导出。它可作为当前 OSP/RDW 项目设计实验框架和数据记录体系时的参考。

## 1. 项目定位

OpenRDW 不是单一 redirected walking 算法 demo，而是一个 Unity RDW 研究框架与 benchmark。它扩展了原始 RDWT，提供：

- 可配置的物理跟踪空间：矩形、梯形、三角形、十字、L/T 形、自定义文件、不同边长正方形。
- 可配置的虚拟路径：程序生成路径、文件路径、真实用户路径。
- 可替换的重定向器 `Redirector`：S2C、S2O、ZigZag、Thomas APF、Messinger APF、Dynamic APF、DeepLearning、PassiveHapticAPF、VisPoly、FixedPrimitive 等。
- 可替换的重置器 `Resetter`：Null、TwoOneTurn、APF。
- 多用户场景：每个 avatar 有自己的 movement/redirection/reset/log 状态，并可通过 Photon/SteamVR 做真实多用户行走。
- 自动仿真与真实 HMD 模式并存：同一套实验定义既可做离线 benchmark，也可接入 HMD 进行真实用户实验。
- 结果导出：summary CSV/XML、sampled metrics、路径图片、视频、截图。

核心源码位于：

- `Refrence/OpenRDW/OpenRDW/Assets/OpenRDW/Scripts/Others/GlobalConfiguration.cs`
- `Refrence/OpenRDW/OpenRDW/Assets/OpenRDW/Scripts/Others/ExperimentSetup.cs`
- `Refrence/OpenRDW/OpenRDW/Assets/OpenRDW/Scripts/Movement/MovementManager.cs`
- `Refrence/OpenRDW/OpenRDW/Assets/OpenRDW/Scripts/Redirection/RedirectionManager.cs`
- `Refrence/OpenRDW/OpenRDW/Assets/OpenRDW/Scripts/Analysis/StatisticsLogger.cs`

## 2. 顶层目录结构

`Refrence/OpenRDW/` 的关键目录如下：

```text
Refrence/OpenRDW/
  README.md
  Figures/
  Sample Command File/
    command - RealUserPath.txt
    command - Shape varied tracking space.txt
    command - Size varied tracking space.txt
  Real User Walking Paths/
    Props Searching/
    Waypoints Collection/
  OpenRDW/
    Assets/OpenRDW/
      Scenes/
      Scripts/
        Analysis/
        Avatar/
        Movement/
        Networking/
        Others/
        Redirection/
```

几个目录的作用：

- `Figures/`：论文/README/文档图片，包括架构图、路径可视化、real walking、多用户等演示。
- `Sample Command File/`：批量实验命令文件样例，是 OpenRDW 的实验配置入口。
- `Real User Walking Paths/`：真实用户实验采集到的虚拟位置序列和采样间隔，供 benchmark 回放。
- `OpenRDW/OpenRDW/Assets/OpenRDW/Scripts/`：Unity 实现主体。

## 3. 架构总览

OpenRDW 的架构可以理解为一个实验驱动的组件系统：

```text
GlobalConfiguration
  -> 生成 ExperimentSetup 列表
  -> 启动每个 trial
  -> 按优先级驱动所有 avatar 的 movement/redirection
  -> 判断 trial 结束/无效
  -> 调用 StatisticsLogger 导出结果

ExperimentSetup
  -> 描述一个 trial 的物理空间、障碍、avatar 列表
  -> 每个 AvatarInfo 记录 redirector/resetter/path/初始位姿

MovementManager
  -> 为单个 avatar 载入路径和初始状态
  -> 根据 AutoPilot/HMD/Keyboard 驱动运动
  -> 更新 waypoint、真实路径回放、avatar 可视化

RedirectionManager
  -> 为单个 avatar 管理 Redirector 与 Resetter
  -> 每步更新当前/上一帧虚拟位置、真实位置、方向、增量
  -> 决定 subtle redirection 或 overt reset

Redirector / Resetter
  -> 算法插件
  -> 通过基类注入 translation/rotation/curvature/reset
  -> 注入过程同时触发 StatisticsLogger 事件

StatisticsLogger
  -> 采样轨迹、gain、reset、距离边界等指标
  -> trial 结束时汇总
  -> 导出 CSV/XML/图片/样本指标
```

这种设计的优点是：算法、路径、空间、实验循环和日志大体分开；缺点是 `GlobalConfiguration` 仍然承担了过多职责，是典型的 Unity manager-heavy 架构。

## 4. 主要模块

### 4.1 `GlobalConfiguration`

`GlobalConfiguration` 是总控脚本，挂在场景根对象上。它负责：

- 全局参数：gain 上下限、curvature radius、reset buffer、target FPS、重复次数、avatar 数量、空间选择、路径选择、是否导出图片/视频。
- 输入模式：`MovementController.Keyboard`、`AutoPilot`、`HMD`。
- 实验来源：Inspector/UI 生成，或从 command txt 文件读取。
- 批处理：`experimentSetupsList` 表示多个命令文件，每个命令文件生成一组 `experimentSetups`。
- 运行循环：`Update()` 根据模式调用 `MakeOneStepCycle()` 或后台循环 `RunInBackstage()`。
- trial 生命周期：`StartNextExperiment()` 初始化当前 trial；`EndExperiment(endState)` 关闭算法、停止记录、导出结果、进入下一个 trial。

关键状态：

- `experimentIterator`：当前 command group / trial id。
- `experimentSetupsListIterator`：当前命令文件 id。
- `experimentInProgress`：trial 是否正在运行。
- `avatarIsWalking`：trial 是否开始行走，首次开始时触发 `statisticsLogger.BeginLogging()`。
- `readyToStart`：HMD 或 networking 模式下，用户按 `R` 后才开始。

### 4.2 `ExperimentSetup`

`ExperimentSetup` 是一个纯数据对象，代表一个 trial：

- `avatars`：每个用户的 `AvatarInfo`。
- `trackingSpaceChoice`：物理空间类型。
- `trackingSpacePoints`：物理空间多边形顶点。
- `squareWidth`：正方形空间边长。
- `obstaclePolygons`：障碍物多边形。
- `obstacleType`：预定义障碍类型。

`AvatarInfo` 记录：

- `redirector`：算法类型，例如 `MessingerAPF_Redirector`。
- `resetter`：reset 策略类型，例如 `APF_Resetter`。
- `pathSeedChoice`：路径模式，例如 `RandomTurn` 或 `RealUserPath`。
- `waypoints`：路径点。
- `samplingIntervals`：真实用户路径回放时的采样间隔。
- `waypointsFilePath` / `samplingIntervalsFilePath`。
- `initialConfiguration`：初始位置和方向。

这使得 OpenRDW 可以把“一个 trial 的空间、算法、路径、用户数”完整封装起来，并在 batch 中顺序执行。

### 4.3 `MovementManager`

每个 redirected avatar 上都有 `MovementManager`。它负责单用户运动与路径：

- `LoadData(id, AvatarInfo)`：载入当前 avatar 的 redirector/resetter/path/initial configuration。
- `GenerateTrackingSpaceMesh()`：为当前 avatar 创建物理空间、障碍和 reset buffer 可视化。
- `UpdateSimulatedWaypointIfRequired()`：根据路径模式切换 waypoint。
- `MakeOneStepMovement()`：驱动 `SimulatedWalker` 更新位置，并判断是否 invalid。
- `GetRealWaypoints()`：把记录路径平移/旋转到当前初始位姿；可按 `alignToInitialForward` 旋转到初始方向。
- `InitializeOtherAvatarRepresentations()`：为多用户场景显示其他用户与其 buffer。

真实用户路径模式 `RealUserPath` 与程序生成路径不同：

- 程序生成路径：avatar 接近当前 target waypoint 后切换到下一点。
- 真实用户路径：用 `redirectionManager.redirectionTime` 与 `samplingIntervals` 对齐，按原始采样时间推进 waypoint。

这意味着真实用户实验数据保留了原始步速、停顿和转向节奏，而不是被重新匀速化。

### 4.4 `RedirectionManager`

每个 avatar 上都有 `RedirectionManager`。它是单用户 RDW 控制核心：

- 维护虚拟位置/方向：`currPos`、`prevPos`、`currDir`、`prevDir`。
- 维护真实位置/方向：`currPosReal`、`prevPosReal`、`currDirReal`、`prevDirReal`。
- 计算增量：`deltaPos`、`deltaDir`。
- 保存当前 `Redirector` 与 `Resetter`。
- 每步执行 `MakeOneStepRedirection()`。

`MakeOneStepRedirection()` 的逻辑：

1. 更新当前用户状态。
2. 如果当前 avatar invalid 或 networking 下不是本地用户，则跳过。
3. 计算位置/朝向变化。
4. 如果 resetter 判断需要 reset，且刚结束 reset 的保护标志未生效，则进入 reset。
5. 若 `inReset`，调用 `resetter.InjectResetting()`。
6. 否则调用 `redirector.InjectRedirection()`。
7. 更新上一帧状态与身体 pose。

`RedirectionManager` 还提供字符串/枚举到算法类型的映射：

- `DecodeRedirector("s2c") -> S2CRedirector`
- `DecodeRedirector("messingerapf") -> MessingerAPF_Redirector`
- `DecodeResetter("twooneturn") -> TwoOneTurnResetter`
- `DecodeResetter("apf") -> APF_Resetter`

### 4.5 `Redirector` 基类

`Redirector` 是所有 subtle redirection 算法的基类，核心接口是：

- `InjectRedirection()`：子类实现具体算法。
- `GetPriority()`：多用户场景中可覆盖，用于改变执行顺序。
- `InjectRotation()`：围绕用户头部旋转物理世界/参考系，并记录 rotation gain。
- `InjectCurvature()`：注入 curvature gain，并记录 curvature gain。
- `InjectTranslation()`：注入 translation gain，并记录 translation gain。

OpenRDW 的算法列表包括：

- `APF_Redirector`
- `MessingerAPF_Redirector`
- `DynamicAPF_Redirector`
- `ThomasAPF_Redirector`
- `S2CRedirector`
- `S2ORedirector`
- `ZigZagRedirector`
- `DeepLearning_Redirector`
- `PassiveHapticAPF_Redirector`
- `VisPoly_Redirector`
- `FixedPrimitiveRedirector`
- `NullRedirector`

这个基类设计很适合借鉴：算法只需要决定注入多少 gain，日志由基类统一记录，避免每个算法自己写统计逻辑。

### 4.6 `Resetter` 基类

`Resetter` 是 overt reset 策略基类，核心接口是：

- `IsResetRequired()`：是否触发 reset。
- `InitializeReset()`：进入 reset 时初始化。
- `InjectResetting()`：reset 期间每步注入旋转。
- `EndReset()`：reset 结束清理。
- `SimulatedWalkerUpdate()`：AutoPilot 中 reset 时模拟用户原地转身。

默认碰撞/触发逻辑在 `Resetter.IfCollisionHappens()` 中：

- 检查真实空间位置是否接近物理边界。
- 检查是否接近障碍物边/顶点。
- 检查是否接近其他 avatar。
- 使用 `RESET_TRIGGER_BUFFER` 作为安全距离。
- 结合当前真实朝向判断是否正朝危险方向走。

两个主要 resetter：

- `TwoOneTurnResetter`：经典 2:1 turn，提示用户原地转身，整体注入约 180 度。
- `APF_Resetter`：依赖 APF redirector 的 total force，计算目标真实旋转方向和幅度，使 reset 后朝更安全方向。

## 5. 实验配置与批处理

OpenRDW 支持两种实验配置方式：

### 5.1 Inspector/UI 配置

当 `loadFromTxt = false` 时，`GenerateExperimentSetupsByUI()` 读取 Inspector 中的参数：

- avatar 数量。
- 空间类型与障碍类型。
- 每个 avatar 当前挂载的 redirector/resetter/path。
- `trialsForRepeating` 重复次数。

它会生成同一条件重复多次的 `experimentSetups`。

### 5.2 Command 文件配置

当 `loadFromTxt = true` 时，`GlobalConfiguration.Start()` 调用：

```text
UserInterfaceManager.GetCommandFilePaths()
GlobalConfiguration.GenerateExperimentSetupsByCommandFiles()
GlobalConfiguration.GenerateAllExperimentSetupsByCommand(...)
```

命令文件使用简单的 key-value 行表示实验条件。`newUser` 表示开始定义一个 avatar；`end` 表示当前 trial 配置结束并写入 `experimentSetups`。

真实用户路径样例：

```text
trackingSpaceChoice = Rectangle
obstacleType = 0
newUser
redirector = MessingerAPF
resetter = APF
pathseedChoice = RealUserPath
waypointsfilepath = E:\yaoling1997\user_virtual_positions.csv
samplingintervalsfilepath = E:\yaoling1997\sampling_intervals.csv
end
```

尺寸变化 benchmark 样例：

```text
trackingSpaceChoice = Square
squareWidth = 10
newUser
redirector = DynamicAPF
resetter = APF
pathseedChoice = 90turn
newUser
end

trackingSpaceChoice = Square
squareWidth = 20
newUser
redirector = DeepLearning
resetter = twooneturn
pathseedChoice = RandomTurn
end
```

形状变化 benchmark 样例：

```text
trackingSpaceChoice = rectangle
obstacletype = 0
newUser
redirector = s2c
resetter = twooneturn
pathseedChoice = RandomTurn
newUser
newUser
newUser
end

trackingSpaceChoice = triangle
obstacletype = 1
newUser
redirector = MessingerAPF
resetter = APF
pathseedChoice = StraightLine
newUser
newUser
newUser
end
```

注意：`AddAvatarToAvatarListWhenDealingCommand()` 会在加入用户后复制上一个 avatar 配置，所以连续写多个 `newUser` 可以快速创建多个配置相同的用户；除非后续行覆盖 redirector/resetter/path/initialConfiguration。

## 6. 运行生命周期

OpenRDW 每个 trial 的生命周期如下：

```text
Start()
  -> 生成 experimentSetups

Update()
  -> AutoPilot: MakeOneStepCycle() 或 RunInBackstage()
  -> Keyboard: MakeOneStepCycle()
  -> HMD: readyToStart 后 MakeOneStepCycle()

MakeOneStepCycle()
  -> MakeOneStepMovement()
       -> 若未开始 trial: StartNextExperiment()
       -> 若刚开始行走: statisticsLogger.BeginLogging()
       -> CustomFunctionCalledInEveryStep()
       -> 按 priority 顺序 MovementManager.MakeOneStepMovement()
  -> MakeOneStepRedirection()
       -> 按 priority 顺序 RedirectionManager.MakeOneStepRedirection()
       -> statisticsLogger.UpdateStats()
  -> 更新可视化
  -> 判断所有 avatar 是否完成
  -> 判断是否 invalid
  -> EndExperiment(0 / -1 / 1)
```

`EndExperiment(endState)` 中：

- `0`：正常完成。
- `-1`：invalid，例如 reset 过多或卡住过久。
- `1`：手动结束，用户按 `Q`。

结束时会：

- 禁用 waypoint、移除 redirector/resetter。
- 记录 passive haptics 位置/角度误差。
- 停止 logging。
- 汇总 summary statistics。
- 可选导出 sampled metrics。
- 可选导出路径图片。
- 写入 `Experiment Results/<time>/...`。
- 进入下一个 trial 或下一个 command 文件。

## 7. 用户实验结构

OpenRDW 中最值得关注的用户实验材料在：

```text
Refrence/OpenRDW/Real User Walking Paths/
  Props Searching/
  Waypoints Collection/
```

这两个目录不是 Unity 场景本身，而是已采集的真实用户虚拟行走轨迹，用于后续 benchmark 回放。

### 7.1 数据集组织

目录结构统一为：

```text
<TaskName>/
  user0/
    0/
      user_virtual_positions.csv
      sampling_intervals.csv
    1/
      user_virtual_positions.csv
      sampling_intervals.csv
  user1/
    ...
```

每个 trial 文件夹包含两个文件：

- `user_virtual_positions.csv`：二维虚拟位置序列，每行 `x, y`。
- `sampling_intervals.csv`：相邻采样点之间的时间间隔，每行一个浮点秒数。

抽样内容如下：

```text
user_virtual_positions.csv
-0.01198855, -0.1055848
-0.00975024, -0.1120655
-0.003263697, -0.1307406
...

sampling_intervals.csv
0.03333664
0.04444122
0.03333664
...
```

采样间隔大约 0.03-0.04 秒，接近 22-30 Hz，但并非完全固定，因此 OpenRDW 用 `samplingIntervals` 保留原始时间节奏。

### 7.2 两类任务

本地数据中有两类真实用户任务：

| 任务 | 用户数 | trial 数 | 单 trial 样本数范围 | 平均样本数 | 单 trial 时长范围 | 平均时长 |
|---|---:|---:|---:|---:|---:|---:|
| Props Searching | 10 | 20 | 4526-9995 | 6494.0 | 185.91-423.90 s | 269.82 s |
| Waypoints Collection | 10 | 60 | 2596-8160 | 4404.7 | 108.08-330.84 s | 179.11 s |

从目录结构可推断：

- `Props Searching`：10 名用户，每人 2 条轨迹。更像带目标搜索/道具搜索的自然行走任务，轨迹较长。
- `Waypoints Collection`：10 名用户，每人 6 条轨迹。更像按 waypoint 引导采集路径，多 trial、时长更短。

源码中没有直接包含问卷、受试者说明或伦理流程文件，因此不能从本地材料确认受试者招募、问卷量表、counterbalancing、练习阶段等实验人因细节。可确定的是：这些真实用户实验最终被抽象成“虚拟位置时间序列 + 采样间隔”，用于 RDW 算法离线回放和公平比较。

### 7.3 真实路径如何被回放

当 command 文件中设置：

```text
pathseedChoice = RealUserPath
waypointsfilepath = ...
samplingintervalsfilepath = ...
```

流程如下：

1. `GenerateWaypoints()` 读取 `user_virtual_positions.csv` 为 `waypoints`。
2. 读取 `sampling_intervals.csv` 为 `samplingIntervals`。
3. `MovementManager.LoadData()` 调用 `GetRealWaypoints()`。
4. `GetRealWaypoints()` 将记录路径平移到当前初始位置。
5. 若 `alignToInitialForward = true`，再将路径旋转到当前初始朝向。
6. `SimulatedWalker.GetPosDirAndSet()` 根据 `redirectionTime` 在相邻采样点之间线性插值。
7. 方向取当前点到下一点的方向。

这一设计有几个重要含义：

- 用户原始路径可以复用到不同物理空间和不同算法条件。
- 同一条真实用户路径可以用多个 redirector/resetter 回放，因此能减少路径差异对算法比较的影响。
- 采样时间被保留，算法面对的速度/停顿/转向节奏更接近真实人走路。
- 初始位姿可通过 `initialConfiguration` 或 tracking space 默认配置指定，从而支持不同空间中的对齐。

### 7.4 HMD 真实实验入口

OpenRDW 也支持直接真实用户实验：

- `movementController = HMD` 时，`RedirectionManager.headTransform` 使用 HMD 追踪对象。
- `Update()` 中只有 `readyToStart = true` 时才运行 `MakeOneStepCycle()`。
- 用户按 `R` 表示 ready/start。
- 用户按 `Q` 手动结束当前 trial。
- 用户按 `P` 截图。
- 用户按 `T` 切换第一/第三人称视图。
- 数字键可切换显示某个 avatar，反引号显示所有 avatar overview。

HMD 模式下 `MovementManager.LoadData()` 的初始配置来自当前 HMD 位置与方向：

```text
initialConfiguration =
  current head position in physical/virtual flattened 2D
  current head forward direction
```

这保证真实实验开始时不会强行把用户移动到预设点，而是以当前站位作为初始条件。

### 7.5 多用户实验入口

多用户支持体现在三个层面：

- `avatarNum` 最多 4，多个 avatar 并行参与一个 trial。
- 每个 avatar 独立持有 `MovementManager`、`RedirectionManager`、`Redirector`、`Resetter`。
- `CustomFunctionCalledInEveryStep()` 每步调用各 redirector 的 `GetPriority()`，再按优先级排序执行 movement/redirection。

多用户 collision/reset 也被纳入 `Resetter.IfCollisionHappens()`：

- 除了物理边界和障碍物，还检查其他 avatar 的真实位置。
- 当前用户朝向其他用户且距离小于 `RESET_TRIGGER_BUFFER` 时触发 reset。

Networking 模式下：

- `networkingMode = true` 时启用 `NetworkManager`。
- `avatarNum` 会被设为 1，本地只控制自己的 avatar。
- 远端 avatar 位置由网络同步，不由本地 redirection 逻辑重复控制。

### 7.6 任务条件如何构造

OpenRDW 的 benchmark 条件主要围绕三类变量：

1. 物理空间：
   - size varied：不同 `squareWidth`。
   - shape varied：rectangle、triangle、trapezoid、cross、L/T 等。
   - obstacleType：预定义障碍组合。
   - trackingSpaceFilePath：自定义空间。

2. 路径：
   - `_90Turn`
   - `RandomTurn`
   - `StraightLine`
   - `Sawtooth`
   - `Circle`
   - `FigureEight`
   - `FilePath`
   - `RealUserPath`

3. RDW 策略：
   - redirector：S2C/S2O/APF/deep learning/passive haptics/visibility polygon 等。
   - resetter：TwoOneTurn/APF/None。
   - gain 参数：translation/rotation/curvature 上下限。
   - reset buffer：安全边界宽度。

这一设计适合搭建公平 comparison：同一个 command 文件描述“空间 + 用户数 + 路径 + 算法”，并通过多 command 文件或多段 `end` 做批处理。

## 8. 日志与指标

`StatisticsLogger` 是 OpenRDW 实验可靠性的关键。它记录两类数据：

### 8.1 Summary statistics

每个 trial 每个 avatar 导出的 summary 包含：

- `reset_count`
- `virtual_way_distance`
- `virtual_distance_between_resets_average`
- `time_elapsed_between_resets_average`
- `sum_injected_translation(IN METERS)`
- `sum_injected_rotation_g_r(IN DEGREES)`
- `sum_injected_rotation_g_c(IN DEGREES)`
- `sum_real_distance_travelled(IN METERS)`
- `sum_virtual_distance_travelled(IN METERS)`
- `min_g_t` / `max_g_t`
- `min_g_r` / `max_g_r`
- `min_g_c` / `max_g_c`
- `g_t_average`
- `g_r_average`
- `g_c_average`
- `injected_translation_average`
- `injected_rotation_average`
- `real_position_average`
- `virtual_position_average`
- `distance_to_boundary_average`
- `distance_to_center_average`
- `experiment_duration`
- `execute_duration`
- `average_sampling_interval`
- passive haptics 模式下额外记录 `positionError`、`angleError`

summary CSV 以 trial 为单位分块，并写入：

```text
Experiment Results/<time>/Summary Statistics/<command-file-name>.csv
```

### 8.2 Sampled metrics

如果 `logSampleVariables = true`，则导出更细的 sampled metrics：

- 一维序列：
  - `distances_to_boundary`
  - `distances_to_center`
  - `g_t`
  - `injected_translations`
  - `g_r`
  - `injected_rotations_from_rotation_gain`
  - `g_c`
  - `injected_rotations_from_curvature_gain`
  - `injected_rotations`
  - `virtual_distances_between_resets`
  - `time_elapsed_between_resets`
  - `sampling_intervals`

- 二维序列：
  - `user_real_positions`
  - `user_virtual_positions`

采样频率由 `samplingFrequency` 控制，默认以 buffer 平均方式采样。

### 8.3 Invalid trial 规则

OpenRDW 会把以下情况标记为 invalid：

- reset 次数超过 `MaxResetCount = 1000`。
- AutoPilot 下长时间站在同一位置，超过 `RedirectionManager.MaxSamePosTime = 50` 秒。

invalid trial 以 `EndExperiment(-1)` 结束，summary 中 `EndState = Invalid`。

## 9. 对当前项目可借鉴的设计

### 9.1 值得直接借鉴

- 使用 `ExperimentSetup` 作为 trial 的不可变配置快照，不要让 logger 从 Inspector 临时状态拼结果。
- 每个 avatar 独立持有 movement/redirection/reset 状态，多用户时由总控排序调度。
- `Redirector` 基类统一提供 gain 注入与日志事件，算法子类只负责策略。
- `Resetter` 基类统一提供 collision/reset trigger 几何判断，具体 resetter 只负责 reset 执行。
- 支持 command txt 批处理，便于复现实验条件。
- 真实用户路径保存为 `positions + sampling_intervals`，回放时保留原始时间节奏。
- summary statistics 和 sampled metrics 分开导出，既方便表格统计，也能保留轨迹级复查能力。

### 9.2 需要谨慎改进

- `GlobalConfiguration` 职责过多，当前项目如果参考，应拆成：
  - `ExperimentRunner`
  - `ExperimentConfigParser`
  - `AvatarRegistry`
  - `TrackingSpaceService`
  - `ResultExporter`
  - `SimulationClock`
- command 文件解析目前依赖 `split[2]`，格式容错较弱；可改成 key-value parser。
- 随机种子只在 `InitRandomState()` 设置，若多条件比较要记录 seed 与路径旋转角。
- `StatisticsLogger` 的采样平均有些地方是按样本数平均，而非严格时间加权；若路径采样间隔不均匀，建议明确指标定义。
- 真实用户实验数据目录缺少 metadata，例如 participant id 映射、任务说明、条件顺序、设备、场地尺寸、问卷等。当前项目若做用户实验，应保留这些 metadata。

## 10. 用户实验参考模板

如果当前项目要参考 OpenRDW 设计真实用户实验，建议至少保留以下结构：

```text
UserExperiment/
  protocol.md
  participants.csv
  conditions.csv
  command_files/
    condition_*.txt
  raw/
    participant_00/
      task_*/trial_*/
        hmd_pose.csv
        virtual_position.csv
        sampling_intervals.csv
        events.csv
  processed/
    real_user_paths/
      <task>/user<trial>/
        user_virtual_positions.csv
        sampling_intervals.csv
  results/
    summary_statistics.csv
    sampled_metrics/
```

每个 trial 至少记录：

- participant id、trial id、condition id。
- redirector、resetter、space、path seed、seed、gain 参数。
- HMD 真实 pose、虚拟位置、采样间隔。
- reset 事件时间、类型、方向、持续时间。
- collision/near-boundary event。
- trial end state：normal、manual、invalid。
- 主观量表或问卷结果，如果用户实验关注舒适度/noticeability/sickness。

## 11. 总结

OpenRDW 的核心价值不只在算法数量，而在它把 RDW 实验拆成了可组合的几个轴：

```text
物理空间 x 路径来源 x 用户数 x redirector x resetter x 日志指标
```

用户实验方面，它最值得参考的是“真实用户路径采集后离线回放”的结构：真实实验负责采集自然路径，benchmark 负责在同一路径上公平比较不同 RDW 策略。这比每个算法都重新找用户走一次更容易控制路径差异，也更适合做大规模自动化评估。

如果当前项目要吸收这套思路，建议优先实现：

1. 清晰的 `ExperimentSetup` 配置快照。
2. 每个 avatar 独立的 state/controller/resetter/logger 绑定。
3. 命令文件或 JSON/YAML 批处理入口。
4. `positions + sampling_intervals` 格式的真实路径回放。
5. summary 与 sampled metrics 双层日志。
6. 每个 trial 的完整配置快照和 end state。
