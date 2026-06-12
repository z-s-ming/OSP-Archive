# OSP LiveVR 多人真人实验重构方案

## 0. 当前实现状态快照

更新时间：2026-06-11

本文记录 OSP 中 LiveVR 多人真人实验的目标架构、当前实现状态、已验证链路与后续风险。当前重构的第一阶段目标是让真人实验中的网络连接、重置、重定向增益注入和软重启稳定可用，同时保留 `LiveVRNetworkManager` 作为 Unity 场景兼容 facade。

标记含义：

- `[x]` 已实现并通过当前真人测试或 Unity 运行验证。
- `[~]` 主链路已实现，但还需要多人、断网、日志或边界场景继续验证。
- `[ ]` 尚未实现或仍停留在方案层。

### 0.1 已完成并通过测试

- `[x]` 保留 `LiveVRNetworkManager` 作为 facade，现有 Host/RDW/Client 组件仍通过它调用网络与状态接口，避免第一轮破坏 Inspector 引用。
- `[x]` 新增 `LiveVRTransport`，集中 UDP bind、send、receive 与 endpoint 管理。
- `[x]` 新增 `LiveVRReliableControlService`，实现 `messageId + CONTROL_ACK + retry + timeout` 的可靠控制消息框架。
- `[x]` 新增 `LiveVRProtocolVersionContext`，维护 `hostRunId/runId/trialId/configVersion/restartEpoch/calibrationVersion/assignmentVersion`。
- `[x]` `RESET_START`、`RESET_DONE`、`RESET_END`、`STATE`、`CALIBRATE_CENTER`、`CLEAR_CALIBRATION`、`CLIENT_RESET_CLEAR` 已接入可靠控制链路。
- `[x]` 新增 `LiveVRClientPresentationState.ClearForRestart(restartEpoch, reason)`，统一清理客户端 reset UI、World HUD、TargetGuide suppression、local reset injection 和旧 virtual pose。
- `[x]` Soft Restart / Recalibrate Restart 使用显式 `restartEpoch++`，并通过 `CLIENT_RESET_CLEAR` 清理客户端状态。
- `[x]` Client PlayerPrefs 已收紧为只保留 Host IP/Port 等基础连接配置；运行状态、userId、reset 状态、实验偏好不再跨应用启动保留。
- `[x]` reset timeout 从固定总时长限制改为 no-progress watchdog，并加入抖动过滤，避免首次体验 VR 的用户因为反应慢被误判失败。
- `[x]` reset 期间 Client 本地 reset injection 不再被 `Apply Client Virtual Pose Only While Running` 阻断。
- `[x]` reset UX 已简化为方向箭头和中文提示，用户只需要按箭头方向转身。
- `[x]` 真人 reset 完成裁判收回 Host：Host 根据 calibrated pose 的 yaw、position、safety stable 判断完成，并发送 `RESET_END`；Client 不再凭本地累计角度决定正式完成。
- `[x]` reset 注入采用“物理转身进度 -> 虚拟 360 度补偿”的映射逻辑；当前小空间单人测试中 reset 体验通过。
- `[x]` Soft Restart 后 Host 清理 `LiveVRHmdMovementController` previous pose cache，避免下一帧产生巨大 delta。
- `[x]` Soft Restart 后 GCM/StateCollector/Voronoi 对 destroyed GameObject 做防御，避免旧 episode 对象导致 `MissingReferenceException` 刷屏。
- `[x]` `LiveRdwWalkingStepper` 已接入 live walking 链路：HMD physical delta 驱动 `virtualUser`，并调用现有 `Redirector/GainRedirector/ARC/OSP/APF` 方法计算重定向增益。
- `[x]` Host 不再让 `LiveVRGainDebugState` 参与实时控制；它只记录日志。实时控制改为 `LiveVRGainCommandService -> LiveVRVirtualPoseBroadcaster -> Client activeGainState`。
- `[x]` Host 下发 `gainRateDegreesPerSecond`，Client 保存最新有效 gain command，并在 `LateUpdate` 每帧执行 `gainRate * Time.deltaTime`，实现连续注入。
- `[x]` duplicate pose 不再清空 gain；reset 期间强制清空 walking gain；超过短有效期没有新有效命令才停止注入。
- `[x]` Host gain 计算改为使用 HMD pose 的接收时间差，而不是固定 `Time.fixedDeltaTime`，避免 36-37Hz pose 被误当作 60Hz 导致速度/角速度偏大。
- `[x]` Client `ApplyGainCheck` 日志已区分 `expectedRootYawDelta` 和 `expectedPerceivedYawDelta`，用于确认不是只收到 gain，而是真的按 RDW 定义旋转 `VirtualWorldRoot`。
- `[x]` 当前测试中，重定向增益注入和 reset 均已通过。

### 0.2 部分完成，仍需继续验证

- `[~]` 协议版本过滤已覆盖可靠控制主链路，但 `POSE` 和 `VIRTUAL_POSE` 仍未完整携带并过滤所有 `runId/trialId/configVersion/restartEpoch/calibrationVersion` 字段。
- `[~]` Reliable control 已有 retry/timeout/ACK，但 Host 端 required users ACK recovery gate 的 UI 与失败状态还需要进一步完善。
- `[~]` Soft Restart 会广播 `CLIENT_RESET_CLEAR` 并重置主链路，但 ACK 全部返回后的自动恢复服务仍未完全拆成独立模块。
- `[~]` Recalibrate Restart 已清 Host calibration 并发送 `CLEAR_CALIBRATION`，但完整 `calibrationVersion` 与 `POSE` 过滤闭环仍未完成。
- `[~]` Stale/reconnect 的 pose cache 清理已实现一部分，但 `ConnectedFresh / ConnectedStale / Disconnected / Reconnecting / NeedsPoseReanchor` 状态机尚未完整落地。
- `[~]` Running 中 structural config change 的拒绝策略已明确，但还没有统一的 `LiveVRExperimentConfigService` 锁。
- `[~]` `LiveVRNetworkLogger` 已保留，部分 restart/control/gain 信息可从 Unity 日志观察；正式实验输出中的 trial end state、retry count、dropped stale count、restartEpoch 还未完全结构化。
- `[~]` 单人小空间链路已通过，仍需测试大空间、多用户、多 Quest、弱网、断网重连、Host 不重启但 Client 重启等场景。

### 0.3 尚未完成

- `[ ]` `LiveVRExperimentConfig` / `LiveVRTrialConfig` / `LiveVRExperimentManifest` 尚未完整落地，正式 trial 配置快照仍不完整。
- `[ ]` `LiveVRHostDiscoveryService`、`LiveVRClientAssignmentService`、`LiveVRPoseStream` 尚未从 `LiveVRNetworkManager` 完整拆出。
- `[ ]` `LiveVRExperimentConfigService` 尚未实现，Running 中配置修改锁还没有统一入口。
- `[ ]` `LiveVRResetArbitrationService` 尚未实现，多人同时 reset 的安全仲裁仍未完成。
- `[ ]` 多人 reset 冲突、双人同时转身、user-user safety stop 尚未形成正式闭环。
- `[ ]` 完整 reconnect recovery 尚未全部完成：断线后 endpoint 更新、重发 `STATE + CLIENT_RESET_CLEAR + config snapshot`、第一帧 reanchor 后恢复 delta。
- `[ ]` 防火墙和 Host discovery preflight 只完成基础 discovery 与 bind，尚未提供完整 UI 提示、本机 IPv4 列表和端口占用可视化。
- `[ ]` 正式实验 manifest、summary metrics、sampled metrics、control logs、calibration/restart events 的对应关系尚未完整实现。
- `[ ]` Unity batchmode Editor 编译与真实 Quest 多机集成测试尚未作为固定验收流程。

## 1. 核心设计原则

OpenRDW 最值得借鉴的是实验系统组织方式，而不是逐类复制：

- 一个 trial 必须有完整配置快照。
- 每个 avatar/user 独立持有 movement、redirection、reset、logging 状态。
- `Redirector` / `Resetter` 是可替换模块。
- 真实 HMD、真实路径回放、自动仿真都应通过统一 movement/redirection 语义进入 RDW。
- summary metrics 和 sampled metrics 分开记录。
- trial end state 必须可解释，不能把异常结束都算作正常完成。

OSP LiveVR 的原则：

- Host 是唯一 RDW/OSP 权威。
- Client 是 sensor/display，不计算多人 RDW、OSP、Voronoi、碰撞或 reset 决策。
- LiveVR 是 adapter，不 fork 一套新 RDW Core。
- 仿真实验和真人实验共享 `RedirectedUnit`、`Redirector`、GCM/OSP、reset decision 和核心统计语义。
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
  -> gain command
  -> reset execution coordination

Shared RDW/OSP Core
  -> RedirectedUnit
  -> Redirector / GainRedirector / ARC / OSP / APF
  -> GlobalCoordinationManager
  -> reset decision / collision / metrics

Host Broadcaster
  -> VIRTUAL_POSE stream
  -> gainRateDegreesPerSecond
  -> RESET / STATE / CONFIG control messages

Client Presentation
  -> VirtualWorldRoot mapping
  -> per-frame gain apply
  -> reset UX
  -> HUD / target guide / local environment

Logger
  -> run manifest
  -> summary metrics
  -> sampled metrics
  -> network/control/gain logs
```

## 3. 实时 RDW 增益链路

当前已采用的实时控制链路：

```text
Quest HMD pose
  -> Client POSE send
  -> Host LiveVRNetworkManager receive
  -> LiveVRHmdMovementController
  -> LiveRdwWalkingStepper
  -> existing Redirector / OSP / APF
  -> LiveVRGainCommandService
  -> LiveVRVirtualPoseBroadcaster
  -> VIRTUAL_POSE with gainRate
  -> Client LiveVRClientVirtualViewBinder
  -> LateUpdate RotateAround(VirtualWorldRoot, HMD pivot, gainRate * dt)
```

关键约束：

- `LiveVRGainDebugState` 只记录，不参与控制。
- Host 发送的是角速度 `gainRateDegreesPerSecond`，不是一次性 yaw delta。
- Client 每帧用本地 `Time.deltaTime` 连续注入。
- duplicate pose 只代表 Host 当前 tick 没有新 pose，不代表用户停止，因此不能清空 gain。
- 没有新有效命令超过短有效期后必须停止，避免断网后继续旋转。
- fresh pose 但速度或角速度接近 0 时，不刷新 walking gain，避免站立不动时视角晃动。
- reset 期间 walking gain 必须强制清空，reset injection 和 walking gain 不能同时生效。

客户端注入对象：

- 旋转主体：`VirtualWorldRoot`。
- 旋转中心：当前 HMD camera 的水平位置，Y 使用 `VirtualWorldRoot.position.y`。
- 摄像机/XR tracking 本身保持权威，不直接旋转 HMD Camera。

验证日志：

```text
[LiveVRRate][ClientPose]
[LiveVR] GainReceived
[LiveVR] ApplyGainCheck
[LiveVR] GainInterval
```

通过标准：

- Client pose rate、Host gain receive/apply rate 接近期望频率。
- `ApplyGainCheck error` 接近 0。
- `lastDelta ~= lastRate * dt`，不会出现单帧异常大角度。
- 静止时 Host 不应持续发送有效 Rotation/Curvature gain。
- reset 期间应看到 walking gain 清空。

## 4. Reset / Restart 闭环

正常 reset：

```text
Host detects reset need
  -> RESET_START / RESET_PROMPT
  -> Client shows arrow prompt
  -> Client local reset injection maps physical turn to virtual 360 deg compensation
  -> Client sends RESET_DONE / pose continues
  -> Host checks yaw/position/safety stable
  -> RESET_END
  -> Client clears reset UI and injection
```

Soft Restart：

```text
stop RDW
  -> clear Host active reset
  -> restartEpoch++
  -> reliable broadcast CLIENT_RESET_CLEAR
  -> clear previous pose/cache
  -> reset RDW runtime
  -> WaitingForUsers / Ready
```

设计要求：

- `RESET_END(eventId)` 只处理正常 reset 闭环。
- `CLIENT_RESET_CLEAR(restartEpoch)` 专门处理 Soft Restart、Recalibrate Restart、Host recovery、trial abort。
- reset timeout 不再是固定总时长，而是 no-progress watchdog。
- watchdog 必须使用“有效进度变化”判断，过滤 HMD 抖动。
- Soft Restart 后第一帧 fresh pose 只作 anchor，不能直接计算巨大 delta。

## 5. 网络与端口

推荐正式实验拓扑：

```text
Windows Host PC
  - UDP listen 0.0.0.0:47770
  - 运行 RDW/OSP Core
  - 接收所有 HMD pose

HMD Client 0..N
  - standalone Android XR app
  - 使用系统分配本地临时 UDP 端口
  - 发送到 hostIp:47770
```

多台 HMD 同时发往同一个 Host port 不会端口冲突。Host 使用 `clientIp:clientSourcePort` 区分当前 endpoint，并用 `deviceKey -> userId` 维护稳定身份。

推荐身份表：

```text
deviceKey -> userId
userId -> latestEndpoint
endpoint -> transientConnectionInfo
```

仍需重点处理：

- Host 不重启但 Client app 重启时，source port/session 可能变化，Host 必须更新 endpoint。
- 旧 endpoint 不能继续接收 Calibrate/Start/Reset 控制消息。
- Client 重连后必须重新收到 `STATE + assignment + calibration/config context`。

## 6. 配置与运行约束

Running 中禁止修改 structural config：

- redirector / RDW method
- physical space
- reset policy
- gain parameters
- expected user count
- calibration mode
- target generation mode

这些配置必须在 `Ready` 前确定，并进入 manifest。Host 和 required clients 未确认当前配置前，不应进入 Running。

Client 只允许持久化：

- Host IP
- Host port
- discovery/connect preference
- 必要的调试显示偏好

Client 不允许持久化：

- userId
- Running/Completed/Resetting 等运行状态
- reset prompt / reset injection 状态
- target progress
- calibration completed 状态
- 上一轮 virtual pose / gain command

## 7. 目标点与实验结束

当前用户实验中，目标小球生成应由 Episode 类型决定。Random 模式应满足：

- 目标点在 walking area 内。
- 目标点与用户当前虚拟位置距离在设定范围内，例如 4-8m。
- 目标点不应生成在不可行走区域或被实验内障碍阻挡。

用户完成条件：

- 用户实验统计“虚拟世界真实走过多少米”时，应按每帧累计 virtual movement distance。
- 单个用户达到目标距离后，Client 应显示该用户已完成，并停止继续要求该用户追目标。
- 多人实验中，某个用户先完成时，其他用户继续实验；Host 等所有 required users 完成或出现 end state 后结束 trial。

## 8. 多人实验风险清单

仍需重点测试：

- 多个 required users 同时 reset。
- 一个用户 reset 时另一个用户接近边界或接近其他用户。
- Client 断网后重连，Host endpoint 是否更新。
- Host 不重启，Client app 重启后 Calibrate/Start 是否发到新 endpoint。
- required Client 未 ACK `CLIENT_RESET_CLEAR` 时，Host 是否停在 recovery/WaitingForUsers。
- Running 中误改配置是否被拒绝。
- 多 Quest 同时发送 POSE 时，Host RDW tick 和 gain broadcaster 是否稳定。
- Soft Restart 后所有 Client UI、WorldHUD、TargetGuide、local target、gain state 是否清空。
- Recalibrate Restart 后旧 calibration pose 是否被丢弃。

## 9. 下一阶段建议

优先级从高到低：

1. 做一次大空间多人全流程测试：连接、校准、开始、行走、reset、完成、Soft Restart、Recalibrate Restart。
2. 专门测试 Host 不重启但 Quest app 重启的 endpoint 更新。
3. 补齐 `POSE` / `VIRTUAL_POSE` 的版本上下文字段过滤。
4. 拆出 `LiveVRExperimentConfigService`，统一锁定 Running 中 structural config change。
5. 拆出 `LiveVRResetArbitrationService`，处理多人 reset 冲突。
6. 将 gain、control、restart、trial end state 写入正式实验结构化日志。

