# Live VR Multiplayer User Experiment

This document describes the current multi-device Live VR user experiment implementation. The system uses the existing RDW project as the authoritative experiment host and connects Android XR headsets such as Quest 3 and PICO 4 Ultra as lightweight clients.

## 1. Goal

The Live VR experiment layer turns the project from an offline multi-user RDW simulation into a networked user experiment setup:

```text
Windows Host
  - Runs RDWSimulationManager and GlobalCoordinationManager
  - Receives HMD poses from all users
  - Owns reset/collision/global coordination decisions
  - Starts/stops the experiment
  - Writes experiment and network logs

Android XR Clients
  - Quest/PICO devices
  - Read local HMD pose
  - Calibrate pose into experiment coordinates
  - Send pose packets to the Host
  - Receive ACK, experiment state, and reset prompts
```

The current design intentionally keeps the custom UDP transport instead of adding Mirror, Photon, or Unity Netcode. The Host remains the only authority for experiment decisions.

## 2. Architecture Rules

The Live VR experiment layer follows these rules:

1. Client HMD tracking is a real sensor source, not an RDW decision maker.
2. Host `realUser` is the global-coordinate mirror of the HMD pose, not a simulation-controlled avatar.
3. Host `virtualUser` is the authoritative virtual state produced by RDW.
4. Host owns APF, OSP, Voronoi, collision, wall reset, user reset, and proactive reset decisions.
5. Client does not compute RDW. It only uploads HMD pose and executes Host-sent display/reset UI instructions.
6. Client XR Camera is not directly rewritten. The client moves an XR Origin or camera rig root so the tracked HMD pose maps into the Host-sent virtual pose.
7. Real-user reset completion must live in the LiveUser outer layer and be based on true HMD yaw compliance, not on the original simulation resetter finishing its synthetic rotation.

The project now separates the two experiment profiles:

```text
Simulation:
  AutoPilot Movement -> RDW Core -> Simulation Logger

LiveUser:
  HMD Movement Controller -> RDW Core -> Live Logger / Client View
```

The intended display mapping is:

```text
Host virtualUser pose
  -> VIRTUAL_POSE message
  -> Client XR Origin/root transform
  -> tracked HMD camera appears at the authoritative virtual pose
```

Do not directly set the XR Camera transform each frame. Assign the XR Origin or camera rig root to `Client Virtual View Root`.

## 3. Runtime Components

Add one scene object named `LiveVRExperiment` and attach/configure `LiveVRExperimentSetup`. The setup component requires and configures the execution components on the same object:

| Component | Responsibility |
| --- | --- |
| `LiveVRExperimentSetup` | Single Inspector configuration entry point. Applies role, network, XR pose, RDW binding, HUD, Host control, and logging settings. |
| `LiveVRNetworkManager` | UDP pose upload, Host ACK, experiment state broadcast, reset prompt delivery, Android Intent/command-line driven runtime configuration. |
| `LiveVRHmdMovementController` | LiveUser movement controller. Applies received HMD poses to `RedirectedUnit[userId].realUser` without entering the original AutoPilot simulation movement path. |
| `LiveVRResetCoordinator` | LiveUser reset executor. Receives RDW Core `ResetPlan`, sends `RESET_PROMPT`, waits for HMD yaw/position alignment, then reports completion back to RDW Core. |
| `LiveVRRdwPoseBridge` | Legacy/debug host-side pose bridge. `LiveVRExperimentSetup` disables its automatic FixedUpdate/LateUpdate pose application when the HMD movement controller is used. |
| `LiveVRVirtualPoseBroadcaster` | Host-side sender for authoritative `virtualUser` pose. |
| `LiveVRClientVirtualViewBinder` | Client-side XR Origin/root mapper from Host `VIRTUAL_POSE` to local first-person view. |
| `LiveVRClientCameraIsolation` | Client-side camera guard. In Client/HostClient mode, keeps the configured XR HMD Camera enabled and disables old scene overview cameras. |
| `LiveVRNetworkStatusOverlay` | Host/debug overlay showing users, pose age, calibration, and experiment state. |
| `LiveVRClientHud` | Client-side debug HUD showing connection state, Host ACK age, experiment state, and reset prompts. |
| `LiveVRClientWorldHud` | Client-side world-space VR HUD attached in front of the XR HMD Camera. This is the primary headset-visible connection/calibration/status prompt. |
| `LiveVRHostExperimentController` | Host keyboard/Inspector control for prepare, start, stop, and manual reset prompt testing. |
| `LiveVRNetworkLogger` | Host-side CSV writer for network quality and pose timing. |
| `LiveSpaceProfileProvider` | Host-side LiveUser physical-space source. Builds a runtime `LiveSpaceProfile` such as a 10m x 10m rectangle or manual polygon and feeds it to RDW realSpace. |

The setup component should be the only field group edited during normal use. The other components are execution helpers and receive their values from `LiveVRExperimentSetup`.

## 4. User ID Mapping

The canonical user ID rule is:

```text
userId = RedirectedUnit array index = SimulationSetting.unitSettings index
```

Examples:

```text
Quest 3 UserId 0 -> RedirectedUnit[0]
PICO 4 Ultra UserId 1 -> RedirectedUnit[1]
Third headset UserId 2 -> RedirectedUnit[2]
```

For a 3-person experiment:

- `LiveVRExperimentSetup.Expected User Count = 3`
- `RDWSimulationManager.simulationSetting.unitSettings.Length = 3`
- each headset must launch with a unique `liveVrUserId`

Running with fewer connected devices than `Expected User Count` is valid for communication testing, but the Host will stay in `WaitingForUsers` and should not be used for formal experiment data.

## 5. Network Protocol

All devices use the same Host IP and UDP port. Users are distinguished by `userId`, not by separate ports.

Default port:

```text
47770
```

Current messages:

| Message | Direction | Meaning |
| --- | --- | --- |
| `POSE` | Client -> Host | HMD pose sample with `userId`, sequence, client timestamp, experiment position, yaw, height, calibration flag. |
| `ACK` | Host -> Client | Confirms latest pose sequence and sends current experiment state. |
| `STATE` | Host -> Client | Broadcasts `Idle`, `WaitingForUsers`, `Ready`, `Running`, `Paused`, or `Completed`. |
| `RESET_PROMPT` | Host -> Client | Tells a target user that a wall/user/proactive reset prompt should be shown. |
| `VIRTUAL_POSE` | Host -> Client | Sends authoritative `virtualUser` position and yaw for client XR Origin/root mapping. |

The Host records the remote UDP endpoint for each `userId` when it receives pose packets. ACK/state/reset packets are sent back to the stored endpoint.

## 6. Unity Configuration

On the scene object `LiveVRExperiment`, configure `LiveVRExperimentSetup`.

### Host

For a computer that only acts as server:

```text
Mode = HostOnly
Expected User Count = 3
Host Pose Port = 47770
Experiment Profile = LiveUser
Enable Live VR Physical User Input = true
Enable Live Reset Coordinator = true
Reset Yaw Error Threshold Degrees = 15
Require Reset Position Alignment = true
Reset Position Tolerance Meters = 0.75
Reset Stable Duration Seconds = 0.3
Show Status Overlay = true
Enable Host Keyboard Controls = true
Enable Network Logging = true
Enable Live Space Profile = true
Live Space Source = Rectangle
Live Space Rectangle Width Meters = 10
Live Space Rectangle Depth Meters = 10
```

For a computer that is both Host and local VR user:

```text
Mode = HostClient
Local User Id = 0
Expected User Count = 3
```

### Client

For Quest/PICO:

```text
Mode = ClientOnly
Local User Id = 0, 1, or 2
Host Address = Windows Host LAN IP
Host Pose Port = 47770
Use Unity XR Head Pose = true
Calibrate On Start = true
Show Client HUD = true
Enable Virtual Pose Sync = true
Client Virtual View Root = XR Origin / Camera Rig root
Client Hmd Camera = tracked HMD camera under that XR Origin
Require Explicit Client View Root = true
Apply Client Virtual Pose Only While Running = true
Isolate Client Cameras = true
```

Do not assign the scene overview `Main Camera` to `Client Virtual View Root`. The view root must be the XR Origin or camera rig root, and the HMD camera must be a child of that root. If the build only contains the old RDW overview `Main Camera`, the headset will see the whole virtual world from the debug camera rather than the participant's virtual viewpoint.

In a headset build, only the XR HMD Camera should render the client view. `LiveVRClientCameraIsolation` disables the old overview `Main Camera` in Client/HostClient mode and retags the XR HMD Camera as `MainCamera`. The overview camera is for Host/debug use only.

The client should also avoid applying `VIRTUAL_POSE` before the Host enters `Running`. If the Host is still in `WaitingForUsers` and keeps broadcasting a static virtual pose, continuously mapping that pose can counter-rotate the XR Origin and make the world appear to follow the participant's head. Keep `Apply Client Virtual Pose Only While Running` enabled for real headset tests.

For formal user experiments, split visible objects into layers before data collection:

- XR HMD Camera: virtual world and virtual avatars only.
- Host/debug camera: real-space walls, real-user mirrors, RDW debug objects, overlays, and diagnostics.

Until the layers are separated, a client camera with `Culling Mask = Everything` may still see real-space/debug objects or other diagnostic avatars even though the camera origin is correct.

### Live Physical Space

LiveUser experiments should not use the visible Unity physical-wall scene as the authority for the real room. The Host uses a `LiveSpaceProfile` instead:

```text
pre-experiment boundary definition
  -> LiveSpaceProfile boundary polygon
  -> RDWSimulationManager realSpace
  -> RDW Core wall/reset/Voronoi/risk calculations
```

The first implemented source is a rectangle fallback, e.g. 10m x 10m. A manual polygon can be entered in `Live Space Manual Boundary Polygon`. Quest/PICO boundary import is reserved as a later source, but the rest of the Host pipeline already consumes the same profile abstraction.

The client headset does not render this physical boundary. It sends calibrated HMD pose into the Host physical coordinate system and renders only the virtual layers configured in `Client Camera Visible Layers`.

### Center/Yaw Calibration

The first LiveUser calibration method is shared-center alignment:

```text
User stands on the real center marker
  -> User faces the real forward direction
  -> User presses keyboard C or controller primary button
  -> Quest local pose maps to Host physical (0,0), yaw 0
  -> User walks to any safe starting position
  -> Host uses that current mapped pose as the realUser pose
```

With `Require Manual Center Calibration` enabled, Client/HostClient does not auto-calibrate on app start. It still streams pose, but `calibrated=false` until the user performs center calibration. Host `Ready` requires each expected user to be connected, streaming fresh pose, calibrated, inside `LiveSpaceProfile`, and farther apart than `Minimum User Distance Before Start Meters`.

This means the experiment does not require fixed per-user start points. Fixed center and forward markers are enough to align every headset into the same Host physical coordinate system.

### Headset Status HUD

Client builds create a world-space HUD in front of the XR HMD Camera. In the headset it should show:

- `LIVE VR CLIENT`
- `UserId -> HostIP:Port`
- `WAITING FOR HOST` or `CONNECTED`
- Host experiment state
- center calibration status
- instructions to press `A/Primary` or `C` at the center marker
- virtual pose and reset prompt status

If the user only sees the virtual scene and no HUD, check `Show Client World Hud`, `Client Hmd Camera`, `Client Camera Visible Layers` including `UI`, and whether the build scene contains `LiveVRExperiment`.

The Android XR and Quest/PICO build settings must already be configured in Unity:

- Android build target
- OpenXR or the selected XR loader
- Internet permission enabled
- ARM64 enabled
- current experiment scene in Scenes In Build

## 7. ADB Launch

ADB can inject client configuration at launch time. This avoids manually entering Host IP/UserId/Port inside the headset.

ADB connection only means the computer can install/start the app. The experiment traffic still uses Wi-Fi UDP between the headset and Host.

Example for Quest/PICO User 0:

```powershell
adb -s <device_serial> shell am start `
  -n <package_name>/com.unity3d.player.UnityPlayerActivity `
  --es liveVrMode client `
  --ei liveVrUserId 0 `
  --es liveVrHost 192.168.1.100 `
  --ei liveVrPort 47770 `
  --ei liveVrUsers 3
```

Example for User 1:

```powershell
adb -s <device_serial> shell am start `
  -n <package_name>/com.unity3d.player.UnityPlayerActivity `
  --es liveVrMode client `
  --ei liveVrUserId 1 `
  --es liveVrHost 192.168.1.100 `
  --ei liveVrPort 47770 `
  --ei liveVrUsers 3
```

Replace `<package_name>` with the Android package identifier configured in Unity Player Settings.

## 8. Host Controls

`LiveVRHostExperimentController` provides first-pass keyboard controls:

| Key | Action |
| --- | --- |
| `F5` | Prepare experiment. Host enters `Ready` only when all expected users are connected and calibrated. |
| `F6` | Start experiment. Calls `RDWSimulationManager.StartSimulation()` and broadcasts `Running`. |
| `F7` | Stop experiment. Sets `BStart = false` and broadcasts `Completed`. |
| `F8` | Send manual reset prompt to `Manual Reset Prompt User Id` for link testing. |

Formal data collection should only start when the Host overlay shows every expected user with fresh calibrated pose data.

## 9. Reset Target And Completion

Reset target/completion should be implemented as a LiveUser outer-layer service, not by changing the original simulation resetter completion path. The intended LiveUser flow remains:

1. RDW Core detects wall/user/proactive reset need and creates a `ResetPlan`.
2. `LiveVRResetCoordinator` takes over the reset execution in `LiveUser` profile.
3. Host sends `RESET_PROMPT` with target direction and target position to the selected client.
4. Client shows reset UI through `LiveVRClientHud` or a formal VR UI and keeps uploading true HMD pose.
5. `LiveVRResetCoordinator` checks HMD yaw, optional target-position alignment, fresh calibrated pose, and optional real-space safety.
6. After the alignment stays valid for the stable duration, `LiveVRResetCoordinator` reports `ResetCompleted` back to RDW Core and normal redirection resumes.

In `Simulation` profile, no LiveUser coordinator takes over, so the original resetter completion path remains unchanged.

Recommended defaults:

```text
yaw error threshold = 10-15 degrees
stable duration = 0.3 seconds
timeout = experiment-specific safety limit
rearm cooldown = 0.75 seconds
```

## 10. Logging

Existing experiment logs remain unchanged. Live VR adds one raw CSV:

```text
CGnA_DataLog/runs/<run>/raw/live_vr_network.csv
```

Fields:

```text
timestamp,frame,simTime,userId,deviceType,sequence,
clientUnixMs,hostReceiveUnixMs,poseAgeMs,
x,y,yaw,height,calibrated,connected,packetGap
```

The run manifest also includes a `liveVR` section with:

- enabled flag
- runtime mode
- local user ID
- Host address
- Host port

Use this network log to diagnose missing packets, stale users, client clock/timing issues, and per-device pose update quality.

## 11. Recommended Test Sequence

### Test 1: Single Headset Communication

1. Run the Windows Host in Unity Editor with `Mode = HostOnly`.
2. Launch one Quest/PICO with `liveVrUserId = 0`.
3. Confirm Host overlay shows `User 0` with changing pose, calibrated state, and low pose age.
4. Confirm Client HUD shows connected and ACK age updating.

Expected result:

```text
User 0: active
User 1/User 2: no pose or stale
Host state: WaitingForUsers
```

Also confirm the Client HUD shows `Virtual view` pose updates after Host has generated `virtualUser` objects.

### Test 2: Two Devices

1. Launch Quest as `UserId = 0`.
2. Launch PICO as `UserId = 1`.
3. Confirm Host overlay shows both users active and not swapped.

### Test 3: Full Expected User Count

1. Launch all expected devices.
2. Press `F5`.
3. Confirm Host enters `Ready`.
4. Press `F6`.
5. Confirm clients show `Running` and RDW simulation starts.

### Test 4: Reset Prompt

1. Press `F8` on Host.
2. Confirm only the selected client shows the reset prompt.
3. Trigger a real wall/user/proactive reset and confirm prompt delivery.

### Test 5: Logging

1. Run a short experiment.
2. Stop with `F7`.
3. Check:

```text
raw/inter_reset_distance.csv
raw/live_vr_network.csv
manifest.json
```

Network CSV should show each expected `userId`, sequence progression, pose age, calibration flag, connected flag, and packet gaps.

## 12. Current Limitations

- Client HUD is debug-oriented and not a polished VR experiment UI.
- `deviceType` in `live_vr_network.csv` is currently written as `unknown`; device model detection can be added later.
- The transport is UDP without reliability. This is acceptable for high-rate pose samples, but state/reset messages may later need repetition or explicit acknowledgement if prompts must be guaranteed.
- ADB is not the experiment connection. ADB only starts the app with parameters; the headset and Host must still be reachable over the same LAN.
- Reset target/completion should be completed in the LiveUser outer layer. Do not reintroduce Quest/HMD-specific logic into `RedirectedUnit.Move`, `SimulationController.VirtualMove`, or the original resetter completion methods.
