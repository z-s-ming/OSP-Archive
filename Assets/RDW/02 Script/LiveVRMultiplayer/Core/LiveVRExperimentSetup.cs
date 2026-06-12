using System;
using UnityEngine;

[RequireComponent(typeof(LiveVRNetworkManager))]
[RequireComponent(typeof(LiveVRRdwPoseBridge))]
[RequireComponent(typeof(LiveVRHmdMovementController))]
[RequireComponent(typeof(LiveVRNetworkStatusOverlay))]
[RequireComponent(typeof(LiveVRClientHud))]
[RequireComponent(typeof(LiveVRClientWorldHud))]
[RequireComponent(typeof(LiveVRHostExperimentController))]
[RequireComponent(typeof(LiveVRNetworkLogger))]
[RequireComponent(typeof(LiveVRVirtualPoseBroadcaster))]
[RequireComponent(typeof(LiveVRClientVirtualViewBinder))]
[RequireComponent(typeof(LiveVRClientPresentationState))]
[RequireComponent(typeof(LiveVRClientCameraIsolation))]
[RequireComponent(typeof(LiveVRClientEnvironmentLoader))]
[RequireComponent(typeof(LiveVRClientTargetGuide))]
[RequireComponent(typeof(LiveSpaceProfileProvider))]
[RequireComponent(typeof(LiveVRResetCoordinator))]
[DefaultExecutionOrder(-11000)]
public class LiveVRExperimentSetup : MonoBehaviour
{
    [Header("Role")]
    [SerializeField] private LiveVRExperimentMode mode = LiveVRExperimentMode.Disabled;
    [SerializeField] private int localUserId = 0;
    [SerializeField] private int expectedUserCount = 2;
    [SerializeField] private bool allowCommandLineOverrides = true;

    [Header("Hybrid Fallback")]
    [SerializeField] private bool allowSimulatedFallbackForMissingUsersOnStart = false;
    [HideInInspector]
    [SerializeField] private LiveVRUserSource[] userSources = new LiveVRUserSource[0];

    [Header("Network")]
    [SerializeField] private string hostAddress = "192.168.1.100";
    [SerializeField] private int hostPosePort = 47770;
    [SerializeField] private float sendRateHz = 60.0f;
    [SerializeField] private bool autoAssignClientUserIds = true;
    [SerializeField] private bool enableHostDiscovery = true;
    [SerializeField] private float hostDiscoveryAckTimeoutSeconds = 3.0f;
    [SerializeField] private float hostDiscoveryIntervalSeconds = 2.0f;

    [Header("XR Pose")]
    [SerializeField] private Transform headTransformOverride;
    [SerializeField] private bool useUnityXRHeadPose = true;
    [SerializeField] private bool calibrateOnStart = true;
    [SerializeField] private bool useControllerPrimaryButtonForCalibration = true;
    [SerializeField] private bool requireManualCenterCalibration = true;

    [Header("RDW Binding")]
    [SerializeField] private LiveVRNetworkManager networkManager;
    [SerializeField] private LiveVRRdwPoseBridge rdwPoseBridge;
    [SerializeField] private LiveVRHmdMovementController hmdMovementController;
    [SerializeField] private LiveVRNetworkStatusOverlay statusOverlay;
    [SerializeField] private LiveVRClientHud clientHud;
    [SerializeField] private LiveVRClientWorldHud clientWorldHud;
    [SerializeField] private LiveVRHostExperimentController hostExperimentController;
    [SerializeField] private LiveVRNetworkLogger networkLogger;
    [SerializeField] private LiveVRVirtualPoseBroadcaster virtualPoseBroadcaster;
    [SerializeField] private LiveVRClientPresentationState clientPresentationState;
    [SerializeField] private LiveVRClientVirtualViewBinder clientVirtualViewBinder;
    [SerializeField] private LiveVRClientCameraIsolation clientCameraIsolation;
    [SerializeField] private LiveVRClientEnvironmentLoader clientEnvironmentLoader;
    [SerializeField] private LiveVRClientTargetGuide clientTargetGuide;
    [SerializeField] private LiveSpaceProfileProvider liveSpaceProfileProvider;
    [SerializeField] private LiveVRResetCoordinator resetCoordinator;
    [SerializeField] private bool bridgeHostOnly = true;
    [SerializeField] private int unitIndexToUserIdOffset = 0;
    [SerializeField] private float stalePoseTimeoutSeconds = 0.5f;
    [SerializeField] private bool requireCalibratedPose = true;
    [SerializeField] private bool applyPoseInFixedUpdate = true;
    [SerializeField] private bool applyPoseInLateUpdate = false;
    [SerializeField] private bool logMissingPoses = true;

    [Header("Runtime Checks")]
    [SerializeField] private bool warnIfUnitCountMismatch = true;
    [SerializeField] private bool enableLiveVRPhysicalUserInput = true;
    [SerializeField] private bool forceVisualizationMode = true;
    [SerializeField] private bool forceRealtimeTimeScale = true;
    [SerializeField] private bool forceLiveVRSendRate = true;
    [SerializeField] private float liveVRSendRateHz = 60.0f;
    [SerializeField] private bool forceLiveVRFixedTickRate = true;
    [SerializeField] private float liveVRFixedTickRateHz = 60.0f;
    [SerializeField] private bool forceQuestTargetFrameRate = true;
    [SerializeField] private int questTargetFrameRateHz = 72;

    [Header("Debug Overlay")]
    [SerializeField] private bool showStatusOverlay = true;
    [SerializeField] private Vector2 overlayScreenPosition = new Vector2(12f, 12f);
    [SerializeField] private Vector2 overlayPanelSize = new Vector2(420f, 28f);
    [SerializeField] private float overlayStaleWarningSeconds = 0.5f;

    [Header("Client HUD")]
    [SerializeField] private bool showClientHud = true;
    [SerializeField] private Vector2 clientHudScreenPosition = new Vector2(12f, 160f);
    [SerializeField] private Vector2 clientHudPanelSize = new Vector2(520f, 34f);
    [SerializeField] private float resetPromptVisibleSeconds = 4.0f;

    [Header("Client World HUD")]
    [SerializeField] private bool showClientWorldHud = true;
    [SerializeField] private float clientWorldHudDistanceMeters = 1.25f;
    [SerializeField] private Vector2 clientWorldHudCanvasSize = new Vector2(1150.0f, 550.0f);
    [SerializeField] private float clientWorldHudVerticalOffsetMeters = -0.08f;
    [SerializeField] private float clientWorldHudUpdateIntervalSeconds = 0.1f;

    [Header("Host Controls")]
    [SerializeField] private bool enableHostKeyboardControls = true;
    [SerializeField] private int manualResetPromptUserId = 0;
    [SerializeField] private bool requireUsersInsideLiveSpaceBeforeStart = true;
    [SerializeField] private float liveSpaceSafetyMarginMeters = 0.15f;
    [SerializeField] private float minimumUserDistanceBeforeStartMeters = 0.75f;

    [Header("Live Reset Completion")]
    [SerializeField] private bool enableLiveResetCoordinator = true;
    [SerializeField] private float resetYawErrorThresholdDegrees = 15.0f;
    [SerializeField] private bool requireResetPositionAlignment = true;
    [SerializeField] private float resetPositionToleranceMeters = 0.75f;
    [SerializeField] private bool requireInsideRealSpaceForResetCompletion = false;
    [SerializeField] private float resetStableDurationSeconds = 0.3f;
    [SerializeField] private float resetTimeoutSeconds = 0.0f;
    [SerializeField] private float resetMeaningfulProgressThreshold = 0.02f;
    [SerializeField] private float resetMeaningfulTurnThresholdDegrees = 2.0f;
    [SerializeField] private float resetPromptRepeatSeconds = 0.5f;

    [Header("Network Logging")]
    [SerializeField] private bool enableNetworkLogging = true;
    [SerializeField] private int networkLogEveryNFrames = 5;

    [Header("Virtual View Sync")]
    [SerializeField] private bool enableVirtualPoseSync = true;
    [SerializeField] private bool sendVirtualPoseOnlyWhileRunning = false;
    [SerializeField] private Transform clientVirtualViewRoot;
    [SerializeField] private Transform clientHmdCamera;
    [SerializeField] private bool preserveTrackedHeadLocalOffset = true;
    [SerializeField] private bool requireExplicitClientViewRoot = true;
    [SerializeField] private float clientViewHeightFallbackMeters = 1.6f;
    [SerializeField] private float staleVirtualPoseTimeoutSeconds = 0.5f;
    [SerializeField] private bool applyClientVirtualPoseOnlyWhileRunning = true;

    [Header("Client Camera Isolation")]
    [SerializeField] private bool isolateClientCameras = true;
    [SerializeField] private bool tagClientHmdCameraAsMainCamera = true;
    [SerializeField] private bool applyClientCameraCullingMask = true;
    [SerializeField] private string[] clientCameraVisibleLayers = { "VirtualWall", "UI" };

    [Header("Client Visual Environment")]
    [SerializeField] private bool loadClientVisualEnvironment = true;
    [SerializeField] private GameObject clientVisualEnvironmentPrefab;
    [SerializeField] private bool fallbackToSimulationVirtualSpacePrefab = true;
    [SerializeField] private Transform clientVisualEnvironmentParent;
    [SerializeField] private string clientVisualEnvironmentLayerName = "VirtualWall";
    [SerializeField] private bool applyVirtualSpaceSettingTransformToClientEnvironment = true;
    [SerializeField] private bool disableClientEnvironmentCameras = true;
    [SerializeField] private bool keepOnlyClientWalkableEnvironment = false;
    [SerializeField] private string[] clientWalkableEnvironmentRootNamesToKeep = { "walkingArea", "Floor_Tiles", "Terrain" };
    [SerializeField] private string[] clientWalkableEnvironmentRootNamesToHideInside =
    {
        "Trees",
        "Rocks",
        "Props",
        "Mushrooms",
        "Plants",
        "Water",
        "Mountains",
        "obstacle_*",
        "Cube*"
    };

    [Header("Client Target Guide")]
    [SerializeField] private bool enableClientTargetGuide = true;
    [SerializeField] private bool suppressHostEpisodeTargetsInLiveUser = true;
    [SerializeField] private bool showClientTargetOnlyWhileRunning = true;
    [SerializeField] private LiveVRClientTargetGuideMode clientTargetGuideMode = LiveVRClientTargetGuideMode.Random;
    [SerializeField] private string clientTargetLayerName = "VirtualWall";
    [SerializeField] private bool useClientTargetAreaAnchorBounds = true;
    [SerializeField] private string clientTargetAreaAnchorName = "walkingArea";
    [SerializeField] private GameObject clientTargetPrefab;
    [SerializeField] private Transform clientTargetParent;
    [SerializeField] private float clientTargetAreaWidthMeters = 16.0f;
    [SerializeField] private float clientTargetAreaDepthMeters = 16.0f;
    [SerializeField] private float clientTargetHeightMeters = 1.35f;
    [SerializeField] private float clientTargetRadiusMeters = 0.18f;
    [SerializeField] private float clientTargetReachDistanceMeters = 0.65f;
    [SerializeField] private float clientTargetMinDistanceFromUserMeters = 2.0f;
    [SerializeField] private float clientTargetMinSpawnDistanceMeters = 4.0f;
    [SerializeField] private float clientTargetMaxSpawnDistanceMeters = 8.0f;
    [SerializeField] private int clientTargetCountPerRun = 1;
    [SerializeField] private int clientTargetBaseSeed = 1000;

    [Header("Client Host Connection")]
    [SerializeField] private bool useSavedClientHostConnection = true;
    [SerializeField] private bool saveClientHostConnectionOnStart = true;

    [Header("Live Physical Space")]
    [SerializeField] private bool enableLiveSpaceProfile = true;
    [SerializeField] private LiveSpaceProfileSource liveSpaceSource = LiveSpaceProfileSource.Rectangle;
    [SerializeField] private float liveSpaceRectangleWidthMeters = 10.0f;
    [SerializeField] private float liveSpaceRectangleDepthMeters = 10.0f;
    [SerializeField] private System.Collections.Generic.List<Vector2> liveSpaceManualBoundaryPolygon = new System.Collections.Generic.List<Vector2>();
    [SerializeField] private string liveSpaceName = "Live Physical Space";

    private void Awake()
    {
        ClearLegacyClientRuntimePreferences();
        ApplySavedClientPreferences();
        ApplyCommandLineOverrides();
        EnsureComponents();
        ApplyConfiguration();
        SaveClientPreferencesIfNeeded();
    }

    private void Start()
    {
        ValidateExperimentScene();
    }

    public void ApplyConfiguration()
    {
        float effectiveSendRateHz = ResolveLiveVRSendRateHz();

        if (networkManager != null)
        {
            NormalizeUserSources();
            networkManager.Configure(
                mode,
                localUserId,
                hostAddress,
                hostPosePort,
                effectiveSendRateHz,
                headTransformOverride,
                useUnityXRHeadPose,
                calibrateOnStart,
                false);
            networkManager.ConfigureUserSources(expectedUserCount, userSources);
            networkManager.SetAutoAssignClientUserIds(autoAssignClientUserIds);
            networkManager.ConfigureHostDiscovery(enableHostDiscovery, hostDiscoveryAckTimeoutSeconds, hostDiscoveryIntervalSeconds);
            networkManager.ClearRuntimeSimulatedFallbacks();
            networkManager.SetControllerCalibrationInputEnabled(useControllerPrimaryButtonForCalibration);
            networkManager.SetManualCenterCalibrationRequired(requireManualCenterCalibration);
        }

        if (rdwPoseBridge != null)
        {
            rdwPoseBridge.Configure(
                networkManager,
                bridgeHostOnly,
                unitIndexToUserIdOffset,
                stalePoseTimeoutSeconds,
                requireCalibratedPose,
                false,
                false,
                logMissingPoses);
        }

        if (hmdMovementController != null)
        {
            hmdMovementController.Configure(
                networkManager,
                bridgeHostOnly,
                unitIndexToUserIdOffset,
                stalePoseTimeoutSeconds,
                requireCalibratedPose,
                logMissingPoses,
                userSources);
        }

        if (statusOverlay != null)
        {
            statusOverlay.Configure(
                networkManager,
                showStatusOverlay,
                expectedUserCount,
                overlayScreenPosition,
                overlayPanelSize,
                overlayStaleWarningSeconds);
        }

        if (clientHud != null)
        {
            clientHud.Configure(
                networkManager,
                showClientHud,
                clientHudScreenPosition,
                clientHudPanelSize,
                resetPromptVisibleSeconds);
        }

        if (clientWorldHud != null)
        {
            clientWorldHud.Configure(
                networkManager,
                clientHmdCamera,
                showClientWorldHud,
                clientWorldHudDistanceMeters,
                clientWorldHudCanvasSize,
                clientWorldHudVerticalOffsetMeters,
                clientWorldHudUpdateIntervalSeconds);
        }

        if (hostExperimentController != null)
        {
            hostExperimentController.Configure(
                networkManager,
                expectedUserCount,
                stalePoseTimeoutSeconds,
                enableHostKeyboardControls,
                manualResetPromptUserId,
                requireUsersInsideLiveSpaceBeforeStart,
                liveSpaceSafetyMarginMeters,
                minimumUserDistanceBeforeStartMeters,
                allowSimulatedFallbackForMissingUsersOnStart,
                userSources);
        }

        if (resetCoordinator != null)
        {
            resetCoordinator.Configure(
                networkManager,
                enableLiveResetCoordinator,
                stalePoseTimeoutSeconds,
                resetYawErrorThresholdDegrees,
                requireResetPositionAlignment,
                resetPositionToleranceMeters,
                requireInsideRealSpaceForResetCompletion,
                resetStableDurationSeconds,
                resetTimeoutSeconds,
                resetMeaningfulProgressThreshold,
                resetMeaningfulTurnThresholdDegrees,
                resetPromptRepeatSeconds);
        }

        if (networkLogger != null)
        {
            networkLogger.Configure(
                networkManager,
                enableNetworkLogging,
                expectedUserCount,
                networkLogEveryNFrames,
                stalePoseTimeoutSeconds);
        }

        if (virtualPoseBroadcaster != null)
        {
            virtualPoseBroadcaster.Configure(
                networkManager,
                true,
                unitIndexToUserIdOffset,
                effectiveSendRateHz,
                sendVirtualPoseOnlyWhileRunning);
        }

        if (clientVirtualViewBinder != null)
        {
            clientVirtualViewBinder.Configure(
                networkManager,
                clientVirtualViewRoot,
                clientHmdCamera,
                enableVirtualPoseSync,
                preserveTrackedHeadLocalOffset,
                requireExplicitClientViewRoot,
                clientViewHeightFallbackMeters,
                staleVirtualPoseTimeoutSeconds,
                applyClientVirtualPoseOnlyWhileRunning);
        }

        if (clientPresentationState != null)
        {
            clientPresentationState.Configure(
                networkManager,
                clientVirtualViewBinder,
                clientHud,
                clientWorldHud,
                clientTargetGuide);
            if (networkManager != null)
                networkManager.ConfigureClientPresentationState(clientPresentationState);
        }

        if (clientCameraIsolation != null)
        {
            clientCameraIsolation.Configure(
                networkManager,
                clientHmdCamera,
                isolateClientCameras,
                tagClientHmdCameraAsMainCamera,
                applyClientCameraCullingMask,
                clientCameraVisibleLayers);
        }

        if (clientEnvironmentLoader != null)
        {
            clientEnvironmentLoader.Configure(
                networkManager,
                clientVisualEnvironmentPrefab,
                fallbackToSimulationVirtualSpacePrefab,
                clientVisualEnvironmentParent,
                loadClientVisualEnvironment,
                clientVisualEnvironmentLayerName,
                applyVirtualSpaceSettingTransformToClientEnvironment,
                disableClientEnvironmentCameras,
                keepOnlyClientWalkableEnvironment,
                clientWalkableEnvironmentRootNamesToKeep,
                clientWalkableEnvironmentRootNamesToHideInside);
        }

        if (clientTargetGuide != null)
        {
            clientTargetGuide.Configure(
                networkManager,
                clientHmdCamera,
                clientTargetParent,
                clientTargetPrefab,
                enableClientTargetGuide,
                showClientTargetOnlyWhileRunning,
                clientTargetGuideMode,
                clientTargetLayerName,
                useClientTargetAreaAnchorBounds,
                clientTargetAreaAnchorName,
                clientTargetAreaWidthMeters,
                clientTargetAreaDepthMeters,
                clientTargetHeightMeters,
                clientTargetRadiusMeters,
                clientTargetReachDistanceMeters,
                clientTargetMinDistanceFromUserMeters,
                clientTargetMinSpawnDistanceMeters,
                clientTargetMaxSpawnDistanceMeters,
                clientTargetCountPerRun,
                clientTargetBaseSeed);
        }

        if (liveSpaceProfileProvider != null)
        {
            liveSpaceProfileProvider.Configure(
                enableLiveSpaceProfile,
                liveSpaceSource,
                liveSpaceRectangleWidthMeters,
                liveSpaceRectangleDepthMeters,
                liveSpaceManualBoundaryPolygon,
                liveSpaceName);
        }

        if (forceRealtimeTimeScale)
            Time.timeScale = 1.0f;

        if (forceLiveVRFixedTickRate && liveVRFixedTickRateHz > 0.0f)
            Time.fixedDeltaTime = 1.0f / liveVRFixedTickRateHz;

        if (forceQuestTargetFrameRate && questTargetFrameRateHz > 0)
            Application.targetFrameRate = questTargetFrameRateHz;
    }

    private float ResolveLiveVRSendRateHz()
    {
        if (forceLiveVRSendRate && liveVRSendRateHz > 0.0f)
            return liveVRSendRateHz;

        return sendRateHz;
    }

    private void EnsureComponents()
    {
        if (networkManager == null)
            networkManager = GetComponent<LiveVRNetworkManager>();

        if (networkManager == null)
            networkManager = gameObject.AddComponent<LiveVRNetworkManager>();

        if (rdwPoseBridge == null)
            rdwPoseBridge = GetComponent<LiveVRRdwPoseBridge>();

        if (rdwPoseBridge == null)
            rdwPoseBridge = gameObject.AddComponent<LiveVRRdwPoseBridge>();

        if (hmdMovementController == null)
            hmdMovementController = GetComponent<LiveVRHmdMovementController>();

        if (hmdMovementController == null)
            hmdMovementController = gameObject.AddComponent<LiveVRHmdMovementController>();

        if (statusOverlay == null)
            statusOverlay = GetComponent<LiveVRNetworkStatusOverlay>();

        if (statusOverlay == null)
            statusOverlay = gameObject.AddComponent<LiveVRNetworkStatusOverlay>();

        if (clientHud == null)
            clientHud = GetComponent<LiveVRClientHud>();

        if (clientHud == null)
            clientHud = gameObject.AddComponent<LiveVRClientHud>();

        if (clientWorldHud == null)
            clientWorldHud = GetComponent<LiveVRClientWorldHud>();

        if (clientWorldHud == null)
            clientWorldHud = gameObject.AddComponent<LiveVRClientWorldHud>();

        if (hostExperimentController == null)
            hostExperimentController = GetComponent<LiveVRHostExperimentController>();

        if (hostExperimentController == null)
            hostExperimentController = gameObject.AddComponent<LiveVRHostExperimentController>();

        if (networkLogger == null)
            networkLogger = GetComponent<LiveVRNetworkLogger>();

        if (networkLogger == null)
            networkLogger = gameObject.AddComponent<LiveVRNetworkLogger>();

        if (virtualPoseBroadcaster == null)
            virtualPoseBroadcaster = GetComponent<LiveVRVirtualPoseBroadcaster>();

        if (virtualPoseBroadcaster == null)
            virtualPoseBroadcaster = gameObject.AddComponent<LiveVRVirtualPoseBroadcaster>();

        if (clientVirtualViewBinder == null)
            clientVirtualViewBinder = GetComponent<LiveVRClientVirtualViewBinder>();

        if (clientVirtualViewBinder == null)
            clientVirtualViewBinder = gameObject.AddComponent<LiveVRClientVirtualViewBinder>();

        if (clientPresentationState == null)
            clientPresentationState = GetComponent<LiveVRClientPresentationState>();

        if (clientPresentationState == null)
            clientPresentationState = gameObject.AddComponent<LiveVRClientPresentationState>();

        if (clientCameraIsolation == null)
            clientCameraIsolation = GetComponent<LiveVRClientCameraIsolation>();

        if (clientCameraIsolation == null)
            clientCameraIsolation = gameObject.AddComponent<LiveVRClientCameraIsolation>();

        if (clientEnvironmentLoader == null)
            clientEnvironmentLoader = GetComponent<LiveVRClientEnvironmentLoader>();

        if (clientEnvironmentLoader == null)
            clientEnvironmentLoader = gameObject.AddComponent<LiveVRClientEnvironmentLoader>();

        if (clientTargetGuide == null)
            clientTargetGuide = GetComponent<LiveVRClientTargetGuide>();

        if (clientTargetGuide == null)
            clientTargetGuide = gameObject.AddComponent<LiveVRClientTargetGuide>();

        if (liveSpaceProfileProvider == null)
            liveSpaceProfileProvider = GetComponent<LiveSpaceProfileProvider>();

        if (liveSpaceProfileProvider == null)
            liveSpaceProfileProvider = gameObject.AddComponent<LiveSpaceProfileProvider>();

        if (resetCoordinator == null)
            resetCoordinator = GetComponent<LiveVRResetCoordinator>();

        if (resetCoordinator == null)
            resetCoordinator = gameObject.AddComponent<LiveVRResetCoordinator>();
    }

    private void ValidateExperimentScene()
    {
        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager == null || simulationManager.simulationSetting == null)
            return;

        if (!IsLiveVRModeActive())
        {
            simulationManager.simulationSetting.experimentProfile = ExperimentProfile.Simulation;
            simulationManager.simulationSetting.useLiveVRPhysicalUserInput = false;
            simulationManager.SetMovementControllerOverride(null);
            return;
        }

        Debug.Log(string.Format("[LiveVR] Runtime role={0}. {1}", mode, GetRoleDescription()));

        if (forceVisualizationMode)
            simulationManager.simulationSetting.useVisualization = true;

        if (mode == LiveVRExperimentMode.ClientOnly)
        {
            simulationManager.simulationSetting.experimentProfile = ExperimentProfile.Simulation;
            simulationManager.simulationSetting.useLiveVRPhysicalUserInput = false;
            simulationManager.SetMovementControllerOverride(null);
            simulationManager.BStart = false;
            Debug.Log("[LiveVR] ClientOnly runtime: local RDW simulation is disabled. Waiting for Host ACK/STATE/virtual pose.");
            return;
        }

        if (enableLiveVRPhysicalUserInput && (mode == LiveVRExperimentMode.HostOnly || mode == LiveVRExperimentMode.HostClient))
        {
            simulationManager.simulationSetting.experimentProfile = ExperimentProfile.LiveUser;
            simulationManager.simulationSetting.useLiveVRPhysicalUserInput = true;
            if (suppressHostEpisodeTargetsInLiveUser)
                simulationManager.simulationSetting.showTarget = false;
            simulationManager.SetMovementControllerOverride(hmdMovementController);
        }

        if (forceRealtimeTimeScale)
            simulationManager.simulspeed = 1.0f;

        if (forceLiveVRFixedTickRate && liveVRFixedTickRateHz > 0.0f)
            Time.fixedDeltaTime = 1.0f / liveVRFixedTickRateHz;

        if (warnIfUnitCountMismatch)
        {
            UnitSetting[] units = simulationManager.simulationSetting.unitSettings;
            int configuredUnitCount = units != null ? units.Length : 0;
            if (configuredUnitCount != expectedUserCount)
            {
                Debug.LogWarning(string.Format(
                    "[LiveVR] Expected {0} users, but SimulationSetting.unitSettings has {1}. Match these before running a real user experiment.",
                    expectedUserCount,
                    configuredUnitCount));
            }
        }
    }

    private void NormalizeUserSources()
    {
        int count = Mathf.Max(0, expectedUserCount);
        if (userSources == null || userSources.Length != count)
        {
            LiveVRUserSource[] normalized = new LiveVRUserSource[count];
            for (int i = 0; i < count; i++)
                normalized[i] = LiveVRUserSource.RequiredLiveHmd;
            userSources = normalized;
        }
        else
        {
            for (int i = 0; i < userSources.Length; i++)
                userSources[i] = LiveVRUserSource.RequiredLiveHmd;
        }
    }

    private string GetRoleDescription()
    {
        if (mode == LiveVRExperimentMode.HostOnly)
            return "Host authority: receives HMD poses, runs RDW/redirection/reset, and writes live logs.";

        if (mode == LiveVRExperimentMode.ClientOnly)
            return "Client sensor/view: sends HMD pose and displays Host virtual pose/reset prompts; physical boundary is configured on Host.";

        if (mode == LiveVRExperimentMode.HostClient)
            return "Host+Client: local headset acts as one user while this machine remains RDW authority.";

        return "LiveVR disabled.";
    }

    private bool IsLiveVRModeActive()
    {
        return mode == LiveVRExperimentMode.HostOnly ||
               mode == LiveVRExperimentMode.HostClient ||
               mode == LiveVRExperimentMode.ClientOnly;
    }

    private void ApplyCommandLineOverrides()
    {
        if (!allowCommandLineOverrides)
            return;

        ApplyAndroidIntentOverrides();

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            string value = i + 1 < args.Length ? args[i + 1] : null;

            if (string.Equals(key, "-liveVrMode", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                LiveVRExperimentMode parsedMode;
                if (TryParseMode(value, out parsedMode))
                    mode = parsedMode;
            }
            else if (string.Equals(key, "-liveVrUserId", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                int.TryParse(value, out localUserId);
            }
            else if (string.Equals(key, "-liveVrHost", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                hostAddress = value;
            }
            else if (string.Equals(key, "-liveVrPort", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                int.TryParse(value, out hostPosePort);
            }
            else if (string.Equals(key, "-liveVrUsers", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                int.TryParse(value, out expectedUserCount);
            }
        }
    }

    private void ApplySavedClientPreferences()
    {
        if (!useSavedClientHostConnection)
            return;

        if (mode != LiveVRExperimentMode.ClientOnly)
            return;

        LiveVRClientPreferences.LoadHostConnection(ref hostAddress, ref hostPosePort);
    }

    private void ClearLegacyClientRuntimePreferences()
    {
        if (mode == LiveVRExperimentMode.ClientOnly)
            LiveVRClientPreferences.ClearRuntimeStateKeys();
    }

    private void SaveClientPreferencesIfNeeded()
    {
        if (!saveClientHostConnectionOnStart)
            return;

        if (mode == LiveVRExperimentMode.ClientOnly)
            LiveVRClientPreferences.SaveHostConnection(hostAddress, hostPosePort);
    }

    private void ApplyAndroidIntentOverrides()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent"))
            using (AndroidJavaObject extras = intent.Call<AndroidJavaObject>("getExtras"))
            {
                if (extras == null)
                    return;

                string modeValue = GetIntentString(extras, "liveVrMode", null);
                if (!string.IsNullOrEmpty(modeValue))
                {
                    LiveVRExperimentMode parsedMode;
                    if (TryParseMode(modeValue, out parsedMode))
                        mode = parsedMode;
                }

                localUserId = GetIntentInt(extras, "liveVrUserId", localUserId);
                hostAddress = GetIntentString(extras, "liveVrHost", hostAddress);
                hostPosePort = GetIntentInt(extras, "liveVrPort", hostPosePort);
                expectedUserCount = GetIntentInt(extras, "liveVrUsers", expectedUserCount);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LiveVR] Failed to read Android intent overrides: " + e.Message);
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static string GetIntentString(AndroidJavaObject extras, string key, string fallback)
    {
        if (!extras.Call<bool>("containsKey", key))
            return fallback;

        string value = extras.Call<string>("getString", key);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static int GetIntentInt(AndroidJavaObject extras, string key, int fallback)
    {
        if (!extras.Call<bool>("containsKey", key))
            return fallback;

        return extras.Call<int>("getInt", key, fallback);
    }
#endif

    private static bool TryParseMode(string value, out LiveVRExperimentMode parsedMode)
    {
        parsedMode = LiveVRExperimentMode.Disabled;
        if (string.Equals(value, "host", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "server", StringComparison.OrdinalIgnoreCase))
        {
            parsedMode = LiveVRExperimentMode.HostOnly;
            return true;
        }

        if (string.Equals(value, "client", StringComparison.OrdinalIgnoreCase))
        {
            parsedMode = LiveVRExperimentMode.ClientOnly;
            return true;
        }

        if (string.Equals(value, "hostclient", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "host-client", StringComparison.OrdinalIgnoreCase))
        {
            parsedMode = LiveVRExperimentMode.HostClient;
            return true;
        }

        return Enum.TryParse(value, true, out parsedMode);
    }
}
