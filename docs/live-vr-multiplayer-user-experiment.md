# OSP Live VR 多人真人实验重构方案

## 0. 当前实现状态快照（2026-06-04）

本节用于标记当前代码相对于本文方案的落地进度。标记含义：

- `[x]` 已完成并已通过 Unity Roslyn 脚本编译检查。
- `[~]` 部分完成，主链路已实现，但仍缺少完整边界、日志或多人实验验证。
- `[ ]` 尚未实现，仍停留在方案设计层。

### 0.1 已完成

- `[x]` 保留 `LiveVRNetworkManager` 作为 Unity 场景兼容 facade，现有 Host/RDW/Client 组件仍通过它调用网络能力。
- `[x]` 新增 `LiveVRTransport`，集中 Host UDP bind 与 UDP send 基础能力。
- `[x]` 新增 `LiveVRReliableControlService`，实现 `messageId + CONTROL_ACK + retry + timeout` 的可靠控制消息框架。
- `[x]` 新增 `LiveVRProtocolVersionContext`，维护 `hostRunId/runId/trialId/configVersion/restartEpoch/calibrationVersion/assignmentVersion`。
- `[x]` 新增新版 `CONTROL|...|payload` envelope、`CONTROL_ACK`、`CLIENT_RESET_CLEAR`、`LiveVRTrialEndState`、`LiveVRUserConnectionState` 基础类型。
- `[x]` `RESET_START`、`RESET_DONE`、`RESET_END`、`STATE`、`CALIBRATE_CENTER`、`CLEAR_CALIBRATION`、`CLIENT_RESET_CLEAR` 已接入可靠控制服务。
- `[x]` 新增 `LiveVRClientPresentationState.ClearForRestart(restartEpoch, reason)`，统一清理 Client reset presentation。
- `[x]` Client 侧已增加幂等清理方法：`ClearLocalResetState()`、HUD/WorldHUD `ClearResetPrompt()`、TargetGuide `ClearResetSuppression()`。
- `[x]` `CLIENT_RESET_CLEAR` 会清理 Client reset prompt、reset start、local reset injection、HUD、WorldHUD、TargetGuide suppression、旧 `VIRTUAL_POSE`。
- `[x]` Soft Restart/Recalibrate Restart 已改为显式 `restartEpoch++`，并广播 `CLIENT_RESET_CLEAR`。
- `[x]` Soft Restart/Recalibrate Restart 已修正 runId 顺序：先生成并设置新 `hostRunId/runId`，再广播 `CLIENT_RESET_CLEAR`，避免 Client/Host 版本分裂。
- `[x]` Client 接收更高 `restartEpoch` 的控制消息时允许推进到新 `hostRunId/runId`。
- `[x]` Client PlayerPrefs 持久化口径已收紧为只保存 Host IP/Port；旧的 userId、expectedUserCount、proactiveReset 偏好会在启动/保存连接时清除。
- `[x]` reset timeout 已从“总时长限制”改为“no-progress watchdog”，避免首次 VR 用户慢速转身被误判失败。
- `[x]` no-progress watchdog 已加入抖动过滤：`resetMeaningfulProgressThreshold` 与 `resetMeaningfulTurnThresholdDegrees`。
- `[x]` reset active 期间 Client 本地 reset injection 不再被 `Apply Client Virtual Pose Only While Running` 阻断。
- `[x]` Client reset injection 已从“累计 yaw delta”改为“当前物理 yaw 相对 reset 起点的绝对映射”，降低抖动、反向转身、起点帧差导致的漂移。
- `[x]` 真人 reset 完成裁判已收回 Host：Host 根据 calibrated pose 的 yaw/position/safety stable 判断提交，并发送 `RESET_END`；Client 不再凭本地累计角度主动决定正式完成。
- `[x]` Soft Restart 后 Host 侧会清 `LiveVRHmdMovementController` previous pose cache，避免下一帧产生巨大 delta。
- `[x]` Soft Restart 后 GCM/StateCollector/Voronoi 对 destroyed GameObject 做了防御，避免旧 episode 用户对象导致 `MissingReferenceException` 刷屏。
- `[x]` `LiveRdwWalkingStepper` 已接入 live walking 链路：HMD physical delta 驱动 `virtualUser`，并调用现有 Redirector/GainRedirector/ARC/OSP 方法计算重定向增益。

### 0.2 部分完成

- `[~]` 协议版本过滤已覆盖可靠控制消息主链路，但 `POSE`、`VIRTUAL_POSE` 尚未完整携带并过滤所有 `runId/trialId/configVersion/restartEpoch/calibrationVersion` 字段。
- `[~]` Reliable control 有 retry/timeout 和 ACK，但 Host 还没有完整的“required users ACK recovery gate” UI 与失败状态展示。
- `[~]` Soft Restart 会根据 pending `CLIENT_RESET_CLEAR` 留在 `WaitingForUsers`，但 ACK 全部返回后自动推进 `Ready` 的恢复服务尚未完整拆出。
- `[~]` Recalibrate Restart 已清 Host calibration 并发送 `CLEAR_CALIBRATION`，但完整 `calibrationVersion` 与 `POSE` 过滤闭环仍未完成。
- `[~]` Stale/reconnect 的 pose cache 清理已做一部分，尚未完整实现 `ConnectedFresh / ConnectedStale / Disconnected / Reconnecting / NeedsPoseReanchor` 状态机。
- `[~]` Running 中 structural config change 的拒绝策略只在方案中明确，代码中尚未形成统一 `LiveVRExperimentConfigService` 锁。
- `[~]` `LiveVRNetworkLogger` 已保留，部分 restart/control 信息可从日志观察，但 trial end state、retry count、dropped stale count、restartEpoch 尚未完整结构化写入实验输出。
- `[~]` Client reset injection 已实现 360 度补偿式视角注入与 Host 权威完成，但仍需要单人、多设备、丢包、Soft Restart 后 reset 的系统化实测。
- `[~]` Client reset injection 的 yaw 映射口径已明确，但 XR Origin 的 Y 轴锁定/恢复仍需落实，避免 Soft Restart 或 reset 后视角高度被上一轮状态污染。
- `[~]` GCM destroyed object 防御已补，但 Soft Restart 后所有 GCM temporal state、prediction/risk/partition/proactive logs 的完整重置仍需继续审计。

### 0.3 尚未完成

- `[ ]` `LiveVRExperimentConfig` / `LiveVRTrialConfig` / `LiveVRExperimentManifest` 尚未落地，正式 trial 配置快照仍未完整实现。
- `[ ]` `LiveVRHostDiscoveryService`、`LiveVRClientAssignmentService`、`LiveVRPoseStream` 尚未从 `LiveVRNetworkManager` 完整拆出。
- `[ ]` `LiveVRExperimentConfigService` 尚未实现，Running 中配置修改锁还没有统一入口。
- `[ ]` `LiveVRResetArbitrationService` 尚未实现，多人同时 reset 的安全仲裁仍未完成。
- `[ ]` 多人 reset 冲突、双人同时转身、user-user safety stop 尚未形成正式闭环。
- `[ ]` 完整 reconnect recovery：断线后 endpoint 更新、重发 `STATE + CLIENT_RESET_CLEAR + config snapshot`、第一帧 reanchor 后恢复 delta，尚未全部完成。
- `[ ]` 防火墙/Host discovery preflight 只完成基础 discovery 与 bind，尚未提供完整 UI 提示、本机 IPv4 列表和端口占用可视化。
- `[ ]` 正式实验 manifest、summary metrics、sampled metrics、control logs、calibration/restart events 的对应关系尚未完整实现。
- `[ ]` Unity batchmode Editor 编译与真实 Quest 多机集成测试尚未完成；当前仅通过 Unity Roslyn C# 脚本编译检查。

### 0.4 当前需要重点回归测试

- `[ ]` Soft Restart -> Start Run 后 Host 与所有 Client 均进入 `Running`。
- `[ ]` Soft Restart 后第一次 reset：Client 出现 `Client reset injection start...`，并产生额外 root yaw 注入。
- `[ ]` Soft Restart 后 GCM 不再出现 destroyed `GameObject` 的 `MissingReferenceException`。
- `[ ]` reset 慢速转身时，只要进度持续增加，不触发 no-progress timeout。
- `[ ]` reset 停止不动超过 watchdog 时间时，进入 `InvalidResetTimeout` 并可靠清 Client UI/injection。
- `[ ]` 丢一次 `RESET_END` 或 `CLIENT_RESET_CLEAR` 时，可靠控制重发后 Client 最终恢复。
- `[ ]` Host/Client runId/restartEpoch 在 Soft Restart/Recalibrate Restart 后一致。
- `[ ]` Soft Restart 或 reset 后 `Client XR Origin` 的 Y 保持初始基准，HMD Camera 不出现地下/悬空视角。

本文档定义 OSP 中 Live VR 多人真人实验的目标架构。它参考 `Refrence/OpenRDW/` 的实验框架思想，但不复制 OpenRDW 的类结构。

核心目标：

```text
一台 Windows Host PC 负责 RDW/OSP 权威计算。
多台 standalone HMD 只负责 HMD pose 上传、虚拟视角显示、reset 引导和本地 HUD。
```

本文档只确定方案和边界，不实现代码。

## 1. 设计原则

OpenRDW 最值得借鉴的是实验系统的组织方式：

- 一个 trial 必须有完整配置快照。
- 每个 avatar/user 独立持有 movement、redirection、reset、logging 状态。
- `Redirector` / `Resetter` 是可替换模块。
- 真实 HMD、真实路径回放、自动仿真都应通过统一 movement/redirection 语义进入 RDW。
- summary metrics 和 sampled metrics 分开记录。
- trial end state 必须可解释，不能把异常都算作正常完成。

OSP LiveVR 的原则：

- Host 是唯一 RDW/OSP 权威。
- Client 是 sensor/display，不计算多人 RDW、OSP、Voronoi、碰撞或 reset 决策。
- LiveVR 是 adapter，不 fork 一套新的 RDW/OSP Core。
- 仿真实验和真人实验共享 `RedirectedUnit`、`Redirector`、GCM/OSP、核心 reset decision 和核心统计语义。
- Client 本地目标、HUD、环境加载不进入 Host RDW 权威。
- 所有正式 trial 必须能用 manifest + sampled metrics 复盘。

## 2. 总体架构

```text
Client HMD
  -> HMD local pose
  -> calibration
  -> POSE stream

Host Network
  -> discovery / assignment
  -> reliable control messages
  -> pose freshness / reconnect

Host LiveVR Adapter
  -> physical pose -> realUser
  -> physical delta -> shared RDW/OSP Core
  -> reset execution coordination

Shared RDW/OSP Core
  -> RedirectedUnit
  -> Redirector / GainRedirector / ARC / OSP / APF
  -> GlobalCoordinationManager
  -> reset decision / collision / metrics

Host Broadcaster
  -> VIRTUAL_POSE stream
  -> RESET / STATE / CONFIG control messages

Client Presentation
  -> XR Origin mapping
  -> reset UX
  -> HUD / target guide / local environment

Logger
  -> run manifest
  -> summary metrics
  -> sampled metrics
  -> network/control logs
```

## 3. 模块职责

### 3.1 Shared RDW/OSP Core

保留并复用：

- `RDWSimulationManager`
- `RedirectedUnit`
- `Redirector` / `GainRedirector` / ARC / OSP / APF 方法
- `GlobalCoordinationManager`
- 原 reset decision、collision、risk、metrics
- 原核心统计指标，例如 reset count、inter-reset distance、collision、distance、duration

禁止：

- 为 LiveVR 重写一套 redirector。
- 让 Client 计算多人 RDW。
- 让 LiveVR reset coordinator 接管所有 reset decision。
- 因真人输入不同而改变核心指标含义。

### 3.2 LiveVR Host Adapter

负责：

- 接收并验证 HMD physical pose。
- 把 pose 写入对应 `RedirectedUnit.realUser`。
- 用 physical delta 推进 `virtualUser`，并调用共享 RDW/OSP Core。
- 处理真人 reset 执行闭环。
- 管理 trial/run/restart/calibration 状态。
- 写 live manifest、network log、control log。

不负责：

- 复制 RDW 算法。
- 直接处理 Client UI 细节。
- 让仿真 movement 链路污染 live pose 链路。

### 3.3 Client Presentation

负责：

- 上传 HMD pose。
- 接收 Host 的 `VIRTUAL_POSE`。
- 移动 XR Origin，使 HMD camera 对齐 Host virtual pose。
- 显示 reset prompt、HUD、目标引导和本地环境。
- 执行 `CLIENT_RESET_CLEAR`，清理所有本地 reset 显示状态。

不负责：

- 判断用户间碰撞。
- 判断是否需要 reset。
- 计算 gain。
- 把 XR Origin 位置回传给 Host 当 physical pose。

## 4. 建议目录结构

```text
Assets/RDW/02 Script/LiveVRMultiplayer/
  Core/
    LiveVRExperimentConfig.cs
    LiveVRTrialConfig.cs
    LiveVRProtocolVersionContext.cs
    LiveVRPoseSample.cs
    LiveVRUserSource.cs

  Network/
    LiveVRTransport.cs
    LiveVRNetworkMessages.cs
    LiveVRReliableControlService.cs
    LiveVRHostDiscoveryService.cs
    LiveVRClientAssignmentService.cs
    LiveVRPoseStream.cs

  Host/
    LiveVRHostExperimentController.cs
    LiveVRExperimentConfigService.cs
    LiveVRNetworkStatusOverlay.cs

  RDW/
    LiveVRHmdMovementController.cs
    LiveRdwWalkingStepper.cs
    LiveVRResetCoordinator.cs
    LiveVRResetArbitrationService.cs
    LiveVRVirtualPoseBroadcaster.cs

  Client/
    LiveVRClientPresentationState.cs
    LiveVRClientVirtualViewBinder.cs
    LiveVRClientHud.cs
    LiveVRClientWorldHud.cs
    LiveVRClientTargetGuide.cs
    LiveVRClientEnvironmentLoader.cs

  Space/
    LiveSpaceProfile.cs
    LiveSpaceProfileProvider.cs
    LiveVRCalibrationSession.cs

  Logging/
    LiveVRExperimentLogger.cs
    LiveVRNetworkLogger.cs
    LiveVRControlMessageLogger.cs
```

不要求一次性拆完。重构时优先把职责边界稳定下来，再逐步把现有 `LiveVRNetworkManager` 中的职责拆出。

## 5. 关键状态与版本

以下状态必须显式区分：

| 状态 | 所在端 | 含义 |
| --- | --- | --- |
| HMD local pose | Client | 头显本地 tracking space 中的原始 pose |
| calibrated physical pose | Host | 映射到统一 Live Physical Space 的真实 pose |
| `realUser` | Host/RDW Core | RDW Core 中的真实用户镜像 |
| `virtualUser` | Host/RDW Core | RDW Core 维护的虚拟用户状态 |
| XR Origin | Client | 用于让 HMD camera 对齐 Host virtual pose 的显示根节点 |
| ResetPlan | Host | reset event、方向、冻结虚拟位姿、验证规则 |
| reset presentation state | Client | reset UI、view injection、HUD、target suppression |

所有会改变状态的消息必须带版本上下文：

```text
hostRunId
runId
trialId
configVersion
restartEpoch
calibrationVersion
assignmentVersion
```

Client 接收控制消息的基本规则：

```text
if hostRunId != selectedHostRunId: drop
if restartEpoch < activeRestartEpoch: drop
if trial-bound and trialId != activeTrialId: drop
if config-bound and configVersion != activeConfigVersion: drop or cache as pending
if calibration-bound and calibrationVersion != activeCalibrationVersion: drop
```

## 6. 网络拓扑与端口

正式实验推荐：

```text
Windows Host PC
  - UDP listen 0.0.0.0:47770
  - 运行 RDW/OSP Core
  - 接收所有 HMD pose

HMD Client 0..N
  - standalone Android XR app
  - 使用系统分配的本地临时 UDP 端口
  - 发送到 hostIp:47770
```

多台 HMD 同时发往同一个 Host port 不会端口冲突。Host 用 `clientIp:clientSourcePort` 区分当前 endpoint，用 `deviceKey -> userId` 维护稳定身份。

推荐身份表：

```text
deviceKey -> userId
userId -> latestEndpoint
endpoint -> transientConnectionInfo
```

端口冲突只在这些场景出现：

- 同一台 Host PC 启动两个 Host，都绑定 `47770`。
- 同一局域网存在多个 Host，Client 发现并连接到错误 Host。
- Client 固定本地端口并运行多个实例。
- Wi-Fi 重连导致 Client source port 变化，Host 仍给旧 endpoint 发包。

Host 启动时必须做 preflight：

```text
try bind 0.0.0.0:hostPosePort
if bind failed: show "port is already in use"
show local IPv4 list
show selected listen port
show discovered clients and lastSeenMs
```

自动发现不能绕过 Windows 防火墙。正式实验建议使用固定 PC Build exe，并一次性配置入站 UDP 防火墙规则。

## 7. 消息协议

### 7.1 可丢弃流

`POSE` 和 `VIRTUAL_POSE` 是高频流，允许丢包，但必须带 sequence 和版本。

`POSE`：

```text
userId
sequence
clientUnixMs
calibrationVersion
experimentPosition.x
experimentPosition.y
yawDegrees
heightMeters
isCalibrated
```

`VIRTUAL_POSE`：

```text
userId
sequence
runId
trialId
restartEpoch
hostUnixMs
virtualPosition.x
virtualPosition.y
virtualYawDegrees
```

### 7.2 可靠控制消息

以下消息必须通过 `LiveVRReliableControlService` 发送，带 ACK、retry、timeout：

| 消息 | 方向 | 作用 |
| --- | --- | --- |
| `ASSIGN` | Host -> Client | 分配 userId |
| `TRIAL_CONFIG` | Host -> Client | 下发 trial 配置 |
| `CONFIG_ACK` | Client -> Host | 确认配置可用 |
| `CONFIG_APPLY` | Host -> Client | 激活配置版本 |
| `CALIBRATE_CENTER` | Host -> Client | 触发中心/朝向校准 |
| `CLEAR_CALIBRATION` | Host -> Client | 清除校准 |
| `RESET_START` | Host -> Client | 开始 reset |
| `RESET_DONE` | Client -> Host | Client 完成物理转身 |
| `RESET_END` | Host -> Client | Host 提交或取消该 reset |
| `CLIENT_RESET_CLEAR` | Host -> Client | restart/recovery 强制清本地 reset 显示 |
| `STATE` | Host -> Client | 同步实验状态 |

可靠消息统一字段：

```text
messageId
messageType
targetUserId or allUsers
hostRunId
runId
trialId
configVersion
restartEpoch
calibrationVersion
hostUnixMs
ackRequired
```

ACK：

```text
CONTROL_ACK
messageId
messageType
userId
accepted
reason optional
clientUnixMs
```

失败策略：

- `TRIAL_CONFIG / CONFIG_APPLY` 超时：保持 `WaitingForUsers`。
- `RESET_START` 超时：pause 或 `InvalidNetworkLost`。
- `RESET_END / CLIENT_RESET_CLEAR` 超时：重试；超过 recovery timeout 后提示实验员，并标记 trial invalid。

## 8. 实验配置

`LiveVRExperimentConfig` 是 run/trial 的配置快照，不从临时 Inspector 状态拼日志。

```text
LiveVRExperimentConfig
  runId
  configVersion
  expectedUserCount
  physicalSpaceProfileId
  physicalSpaceVersion
  physicalSpacePolygon
  virtualSceneId
  clientVisualEnvironmentId
  conditionId
  methodName
  redirectorType
  resetPolicy
  gainParameters
  resetParameters
  userSources[]
  loggerSettings

LiveVRTrialConfig
  trialId
  trialSeed
  targetSequenceSeed
  initialPhysicalPoseByUser
  initialVirtualPoseByUser
  maxDuration
  stopCondition
```

配置分两类：

| 类型 | 字段 | Running 中是否允许改变 |
| --- | --- | --- |
| Structural | redirectorType、methodName、physicalSpaceProfile、resetPolicy、gainParameters、expectedUserCount | 不允许 |
| PresentationSafe | HUD、debug overlay、非权威目标视觉样式 | 可谨慎允许 |

规则：

- `WaitingForUsers` 和 `Ready` 可改配置。
- `Running` 中 structural change 必须拒绝。
- 如必须切换条件，先 `EndTrial(ManualStop)`，再生成新的 `trialId/configVersion`。
- `TRIAL_CONFIG` 未被 required users 全员 ACK，Host 不能进入 `Running`。

### 8.1 目标生成与用户完成条件

目标生成和实验完成是两个不同口径，不能混在一个配置里。

目标生成由当前 episode 类型决定：

- `Random`：每个目标相对当前虚拟位置生成，距离通常为 4-8m，并限制在 `walkingArea` 可行走区域内。
- `LongWalk` / `NaturalTouring` / `PreDefined` / wandering 类 episode：由对应 episode 语义决定目标距离、方向或预定义序列。
- `walkingArea` 是虚拟可行走区域约束，不等于单个目标必须离用户几十米。

单个 Client 是否完成自己的实验距离，由 `GlobalCoordinationManager.TargetDistancePerUser` 控制。该值表示该用户累计完成的虚拟目标段距离，而不是物理行走距离，也不是单个目标距离。

```text
on target reached:
  segmentDistance = distance(currentVirtualUserPosition, reachedTargetPosition)
  cumulativeTargetDistance += segmentDistance
  if cumulativeTargetDistance >= TargetDistancePerUser:
      Client marks local run complete
      Client hides target and shows waiting/completed prompt
      Client sends TARGET_REACHED(..., runComplete=true)
```

Host 结束整个 trial 的条件：

```text
if all required live users have runComplete=true:
  EndTrial(Normal)
else:
  completed users wait
  unfinished users continue walking
```

如果没有 `GlobalCoordinationManager` 或没有距离目标配置，才允许 fallback 到 episode target count。正式真人实验应优先使用 `TargetDistancePerUser`，并把该值写入 manifest。

## 9. userId 与设备分配

Client 首次启动：

```text
read or create persistent deviceKey
DISCOVER_HOST
HELLO(deviceKey, deviceName)
wait ASSIGN(userId)
```

Host 分配：

```text
if deviceKey already known:
  reuse existing userId and update latestEndpoint
else:
  assign smallest free userId
```

Soft Restart：

- 保留 `deviceKey -> userId`。
- 保留 connected endpoint，但刷新 lastSeen。
- 不清 assignment table。

只有明确执行 `Clear Client Assignment` 时才清空分配。

## 10. 校准与物理空间

所有用户必须映射到同一个 Host physical coordinate system。

`LiveVRCalibrationSession`：

```text
calibrationVersion
physicalSpaceVersion
userId
deviceKey
hmdLocalOriginPose
hostPhysicalOriginPose
trackingOriginMode
floorHeightMeters
calibratedUnixMs
```

规则：

- `physicalSpaceVersion` 改变必须 `Recalibrate Restart`。
- `POSE` 必须带 `calibrationVersion`。
- Host 只接受当前 calibration version 的 pose。
- Client tracking origin recenter/reset 后必须清 calibration，并上报 `TRACKING_ORIGIN_CHANGED`。
- `CLEAR_CALIBRATION` 后 Client 不再发送 `isCalibrated=true` 的 pose。

Ready 条件：

- required live users 已连接。
- required live users 已校准。
- pose fresh。
- 用户在 physical space 内。
- 用户之间起始距离大于安全阈值。
- required users 已 ACK 当前 config。

## 11. Live walking 与增益映射

真人行走链路：

```text
Client HMD local pose
  -> calibration
  -> POSE
  -> Host LiveVRPoseSample
  -> realUser
  -> physical delta
  -> shared Redirector / OSP
  -> virtualUser
  -> VIRTUAL_POSE
  -> Client XR Origin
```

Host step 语义：

```text
physicalDelta = currentPhysicalPosition - previousPhysicalPosition
physicalYawDelta = DeltaAngle(previousPhysicalYaw, currentPhysicalYaw)

realUser.pose = currentPhysicalPose
virtualUser.candidate = previousVirtualPose + physicalDelta/yawDelta
redirector/OSP evaluates gain
virtualUser applies final result
```

注意：

- 原地转身主要触发 rotation gain。
- 平移时可触发 translation/curvature gain。
- reset active 期间的 yaw delta 不能在恢复 walking 后再次进入 gain。
- stale/reconnect 后必须先 reanchor previous pose，再恢复 delta 计算。

## 12. Client 视角映射

Client 只把本机显示对齐 Host virtual pose。

普通 walking：

```text
Host VIRTUAL_POSE
  -> desired camera virtual position/yaw
  -> compute XR Origin XZ/yaw from current tracked HMD local planar offset
  -> keep XR Origin Y at its initial/session baseline
  -> apply XR Origin
```

Y 轴不变量：

- `VIRTUAL_POSE.virtualPosition` 是虚拟平面坐标，只应驱动 Client XR Origin 的 XZ 和 yaw。
- HMD Camera 的高度来自本机 XR tracking，不应由 Host virtual pose 或 reset injection 反推修改。
- `Client XR Origin` 的 Y 必须在 app session 内有稳定基准，Soft Restart、Recalibrate Restart、`CLIENT_RESET_CLEAR`、`RESET_END` 后都应恢复到该基准。
- reset injection 计算 root transform 时只能使用 HMD 相对 root 的平面 XZ offset；不能把完整 3D head offset 的 Y 分量减进 root position。
- 如果检测到 `Client XR Origin` Y、`Camera Offset` Y 或环境根节点 Y 在 restart 后变化，必须记录 warning 并阻止进入正式 `Running`，否则会出现视角地下/悬空。

禁止：

- 直接每帧设置 HMD Camera transform。
- 长期叠加 client-only yaw offset 修补 Host 状态。
- reset active 时用普通 `VIRTUAL_POSE` 覆盖 reset UX。
- 把 XR Origin 位置回传 Host 当 physical pose。

## 13. Reset 架构

Reset 分两层：

```text
Reset decision
  - wall / obstacle
  - user-user distance
  - proactive reset
  - OSP coordination risk

Reset execution
  - Host creates ResetPlan
  - Host validates safety
  - Client executes reset UX
  - Host commits completion
```

`LiveVRResetCoordinator` 只负责真人 reset execution，不重写 reset decision。

Reset 正常闭环：

```text
Host RDW Core detects reset
Host creates ResetPlan
Host freezes virtualUser pose
Host reliable sends RESET_START / RESET_PROMPT
Client enters reset UX
Client stops normal walking sync
Client sends RESET_DONE
Host validates eventId / yaw / position / safety
Host CommitLiveResetDone
Host reliable sends RESET_END
Client clears reset presentation state
```

`CommitLiveResetDone` 必须：

1. 验证 reset event 仍 active。
2. 验证 final yaw 接近目标方向。
3. 验证 final physical position 安全。
4. 保持 `virtualUser` 为 frozen virtual pose。
5. 更新 `realUser` 为 final HMD physical pose。
6. 重置 previous pose cache。
7. 清 active reset。
8. 发送 `RESET_END`。

Reset timeout 和失败：

```text
RESET_START
  -> wait RESET_DONE until resetTimeout
  -> if timeout: RESET_ABORT / CLIENT_RESET_CLEAR
  -> EndTrial(InvalidResetTimeout)
```

## 14. Reset 视角策略

阶段 1 采用冻结/淡出策略：

```text
RESET_START
  -> freeze or fade virtual view
  -> show turn direction/progress
  -> send RESET_DONE
  -> wait RESET_END
  -> clear reset state
  -> resume fresh VIRTUAL_POSE
```

原因：

- 安全。
- 容易验证。
- reset 后不容易产生视角跳变。

360 度 reset injection 可作为后续独立阶段实现，不进入第一版正式多人实验。它必须通过单人边界 reset、反向转身、丢包恢复、Soft Restart 恢复后再进入多人实验。

## 15. Restart 与对象复用

生命周期分四级：

```text
Application Session
  - Host app 启动到关闭
  - UDP socket / assignment table 持续存在

Experiment Run
  - 一次正式 runId
  - 对应问卷和日志目录

Trial / Episode
  - 一个具体条件下的行走任务
  - 有 trialId / configVersion / seed

Config Rebuild
  - 用户数、空间拓扑、方法结构或场景结构变化
  - 才重建 RDW spaces/units
```

默认复用：

- `LiveVRNetworkManager` 或拆分后的 transport/services。
- UDP socket。
- assignment table。
- Host/Client controller。
- Client XR Origin、HMD camera、HUD、environment loader。
- `RDWSimulationManager` / `GlobalCoordinationManager` manager 实例。

同一配置下可复用：

- `RedirectedUnit[]`
- `realSpace` / `virtualSpace`
- `realUser` / `virtualUser`
- redirector/resetter 对象，但必须重置 runtime state。

每个 trial 必须重置：

- real/virtual pose。
- `RedirectedUnit` reset/status。
- resetter 临时状态。
- active reset tables。
- previous HMD pose cache。
- Client reset presentation state。
- Client XR Origin Y baseline / local reset mapping baseline。
- StateCollector / GM_DataRecord episode state。
- GCM temporal state、prediction/risk/partition/proactive logs。
- debug trail、reset locator、visualizer transient object。

### Soft Restart

目标：

- 保留 client assignment。
- 保留 calibration。
- 清 Host active reset。
- 清 Client reset presentation state。
- 清 previous pose cache 和 destroyed object cache。
- 恢复 Client XR Origin Y 基准，丢弃上一轮 reset injection 的 3D anchor。

流程：

```text
Stop RDW runtime
ClearActiveResets(notifyClients=true)
BroadcastReliable CLIENT_RESET_CLEAR(reason=soft_restart, restartEpoch++)
Wait required CONTROL_ACK
CancelExternalResetForLiveRestart for all units
FlushAndResetSession logger
Reset RDW episode/runtime state
Clear previous HMD pose cache
Restore client XR Origin Y baseline
Discard stale virtual pose gate
Set STATE(Ready or WaitingForUsers)
```

### Recalibrate Restart

目标：

- 保留 client assignment。
- 清 calibration。
- 要求所有 required live users 重新站中心/朝向。

流程：

```text
Soft Restart base cleanup
ClearHostCalibrationStateForAllUsers
BroadcastReliable CLIENT_RESET_CLEAR(reason=recalibrate_restart, restartEpoch++)
SendReliable CLEAR_CALIBRATION(calibrationVersion++)
Set STATE(WaitingForUsers)
```

## 16. Client Reset 清理

`RESET_END(eventId)` 只结束一个正常 reset。它不能替代 restart recovery。

`CLIENT_RESET_CLEAR(restartEpoch)` 是强制清理消息，用于 Soft Restart、Recalibrate Restart、Host recovery、trial abort。

Client 收到后必须调用：

```text
LiveVRClientPresentationState.ClearForRestart(restartEpoch, reason)
```

清理内容：

- `hasResetPrompt / latestResetPrompt`
- `hasResetStart / latestResetStart`
- `activeLocalResetEventId / completedLocalResetEventId`
- local reset injection accumulated yaw/root offset
- `localResetDoneSent`
- HUD reset text/timer
- WorldHUD reset prompt
- TargetGuide reset suppression
- stale virtual pose from previous run
- Client XR Origin Y/root-height baseline check
- local reset start camera world position / 3D anchor

幂等要求：

- 重复收到同一个 `CLIENT_RESET_CLEAR` 不产生额外位移或旋转。
- 重复清理不能改变 HMD Camera tracking height；只允许恢复 XR Origin 到初始 Y 基准。
- Client 重连后，Host 立即重发当前 `STATE + CLIENT_RESET_CLEAR + TRIAL_CONFIG` 快照。

## 17. stale pose、断连与恢复

Host 维护：

```text
LiveVRUserConnectionState
  ConnectedFresh
  ConnectedStale
  Disconnected
  Reconnecting
  NeedsPoseReanchor
```

规则：

```text
if poseAge > stalePoseLimit:
  mark ConnectedStale
  suppress gain computation for user
  pause or invalid trial for required live users

if user reconnects:
  set NeedsPoseReanchor
  clear previousPoseByUserId
  wait one fresh pose as anchor
  only then resume delta computation
```

正式实验中，required live user stale 后不自动 fallback sim。fallback 只用于 debug 或明确标注 `MixedLiveSim` 条件。

## 18. 多人 reset 与安全仲裁

多人 reset 不能只按单用户独立执行。新增 `LiveVRResetArbitrationService`：

```text
candidate reset plans
  -> safety score
  -> user-user distance prediction
  -> boundary risk
  -> priority
  -> selected plans
```

规则：

- 同时 reset 前必须验证多个 turn direction 不会降低用户间距。
- 冲突时优先处理更危险用户，另一个用户 hold/freeze。
- 无安全方向时进入 `Paused`，提示实验员重新站位。
- 用户间距低于硬阈值时立即 safety stop。
- arbitration result 进入日志。

## 19. 状态机与 End State

实验状态：

```text
Idle
WaitingForUsers
Ready
Running
Paused
Completed
Invalid
```

转换规则：

- `Running` 只能从 `Ready` 进入。
- `Ready` 必须通过 readiness check。
- `Completed/Invalid` 必须可靠广播给 Client。
- 任意 restart/recovery 都必须广播 `CLIENT_RESET_CLEAR`。
- Client 收到非 `Running` 时不得继续 reset injection。

Trial end state：

```text
Normal
ManualStop
InvalidTrackingLost
InvalidTrackingOriginChanged
InvalidNetworkLost
InvalidResetTimeout
InvalidResetWrongDirection
InvalidSafetyBoundary
InvalidUserAbort
InvalidHostError
```

异常不能写成普通 `Completed`。

## 20. 日志与复现

每个 run 输出：

```text
run_manifest.json
control_messages.csv
network_quality.csv
trial_summary.csv
sampled_metrics/user_<id>/
  hmd_pose.csv
  physical_pose.csv
  virtual_pose.csv
  gains.csv
  reset_events.csv
  calibration_events.csv
```

`run_manifest.json` 至少包含：

```text
gitCommit
hostBuildVersion
clientApkVersion
deviceModels
hostNetworkMode
participantMapping
conditionOrder
counterbalanceGroup
physicalSpaceProfile
trialSeeds
methodName
redirectorType
resetPolicy
gainParameters
resetParameters
```

summary metrics：

- endState
- reset_count
- collision_count
- inter_reset_distance
- physical_distance
- virtual_distance
- reset_duration
- duration
- userSource

network/control metrics：

- poseAgeMs
- packetGap
- endpoint changes
- control retry count
- dropped stale message count
- calibration/restart events

Manifest 写入失败时，不应开始正式 trial。

## 21. 分阶段重构路线

### Phase 0：冻结边界

- 以本文档为准冻结 Host/Client/RDW/Reset/Logging 边界。
- 标注现有脚本职责和风险。
- 不改变行为。

### Phase 1：配置快照与日志

- 引入 `LiveVRExperimentConfig` / `LiveVRTrialConfig`。
- 输出 run manifest。
- 日志区分 live/sim/fallback/stale。

### Phase 2：可靠控制消息与版本上下文

- 实现 ACK/retry/timeout。
- 所有控制消息带 `runId/trialId/configVersion/restartEpoch/calibrationVersion`。
- 旧消息过滤。

### Phase 3：Pose/RDW Adapter 清晰化

- HMD pose -> realUser -> physical delta -> virtualUser 成为唯一 live walking 链路。
- stale/reconnect 后 reanchor。
- reset 完成后清 previous pose。

### Phase 4：Reset 与 Restart 稳定化

- `RESET_START / RESET_DONE / RESET_END` 可靠闭环。
- `CLIENT_RESET_CLEAR` 幂等清理。
- reset timeout 和 invalid end state。

### Phase 5：多人正式实验

- 多台 HMD 同时参与。
- reset arbitration。
- stale/disconnect/safety stop 验证。
- 完整日志覆盖所有 userId。

### Phase 6：高级 reset 视角注入

- 独立分支实现 360 度 reset injection。
- 通过单人和多人 recovery 测试后再进入正式实验。

## 22. 实验前检查清单

- Host 只启动一个实例，端口 bind 成功。
- 多台 HMD 自动发现同一个 Host。
- 两台 HMD 断线重连后 userId 不交换。
- `TRIAL_CONFIG` 未全员 ACK 时不能 Running。
- Running 中 structural config change 被拒绝。
- physical space 改变后必须重新校准。
- tracking origin 改变后 trial invalid 或强制 recalibrate。
- Soft Restart 后所有 Client reset UI/injection 清空。
- Soft Restart、Recalibrate Restart、reset 完成后 `Client XR Origin` Y 不漂移，HMD Camera 不进入地下/悬空。
- 单个 Client 达到 `TargetDistancePerUser` 后只隐藏本机目标并显示等待，不提前结束其他用户 trial。
- Host 只有在所有 required live users 都 `runComplete=true` 后才 `EndTrial(Normal)`。
- reset timeout 不会无限卡住。
- stale pose 后 Host 不继续用旧 pose 算 gain。
- reconnect 后第一帧不会产生巨大 delta。
- 两人同时 reset 有安全仲裁。
- 旧 `VIRTUAL_POSE/RESET_START` 晚到不会影响新 run。
- trial end state 区分 normal/manual/invalid。
- manifest、summary、sampled metrics 能互相对应。

## 23. 方案落实表

| 风险 | 方案 | 负责模块 |
| --- | --- | --- |
| 控制消息丢包 | ACK/retry/timeout | `LiveVRReliableControlService` |
| 旧消息污染新 trial | 版本上下文过滤 | `LiveVRProtocolVersionContext` |
| Soft Restart 后 Client 卡 reset | `CLIENT_RESET_CLEAR` | `LiveVRClientPresentationState` |
| Soft Restart/reset 后视角地下或悬空 | XR Origin Y baseline 锁定；reset injection 只使用平面 offset | `LiveVRClientVirtualViewBinder` / `LiveVRClientPresentationState` |
| 目标生成距离与实验完成距离混淆 | episode 决定目标生成；`TargetDistancePerUser` 决定累计完成 | `LiveVRClientTargetGuide` / `GlobalCoordinationManager` |
| 某用户先完成导致其他用户被提前结束 | completed user local wait；Host 等全员 `runComplete` | `LiveVRNetworkManager` / `LiveVRClientHud` |
| 校准失效 | calibration/space version | `LiveVRCalibrationSession` |
| stale pose | pause/invalid + reanchor | `LiveVRUserConnectionState` |
| reset 卡死 | timeout + end state | `LiveVRResetCoordinator` |
| 多人 reset 冲突 | safety arbitration | `LiveVRResetArbitrationService` |
| Running 误改配置 | config lock | `LiveVRExperimentConfigService` |
| 日志不可复现 | manifest + sampled metrics | `LiveVRExperimentLogger` |

这套重构方案的重点是先稳定实验语义和状态闭环，再逐步优化视觉 reset 策略和多人高级交互。只有当配置、网络、校准、reset、restart、日志都形成闭环后，真人多人实验结果才有安全性和可解释性。
