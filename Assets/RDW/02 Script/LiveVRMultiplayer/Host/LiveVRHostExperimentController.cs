using UnityEngine;

public class LiveVRHostExperimentController : MonoBehaviour
{
    [HideInInspector]
    [SerializeField] private LiveVRNetworkManager networkManager;
    [HideInInspector]
    [SerializeField] private int expectedUserCount = 2;
    [HideInInspector]
    [SerializeField] private float stalePoseTimeoutSeconds = 0.5f;
    [HideInInspector]
    [SerializeField] private bool enableKeyboardControls = true;
    [HideInInspector]
    [SerializeField] private int manualResetPromptUserId = 0;
    [HideInInspector]
    [SerializeField] private int selectedCalibrationUserId = 0;
    [HideInInspector]
    [SerializeField] private bool requireUsersInsideLiveSpaceBeforeStart = true;
    [HideInInspector]
    [SerializeField] private float liveSpaceSafetyMarginMeters = 0.15f;
    [HideInInspector]
    [SerializeField] private float minimumUserDistanceBeforeStartMeters = 0.75f;
    [HideInInspector]
    [SerializeField] private bool allowSimulatedFallbackForMissingUsersOnStart = false;
    [HideInInspector]
    [SerializeField] private int selectedClientIndex;
    [HideInInspector]
    [SerializeField] private int assignmentUserId;
    [HideInInspector]
    [SerializeField] private bool assignmentProactiveResetEnabled = true;
    [HideInInspector]
    [SerializeField] private LiveVRUserSource[] userSources;

    private GUIStyle panelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private string hostRunId = string.Empty;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        int newExpectedUserCount,
        float newStalePoseTimeoutSeconds,
        bool newEnableKeyboardControls,
        int newManualResetPromptUserId,
        bool newRequireUsersInsideLiveSpaceBeforeStart,
        float newLiveSpaceSafetyMarginMeters,
        float newMinimumUserDistanceBeforeStartMeters,
        bool newAllowSimulatedFallbackForMissingUsersOnStart,
        LiveVRUserSource[] newUserSources)
    {
        networkManager = newNetworkManager;
        expectedUserCount = newExpectedUserCount;
        stalePoseTimeoutSeconds = newStalePoseTimeoutSeconds;
        enableKeyboardControls = newEnableKeyboardControls;
        manualResetPromptUserId = newManualResetPromptUserId;
        selectedCalibrationUserId = Mathf.Clamp(selectedCalibrationUserId, 0, Mathf.Max(0, expectedUserCount - 1));
        requireUsersInsideLiveSpaceBeforeStart = newRequireUsersInsideLiveSpaceBeforeStart;
        liveSpaceSafetyMarginMeters = newLiveSpaceSafetyMarginMeters;
        minimumUserDistanceBeforeStartMeters = newMinimumUserDistanceBeforeStartMeters;
        allowSimulatedFallbackForMissingUsersOnStart = newAllowSimulatedFallbackForMissingUsersOnStart;
        userSources = newUserSources;
    }

    private void Start()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager != null && manager.IsHost)
        {
            EnsureHostRunId();
            manager.SetExperimentState(LiveVRExperimentState.WaitingForUsers);
        }
    }

    private void Update()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        if (manager.ExperimentState == LiveVRExperimentState.WaitingForUsers && AreExpectedUsersReady())
            manager.SetExperimentState(LiveVRExperimentState.Ready);
        else if (manager.ExperimentState == LiveVRExperimentState.Ready && !AreExpectedUsersReady())
            manager.SetExperimentState(LiveVRExperimentState.WaitingForUsers);

        if (!enableKeyboardControls)
            return;

        if (Input.GetKeyDown(KeyCode.F3))
            SelectPreviousUser();
        if (Input.GetKeyDown(KeyCode.F4))
            CalibrateSelectedUser();
        if (Input.GetKeyDown(KeyCode.F5))
            PrepareExperiment();
        if (Input.GetKeyDown(KeyCode.F6))
            StartExperiment();
        if (Input.GetKeyDown(KeyCode.F7))
            StopExperiment();
        if (Input.GetKeyDown(KeyCode.F8))
            SoftRestartRun();
        if (Input.GetKeyDown(KeyCode.F9))
            RecalibrateAndRestart();
    }

    private void OnGUI()
    {
        if (!enableKeyboardControls)
            return;

        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        EnsureGuiStyles();
        LiveVRClientConnectionInfo[] clients = manager.GetClientConnectionsSnapshot();

        Rect panel = new Rect(Screen.width - 360.0f, 12.0f, 348.0f, 318.0f);
        GUI.Box(panel, GUIContent.none, panelStyle);
        GUI.Label(new Rect(panel.x + 12.0f, panel.y + 10.0f, 320.0f, 24.0f), "LiveVR Host Controls", labelStyle);
        GUI.Label(new Rect(panel.x + 12.0f, panel.y + 34.0f, 320.0f, 22.0f), string.Format("Run ID: {0}", GetHostRunIdLabel()), labelStyle);
        GUI.Label(new Rect(panel.x + 12.0f, panel.y + 58.0f, 320.0f, 22.0f), string.Format("Selected User: {0}", selectedCalibrationUserId), labelStyle);
        GUI.Label(new Rect(panel.x + 188.0f, panel.y + 58.0f, 140.0f, 22.0f), GetUserSourceLabel(manager, selectedCalibrationUserId), labelStyle);

        if (GUI.Button(new Rect(panel.x + 12.0f, panel.y + 86.0f, 60.0f, 30.0f), "User -", buttonStyle))
            SelectPreviousUser();
        if (GUI.Button(new Rect(panel.x + 82.0f, panel.y + 86.0f, 60.0f, 30.0f), "User +", buttonStyle))
            SelectNextUser();
        if (GUI.Button(new Rect(panel.x + 152.0f, panel.y + 86.0f, 172.0f, 30.0f), "Calibrate", buttonStyle))
            CalibrateSelectedUser();

        if (GUI.Button(new Rect(panel.x + 12.0f, panel.y + 126.0f, 100.0f, 30.0f), "Check Ready", buttonStyle))
            PrepareExperiment();
        if (GUI.Button(new Rect(panel.x + 118.0f, panel.y + 126.0f, 100.0f, 30.0f), "Start Run", buttonStyle))
            StartExperiment();
        if (GUI.Button(new Rect(panel.x + 224.0f, panel.y + 126.0f, 100.0f, 30.0f), "Stop Run", buttonStyle))
            StopExperiment();

        GUI.Label(
            new Rect(panel.x + 12.0f, panel.y + 164.0f, 320.0f, 36.0f),
            "F3 prev user, F4 calibrate, F5 check, F6 start, F7 stop",
            labelStyle);

        GUI.Label(new Rect(panel.x + 12.0f, panel.y + 204.0f, 320.0f, 22.0f), "Recovery", labelStyle);
        if (GUI.Button(new Rect(panel.x + 12.0f, panel.y + 232.0f, 150.0f, 30.0f), "Soft Restart Run", buttonStyle))
            SoftRestartRun();
        if (GUI.Button(new Rect(panel.x + 170.0f, panel.y + 232.0f, 154.0f, 30.0f), "Recalibrate Restart", buttonStyle))
            RecalibrateAndRestart();

        GUI.Label(
            new Rect(panel.x + 12.0f, panel.y + 270.0f, 320.0f, 38.0f),
            string.Format("Auto assignment active. Clients seen: {0}. F8 soft restart, F9 recalibrate restart.", clients != null ? clients.Length : 0),
            labelStyle);
    }

    public void PrepareExperiment()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        if (!HasRequiredLiveSpaceProfile())
        {
            manager.SetExperimentState(LiveVRExperimentState.WaitingForUsers);
            Debug.LogWarning("[LiveVR] Host is in LiveUser mode but has no valid LiveSpaceProfile. Configure the physical boundary before preparing the experiment.");
            return;
        }

        manager.ClearRuntimeSimulatedFallbacks();
        manager.SetExperimentState(AreExpectedUsersReady() ? LiveVRExperimentState.Ready : LiveVRExperimentState.WaitingForUsers);
    }

    public void StartExperiment()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        if (!HasRequiredLiveSpaceProfile())
        {
            manager.SetExperimentState(LiveVRExperimentState.WaitingForUsers);
            Debug.LogWarning("[LiveVR] Cannot start experiment: LiveUser Host has no valid LiveSpaceProfile.");
            return;
        }

        if (allowSimulatedFallbackForMissingUsersOnStart)
            manager.ActivateSimulatedFallbackForMissingUsers(expectedUserCount, stalePoseTimeoutSeconds, true);
        else
            manager.ClearRuntimeSimulatedFallbacks();

        if (!AreExpectedUsersReady())
        {
            manager.SetExperimentState(LiveVRExperimentState.WaitingForUsers);
            Debug.LogWarning("[LiveVR] Cannot start experiment: " + GetReadinessFailureReason());
            return;
        }

        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager != null)
            simulationManager.StartSimulation();

        manager.SetExperimentState(LiveVRExperimentState.Running);
    }

    public void StopExperiment()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager != null)
            simulationManager.BStart = false;

        manager.SetExperimentState(LiveVRExperimentState.Completed);
    }

    public void SoftRestartRun()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        StopRdwRuntime();
        BeginNewRunSession("soft_restart");
        ResetRdwRuntimeForNextRun();
        manager.ClearRuntimeSimulatedFallbacks();

        LiveVRExperimentState nextState = AreExpectedUsersReady()
            ? LiveVRExperimentState.Ready
            : LiveVRExperimentState.WaitingForUsers;
        manager.SetExperimentState(nextState);
        Debug.Log(string.Format("[LiveVR] Soft Restart Run complete. state={0} runId={1}", nextState, GetHostRunIdLabel()));
    }

    public void RecalibrateAndRestart()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        StopRdwRuntime();
        BeginNewRunSession("recalibrate_restart");
        ResetRdwRuntimeForNextRun();
        manager.ClearRuntimeSimulatedFallbacks();
        manager.ClearHostCalibrationStateForAllUsers();
        manager.SetExperimentState(LiveVRExperimentState.WaitingForUsers);

        for (int userId = 0; userId < expectedUserCount; userId++)
        {
            if (manager.IsUserConnected(userId, stalePoseTimeoutSeconds * 4.0f) && manager.RequiresLivePoseForStart(userId))
                manager.SendClearCalibrationCommand(userId);
        }

        Debug.Log(string.Format("[LiveVR] Recalibrate And Restart complete. Ask users to stand CENTER/FORWARD, then calibrate each user. runId={0}", GetHostRunIdLabel()));
    }

    public void ManualResetPrompt(int userId)
    {
        SendResetPrompt(userId, "MANUAL_TEST", Vector2.zero);
    }

    public void CalibrateSelectedUser()
    {
        CalibrateUser(selectedCalibrationUserId);
    }

    public void CalibrateUser(int userId)
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        if (!manager.IsUserConnected(userId, stalePoseTimeoutSeconds * 4.0f))
        {
            if (!manager.RequiresLivePoseForStart(userId))
                Debug.LogWarning(string.Format("[LiveVR] User {0} is configured for simulation fallback. Connect/assign a headset first if this user should be calibrated as live.", userId));
            else
                Debug.LogWarning(string.Format("[LiveVR] Cannot calibrate user {0}: client is not connected yet.", userId));
            return;
        }

        manager.SendCenterCalibrationCommand(userId);
        Debug.Log(string.Format("[LiveVR] Sent center calibration command to user {0}.", userId));
    }

    public void SelectPreviousUser()
    {
        selectedCalibrationUserId = Mathf.Max(0, selectedCalibrationUserId - 1);
    }

    public void SelectNextUser()
    {
        selectedCalibrationUserId = Mathf.Min(Mathf.Max(0, expectedUserCount - 1), selectedCalibrationUserId + 1);
    }

    public void SendResetPrompt(int userId, string resetType, Vector2 directionHint)
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        manager.SendResetPrompt(userId, resetType, directionHint);
    }

    public void AssignSelectedClient()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        LiveVRClientConnectionInfo[] clients = manager.GetClientConnectionsSnapshot();
        if (clients == null || clients.Length == 0)
        {
            Debug.LogWarning("[LiveVR] Cannot assign client: no connected clients.");
            return;
        }

        selectedClientIndex = Mathf.Clamp(selectedClientIndex, 0, clients.Length - 1);
        assignmentUserId = Mathf.Clamp(assignmentUserId, 0, Mathf.Max(0, expectedUserCount - 1));
        ApplyExperimentModeSelection();
        manager.AssignClientEndpoint(clients[selectedClientIndex].EndpointKey, assignmentUserId, assignmentProactiveResetEnabled);
    }

    private void ApplyExperimentModeSelection()
    {
        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager == null || simulationManager.simulationSetting == null || simulationManager.simulationSetting.proactiveUserReset == null)
            return;

        simulationManager.simulationSetting.proactiveUserReset.enableStrategy = assignmentProactiveResetEnabled;
    }

    private void DrawClientAssignmentGui(Rect panel, LiveVRClientConnectionInfo[] clients)
    {
        if (clients == null || clients.Length == 0)
        {
            GUI.Label(new Rect(panel.x + 12.0f, panel.y + 230.0f, 320.0f, 24.0f), "No client HELLO/POSE yet.", labelStyle);
            return;
        }

        selectedClientIndex = Mathf.Clamp(selectedClientIndex, 0, clients.Length - 1);
        LiveVRClientConnectionInfo selected = clients[selectedClientIndex];

        GUI.Label(
            new Rect(panel.x + 12.0f, panel.y + 230.0f, 320.0f, 42.0f),
            string.Format(
                "#{0}/{1} {2}\nreported={3} assigned={4} hello={5} pose={6}",
                selectedClientIndex + 1,
                clients.Length,
                selected.EndpointKey,
                selected.ReportedUserId,
                selected.AssignedUserId >= 0 ? selected.AssignedUserId.ToString() : "-",
                FormatAge(selected.HelloAgeSeconds),
                FormatAge(selected.PoseAgeSeconds)),
            labelStyle);

        if (GUI.Button(new Rect(panel.x + 12.0f, panel.y + 278.0f, 76.0f, 28.0f), "Client -", buttonStyle))
            selectedClientIndex = Mathf.Max(0, selectedClientIndex - 1);
        if (GUI.Button(new Rect(panel.x + 96.0f, panel.y + 278.0f, 76.0f, 28.0f), "Client +", buttonStyle))
            selectedClientIndex = Mathf.Min(clients.Length - 1, selectedClientIndex + 1);

        if (GUI.Button(new Rect(panel.x + 184.0f, panel.y + 278.0f, 68.0f, 28.0f), "User -", buttonStyle))
            assignmentUserId = Mathf.Max(0, assignmentUserId - 1);
        if (GUI.Button(new Rect(panel.x + 260.0f, panel.y + 278.0f, 64.0f, 28.0f), "User +", buttonStyle))
            assignmentUserId = Mathf.Min(Mathf.Max(0, expectedUserCount - 1), assignmentUserId + 1);

        GUI.Label(new Rect(panel.x + 12.0f, panel.y + 312.0f, 132.0f, 24.0f), string.Format("Assign User: {0}", assignmentUserId), labelStyle);
        assignmentProactiveResetEnabled = GUI.Toggle(
            new Rect(panel.x + 150.0f, panel.y + 312.0f, 174.0f, 24.0f),
            assignmentProactiveResetEnabled,
            "Proactive Reset");

        if (GUI.Button(new Rect(panel.x + 12.0f, panel.y + 342.0f, 312.0f, 28.0f), "Assign Selected Client", buttonStyle))
            AssignSelectedClient();
    }

    private static string FormatAge(float seconds)
    {
        if (float.IsInfinity(seconds))
            return "-";

        return string.Format("{0:F1}s", seconds);
    }

    private bool AreExpectedUsersReady()
    {
        return string.IsNullOrEmpty(GetReadinessFailureReason());
    }

    private string GetReadinessFailureReason()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null)
            return "network manager missing";

        for (int userId = 0; userId < expectedUserCount; userId++)
        {
            LiveVRUserSource source = GetConfiguredUserSource(userId);
            if (manager.ShouldUseSimulatedUser(userId, stalePoseTimeoutSeconds, true))
                continue;

            bool liveRequired = source != LiveVRUserSource.SimulatedOnly;
            bool liveReady = manager.IsLivePoseReady(userId, stalePoseTimeoutSeconds, true);
            if (!liveRequired && !liveReady)
                continue;

            LiveVRPoseSample sample;
            if (!manager.TryGetPose(userId, out sample))
                return string.Format("user {0} has no pose stream", userId);

            if (sample.AgeSeconds > stalePoseTimeoutSeconds)
                return string.Format("user {0} pose is stale ({1:F2}s)", userId, sample.AgeSeconds);

            if (!sample.IsCalibrated)
                return string.Format("user {0} is not calibrated", userId);

            if (requireUsersInsideLiveSpaceBeforeStart && !IsUserInsideLiveSpace(userId))
                return string.Format("user {0} is outside LiveSpace or inside safety margin", userId);
        }

        if (!AreUsersSafelySeparated(manager))
            return string.Format("users are closer than {0:F2}m", minimumUserDistanceBeforeStartMeters);

        return string.Empty;
    }

    private bool IsUserInsideLiveSpace(int userId)
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        LiveSpaceProfileProvider provider = LiveSpaceProfileProvider.Instance;
        if (manager == null || provider == null || !provider.IsEnabled)
            return false;

        LiveVRPoseSample sample;
        if (!manager.TryGetPose(userId, out sample))
            return false;

        bool contains;
        return provider.TryContainsPoint(sample.ExperimentPosition, liveSpaceSafetyMarginMeters, out contains) && contains;
    }

    private bool AreUsersSafelySeparated(LiveVRNetworkManager manager)
    {
        if (minimumUserDistanceBeforeStartMeters <= 0.0f)
            return true;

        for (int a = 0; a < expectedUserCount; a++)
        {
            LiveVRPoseSample sampleA;
            if (!ShouldIncludeUserPoseInStartSafety(manager, a) || !manager.TryGetPose(a, out sampleA))
                continue;

            for (int b = a + 1; b < expectedUserCount; b++)
            {
                LiveVRPoseSample sampleB;
                if (!ShouldIncludeUserPoseInStartSafety(manager, b) || !manager.TryGetPose(b, out sampleB))
                    continue;

                if (Vector2.Distance(sampleA.ExperimentPosition, sampleB.ExperimentPosition) < minimumUserDistanceBeforeStartMeters)
                    return false;
            }
        }

        return true;
    }

    private bool ShouldIncludeUserPoseInStartSafety(LiveVRNetworkManager manager, int userId)
    {
        if (manager == null)
            return false;

        return manager.IsLivePoseReady(userId, stalePoseTimeoutSeconds, true);
    }

    private bool HasRequiredLiveSpaceProfile()
    {
        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager == null || simulationManager.simulationSetting == null)
            return true;

        SimulationSetting setting = simulationManager.simulationSetting;
        bool liveUserMode = setting.experimentProfile == ExperimentProfile.LiveUser || setting.useLiveVRPhysicalUserInput;
        if (!liveUserMode)
            return true;

        LiveSpaceProfileProvider provider = LiveSpaceProfileProvider.Instance;
        if (provider == null || !provider.IsEnabled)
            return false;

        provider.GetStatusText();
        return provider.HasActiveProfile;
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }

    private LiveVRUserSource GetConfiguredUserSource(int userId)
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager != null)
            return manager.GetConfiguredUserSource(userId);

        if (userSources == null || userId < 0 || userId >= userSources.Length)
            return LiveVRUserSource.RequiredLiveHmd;

        return userSources[userId];
    }

    private string GetUserSourceLabel(LiveVRNetworkManager manager, int userId)
    {
        if (manager == null)
            return "source=?";

        return manager.GetUserSourceLabel(userId, stalePoseTimeoutSeconds, true);
    }

    private void StopRdwRuntime()
    {
        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager != null)
            simulationManager.BStart = false;
    }

    private void ResetRdwRuntimeForNextRun()
    {
        if (_GCM.GlobalCoordinationManager.instance != null)
        {
            _GCM.GlobalCoordinationManager.instance.ResetEpisode();
            StopRdwRuntime();
            return;
        }

        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager != null)
        {
            simulationManager.BStart = false;
            simulationManager.StartSimulation();
            simulationManager.BStart = false;
        }
    }

    private void BeginNewRunSession(string reason)
    {
        LiveVRNetworkLogger logger = FindObjectOfType<LiveVRNetworkLogger>();
        if (logger != null)
            logger.FlushAndResetSession();

        if (_GCM.GM_DataRecord.instance != null)
            hostRunId = _GCM.GM_DataRecord.instance.BeginNewRunSession(reason);
        else
            hostRunId = string.Empty;
    }

    private void EnsureHostRunId()
    {
        if (!string.IsNullOrEmpty(hostRunId))
            return;

        if (_GCM.GM_DataRecord.instance != null)
        {
            hostRunId = _GCM.GM_DataRecord.instance.GetRunId();
            Debug.Log("[LiveVR] Host run ID for questionnaire/log matching: " + hostRunId);
        }
        else
        {
            hostRunId = "NO_GM_DATARECORD";
            Debug.LogWarning("[LiveVR] Cannot create run ID: GM_DataRecord.instance is missing.");
        }
    }

    private string GetHostRunIdLabel()
    {
        EnsureHostRunId();
        return string.IsNullOrEmpty(hostRunId) ? "creating..." : hostRunId;
    }

    private void EnsureGuiStyles()
    {
        if (panelStyle != null && buttonStyle != null && labelStyle != null)
            return;

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.textColor = Color.white;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 14;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 14;
        labelStyle.normal.textColor = Color.white;
        labelStyle.wordWrap = true;
    }
}
