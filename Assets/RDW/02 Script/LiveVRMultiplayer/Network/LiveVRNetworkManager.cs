using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.XR;

public enum LiveVRExperimentMode
{
    Disabled = 0,
    HostOnly = 1,
    ClientOnly = 2,
    HostClient = 3
}

public struct LiveVRClientConnectionInfo
{
    public string EndpointKey;
    public string DeviceKey;
    public string DeviceName;
    public string ClientSessionId;
    public string Address;
    public int Port;
    public int ReportedUserId;
    public int AssignedUserId;
    public string AssignmentStatus;
    public bool ProactiveResetEnabled;
    public long LastHelloReceiveUnixMilliseconds;
    public long LastPoseReceiveUnixMilliseconds;

    public float HelloAgeSeconds
    {
        get
        {
            if (LastHelloReceiveUnixMilliseconds <= 0)
                return float.PositiveInfinity;

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Mathf.Max(0.0f, (now - LastHelloReceiveUnixMilliseconds) / 1000.0f);
        }
    }

    public float PoseAgeSeconds
    {
        get
        {
            if (LastPoseReceiveUnixMilliseconds <= 0)
                return float.PositiveInfinity;

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Mathf.Max(0.0f, (now - LastPoseReceiveUnixMilliseconds) / 1000.0f);
        }
    }
}

public class LiveVRNetworkManager : MonoBehaviour
{
    public static LiveVRNetworkManager Instance { get; private set; }

    [HideInInspector]
    [SerializeField] private LiveVRExperimentMode mode = LiveVRExperimentMode.Disabled;
    [HideInInspector]
    [SerializeField] private int localUserId = 0;

    [HideInInspector]
    [SerializeField] private string hostAddress = "192.168.1.100";
    [HideInInspector]
    [SerializeField] private int hostPosePort = 47770;
    [HideInInspector]
    [SerializeField] private float sendRateHz = 60.0f;
    [HideInInspector]
    [SerializeField] private bool enableHostDiscovery = true;
    [HideInInspector]
    [SerializeField] private float hostDiscoveryAckTimeoutSeconds = 3.0f;
    [HideInInspector]
    [SerializeField] private float hostDiscoveryIntervalSeconds = 2.0f;

    [HideInInspector]
    [SerializeField] private Transform headTransformOverride;
    [HideInInspector]
    [SerializeField] private bool useUnityXRHeadPose = true;
    [HideInInspector]
    [SerializeField] private bool calibrateOnStart = true;
    [HideInInspector]
    [SerializeField] private KeyCode recalibrateKey = KeyCode.C;
    [HideInInspector]
    [SerializeField] private bool useControllerPrimaryButtonForCalibration = true;
    [HideInInspector]
    [SerializeField] private bool requireManualCenterCalibration = true;

    [HideInInspector]
    [SerializeField] private Vector2 experimentOriginOffset = Vector2.zero;
    [HideInInspector]
    [SerializeField] private float experimentYawOffsetDegrees = 0.0f;
    [HideInInspector]
    [SerializeField] private float metersScale = 1.0f;
    [HideInInspector]
    [SerializeField] private bool enablePoseJumpGuard = true;
    [HideInInspector]
    [SerializeField] private float maxCalibratedPoseStepMeters = 0.75f;
    [HideInInspector]
    [SerializeField] private float maxCalibratedPoseSpeedMetersPerSecond = 4.0f;
    [HideInInspector]
    [SerializeField] private float maxCalibratedYawRateDegreesPerSecond = 720.0f;

    private readonly object posesLock = new object();
    private readonly object endpointsLock = new object();
    private readonly object assignmentLock = new object();
    private readonly object clientStateLock = new object();
    private readonly object clientGainRateLogLock = new object();
    private readonly Dictionary<int, LiveVRPoseSample> latestPoses = new Dictionary<int, LiveVRPoseSample>();
    private readonly Dictionary<int, IPEndPoint> clientEndpoints = new Dictionary<int, IPEndPoint>();
    private readonly Dictionary<int, long> latestHelloReceiveUnixMs = new Dictionary<int, long>();
    private readonly Dictionary<int, long> nextPoseJumpWarningUnixMsByUserId = new Dictionary<int, long>();
    private readonly Dictionary<int, long> nextCalibrationInvalidWarningUnixMsByUserId = new Dictionary<int, long>();
    private readonly Dictionary<string, LiveVRClientConnectionInfo> clientConnectionsByEndpoint = new Dictionary<string, LiveVRClientConnectionInfo>();
    private readonly Dictionary<int, string> assignedEndpointByUserId = new Dictionary<int, string>();
    private readonly Dictionary<string, int> assignedUserIdByDeviceKey = new Dictionary<string, int>();
    private readonly Dictionary<int, string> assignedDeviceKeyByUserId = new Dictionary<int, string>();

    private UdpClient hostReceiver;
    private UdpClient clientSender;
    private IPEndPoint hostEndPoint;
    private Thread receiverThread;
    private Thread clientReceiverThread;
    private volatile bool receiverRunning;
    private volatile bool clientReceiverRunning;
    private bool hasCalibration;
    private Vector2 calibrationOriginPosition;
    private float calibrationOriginYaw;
    private float nextSendTime;
    private uint sequence;
    private LiveVRExperimentState experimentState = LiveVRExperimentState.Idle;
    private int resetPromptEventId;
    private uint lastAckSequence;
    private long lastAckUnixMilliseconds;
    private LiveVRExperimentState lastHostExperimentState = LiveVRExperimentState.Idle;
    private bool hasResetPrompt;
    private LiveVRResetPromptMessage latestResetPrompt;
    private long lastResetPromptReceiveUnixMilliseconds;
    private bool hasResetStart;
    private LiveVRResetStartMessage latestResetStart;
    private long lastResetStartReceiveUnixMilliseconds;
    private readonly Dictionary<int, int> latestResetDoneEventByUserId = new Dictionary<int, int>();
    private readonly Dictionary<int, LiveVRResetDoneMessage> latestResetDoneByUserId = new Dictionary<int, LiveVRResetDoneMessage>();
    private readonly HashSet<int> targetReachedUserIds = new HashSet<int>();
    private bool hasVirtualPose;
    private LiveVRVirtualPoseMessage latestVirtualPose;
    private long latestVirtualPoseReceiveUnixMilliseconds;
    private uint virtualPoseSequence;
    private int centerCalibrationEventId;
    private int clearCalibrationEventId;
    private int lastCenterCalibrationCommandEventId = -1;
    private long lastCenterCalibrationCommandUnixMs;
    private long lastCenterCalibrationCompleteUnixMs;
    private string lastCenterCalibrationStatus = "waiting for host command";
    private bool wasControllerPrimaryButtonPressed;
    private float nextHelloTime;
    private float nextHostDiscoveryTime;
    private uint sentPosePacketCount;
    private uint sentHelloPacketCount;
    private uint sentHostDiscoveryPacketCount;
    private float clientPoseRateWindowStartTime;
    private int clientPoseRateWindowCount;
    private uint clientPoseRateWindowStartSequence;
    private uint clientPoseRateWindowLastSequence;
    private bool hasClientPoseRateWindow;
    private float nextClientPoseRateLogTime;
    private long clientGainRateWindowStartUnixMs;
    private int clientGainReceivedWindowCount;
    private int clientGainReceivedNonZeroWindowCount;
    private int clientGainReceivedUndefinedWindowCount;
    private uint clientGainReceivedStartSequence;
    private uint clientGainReceivedLastSequence;
    private bool hasClientGainReceivedWindow;
    private float clientGainReceivedAbsRateSum;
    private float clientGainReceivedMaxAbsRate;
    private GainType clientGainReceivedLastType = GainType.Undefined;
    private float clientGainReceivedLastRate;
    private float clientGainReceivedLastValidSeconds;
    private float nextClientGainReceivedLogTime;
    private string lastClientSendError = string.Empty;
    private string lastPoseSourceStatus = "not sampled";
    private bool hasLastAcceptedLocalPose;
    private LiveVRPoseSample lastAcceptedLocalPose;
    private string hostDiscoveryStatus = "idle";
    private uint hostRawPacketCount;
    private uint hostPosePacketCount;
    private uint hostHelloPacketCount;
    private uint hostParseFailCount;
    private string hostLastRawPacketPreview = string.Empty;
    private string hostLastRemoteEndpoint = string.Empty;
    private bool waitForClientStartupConfirmation;
    private bool clientStartupConfirmed = true;
    private bool clientProactiveResetEnabled = true;
    private bool hasHostAssignment;
    private bool autoAssignClientUserIds = true;
    private int expectedUserCountForAssignment;
    private string hostRunId = string.Empty;
    private string lastAssignedHostRunId = string.Empty;
    private string clientSessionId = Guid.NewGuid().ToString("N");
    private string clientDeviceKey = string.Empty;
    private string clientDeviceName = string.Empty;
    private string clientAssignmentStatus = "waiting for host assignment";
    private int targetSeed = int.MinValue;
    private int targetSeedVersion = 0;
    private LiveVRUserSource[] userSources = new LiveVRUserSource[0];
    private readonly HashSet<int> runtimeSimulatedFallbackUsers = new HashSet<int>();
    private readonly LiveVRProtocolVersionContext protocolContext = new LiveVRProtocolVersionContext();
    private readonly LiveVRReliableControlService reliableControl = new LiveVRReliableControlService();
    private LiveVRClientPresentationState clientPresentationState;
    private LiveVRTrialEndState lastTrialEndState = LiveVRTrialEndState.Normal;
    private readonly Dictionary<int, LiveVRUserConnectionState> userConnectionStates = new Dictionary<int, LiveVRUserConnectionState>();

    public LiveVRExperimentMode Mode { get { return mode; } }
    public int LocalUserId { get { return localUserId; } }
    public int HostPosePort { get { return hostPosePort; } }
    public string HostAddress { get { return hostAddress; } }
    public LiveVRExperimentState ExperimentState { get { return IsHost ? experimentState : lastHostExperimentState; } }
    public bool HasCalibration { get { return hasCalibration; } }
    public bool IsConnectedToHost { get { return LastAckAgeSeconds <= 1.5f; } }
    public float LastAckAgeSeconds
    {
        get
        {
            lock (clientStateLock)
            {
                if (lastAckUnixMilliseconds <= 0)
                    return float.PositiveInfinity;

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return Mathf.Max(0.0f, (now - lastAckUnixMilliseconds) / 1000.0f);
            }
        }
    }
    public uint LastAckSequence
    {
        get
        {
            lock (clientStateLock)
            {
                return lastAckSequence;
            }
        }
    }
    public bool IsHost { get { return mode == LiveVRExperimentMode.HostOnly || mode == LiveVRExperimentMode.HostClient; } }
    public bool SendsLocalPose { get { return mode == LiveVRExperimentMode.ClientOnly || mode == LiveVRExperimentMode.HostClient; } }
    public uint SentPosePacketCount { get { return sentPosePacketCount; } }
    public uint SentHelloPacketCount { get { return sentHelloPacketCount; } }
    public uint SentHostDiscoveryPacketCount { get { return sentHostDiscoveryPacketCount; } }
    public string LastClientSendError { get { return lastClientSendError; } }
    public string LastPoseSourceStatus { get { return lastPoseSourceStatus; } }
    public string HostDiscoveryStatus { get { return hostDiscoveryStatus; } }
    public uint HostRawPacketCount { get { return hostRawPacketCount; } }
    public uint HostPosePacketCount { get { return hostPosePacketCount; } }
    public uint HostHelloPacketCount { get { return hostHelloPacketCount; } }
    public uint HostParseFailCount { get { return hostParseFailCount; } }
    public string HostLastRawPacketPreview { get { return hostLastRawPacketPreview; } }
    public string HostLastRemoteEndpoint { get { return hostLastRemoteEndpoint; } }
    public int LastCenterCalibrationCommandEventId { get { return lastCenterCalibrationCommandEventId; } }
    public string LastCenterCalibrationStatus { get { return lastCenterCalibrationStatus; } }
    public bool HasReceivedCenterCalibrationCommand { get { return lastCenterCalibrationCommandEventId >= 0; } }
    public bool HasHostAssignment { get { return mode != LiveVRExperimentMode.ClientOnly || hasHostAssignment; } }
    public bool ClientProactiveResetEnabled { get { return clientProactiveResetEnabled; } }
    public string ClientAssignmentStatus { get { return clientAssignmentStatus; } }
    public int RestartEpoch { get { return protocolContext.RestartEpoch; } }
    public int CalibrationVersion { get { return protocolContext.CalibrationVersion; } }
    public int ReliableControlPendingCount { get { return reliableControl.PendingCount; } }
    public int ReliableControlRetryCountTotal { get { return reliableControl.RetryCountTotal; } }
    public int ReliableControlTimeoutCountTotal { get { return reliableControl.TimeoutCountTotal; } }
    public LiveVRTrialEndState LastTrialEndState { get { return lastTrialEndState; } }
    public string CurrentRunId { get { return protocolContext.RunId; } }
    public int TargetSeed { get { return targetSeed; } }
    public int TargetSeedVersion { get { return targetSeedVersion; } }
    public bool IsWaitingForClientStartupConfirmation
    {
        get { return mode == LiveVRExperimentMode.ClientOnly && waitForClientStartupConfirmation && !clientStartupConfirmed; }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[LiveVR] Multiple LiveVRNetworkManager instances detected. Keeping the newest one active.");
        }

        Instance = this;
        ConfigureReliableControl();
    }

    private void Start()
    {
        if (mode == LiveVRExperimentMode.Disabled)
            return;

        if (IsHost)
            StartHostReceiver();

        if (mode == LiveVRExperimentMode.ClientOnly && clientStartupConfirmed)
            EnsureClientTransportStarted();

        if (calibrateOnStart && SendsLocalPose && !requireManualCenterCalibration)
            CalibrateNow();
    }

    private void Update()
    {
        if (mode == LiveVRExperimentMode.Disabled)
            return;

        reliableControl.Tick();
        LogClientGainReceivedRateIfNeeded();

        if (IsWaitingForClientStartupConfirmation)
            return;

        if (!requireManualCenterCalibration && IsRecalibrateRequested())
            CalibrateNow();

        if (mode == LiveVRExperimentMode.ClientOnly && Time.unscaledTime >= nextHelloTime)
        {
            nextHelloTime = Time.unscaledTime + 1.0f;
            SendHelloToHost();
        }

        if (mode == LiveVRExperimentMode.ClientOnly)
            TickHostDiscovery();

        if (mode == LiveVRExperimentMode.ClientOnly && !hasHostAssignment)
            return;

        if (!SendsLocalPose || Time.unscaledTime < nextSendTime)
            return;

        float interval = sendRateHz > 0.0f ? 1.0f / sendRateHz : 0.033f;
        nextSendTime = Time.unscaledTime + interval;

        LiveVRPoseSample sample;
        if (!TryBuildLocalPoseSample(out sample))
            return;

        StorePose(sample);

        if (mode == LiveVRExperimentMode.ClientOnly)
            SendPoseToHost(sample);
    }

    private void OnDestroy()
    {
        StopHostReceiver();
        StopClientReceiver();

        if (clientSender != null)
        {
            clientSender.Close();
            clientSender = null;
        }

        if (Instance == this)
            Instance = null;
    }

    public void Configure(
        LiveVRExperimentMode newMode,
        int newLocalUserId,
        string newHostAddress,
        int newHostPosePort,
        float newSendRateHz,
        Transform newHeadTransformOverride,
        bool newUseUnityXRHeadPose,
        bool newCalibrateOnStart,
        bool newWaitForClientStartupConfirmation)
    {
        mode = newMode;
        localUserId = newLocalUserId;
        hostAddress = newHostAddress;
        hostPosePort = newHostPosePort;
        sendRateHz = newSendRateHz;
        headTransformOverride = newHeadTransformOverride;
        useUnityXRHeadPose = newUseUnityXRHeadPose;
        calibrateOnStart = newCalibrateOnStart;
        waitForClientStartupConfirmation = false;
        clientStartupConfirmed = true;
        hasHostAssignment = mode != LiveVRExperimentMode.ClientOnly;
        clientAssignmentStatus = hasHostAssignment ? "host/local mode" : "waiting for host assignment";
        ConfigureReliableControl();
    }

    public void ConfigureClientPresentationState(LiveVRClientPresentationState presentationState)
    {
        clientPresentationState = presentationState;
    }

    public void SetHostRunId(string newHostRunId)
    {
        if (!string.IsNullOrEmpty(newHostRunId))
            hostRunId = newHostRunId;

        protocolContext.HostRunId = hostRunId;
        protocolContext.RunId = hostRunId;
    }

    public int BeginRestartEpoch()
    {
        return protocolContext.IncrementRestartEpoch();
    }

    public int IncrementCalibrationVersion()
    {
        return protocolContext.IncrementCalibrationVersion();
    }

    public void SetTrialEndState(LiveVRTrialEndState endState)
    {
        lastTrialEndState = endState;
    }

    public void ClearTargetReachedRuntime()
    {
        lock (clientStateLock)
        {
            targetReachedUserIds.Clear();
        }
    }

    public bool IsUserRunComplete(int userId)
    {
        lock (clientStateLock)
        {
            return targetReachedUserIds.Contains(userId);
        }
    }

    public bool MarkUserRunCompleteFromHost(int userId, float cumulativeDistanceMeters, string reason)
    {
        bool added;
        bool allTargetsReached;
        lock (clientStateLock)
        {
            added = targetReachedUserIds.Add(userId);
            allTargetsReached = AreAllExpectedTargetsReachedLocked();
        }

        if (added)
        {
            Debug.Log(string.Format(
                "[LiveVR] Host marked user {0} complete reason={1} cumulative={2:F2}.",
                userId,
                string.IsNullOrEmpty(reason) ? "distance_goal" : reason,
                cumulativeDistanceMeters));
        }

        if (experimentState == LiveVRExperimentState.Running && allTargetsReached)
        {
            SetTrialEndState(LiveVRTrialEndState.Normal);
            SetExperimentState(LiveVRExperimentState.Completed);
            Debug.Log("[LiveVR] All expected users reached their target distance. Trial completed.");
        }

        return added;
    }

    public bool AreAllExpectedUsersRunComplete()
    {
        lock (clientStateLock)
        {
            return AreAllExpectedTargetsReachedLocked();
        }
    }

    public bool HasPendingReliableControlType(string messageType)
    {
        return reliableControl.HasPendingType(messageType);
    }

    public bool HasTimedOutReliableControlType(string messageType)
    {
        return reliableControl.HasTimedOutType(messageType);
    }

    public void ClearAllClientResetState()
    {
        lock (clientStateLock)
        {
            hasResetPrompt = false;
            latestResetPrompt = default(LiveVRResetPromptMessage);
            lastResetPromptReceiveUnixMilliseconds = 0;
            hasResetStart = false;
            latestResetStart = default(LiveVRResetStartMessage);
            lastResetStartReceiveUnixMilliseconds = 0;
            hasVirtualPose = false;
            latestVirtualPose = default(LiveVRVirtualPoseMessage);
            latestVirtualPoseReceiveUnixMilliseconds = 0;
        }
    }

    public void BroadcastClientResetClear(string reason)
    {
        if (!IsHost)
            return;

        LiveVRClientResetClearMessage clear = new LiveVRClientResetClearMessage
        {
            UserId = -1,
            RestartEpoch = protocolContext.RestartEpoch,
            Reason = reason,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        int targetCount = 0;
        for (int userId = 0; userId < expectedUserCountForAssignment; userId++)
        {
            if (IsUserConnected(userId, 10.0f))
            {
                SendReliableToUser(userId, "CLIENT_RESET_CLEAR", clear.ToNetworkMessage());
                targetCount++;
            }
        }

        Debug.Log(string.Format(
            "[LiveVR] Broadcast CLIENT_RESET_CLEAR reason={0} restartEpoch={1} targets={2} pending={3}.",
            string.IsNullOrEmpty(reason) ? "unknown" : reason,
            protocolContext.RestartEpoch,
            targetCount,
            reliableControl.PendingCount));
    }

    public void ConfirmClientStartup()
    {
        if (mode != LiveVRExperimentMode.ClientOnly)
            return;

        clientStartupConfirmed = true;
        EnsureClientTransportStarted();
    }

    public void SetClientStartupConfirmationRequired(bool required)
    {
        waitForClientStartupConfirmation = false;
        clientStartupConfirmed = true;
    }

    public void SetClientProactiveResetEnabled(bool enabled)
    {
        clientProactiveResetEnabled = enabled;
    }

    public void ConfigureUserSources(int expectedUserCount, LiveVRUserSource[] configuredUserSources)
    {
        int count = Mathf.Max(0, expectedUserCount);
        expectedUserCountForAssignment = count;
        userSources = new LiveVRUserSource[count];
        for (int i = 0; i < count; i++)
            userSources[i] = ResolveConfiguredUserSource(configuredUserSources, i);

        PruneAssignmentsToExpectedUserCount(count);
    }

    public void SetAutoAssignClientUserIds(bool enabled)
    {
        autoAssignClientUserIds = enabled;
    }

    public void ConfigureHostDiscovery(bool enabled, float ackTimeoutSeconds, float intervalSeconds)
    {
        enableHostDiscovery = enabled;
        hostDiscoveryAckTimeoutSeconds = Mathf.Max(0.25f, ackTimeoutSeconds);
        hostDiscoveryIntervalSeconds = Mathf.Max(0.25f, intervalSeconds);
    }

    private void PruneAssignmentsToExpectedUserCount(int expectedUserCount)
    {
        lock (assignmentLock)
        {
            lock (endpointsLock)
            {
                List<int> userIdsToRemove = new List<int>();
                foreach (int userId in assignedEndpointByUserId.Keys)
                {
                    if (userId < 0 || userId >= expectedUserCount)
                        userIdsToRemove.Add(userId);
                }

                for (int i = 0; i < userIdsToRemove.Count; i++)
                {
                    int userId = userIdsToRemove[i];
                    string deviceKey;
                    if (assignedDeviceKeyByUserId.TryGetValue(userId, out deviceKey))
                        assignedUserIdByDeviceKey.Remove(deviceKey);

                    assignedDeviceKeyByUserId.Remove(userId);
                    assignedEndpointByUserId.Remove(userId);
                    clientEndpoints.Remove(userId);
                }

                List<string> endpointKeys = new List<string>(clientConnectionsByEndpoint.Keys);
                for (int i = 0; i < endpointKeys.Count; i++)
                {
                    LiveVRClientConnectionInfo info = clientConnectionsByEndpoint[endpointKeys[i]];
                    if (info.AssignedUserId >= expectedUserCount)
                    {
                        info.AssignedUserId = -1;
                        info.AssignmentStatus = "unassigned: user count changed";
                        clientConnectionsByEndpoint[endpointKeys[i]] = info;
                    }
                }
            }
        }
    }

    public void ClearRuntimeSimulatedFallbacks()
    {
        runtimeSimulatedFallbackUsers.Clear();
    }

    public int ActivateSimulatedFallbackForMissingUsers(int expectedUserCount, float staleTimeoutSeconds, bool requireCalibratedPose)
    {
        int activatedCount = 0;
        int count = Mathf.Max(0, expectedUserCount);
        for (int userId = 0; userId < count; userId++)
        {
            if (GetConfiguredUserSource(userId) == LiveVRUserSource.SimulatedOnly)
                continue;

            if (IsLivePoseReady(userId, staleTimeoutSeconds, requireCalibratedPose))
                continue;

            if (runtimeSimulatedFallbackUsers.Add(userId))
                activatedCount++;
        }

        if (activatedCount > 0)
            Debug.Log(string.Format("[LiveVR] Activated simulated fallback for {0} missing live user(s).", activatedCount));

        return activatedCount;
    }

    public LiveVRUserSource GetConfiguredUserSource(int userId)
    {
        if (userId < 0)
            return LiveVRUserSource.RequiredLiveHmd;

        if (userSources == null || userId >= userSources.Length)
            return LiveVRUserSource.RequiredLiveHmd;

        return userSources[userId];
    }

    public bool IsLivePoseReady(int userId, float staleTimeoutSeconds, bool requireCalibratedPose)
    {
        LiveVRPoseSample sample;
        if (!TryGetPose(userId, out sample))
            return false;

        if (requireCalibratedPose && !sample.IsCalibrated)
            return false;

        if (requireCalibratedPose && !IsPoseCalibrationCurrent(sample))
            return false;

        return sample.AgeSeconds <= Mathf.Max(0.0f, staleTimeoutSeconds);
    }

    public bool IsPoseCalibrationCurrent(LiveVRPoseSample sample)
    {
        if (!sample.IsCalibrated)
            return false;

        if (protocolContext.CalibrationVersion <= 0)
            return true;

        return sample.CalibrationVersion >= protocolContext.CalibrationVersion;
    }

    public bool RequiresLivePoseForStart(int userId)
    {
        return GetConfiguredUserSource(userId) != LiveVRUserSource.SimulatedOnly &&
               !runtimeSimulatedFallbackUsers.Contains(userId);
    }

    public bool ConsumePoseReanchorRequest(int userId)
    {
        lock (clientStateLock)
        {
            LiveVRUserConnectionState state;
            if (!userConnectionStates.TryGetValue(userId, out state) ||
                state != LiveVRUserConnectionState.NeedsPoseReanchor)
            {
                return false;
            }

            userConnectionStates[userId] = LiveVRUserConnectionState.ConnectedFresh;
            return true;
        }
    }

    public void MarkUserNeedsPoseReanchor(int userId)
    {
        lock (clientStateLock)
        {
            userConnectionStates[userId] = LiveVRUserConnectionState.NeedsPoseReanchor;
        }
    }

    public bool ShouldUseSimulatedUser(int userId, float staleTimeoutSeconds, bool requireCalibratedPose)
    {
        LiveVRUserSource source = GetConfiguredUserSource(userId);
        if (source == LiveVRUserSource.SimulatedOnly)
            return true;

        return runtimeSimulatedFallbackUsers.Contains(userId);
    }

    public bool IsRuntimeSimulatedFallback(int userId)
    {
        return runtimeSimulatedFallbackUsers.Contains(userId);
    }

    public string GetUserSourceLabel(int userId, float staleTimeoutSeconds, bool requireCalibratedPose)
    {
        LiveVRUserSource source = GetConfiguredUserSource(userId);
        if (runtimeSimulatedFallbackUsers.Contains(userId))
            return "SIM_FALLBACK";

        if (source == LiveVRUserSource.RequiredLiveHmd || source == LiveVRUserSource.OptionalLiveHmdFallbackSim)
            return "LIVE_REQUIRED";

        if (source == LiveVRUserSource.SimulatedOnly)
            return "SIM_ONLY";

        return "LIVE_REQUIRED";
    }

    public void SetRuntimeClientConfiguration(int newLocalUserId, string newHostAddress, int newHostPosePort)
    {
        int sanitizedUserId = Mathf.Max(0, newLocalUserId);
        bool userChanged = localUserId != sanitizedUserId;
        bool endpointChanged = !string.Equals(hostAddress, newHostAddress, StringComparison.OrdinalIgnoreCase) ||
                               hostPosePort != newHostPosePort;

        localUserId = sanitizedUserId;
        hostAddress = string.IsNullOrEmpty(newHostAddress) ? hostAddress : newHostAddress;
        hostPosePort = Mathf.Max(1, newHostPosePort);

        if (userChanged)
        {
            ClearLocalCalibration("runtime_user_id_changed");
            lastCenterCalibrationCommandEventId = -1;
            lastCenterCalibrationStatus = "user changed; waiting for host command";
        }

        if (mode == LiveVRExperimentMode.ClientOnly && endpointChanged)
        {
            try
            {
                hostEndPoint = new IPEndPoint(IPAddress.Parse(hostAddress), hostPosePort);
                lock (clientStateLock)
                {
                    lastAckUnixMilliseconds = 0;
                    lastAckSequence = 0;
                }
            }
            catch (Exception e)
            {
                lastClientSendError = e.Message;
                Debug.LogWarning("[LiveVR] Failed to update client host endpoint: " + e.Message);
            }
        }

        Debug.Log(string.Format("[LiveVR] Runtime client configuration: user={0} host={1}:{2}", localUserId, hostAddress, hostPosePort));
    }

    private void EnsureClientTransportStarted()
    {
        if (mode != LiveVRExperimentMode.ClientOnly)
            return;

        if (clientSender == null)
        {
            clientSender = new UdpClient();
            clientSender.EnableBroadcast = true;
        }

        if (hostEndPoint == null)
            hostEndPoint = new IPEndPoint(IPAddress.Parse(hostAddress), hostPosePort);

        StartClientReceiver();
    }

    public void SetControllerCalibrationInputEnabled(bool enabled)
    {
        useControllerPrimaryButtonForCalibration = enabled;
    }

    public void SetManualCenterCalibrationRequired(bool required)
    {
        bool previous = requireManualCenterCalibration;
        requireManualCenterCalibration = required;
        if (required && !previous && hasCalibration)
            ClearLocalCalibration("manual_center_calibration_enabled");
    }

    public void CalibrateNow()
    {
        Vector3 position;
        Quaternion rotation;
        if (!TryReadHeadPose(out position, out rotation))
        {
            Debug.LogWarning("[LiveVR] Cannot calibrate because no head pose is available.");
            return;
        }

        calibrationOriginPosition = new Vector2(position.x, position.z);
        calibrationOriginYaw = ToProjectYaw(rotation);
        hasCalibration = true;
        hasLastAcceptedLocalPose = false;
        lastCenterCalibrationCompleteUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lastCenterCalibrationStatus = string.Format("calibrated at event {0}", lastCenterCalibrationCommandEventId);
        Debug.Log(string.Format("[LiveVR] Calibrated user {0}: origin={1}, yaw={2:F2}", localUserId, calibrationOriginPosition, calibrationOriginYaw));
    }

    public bool TryGetPose(int userId, out LiveVRPoseSample sample)
    {
        lock (posesLock)
        {
            return latestPoses.TryGetValue(userId, out sample);
        }
    }

    public LiveVRPoseSample[] GetAllPosesSnapshot()
    {
        lock (posesLock)
        {
            LiveVRPoseSample[] snapshot = new LiveVRPoseSample[latestPoses.Count];
            latestPoses.Values.CopyTo(snapshot, 0);
            return snapshot;
        }
    }

    public LiveVRClientConnectionInfo[] GetClientConnectionsSnapshot()
    {
        lock (endpointsLock)
        {
            LiveVRClientConnectionInfo[] snapshot = new LiveVRClientConnectionInfo[clientConnectionsByEndpoint.Count];
            clientConnectionsByEndpoint.Values.CopyTo(snapshot, 0);
            Array.Sort(snapshot, delegate(LiveVRClientConnectionInfo a, LiveVRClientConnectionInfo b)
            {
                int assignedCompare = a.AssignedUserId.CompareTo(b.AssignedUserId);
                if (assignedCompare != 0)
                    return assignedCompare;

                return string.Compare(a.EndpointKey, b.EndpointKey, StringComparison.Ordinal);
            });
            return snapshot;
        }
    }

    public void AssignClientEndpoint(string endpointKey, int assignedUserId, bool proactiveResetEnabled)
    {
        if (!IsHost || string.IsNullOrEmpty(endpointKey))
            return;

        IPEndPoint endpoint = null;
        LiveVRClientConnectionInfo info;
        lock (assignmentLock)
        {
            lock (endpointsLock)
            {
                if (!clientConnectionsByEndpoint.TryGetValue(endpointKey, out info))
                    return;

                endpoint = BuildEndpoint(info);
                info.AssignedUserId = Mathf.Max(0, assignedUserId);
                info.ProactiveResetEnabled = proactiveResetEnabled;
                info.AssignmentStatus = "assigned";
                clientConnectionsByEndpoint[endpointKey] = info;
                assignedEndpointByUserId[info.AssignedUserId] = endpointKey;
                if (!string.IsNullOrEmpty(info.DeviceKey))
                {
                    assignedUserIdByDeviceKey[info.DeviceKey] = info.AssignedUserId;
                    assignedDeviceKeyByUserId[info.AssignedUserId] = info.DeviceKey;
                }
                clientEndpoints[info.AssignedUserId] = endpoint;
            }
        }

        LiveVRClientAssignmentMessage assignment = new LiveVRClientAssignmentMessage
        {
            UserId = Mathf.Max(0, assignedUserId),
            ExpectedUserCount = expectedUserCountForAssignment,
            ProactiveResetEnabled = proactiveResetEnabled,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            HostRunId = hostRunId
        };
        SendHostPacket(assignment.ToNetworkMessage(), endpoint);
        Debug.LogFormat("[LiveVR] Assigned client {0} -> user {1}, proactiveReset={2}.", endpointKey, assignedUserId, proactiveResetEnabled);
    }

    public bool IsUserConnected(int userId, float staleTimeoutSeconds)
    {
        LiveVRPoseSample sample;
        if (TryGetPose(userId, out sample) && sample.AgeSeconds <= staleTimeoutSeconds)
            return true;

        return IsClientHelloRecent(userId, staleTimeoutSeconds);
    }

    public bool IsUserCalibratedAndConnected(int userId, float staleTimeoutSeconds)
    {
        LiveVRPoseSample sample;
        return TryGetPose(userId, out sample) && sample.IsCalibrated && sample.AgeSeconds <= staleTimeoutSeconds;
    }

    public void ClearHostCalibrationStateForAllUsers()
    {
        if (!IsHost)
            return;

        lock (posesLock)
        {
            List<int> userIds = new List<int>(latestPoses.Keys);
            for (int i = 0; i < userIds.Count; i++)
            {
                LiveVRPoseSample sample = latestPoses[userIds[i]];
                sample.IsCalibrated = false;
                latestPoses[userIds[i]] = sample;
                LogCalibrationInvalidated(
                    sample.UserId,
                    "host_clear_all_calibration",
                    sample.CalibrationVersion,
                    protocolContext.CalibrationVersion,
                    sample.ClientSessionId);
            }
        }
    }

    public void ClearHostPoseStateForUser(int userId)
    {
        if (!IsHost)
            return;

        lock (posesLock)
        {
            latestPoses.Remove(userId);
        }
    }

    public bool IsClientHelloRecent(int userId, float staleTimeoutSeconds)
    {
        lock (posesLock)
        {
            long lastHello;
            if (!latestHelloReceiveUnixMs.TryGetValue(userId, out lastHello) || lastHello <= 0)
                return false;

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return (now - lastHello) / 1000.0f <= staleTimeoutSeconds;
        }
    }

    public bool TryGetLatestResetPrompt(out LiveVRResetPromptMessage prompt)
    {
        lock (clientStateLock)
        {
            prompt = latestResetPrompt;
            return hasResetPrompt;
        }
    }

    public float LastResetPromptReceiveAgeSeconds
    {
        get
        {
            lock (clientStateLock)
            {
                if (lastResetPromptReceiveUnixMilliseconds <= 0)
                    return float.PositiveInfinity;

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return Mathf.Max(0.0f, (now - lastResetPromptReceiveUnixMilliseconds) / 1000.0f);
            }
        }
    }

    public bool HasFreshResetPrompt(float maxAgeSeconds)
    {
        return LastResetPromptReceiveAgeSeconds <= Mathf.Max(0.0f, maxAgeSeconds);
    }

    public bool TryGetLatestResetStart(out LiveVRResetStartMessage resetStart)
    {
        lock (clientStateLock)
        {
            resetStart = latestResetStart;
            return hasResetStart;
        }
    }

    public float LastResetStartReceiveAgeSeconds
    {
        get
        {
            lock (clientStateLock)
            {
                if (lastResetStartReceiveUnixMilliseconds <= 0)
                    return float.PositiveInfinity;

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return Mathf.Max(0.0f, (now - lastResetStartReceiveUnixMilliseconds) / 1000.0f);
            }
        }
    }

    public bool HasFreshResetStart(float maxAgeSeconds)
    {
        return LastResetStartReceiveAgeSeconds <= Mathf.Max(0.0f, maxAgeSeconds);
    }

    public bool HasClientReportedResetDone(int userId, int eventId)
    {
        lock (clientStateLock)
        {
            int completedEventId;
            return latestResetDoneEventByUserId.TryGetValue(userId, out completedEventId) && completedEventId == eventId;
        }
    }

    public bool TryGetClientResetDone(int userId, int eventId, out LiveVRResetDoneMessage resetDone)
    {
        lock (clientStateLock)
        {
            if (latestResetDoneByUserId.TryGetValue(userId, out resetDone) && resetDone.EventId == eventId)
                return true;
        }

        resetDone = default(LiveVRResetDoneMessage);
        return false;
    }

    public void ClearClientResetDone(int userId, int eventId)
    {
        lock (clientStateLock)
        {
            int completedEventId;
            if (latestResetDoneEventByUserId.TryGetValue(userId, out completedEventId) && completedEventId == eventId)
                latestResetDoneEventByUserId.Remove(userId);

            LiveVRResetDoneMessage resetDone;
            if (latestResetDoneByUserId.TryGetValue(userId, out resetDone) && resetDone.EventId == eventId)
                latestResetDoneByUserId.Remove(userId);
        }
    }

    public void ClearLatestResetPrompt()
    {
        lock (clientStateLock)
        {
            hasResetPrompt = false;
        }
    }

    public void ClearLatestResetState(int eventId)
    {
        lock (clientStateLock)
        {
            if (hasResetPrompt && latestResetPrompt.EventId == eventId)
                hasResetPrompt = false;
            if (hasResetStart && latestResetStart.EventId == eventId)
                hasResetStart = false;
        }
    }

    public bool TryGetLatestVirtualPose(out LiveVRVirtualPoseMessage virtualPose)
    {
        lock (clientStateLock)
        {
            virtualPose = latestVirtualPose;
            return hasVirtualPose;
        }
    }

    public bool TryGetLatestVirtualPose(out LiveVRVirtualPoseMessage virtualPose, out long receiveUnixMilliseconds)
    {
        lock (clientStateLock)
        {
            virtualPose = latestVirtualPose;
            receiveUnixMilliseconds = latestVirtualPoseReceiveUnixMilliseconds;
            return hasVirtualPose;
        }
    }

    public bool TryGetLocalPoseSample(out LiveVRPoseSample sample)
    {
        return TryBuildLocalPoseSample(out sample);
    }

    public void SetExperimentState(LiveVRExperimentState newState, bool broadcast = true)
    {
        if (!IsHost)
            return;

        if (newState == LiveVRExperimentState.Running && experimentState != LiveVRExperimentState.Running)
            ClearTargetReachedRuntime();

        experimentState = newState;
        if (broadcast)
            BroadcastState();
    }

    public void BroadcastState()
    {
        if (!IsHost)
            return;

        LiveVRStateMessage stateMessage = new LiveVRStateMessage
        {
            ExperimentState = experimentState,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TargetSeed = ResolveCurrentTargetSeed(),
            TargetSeedVersion = ResolveCurrentTargetSeedVersion()
        };
        targetSeed = stateMessage.TargetSeed;
        targetSeedVersion = stateMessage.TargetSeedVersion;
        for (int userId = 0; userId < expectedUserCountForAssignment; userId++)
        {
            if (IsUserConnected(userId, 10.0f))
                SendReliableToUser(userId, "STATE", stateMessage.ToNetworkMessage());
        }
    }

    private int ResolveCurrentTargetSeed()
    {
        if (_GCM.GlobalCoordinationManager.instance != null)
        {
            int currentSeed = _GCM.GlobalCoordinationManager.instance.CurrentEpisodeSeed;
            if (currentSeed != int.MinValue)
                return currentSeed;

            return 1000 + _GCM.GlobalCoordinationManager.instance.CurrentEpisodeSeedVersion;
        }

        return targetSeed;
    }

    private int ResolveCurrentTargetSeedVersion()
    {
        if (_GCM.GlobalCoordinationManager.instance != null)
            return _GCM.GlobalCoordinationManager.instance.CurrentEpisodeSeedVersion;

        return targetSeedVersion;
    }

    public void SendResetPrompt(int userId, string resetType, Vector2 directionHint, int eventId)
    {
        SendResetPrompt(userId, resetType, directionHint, eventId, false, Vector2.zero);
    }

    public void SendResetPrompt(int userId, string resetType, Vector2 directionHint, int eventId, bool hasTargetPosition, Vector2 targetPosition)
    {
        SendResetPrompt(userId, resetType, directionHint, eventId, hasTargetPosition, targetPosition, false, 0, 0.0f, 0.0f, 0.0f);
    }

    public void SendResetPrompt(
        int userId,
        string resetType,
        Vector2 directionHint,
        int eventId,
        bool hasTargetPosition,
        Vector2 targetPosition,
        bool hasTurnInstruction,
        int turnDirectionSign,
        float totalTurnDegrees,
        float remainingTurnDegrees,
        float progress01)
    {
        if (!IsHost)
            return;

        LiveVRResetPromptMessage prompt = new LiveVRResetPromptMessage
        {
            UserId = userId,
            ResetType = resetType,
            DirectionHint = directionHint,
            HasTargetPosition = hasTargetPosition,
            TargetPosition = targetPosition,
            HasTurnInstruction = hasTurnInstruction,
            TurnDirectionSign = turnDirectionSign,
            TotalTurnDegrees = totalTurnDegrees,
            RemainingTurnDegrees = remainingTurnDegrees,
            Progress01 = progress01,
            EventId = eventId,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        SendToUser(userId, prompt.ToNetworkMessage());
    }

    public void SendResetPrompt(int userId, string resetType, Vector2 directionHint)
    {
        SendResetPrompt(userId, resetType, directionHint, ++resetPromptEventId);
    }

    public void SendResetStart(
        int userId,
        string resetType,
        Vector2 directionHint,
        int eventId,
        bool hasTargetPosition,
        Vector2 targetPosition,
        int turnDirectionSign,
        float physicalTurnDegrees,
        float injectedTurnDegrees)
    {
        if (!IsHost)
            return;

        LiveVRResetStartMessage resetStart = new LiveVRResetStartMessage
        {
            UserId = userId,
            ResetType = resetType,
            DirectionHint = directionHint,
            HasTargetPosition = hasTargetPosition,
            TargetPosition = targetPosition,
            TurnDirectionSign = turnDirectionSign,
            PhysicalTurnDegrees = physicalTurnDegrees,
            InjectedTurnDegrees = injectedTurnDegrees,
            EventId = eventId,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        SendReliableToUser(userId, "RESET_START", resetStart.ToNetworkMessage());
        Debug.Log(string.Format(
            "[LiveVR] Sent RESET_START user={0} type={1} event={2} physical={3:F1} injected={4:F1} turn={5}",
            userId,
            resetType,
            eventId,
            physicalTurnDegrees,
            injectedTurnDegrees,
            turnDirectionSign));
    }

    public void SendResetDone(int eventId, float finalPhysicalYawDegrees, float finalInjectedTurnDegrees)
    {
        if (!SendsLocalPose || IsHost || clientSender == null || hostEndPoint == null)
            return;

        try
        {
            LiveVRResetDoneMessage resetDone = new LiveVRResetDoneMessage
            {
                UserId = localUserId,
                EventId = eventId,
                ClientUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                FinalPhysicalYawDegrees = finalPhysicalYawDegrees,
                FinalInjectedTurnDegrees = finalInjectedTurnDegrees
            };
            reliableControl.SendReliableToUser(0, "RESET_DONE", resetDone.ToNetworkMessage(), protocolContext);
            lastClientSendError = string.Empty;
            Debug.Log(string.Format(
                "[LiveVR] Client sent RESET_DONE user={0} event={1} yaw={2:F1} injected={3:F1}",
                localUserId,
                eventId,
                finalPhysicalYawDegrees,
                finalInjectedTurnDegrees));
        }
        catch (Exception e)
        {
            lastClientSendError = e.Message;
            Debug.LogWarning("[LiveVR] Failed to send RESET_DONE: " + e.Message);
        }
    }

    public void SendTargetReached(
        int targetIndex,
        Vector2 targetPosition,
        Vector2 userPosition,
        float distanceMeters,
        float cumulativeDistanceMeters,
        bool runComplete)
    {
        if (!SendsLocalPose || IsHost || clientSender == null || hostEndPoint == null)
            return;

        try
        {
            LiveVRTargetReachedMessage reached = new LiveVRTargetReachedMessage
            {
                UserId = localUserId,
                TargetIndex = targetIndex,
                ClientUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                TargetPosition = targetPosition,
                UserPosition = userPosition,
                DistanceMeters = distanceMeters,
                CumulativeDistanceMeters = cumulativeDistanceMeters,
                RunComplete = runComplete
            };
            reliableControl.SendReliableToUser(0, "TARGET_REACHED", reached.ToNetworkMessage(), protocolContext);
            lastClientSendError = string.Empty;
            Debug.Log(string.Format(
                "[LiveVR] Client sent TARGET_REACHED user={0} target={1} distance={2:F2} cumulative={3:F2} complete={4}",
                localUserId,
                targetIndex,
                distanceMeters,
                cumulativeDistanceMeters,
                runComplete));
        }
        catch (Exception e)
        {
            lastClientSendError = e.Message;
            Debug.LogWarning("[LiveVR] Failed to send TARGET_REACHED: " + e.Message);
        }
    }

    public void SendResetEnd(int userId, int eventId)
    {
        if (!IsHost)
            return;

        LiveVRResetEndMessage resetEnd = new LiveVRResetEndMessage
        {
            UserId = userId,
            EventId = eventId,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        SendReliableToUser(userId, "RESET_END", resetEnd.ToNetworkMessage());
        Debug.Log(string.Format("[LiveVR] Sent RESET_END user={0} event={1}", userId, eventId));
    }

    public void SendCenterCalibrationCommand(int userId)
    {
        if (!IsHost)
            return;

        LiveVRCalibrateCenterMessage message = new LiveVRCalibrateCenterMessage
        {
            UserId = userId,
            EventId = ++centerCalibrationEventId,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        SendReliableToUser(userId, "CALIBRATE_CENTER", message.ToNetworkMessage());
        Debug.Log(string.Format("[LiveVR] Sent center calibration command to user {0} endpoint={1}.", userId, GetEndpointDebugLabel(userId)));
    }

    public void SendClearCalibrationCommand(int userId)
    {
        if (!IsHost)
            return;

        LiveVRClearCalibrationMessage message = new LiveVRClearCalibrationMessage
        {
            UserId = userId,
            EventId = ++clearCalibrationEventId,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        SendReliableToUser(userId, "CLEAR_CALIBRATION", message.ToNetworkMessage());
    }

    public void SendVirtualPose(int userId, Vector2 virtualPosition, float virtualYawDegrees)
    {
        SendVirtualPose(userId, virtualPosition, virtualYawDegrees, 0.0f, GainType.Undefined, 0.0f, 0.0f);
    }

    public void SendVirtualPose(
        int userId,
        Vector2 virtualPosition,
        float virtualYawDegrees,
        float injectedYawDeltaDegrees,
        GainType gainType)
    {
        SendVirtualPose(userId, virtualPosition, virtualYawDegrees, injectedYawDeltaDegrees, gainType, 0.0f, 0.0f);
    }

    public void SendVirtualPose(
        int userId,
        Vector2 virtualPosition,
        float virtualYawDegrees,
        float injectedYawDeltaDegrees,
        GainType gainType,
        float gainRateDegreesPerSecond,
        float gainValidSeconds)
    {
        if (!IsHost)
            return;

        LiveVRVirtualPoseMessage virtualPose = new LiveVRVirtualPoseMessage
        {
            UserId = userId,
            Sequence = ++virtualPoseSequence,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            VirtualPosition = virtualPosition,
            VirtualYawDegrees = virtualYawDegrees,
            InjectedYawDeltaDegrees = injectedYawDeltaDegrees,
            GainType = gainType,
            GainRateDegreesPerSecond = gainRateDegreesPerSecond,
            GainValidSeconds = gainValidSeconds
        };
        SendToUser(userId, virtualPose.ToNetworkMessage());
    }

    private void ConfigureReliableControl()
    {
        reliableControl.Configure(
            SendReliableControlPacket,
            message => Debug.LogWarning(message),
            0.25f,
            3.0f);
    }

    private void SendReliableToUser(int userId, string messageType, string payload)
    {
        protocolContext.EnsureHostRunId();
        reliableControl.SendReliableToUser(userId, messageType, payload, protocolContext);
    }

    private void SendReliableControlPacket(int userId, string message)
    {
        if (IsHost)
            SendToUser(userId, message);
        else
            SendClientPacketToHost(message);
    }

    private void SendClientPacketToHost(string message)
    {
        if (clientSender == null || hostEndPoint == null || string.IsNullOrEmpty(message))
            return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            clientSender.Send(data, data.Length, hostEndPoint);
            lastClientSendError = string.Empty;
        }
        catch (Exception e)
        {
            lastClientSendError = e.Message;
            Debug.LogWarning("[LiveVR] Failed to send client control packet: " + e.Message);
        }
    }

    private bool TryBuildLocalPoseSample(out LiveVRPoseSample sample)
    {
        sample = default(LiveVRPoseSample);
        Vector3 rawPosition;
        Quaternion rawRotation;
        if (!TryReadHeadPose(out rawPosition, out rawRotation))
        {
            lastPoseSourceStatus = "no head pose";
            return false;
        }

        if (!hasCalibration && !requireManualCenterCalibration)
            CalibrateNow();

        Vector2 raw2D = new Vector2(rawPosition.x, rawPosition.z);
        float rawYaw = ToProjectYaw(rawRotation);
        Vector2 experimentPosition = hasCalibration
            ? Rotate2D(raw2D - calibrationOriginPosition, -calibrationOriginYaw) * metersScale + experimentOriginOffset
            : raw2D * metersScale + experimentOriginOffset;

        float experimentYaw = hasCalibration
            ? NormalizeDegrees(rawYaw - calibrationOriginYaw + experimentYawOffsetDegrees)
            : NormalizeDegrees(rawYaw + experimentYawOffsetDegrees);
        bool sampleIsCalibrated = hasCalibration;

        sample.UserId = localUserId;
        sample.Sequence = sequence++;
        sample.ClientUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        sample.HostReceiveUnixMilliseconds = sample.ClientUnixMilliseconds;
        sample.ExperimentPosition = experimentPosition;
        sample.YawDegrees = experimentYaw;
        sample.HeightMeters = rawPosition.y;
        sample.IsCalibrated = sampleIsCalibrated;
        sample.ClientSessionId = clientSessionId;
        sample.CalibrationVersion = protocolContext.CalibrationVersion;
        if (sample.IsCalibrated && ShouldRejectPoseJump(sample, lastAcceptedLocalPose, hasLastAcceptedLocalPose, out string localJumpReason))
        {
            ClearLocalCalibration("local_pose_jump");
            lastCenterCalibrationStatus = "tracking jump detected; recalibrate";

            sample.ExperimentPosition = raw2D * metersScale + experimentOriginOffset;
            sample.YawDegrees = NormalizeDegrees(rawYaw + experimentYawOffsetDegrees);
            sample.IsCalibrated = false;

            Debug.LogWarning(string.Format(
                "[LiveVR] Local HMD pose jump rejected for user {0}; calibration cleared. {1}",
                localUserId,
                localJumpReason));
        }

        if (sample.IsCalibrated)
        {
            lastAcceptedLocalPose = sample;
            hasLastAcceptedLocalPose = true;
        }

        lastPoseSourceStatus = string.Format(
            "ok raw=({0:F2},{1:F2},{2:F2}) rawYaw={3:F1} mapped=({4:F2},{5:F2}) yaw={6:F1} cal={7} scale={8:F3}",
            rawPosition.x,
            rawPosition.y,
            rawPosition.z,
            rawYaw,
            sample.ExperimentPosition.x,
            sample.ExperimentPosition.y,
            sample.YawDegrees,
            sample.IsCalibrated,
            metersScale);
        return true;
    }

    private bool TryReadHeadPose(out Vector3 position, out Quaternion rotation)
    {
        if (useUnityXRHeadPose && XRSettings.isDeviceActive)
        {
            position = InputTracking.GetLocalPosition(XRNode.Head);
            rotation = InputTracking.GetLocalRotation(XRNode.Head);
            return true;
        }

        if (headTransformOverride != null)
        {
            position = headTransformOverride.localPosition;
            rotation = headTransformOverride.localRotation;
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    private bool IsRecalibrateRequested()
    {
        if (Input.GetKeyDown(recalibrateKey))
            return true;

        if (!useControllerPrimaryButtonForCalibration || !SendsLocalPose)
            return false;

        bool pressed = false;
        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        bool rightPressed;
        bool leftPressed;
        if (rightHand.isValid && rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out rightPressed))
            pressed |= rightPressed;
        if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.primaryButton, out leftPressed))
            pressed |= leftPressed;

        bool requested = pressed && !wasControllerPrimaryButtonPressed;
        wasControllerPrimaryButtonPressed = pressed;
        return requested;
    }

    private void StartHostReceiver()
    {
        try
        {
            string error;
            if (!LiveVRTransport.TryCreateHostSocket(hostPosePort, out hostReceiver, out error))
            {
                Debug.LogError(string.Format("[LiveVR] Failed to bind UDP port {0}: {1}", hostPosePort, error));
                return;
            }
            if (string.IsNullOrEmpty(hostRunId))
                hostRunId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            protocolContext.HostRunId = hostRunId;
            protocolContext.RunId = hostRunId;
            receiverRunning = true;
            receiverThread = new Thread(ReceiveLoop);
            receiverThread.IsBackground = true;
            receiverThread.Start();
            Debug.Log(string.Format("[LiveVR] Host listening for pose packets on UDP port {0}.", hostPosePort));
        }
        catch (Exception e)
        {
            Debug.LogError("[LiveVR] Failed to start host receiver: " + e.Message);
        }
    }

    private void StopHostReceiver()
    {
        receiverRunning = false;

        if (hostReceiver != null)
        {
            hostReceiver.Close();
            hostReceiver = null;
        }

        if (receiverThread != null)
        {
            receiverThread.Join(100);
            receiverThread = null;
        }
    }

    private void StartClientReceiver()
    {
        if (clientSender == null)
            return;

        if (clientReceiverRunning)
            return;

        clientReceiverRunning = true;
        clientReceiverThread = new Thread(ClientReceiveLoop);
        clientReceiverThread.IsBackground = true;
        clientReceiverThread.Start();
    }

    private void StopClientReceiver()
    {
        clientReceiverRunning = false;

        if (clientSender != null)
            clientSender.Close();

        if (clientReceiverThread != null)
        {
            clientReceiverThread.Join(100);
            clientReceiverThread = null;
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (receiverRunning)
        {
            try
            {
                byte[] data = hostReceiver.Receive(ref remote);
                string message = Encoding.UTF8.GetString(data);
                hostRawPacketCount++;
                hostLastRemoteEndpoint = remote.ToString();
                hostLastRawPacketPreview = message.Length > 96 ? message.Substring(0, 96) : message;

                LiveVRControlAckMessage controlAck;
                if (LiveVRControlAckMessage.TryParse(message, out controlAck))
                {
                    HandleControlAck(controlAck);
                    continue;
                }

                LiveVRControlEnvelope controlEnvelope;
                if (LiveVRControlEnvelope.TryParse(message, out controlEnvelope))
                {
                    SendControlAckToEndpoint(controlEnvelope, remote, true, string.Empty);
                    message = controlEnvelope.Payload;
                }

                LiveVRPoseSample sample;
                if (LiveVRPoseSample.TryParse(message, out sample))
                {
                    hostPosePacketCount++;
                    if (!IsPoseSenderAllowed(sample.UserId, remote))
                    {
                        SendAssignmentForKnownEndpoint(remote);
                        continue;
                    }

                    StoreClientConnection(remote, sample.UserId, clientProactiveResetEnabled, true, string.Empty, string.Empty, sample.ClientSessionId);
                    StorePose(sample);
                    StoreClientEndpoint(sample.UserId, remote);
                    SendAck(sample.UserId, sample.Sequence, remote);
                    continue;
                }

                LiveVRHostDiscoveryRequestMessage discoveryRequest;
                if (LiveVRHostDiscoveryRequestMessage.TryParse(message, out discoveryRequest))
                {
                    StoreClientConnection(remote, -1, true, false, discoveryRequest.DeviceKey, discoveryRequest.DeviceName, string.Empty);
                    SendHostAdvertisement(remote);
                    continue;
                }

                LiveVRHelloMessage hello;
                if (LiveVRHelloMessage.TryParse(message, out hello))
                {
                    hostHelloPacketCount++;
                    StoreClientConnection(remote, hello.UserId, hello.ProactiveResetEnabled, false, hello.DeviceKey, hello.DeviceName, hello.ClientSessionId);
                    int assignedUserId;
                    if (TryAssignUserIdForHello(remote, hello, out assignedUserId))
                    {
                        StoreHello(assignedUserId, remote);
                        SendAck(assignedUserId, 0, remote);
                    }
                    continue;
                }

                LiveVRResetDoneMessage resetDone;
                if (LiveVRResetDoneMessage.TryParse(message, out resetDone))
                {
                    StoreResetDone(resetDone, remote);
                    SendAck(resetDone.UserId, 0, remote);
                    continue;
                }

                LiveVRTargetReachedMessage targetReached;
                if (LiveVRTargetReachedMessage.TryParse(message, out targetReached))
                {
                    StoreTargetReached(targetReached, remote);
                    SendAck(targetReached.UserId, 0, remote);
                    continue;
                }

                hostParseFailCount++;
            }
            catch (SocketException)
            {
                if (receiverRunning)
                    Debug.LogWarning("[LiveVR] Host receiver socket interrupted.");
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LiveVR] Failed to receive pose packet: " + e.Message);
            }
        }
    }

    private void ClientReceiveLoop()
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (clientReceiverRunning)
        {
            try
            {
                byte[] data = clientSender.Receive(ref remote);
                string message = Encoding.UTF8.GetString(data);
                HandleClientMessage(message, remote);
            }
            catch (SocketException)
            {
                if (clientReceiverRunning)
                    Debug.LogWarning("[LiveVR] Client receiver socket interrupted.");
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LiveVR] Failed to receive host packet: " + e.Message);
            }
        }
    }

    private void SendPoseToHost(LiveVRPoseSample sample)
    {
        if (clientSender == null || hostEndPoint == null)
            return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(sample.ToNetworkMessage());
            clientSender.Send(data, data.Length, hostEndPoint);
            sentPosePacketCount++;
            RecordClientPoseSendForRateLog(sample);
            lastClientSendError = string.Empty;
        }
        catch (Exception e)
        {
            lastClientSendError = e.Message;
            Debug.LogWarning("[LiveVR] Failed to send pose packet: " + e.Message);
        }
    }

    private void SendHelloToHost()
    {
        if (clientSender == null || hostEndPoint == null)
            return;

        try
        {
            EnsureClientIdentity();
            LiveVRHelloMessage hello = new LiveVRHelloMessage
            {
                UserId = localUserId,
                ClientUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ProactiveResetEnabled = clientProactiveResetEnabled,
                DeviceKey = clientDeviceKey,
                DeviceName = clientDeviceName,
                ClientSessionId = clientSessionId
            };
            byte[] data = Encoding.UTF8.GetBytes(hello.ToNetworkMessage());
            clientSender.Send(data, data.Length, hostEndPoint);
            sentHelloPacketCount++;
            lastClientSendError = string.Empty;
        }
        catch (Exception e)
        {
            lastClientSendError = e.Message;
            Debug.LogWarning("[LiveVR] Failed to send hello packet: " + e.Message);
        }
    }

    private void TickHostDiscovery()
    {
        if (!enableHostDiscovery || clientSender == null)
            return;

        if (IsConnectedToHost)
        {
            hostDiscoveryStatus = string.Format("connected to {0}:{1}", hostAddress, hostPosePort);
            return;
        }

        if (LastAckAgeSeconds < hostDiscoveryAckTimeoutSeconds)
            return;

        if (Time.unscaledTime < nextHostDiscoveryTime)
            return;

        nextHostDiscoveryTime = Time.unscaledTime + hostDiscoveryIntervalSeconds;
        SendHostDiscoveryRequest();
    }

    private void SendHostDiscoveryRequest()
    {
        if (clientSender == null)
            return;

        try
        {
            EnsureClientIdentity();
            LiveVRHostDiscoveryRequestMessage request = new LiveVRHostDiscoveryRequestMessage
            {
                DeviceKey = clientDeviceKey,
                DeviceName = clientDeviceName,
                ClientUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            byte[] data = Encoding.UTF8.GetBytes(request.ToNetworkMessage());
            IPEndPoint broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, hostPosePort);
            clientSender.Send(data, data.Length, broadcastEndpoint);
            sentHostDiscoveryPacketCount++;
            hostDiscoveryStatus = string.Format("broadcast discovery on UDP {0}", hostPosePort);
            lastClientSendError = string.Empty;
        }
        catch (Exception e)
        {
            lastClientSendError = e.Message;
            hostDiscoveryStatus = "discovery failed: " + e.Message;
            Debug.LogWarning("[LiveVR] Failed to send host discovery request: " + e.Message);
        }
    }

    private void SendAck(int userId, uint sampleSequence, IPEndPoint remote)
    {
        if (hostReceiver == null || remote == null)
            return;

        LiveVRAckMessage ack = new LiveVRAckMessage
        {
            UserId = userId,
            LastSequence = sampleSequence,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ExperimentState = experimentState
        };
        SendHostPacket(ack.ToNetworkMessage(), remote);
    }

    private void SendControlAckToEndpoint(LiveVRControlEnvelope envelope, IPEndPoint remote, bool accepted, string reason)
    {
        if (hostReceiver == null || remote == null)
            return;

        LiveVRControlAckMessage ack = new LiveVRControlAckMessage
        {
            MessageId = envelope.MessageId,
            MessageType = envelope.MessageType,
            UserId = envelope.TargetUserId,
            Accepted = accepted,
            Reason = reason,
            UnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        SendHostPacket(ack.ToNetworkMessage(), remote);
    }

    private void SendControlAckToHost(LiveVRControlEnvelope envelope, bool accepted, string reason)
    {
        LiveVRControlAckMessage ack = new LiveVRControlAckMessage
        {
            MessageId = envelope.MessageId,
            MessageType = envelope.MessageType,
            UserId = localUserId,
            Accepted = accepted,
            Reason = reason,
            UnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        SendClientPacketToHost(ack.ToNetworkMessage());
    }

    private void SendToAllKnownClients(string message)
    {
        List<IPEndPoint> endpoints = new List<IPEndPoint>();
        lock (endpointsLock)
        {
            foreach (IPEndPoint endpoint in clientEndpoints.Values)
            {
                if (endpoint != null)
                    endpoints.Add(endpoint);
            }
        }

        for (int i = 0; i < endpoints.Count; i++)
            SendHostPacket(message, endpoints[i]);
    }

    private void SendToUser(int userId, string message)
    {
        IPEndPoint endpoint = null;
        lock (endpointsLock)
        {
            string endpointKey;
            if (assignedEndpointByUserId.TryGetValue(userId, out endpointKey))
            {
                LiveVRClientConnectionInfo info;
                if (clientConnectionsByEndpoint.TryGetValue(endpointKey, out info))
                    endpoint = BuildEndpoint(info);
            }

            if (endpoint == null)
                clientEndpoints.TryGetValue(userId, out endpoint);
        }

        if (endpoint != null)
            SendHostPacket(message, endpoint);
    }

    private void SendHostPacket(string message, IPEndPoint endpoint)
    {
        if (hostReceiver == null || endpoint == null)
            return;

        try
        {
            LiveVRTransport.Send(hostReceiver, message, endpoint);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LiveVR] Failed to send host packet: " + e.Message);
        }
    }

    private void SendHostAdvertisement(IPEndPoint endpoint)
    {
        if (!IsHost || endpoint == null)
            return;

        LiveVRHostAdvertisementMessage advertisement = new LiveVRHostAdvertisementMessage
        {
            HostPosePort = hostPosePort,
            ExpectedUserCount = expectedUserCountForAssignment,
            HostRunId = hostRunId,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        SendHostPacket(advertisement.ToNetworkMessage(), endpoint);
    }

    private void HandleClientMessage(string message, IPEndPoint remote)
    {
        LiveVRControlAckMessage controlAck;
        if (LiveVRControlAckMessage.TryParse(message, out controlAck))
        {
            HandleControlAck(controlAck);
            return;
        }

        LiveVRControlEnvelope controlEnvelope;
        if (LiveVRControlEnvelope.TryParse(message, out controlEnvelope))
        {
            bool targetMatches = controlEnvelope.TargetUserId < 0 || controlEnvelope.TargetUserId == localUserId;
            bool accepted = targetMatches && protocolContext.Accepts(controlEnvelope);
            SendControlAckToHost(controlEnvelope, accepted, accepted ? string.Empty : "stale_or_wrong_target");
            if (!accepted)
                return;

            if (!string.IsNullOrEmpty(controlEnvelope.HostRunId))
                protocolContext.HostRunId = controlEnvelope.HostRunId;
            if (!string.IsNullOrEmpty(controlEnvelope.RunId))
                protocolContext.RunId = controlEnvelope.RunId;
            protocolContext.RestartEpoch = Mathf.Max(protocolContext.RestartEpoch, controlEnvelope.RestartEpoch);
            protocolContext.ConfigVersion = Mathf.Max(protocolContext.ConfigVersion, controlEnvelope.ConfigVersion);
            if (controlEnvelope.CalibrationVersion > 0)
                protocolContext.CalibrationVersion = controlEnvelope.CalibrationVersion;

            HandleClientMessage(controlEnvelope.Payload, remote);
            return;
        }

        LiveVRHostAdvertisementMessage advertisement;
        if (LiveVRHostAdvertisementMessage.TryParse(message, out advertisement))
        {
            ApplyHostAdvertisement(advertisement, remote);
            return;
        }

        LiveVRClientAssignmentMessage assignment;
        if (LiveVRClientAssignmentMessage.TryParse(message, out assignment))
        {
            ApplyHostAssignment(assignment);
            return;
        }

        LiveVRClientAssignmentRejectMessage assignmentReject;
        if (LiveVRClientAssignmentRejectMessage.TryParse(message, out assignmentReject))
        {
            lock (clientStateLock)
            {
                hasHostAssignment = false;
                lastAckUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                clientAssignmentStatus = string.Format(
                    "assignment rejected: {0} ({1} expected users)",
                    assignmentReject.Reason,
                    assignmentReject.ExpectedUserCount);
            }
            return;
        }

        LiveVRAckMessage ack;
        if (LiveVRAckMessage.TryParse(message, out ack))
        {
            lock (clientStateLock)
            {
                lastAckSequence = ack.LastSequence;
                lastAckUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lastHostExperimentState = ack.ExperimentState;
            }
            return;
        }

        LiveVRStateMessage stateMessage;
        if (LiveVRStateMessage.TryParse(message, out stateMessage))
        {
            lock (clientStateLock)
            {
                lastHostExperimentState = stateMessage.ExperimentState;
                lastAckUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                targetSeed = stateMessage.TargetSeed;
                targetSeedVersion = stateMessage.TargetSeedVersion;
            }
            if (stateMessage.ExperimentState != LiveVRExperimentState.Running && clientPresentationState != null)
                clientPresentationState.ClearForRestart(protocolContext.RestartEpoch, "state_" + stateMessage.ExperimentState);
            return;
        }

        LiveVRCalibrateCenterMessage calibrate;
        if (LiveVRCalibrateCenterMessage.TryParse(message, out calibrate))
        {
            if (calibrate.UserId != localUserId)
                return;

            lock (clientStateLock)
            {
                lastCenterCalibrationCommandEventId = calibrate.EventId;
                lastCenterCalibrationCommandUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lastCenterCalibrationStatus = "host command received";
                lastAckUnixMilliseconds = lastCenterCalibrationCommandUnixMs;
            }

            CalibrateNow();
            return;
        }

        LiveVRClearCalibrationMessage clearCalibration;
        if (LiveVRClearCalibrationMessage.TryParse(message, out clearCalibration))
        {
            if (clearCalibration.UserId != localUserId)
                return;

            ClearLocalCalibration("clear_calibration_command");
            lock (clientStateLock)
            {
                lastCenterCalibrationCommandEventId = -1;
                lastCenterCalibrationCommandUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lastCenterCalibrationStatus = "recalibration requested; waiting for host calibration";
                lastAckUnixMilliseconds = lastCenterCalibrationCommandUnixMs;
            }
            return;
        }

        LiveVRResetStartMessage resetStart;
        if (LiveVRResetStartMessage.TryParse(message, out resetStart))
        {
            if (resetStart.UserId != localUserId)
                return;

            lock (clientStateLock)
            {
                latestResetStart = resetStart;
                hasResetStart = true;
                lastResetStartReceiveUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lastAckUnixMilliseconds = lastResetStartReceiveUnixMilliseconds;
            }

            Debug.Log(string.Format(
                "[LiveVR] Client received RESET_START user={0} event={1} physical={2:F1} injected={3:F1}",
                resetStart.UserId,
                resetStart.EventId,
                resetStart.PhysicalTurnDegrees,
                resetStart.InjectedTurnDegrees));
            return;
        }

        LiveVRResetPromptMessage resetPrompt;
        if (LiveVRResetPromptMessage.TryParse(message, out resetPrompt))
        {
            if (resetPrompt.UserId != localUserId)
                return;

            lock (clientStateLock)
            {
                latestResetPrompt = resetPrompt;
                hasResetPrompt = true;
                lastResetPromptReceiveUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lastAckUnixMilliseconds = lastResetPromptReceiveUnixMilliseconds;
            }
            return;
        }

        LiveVRResetEndMessage resetEnd;
        if (LiveVRResetEndMessage.TryParse(message, out resetEnd))
        {
            if (resetEnd.UserId != localUserId)
                return;

            lock (clientStateLock)
            {
                if (hasResetPrompt && latestResetPrompt.EventId == resetEnd.EventId)
                    hasResetPrompt = false;
                if (hasResetStart && latestResetStart.EventId == resetEnd.EventId)
                    hasResetStart = false;
                lastAckUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            Debug.Log(string.Format(
                "[LiveVR] Client received RESET_END user={0} event={1}",
                resetEnd.UserId,
                resetEnd.EventId));
            return;
        }

        LiveVRClientResetClearMessage resetClear;
        if (LiveVRClientResetClearMessage.TryParse(message, out resetClear))
        {
            if (resetClear.UserId >= 0 && resetClear.UserId != localUserId)
                return;

            protocolContext.RestartEpoch = Mathf.Max(protocolContext.RestartEpoch, resetClear.RestartEpoch);
            if (clientPresentationState != null)
                clientPresentationState.ClearForRestart(resetClear.RestartEpoch, resetClear.Reason);
            else
                ClearAllClientResetState();
            return;
        }

        LiveVRVirtualPoseMessage virtualPose;
        if (LiveVRVirtualPoseMessage.TryParse(message, out virtualPose))
        {
            if (virtualPose.UserId != localUserId)
                return;

            lock (clientStateLock)
            {
                latestVirtualPose = virtualPose;
                hasVirtualPose = true;
                latestVirtualPoseReceiveUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lastAckUnixMilliseconds = latestVirtualPoseReceiveUnixMilliseconds;
            }
            RecordClientGainReceivedForRateLog(virtualPose);
        }
    }

    private void RecordClientPoseSendForRateLog(LiveVRPoseSample sample)
    {
        if (mode != LiveVRExperimentMode.ClientOnly)
            return;

        float now = Time.unscaledTime;
        if (!hasClientPoseRateWindow)
        {
            hasClientPoseRateWindow = true;
            clientPoseRateWindowStartTime = now;
            clientPoseRateWindowStartSequence = sample.Sequence;
            nextClientPoseRateLogTime = now + 1.0f;
        }

        clientPoseRateWindowCount++;
        clientPoseRateWindowLastSequence = sample.Sequence;

        if (now < nextClientPoseRateLogTime)
            return;

        float duration = Mathf.Max(0.001f, now - clientPoseRateWindowStartTime);
        uint sequenceDelta = clientPoseRateWindowLastSequence >= clientPoseRateWindowStartSequence
            ? clientPoseRateWindowLastSequence - clientPoseRateWindowStartSequence + 1u
            : 0u;
        Debug.Log(string.Format(
            "[LiveVRRate][ClientPose] user={0} hz={1:F1} sent={2} seqDelta={3} seq={4} configuredRate={5:F1}",
            localUserId,
            clientPoseRateWindowCount / duration,
            clientPoseRateWindowCount,
            sequenceDelta,
            clientPoseRateWindowLastSequence,
            sendRateHz));

        hasClientPoseRateWindow = false;
        clientPoseRateWindowStartTime = 0.0f;
        clientPoseRateWindowStartSequence = 0;
        clientPoseRateWindowLastSequence = 0;
        clientPoseRateWindowCount = 0;
        nextClientPoseRateLogTime = now + 1.0f;
    }

    private void RecordClientGainReceivedForRateLog(LiveVRVirtualPoseMessage virtualPose)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        float absRate = Mathf.Abs(virtualPose.GainRateDegreesPerSecond);

        lock (clientGainRateLogLock)
        {
            if (!hasClientGainReceivedWindow)
            {
                hasClientGainReceivedWindow = true;
                clientGainRateWindowStartUnixMs = now;
                clientGainReceivedStartSequence = virtualPose.Sequence;
            }

            clientGainReceivedWindowCount++;
            clientGainReceivedLastSequence = virtualPose.Sequence;
            clientGainReceivedLastType = virtualPose.GainType;
            clientGainReceivedLastRate = virtualPose.GainRateDegreesPerSecond;
            clientGainReceivedLastValidSeconds = virtualPose.GainValidSeconds;
            clientGainReceivedAbsRateSum += absRate;
            clientGainReceivedMaxAbsRate = Mathf.Max(clientGainReceivedMaxAbsRate, absRate);

            if (virtualPose.GainType == GainType.Undefined ||
                absRate <= Mathf.Epsilon ||
                virtualPose.GainValidSeconds <= 0.0f)
            {
                clientGainReceivedUndefinedWindowCount++;
            }
            else
            {
                clientGainReceivedNonZeroWindowCount++;
            }
        }
    }

    private void LogClientGainReceivedRateIfNeeded()
    {
        if (mode != LiveVRExperimentMode.ClientOnly || Time.unscaledTime < nextClientGainReceivedLogTime)
            return;

        nextClientGainReceivedLogTime = Time.unscaledTime + 1.0f;

        int received;
        int nonZero;
        int undefined;
        uint startSequence;
        uint lastSequence;
        long startUnixMs;
        float absRateSum;
        float maxAbsRate;
        GainType lastType;
        float lastRate;
        float lastValidSeconds;

        lock (clientGainRateLogLock)
        {
            if (!hasClientGainReceivedWindow || clientGainReceivedWindowCount <= 0)
                return;

            received = clientGainReceivedWindowCount;
            nonZero = clientGainReceivedNonZeroWindowCount;
            undefined = clientGainReceivedUndefinedWindowCount;
            startSequence = clientGainReceivedStartSequence;
            lastSequence = clientGainReceivedLastSequence;
            startUnixMs = clientGainRateWindowStartUnixMs;
            absRateSum = clientGainReceivedAbsRateSum;
            maxAbsRate = clientGainReceivedMaxAbsRate;
            lastType = clientGainReceivedLastType;
            lastRate = clientGainReceivedLastRate;
            lastValidSeconds = clientGainReceivedLastValidSeconds;

            hasClientGainReceivedWindow = false;
            clientGainRateWindowStartUnixMs = 0;
            clientGainReceivedWindowCount = 0;
            clientGainReceivedNonZeroWindowCount = 0;
            clientGainReceivedUndefinedWindowCount = 0;
            clientGainReceivedStartSequence = 0;
            clientGainReceivedLastSequence = 0;
            clientGainReceivedAbsRateSum = 0.0f;
            clientGainReceivedMaxAbsRate = 0.0f;
            clientGainReceivedLastType = GainType.Undefined;
            clientGainReceivedLastRate = 0.0f;
            clientGainReceivedLastValidSeconds = 0.0f;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        float duration = Mathf.Max(0.001f, (now - startUnixMs) / 1000.0f);
        uint sequenceDelta = lastSequence >= startSequence ? lastSequence - startSequence + 1u : 0u;
        Debug.Log(string.Format(
            "[LiveVR] GainReceived user={0} hz={1:F1} received={2} seqDelta={3} nonZero={4} undefined={5} avgAbsRate={6:F2}/s maxAbsRate={7:F2}/s lastType={8} lastRate={9:F2}/s lastValid={10:F3}s",
            localUserId,
            received / duration,
            received,
            sequenceDelta,
            nonZero,
            undefined,
            received > 0 ? absRateSum / received : 0.0f,
            maxAbsRate,
            lastType,
            lastRate,
            lastValidSeconds));
    }

    private void ApplyHostAdvertisement(LiveVRHostAdvertisementMessage advertisement, IPEndPoint remote)
    {
        if (mode != LiveVRExperimentMode.ClientOnly || remote == null)
            return;

        string discoveredAddress = remote.Address.ToString();
        int discoveredPort = advertisement.HostPosePort > 0 ? advertisement.HostPosePort : hostPosePort;
        bool changed = !string.Equals(hostAddress, discoveredAddress, StringComparison.OrdinalIgnoreCase) ||
                       hostPosePort != discoveredPort;

        hostAddress = discoveredAddress;
        hostPosePort = discoveredPort;
        hostEndPoint = new IPEndPoint(remote.Address, discoveredPort);
        hostDiscoveryStatus = string.Format(
            "found host {0}:{1} users={2}",
            hostAddress,
            hostPosePort,
            advertisement.ExpectedUserCount);

        lock (clientStateLock)
        {
            if (changed)
            {
                lastAckUnixMilliseconds = 0;
                lastAckSequence = 0;
                hasHostAssignment = false;
                clientAssignmentStatus = "found host; waiting for assignment";
            }
        }

        SendHelloToHost();
        Debug.Log(string.Format("[LiveVR] Discovered host at {0}:{1}.", hostAddress, hostPosePort));
    }

    private bool TryAssignUserIdForHello(IPEndPoint endpoint, LiveVRHelloMessage hello, out int assignedUserId)
    {
        assignedUserId = -1;
        if (endpoint == null)
            return false;

        string endpointKey = endpoint.ToString();
        string deviceKey = BuildAssignmentDeviceKey(endpointKey, hello.DeviceKey);

        lock (assignmentLock)
        {
            lock (endpointsLock)
            {
                LiveVRClientConnectionInfo info;
                if (!clientConnectionsByEndpoint.TryGetValue(endpointKey, out info))
                    return false;

                if (!autoAssignClientUserIds)
                {
                    assignedUserId = Mathf.Max(0, hello.UserId);
                    ApplyAssignmentLocked(endpointKey, deviceKey, assignedUserId, hello.ProactiveResetEnabled, ref info);
                    SendAssignmentLocked(info);
                    return true;
                }

                if (assignedUserIdByDeviceKey.TryGetValue(deviceKey, out assignedUserId))
                {
                    ApplyAssignmentLocked(endpointKey, deviceKey, assignedUserId, hello.ProactiveResetEnabled, ref info);
                    SendAssignmentLocked(info);
                    return true;
                }

                assignedUserId = FindSmallestFreeUserIdLocked();
                if (assignedUserId < 0)
                {
                    info.AssignedUserId = -1;
                    info.AssignmentStatus = "rejected: full";
                    clientConnectionsByEndpoint[endpointKey] = info;
                    SendAssignmentReject(BuildEndpoint(info), "FULL");
                    return false;
                }

                ApplyAssignmentLocked(endpointKey, deviceKey, assignedUserId, hello.ProactiveResetEnabled, ref info);
                SendAssignmentLocked(info);
                Debug.LogFormat(
                    "[LiveVR] Auto-assigned client {0} ({1}) -> user {2}.",
                    endpointKey,
                    string.IsNullOrEmpty(info.DeviceName) ? deviceKey : info.DeviceName,
                    assignedUserId);
                return true;
            }
        }
    }

    private void ApplyAssignmentLocked(
        string endpointKey,
        string deviceKey,
        int assignedUserId,
        bool proactiveResetEnabled,
        ref LiveVRClientConnectionInfo info)
    {
        string previousEndpointKey;
        bool endpointChanged = assignedEndpointByUserId.TryGetValue(assignedUserId, out previousEndpointKey) &&
                               !string.Equals(previousEndpointKey, endpointKey, StringComparison.Ordinal);
        if (endpointChanged)
        {
            LiveVRClientConnectionInfo previousInfo;
            if (clientConnectionsByEndpoint.TryGetValue(previousEndpointKey, out previousInfo))
            {
                previousInfo.AssignedUserId = -1;
                previousInfo.AssignmentStatus = "replaced by reconnect";
                clientConnectionsByEndpoint[previousEndpointKey] = previousInfo;
            }
        }

        string previousDeviceKey;
        if (assignedDeviceKeyByUserId.TryGetValue(assignedUserId, out previousDeviceKey) &&
            !string.Equals(previousDeviceKey, deviceKey, StringComparison.Ordinal))
        {
            assignedUserIdByDeviceKey.Remove(previousDeviceKey);
        }

        info.AssignedUserId = assignedUserId;
        info.ProactiveResetEnabled = proactiveResetEnabled;
        info.AssignmentStatus = "assigned";
        info.DeviceKey = deviceKey;

        clientConnectionsByEndpoint[endpointKey] = info;
        assignedEndpointByUserId[assignedUserId] = endpointKey;
        assignedUserIdByDeviceKey[deviceKey] = assignedUserId;
        assignedDeviceKeyByUserId[assignedUserId] = deviceKey;
        runtimeSimulatedFallbackUsers.Remove(assignedUserId);
        if (!endpointChanged)
        {
            lock (clientStateLock)
            {
                LiveVRUserConnectionState state;
                if (!userConnectionStates.TryGetValue(assignedUserId, out state) ||
                    state != LiveVRUserConnectionState.NeedsPoseReanchor)
                {
                    userConnectionStates[assignedUserId] = LiveVRUserConnectionState.ConnectedFresh;
                }
            }
        }

        IPEndPoint assignedEndpoint = BuildEndpoint(info);
        if (assignedEndpoint != null)
            clientEndpoints[assignedUserId] = assignedEndpoint;

        if (endpointChanged)
        {
            ClearHostPoseStateForUser(assignedUserId);
            MarkUserNeedsPoseReanchor(assignedUserId);
            Debug.LogFormat(
                "[LiveVR] Client user {0} endpoint updated {1} -> {2}; fallback and cached pose cleared.",
                assignedUserId,
                previousEndpointKey,
                endpointKey);
        }
    }

    private void SendAssignmentLocked(LiveVRClientConnectionInfo info)
    {
        IPEndPoint endpoint = BuildEndpoint(info);
        if (endpoint == null)
            return;

        LiveVRClientAssignmentMessage assignment = new LiveVRClientAssignmentMessage
        {
            UserId = info.AssignedUserId,
            ExpectedUserCount = expectedUserCountForAssignment,
            ProactiveResetEnabled = info.ProactiveResetEnabled,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            HostRunId = hostRunId
        };
        SendHostPacket(assignment.ToNetworkMessage(), endpoint);
    }

    private void SendAssignmentReject(IPEndPoint endpoint, string reason)
    {
        if (endpoint == null)
            return;

        LiveVRClientAssignmentRejectMessage reject = new LiveVRClientAssignmentRejectMessage
        {
            Reason = reason,
            ExpectedUserCount = expectedUserCountForAssignment,
            HostUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        SendHostPacket(reject.ToNetworkMessage(), endpoint);
        Debug.LogWarningFormat("[LiveVR] Rejected client assignment: {0}.", reason);
    }

    private int FindSmallestFreeUserIdLocked()
    {
        int count = Mathf.Max(0, expectedUserCountForAssignment);
        for (int userId = 0; userId < count; userId++)
        {
            if (!assignedDeviceKeyByUserId.ContainsKey(userId))
                return userId;
        }

        return -1;
    }

    private bool IsPoseSenderAllowed(int userId, IPEndPoint endpoint)
    {
        if (!autoAssignClientUserIds || endpoint == null)
            return true;

        lock (endpointsLock)
        {
            string assignedEndpointKey;
            if (!assignedEndpointByUserId.TryGetValue(userId, out assignedEndpointKey))
                return false;

            LiveVRClientConnectionInfo info;
            if (!clientConnectionsByEndpoint.TryGetValue(assignedEndpointKey, out info))
                return false;

            return string.Equals(info.Address, endpoint.Address.ToString(), StringComparison.OrdinalIgnoreCase) &&
                   info.Port == endpoint.Port;
        }
    }

    private void SendAssignmentForKnownEndpoint(IPEndPoint endpoint)
    {
        if (endpoint == null)
            return;

        lock (endpointsLock)
        {
            LiveVRClientConnectionInfo info;
            if (!clientConnectionsByEndpoint.TryGetValue(endpoint.ToString(), out info))
                return;

            if (info.AssignedUserId >= 0)
                SendAssignmentLocked(info);
        }
    }

    private void StorePose(LiveVRPoseSample sample)
    {
        if (sample.CalibrationVersion > 0 &&
            protocolContext.CalibrationVersion > 0 &&
            sample.CalibrationVersion < protocolContext.CalibrationVersion)
        {
            sample.IsCalibrated = false;
            LogCalibrationInvalidated(
                sample.UserId,
                "stale_calibration_version",
                sample.CalibrationVersion,
                protocolContext.CalibrationVersion,
                sample.ClientSessionId);
        }

        lock (posesLock)
        {
            LiveVRPoseSample previousSample;
            bool hasPreviousSample = latestPoses.TryGetValue(sample.UserId, out previousSample);
            if (sample.IsCalibrated && ShouldRejectPoseJump(sample, previousSample, hasPreviousSample, out string jumpReason))
            {
                MarkUserNeedsPoseReanchor(sample.UserId);
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long nextWarningUnixMs;
                if (!nextPoseJumpWarningUnixMsByUserId.TryGetValue(sample.UserId, out nextWarningUnixMs) || now >= nextWarningUnixMs)
                {
                    nextPoseJumpWarningUnixMsByUserId[sample.UserId] = now + 1000;
                    Debug.LogWarning(string.Format(
                        "[LiveVR] Host rejected calibrated pose jump for user {0}; keeping previous pose until fresh calibration/reanchor. {1}",
                        sample.UserId,
                        jumpReason));
                }
                return;
            }

            latestPoses[sample.UserId] = sample;
        }
    }

    private void StoreClientEndpoint(int userId, IPEndPoint endpoint)
    {
        if (endpoint == null)
            return;

        lock (endpointsLock)
        {
            clientEndpoints[userId] = new IPEndPoint(endpoint.Address, endpoint.Port);
        }
    }

    private void StoreClientConnection(
        IPEndPoint endpoint,
        int reportedUserId,
        bool proactiveResetEnabled,
        bool posePacket,
        string deviceKey,
        string deviceName,
        string clientSession)
    {
        if (endpoint == null)
            return;

        string endpointKey = endpoint.ToString();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (endpointsLock)
        {
            LiveVRClientConnectionInfo info;
            if (!clientConnectionsByEndpoint.TryGetValue(endpointKey, out info))
            {
                info = new LiveVRClientConnectionInfo
                {
                    EndpointKey = endpointKey,
                    Address = endpoint.Address.ToString(),
                    Port = endpoint.Port,
                    AssignedUserId = -1,
                    AssignmentStatus = "seen"
                };
            }

            bool sessionChanged = !string.IsNullOrEmpty(clientSession) &&
                                  !string.IsNullOrEmpty(info.ClientSessionId) &&
                                  !string.Equals(info.ClientSessionId, clientSession, StringComparison.Ordinal);

            info.ReportedUserId = reportedUserId;
            if (!string.IsNullOrEmpty(deviceKey))
                info.DeviceKey = BuildAssignmentDeviceKey(endpointKey, deviceKey);
            if (!string.IsNullOrEmpty(deviceName))
                info.DeviceName = deviceName;
            if (!string.IsNullOrEmpty(clientSession))
                info.ClientSessionId = clientSession;

            if (posePacket)
            {
                info.LastPoseReceiveUnixMilliseconds = now;
            }
            else
            {
                info.ProactiveResetEnabled = proactiveResetEnabled;
                info.LastHelloReceiveUnixMilliseconds = now;
            }

            clientConnectionsByEndpoint[endpointKey] = info;

            if (sessionChanged && info.AssignedUserId >= 0)
            {
                ClearHostPoseStateForUser(info.AssignedUserId);
                MarkUserNeedsPoseReanchor(info.AssignedUserId);
                Debug.LogFormat(
                    "[LiveVR] Client user {0} session changed on {1}; cached pose cleared and reanchor required.",
                    info.AssignedUserId,
                    endpointKey);
            }
        }
    }

    private static string BuildAssignmentDeviceKey(string endpointKey, string reportedDeviceKey)
    {
        if (!string.IsNullOrEmpty(reportedDeviceKey))
            return reportedDeviceKey;

        return "endpoint:" + endpointKey;
    }

    private void EnsureClientIdentity()
    {
        if (!string.IsNullOrEmpty(clientDeviceKey))
            return;

        const string playerPrefsKey = "LiveVR.ClientDeviceKey";
        clientDeviceKey = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(clientDeviceKey))
        {
            clientDeviceKey = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(playerPrefsKey, clientDeviceKey);
            PlayerPrefs.Save();
        }

        clientDeviceName = SystemInfo.deviceName;
        if (string.IsNullOrEmpty(clientDeviceName))
            clientDeviceName = "LiveVRClient";
    }

    private static IPEndPoint BuildEndpoint(LiveVRClientConnectionInfo info)
    {
        IPAddress address;
        if (!IPAddress.TryParse(info.Address, out address))
            return null;

        return new IPEndPoint(address, info.Port);
    }

    private string GetEndpointDebugLabel(int userId)
    {
        lock (endpointsLock)
        {
            string endpointKey;
            if (assignedEndpointByUserId.TryGetValue(userId, out endpointKey))
                return endpointKey;

            IPEndPoint endpoint;
            if (clientEndpoints.TryGetValue(userId, out endpoint) && endpoint != null)
                return endpoint.ToString();
        }

        return "none";
    }

    private void ApplyHostAssignment(LiveVRClientAssignmentMessage assignment)
    {
        if (mode != LiveVRExperimentMode.ClientOnly)
            return;

        int previousUserId = localUserId;
        bool hostRunChanged = !string.IsNullOrEmpty(assignment.HostRunId) &&
                              !string.Equals(lastAssignedHostRunId, assignment.HostRunId, StringComparison.Ordinal);
        localUserId = Mathf.Max(0, assignment.UserId);
        clientProactiveResetEnabled = assignment.ProactiveResetEnabled;
        hasHostAssignment = true;
        if (!string.IsNullOrEmpty(assignment.HostRunId))
        {
            lastAssignedHostRunId = assignment.HostRunId;
            protocolContext.HostRunId = assignment.HostRunId;
            protocolContext.RunId = assignment.HostRunId;
        }
        clientAssignmentStatus = string.Format(
            "assigned user {0}/{1} run={2}",
            localUserId,
            assignment.ExpectedUserCount > 0 ? assignment.ExpectedUserCount.ToString() : "?",
            string.IsNullOrEmpty(assignment.HostRunId) ? "unknown" : assignment.HostRunId);

        if (previousUserId != localUserId)
        {
            ClearLocalCalibration("assigned_user_changed");
            lastCenterCalibrationCommandEventId = -1;
            lastCenterCalibrationStatus = "host assigned user; waiting for calibration";
            if (clientPresentationState != null)
                clientPresentationState.ClearForRestart(protocolContext.RestartEpoch, "assignment");
        }
        else if (hostRunChanged)
        {
            lastCenterCalibrationStatus = hasCalibration
                ? "new host run assigned; calibration preserved"
                : "new host run assigned; waiting for calibration";
            if (clientPresentationState != null)
                clientPresentationState.ClearForRestart(protocolContext.RestartEpoch, "assignment_run");
        }

        lock (clientStateLock)
        {
            lastAckUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        LiveVRClientPreferences.SaveHostConnection(hostAddress, hostPosePort);
        Debug.LogFormat(
            "[LiveVR] Host assignment received: user={0}, proactiveReset={1}, hostRunChanged={2}, calibration={3}, calVersion={4}.",
            localUserId,
            clientProactiveResetEnabled,
            hostRunChanged,
            hasCalibration,
            protocolContext.CalibrationVersion);
    }

    private void HandleControlAck(LiveVRControlAckMessage controlAck)
    {
        if (controlAck.Accepted)
        {
            reliableControl.Acknowledge(controlAck.MessageId);
            return;
        }

        Debug.LogWarning(string.Format(
            "[LiveVR] Reliable control rejected by client user={0} type={1} messageId={2} reason={3}; pending kept for retry/timeout.",
            controlAck.UserId,
            controlAck.MessageType,
            controlAck.MessageId,
            string.IsNullOrEmpty(controlAck.Reason) ? "unknown" : controlAck.Reason));
    }

    private void ClearLocalCalibration(string reason)
    {
        hasCalibration = false;
        hasLastAcceptedLocalPose = false;
        LogLocalCalibrationCleared(reason);
    }

    private void LogLocalCalibrationCleared(string reason)
    {
        Debug.LogWarning(string.Format(
            "[LiveVR] Local calibration cleared user={0} reason={1} hostRun={2} restartEpoch={3} calibrationVersion={4} session={5} state={6}.",
            localUserId,
            string.IsNullOrEmpty(reason) ? "unknown" : reason,
            protocolContext.HostRunId,
            protocolContext.RestartEpoch,
            protocolContext.CalibrationVersion,
            clientSessionId,
            ExperimentState));
    }

    private void LogCalibrationInvalidated(
        int userId,
        string reason,
        int sampleCalibrationVersion,
        int hostCalibrationVersion,
        string sampleClientSessionId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long nextWarningUnixMs;
        if (nextCalibrationInvalidWarningUnixMsByUserId.TryGetValue(userId, out nextWarningUnixMs) && now < nextWarningUnixMs)
            return;

        nextCalibrationInvalidWarningUnixMsByUserId[userId] = now + 1000;
        Debug.LogWarning(string.Format(
            "[LiveVR] Host marked pose uncalibrated user={0} reason={1} sampleCalVersion={2} hostCalVersion={3} hostRun={4} restartEpoch={5} sampleSession={6} state={7}.",
            userId,
            string.IsNullOrEmpty(reason) ? "unknown" : reason,
            sampleCalibrationVersion,
            hostCalibrationVersion,
            protocolContext.HostRunId,
            protocolContext.RestartEpoch,
            string.IsNullOrEmpty(sampleClientSessionId) ? "-" : sampleClientSessionId,
            experimentState));
    }

    private void StoreHello(int userId, IPEndPoint endpoint)
    {
        lock (posesLock)
        {
            latestHelloReceiveUnixMs[userId] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        StoreClientEndpoint(userId, endpoint);
    }

    private void StoreResetDone(LiveVRResetDoneMessage resetDone, IPEndPoint endpoint)
    {
        lock (clientStateLock)
        {
            latestResetDoneEventByUserId[resetDone.UserId] = resetDone.EventId;
            latestResetDoneByUserId[resetDone.UserId] = resetDone;
        }

        StoreClientEndpoint(resetDone.UserId, endpoint);
        Debug.Log(string.Format(
            "[LiveVR] Host received RESET_DONE user={0} event={1} yaw={2:F1} injected={3:F1}",
            resetDone.UserId,
            resetDone.EventId,
            resetDone.FinalPhysicalYawDegrees,
            resetDone.FinalInjectedTurnDegrees));
    }

    private void StoreTargetReached(LiveVRTargetReachedMessage targetReached, IPEndPoint endpoint)
    {
        StoreClientEndpoint(targetReached.UserId, endpoint);

        bool allTargetsReached;
        lock (clientStateLock)
        {
            if (targetReached.RunComplete)
                targetReachedUserIds.Add(targetReached.UserId);
            allTargetsReached = AreAllExpectedTargetsReachedLocked();
        }

        Debug.Log(string.Format(
            "[LiveVR] Host received TARGET_REACHED user={0} target={1} distance={2:F2} cumulative={3:F2} complete={4} pos=({5:F2},{6:F2}) targetPos=({7:F2},{8:F2})",
            targetReached.UserId,
            targetReached.TargetIndex,
            targetReached.DistanceMeters,
            targetReached.CumulativeDistanceMeters,
            targetReached.RunComplete,
            targetReached.UserPosition.x,
            targetReached.UserPosition.y,
            targetReached.TargetPosition.x,
            targetReached.TargetPosition.y));

        if (experimentState == LiveVRExperimentState.Running && targetReached.RunComplete && allTargetsReached)
        {
            SetTrialEndState(LiveVRTrialEndState.Normal);
            SetExperimentState(LiveVRExperimentState.Completed);
            Debug.Log("[LiveVR] All expected users reached their targets. Trial completed.");
        }
    }

    private bool AreAllExpectedTargetsReachedLocked()
    {
        int requiredCount = Mathf.Max(0, expectedUserCountForAssignment);
        if (requiredCount == 0)
            return false;

        for (int userId = 0; userId < requiredCount; userId++)
        {
            if (!targetReachedUserIds.Contains(userId))
                return false;
        }

        return true;
    }

    private static LiveVRUserSource ResolveConfiguredUserSource(LiveVRUserSource[] configuredUserSources, int userId)
    {
        if (configuredUserSources == null || userId < 0 || userId >= configuredUserSources.Length)
            return LiveVRUserSource.RequiredLiveHmd;

        return configuredUserSources[userId];
    }

    private static float ToProjectYaw(Quaternion rotation)
    {
        return NormalizeDegrees(-rotation.eulerAngles.y);
    }

    private bool ShouldRejectPoseJump(
        LiveVRPoseSample current,
        LiveVRPoseSample previous,
        bool hasPrevious,
        out string reason)
    {
        reason = string.Empty;
        if (!enablePoseJumpGuard || !hasPrevious)
            return false;

        if (!current.IsCalibrated || !previous.IsCalibrated)
            return false;

        if (current.UserId != previous.UserId)
            return false;

        if (!string.Equals(current.ClientSessionId, previous.ClientSessionId, StringComparison.Ordinal))
            return false;

        if (current.CalibrationVersion != previous.CalibrationVersion)
            return false;

        float deltaSeconds = (current.ClientUnixMilliseconds - previous.ClientUnixMilliseconds) / 1000.0f;
        if (deltaSeconds <= 0.0001f)
            deltaSeconds = (current.HostReceiveUnixMilliseconds - previous.HostReceiveUnixMilliseconds) / 1000.0f;

        if (deltaSeconds <= 0.0001f)
            return false;

        float distance = Vector2.Distance(previous.ExperimentPosition, current.ExperimentPosition);
        float speed = distance / deltaSeconds;
        float yawDelta = Mathf.Abs(Mathf.DeltaAngle(previous.YawDegrees, current.YawDegrees));
        float yawRate = yawDelta / deltaSeconds;

        bool impossibleTranslation =
            distance > Mathf.Max(0.05f, maxCalibratedPoseStepMeters) &&
            speed > Mathf.Max(0.1f, maxCalibratedPoseSpeedMetersPerSecond);
        bool impossiblePoseSnap =
            distance > Mathf.Max(0.25f, maxCalibratedPoseStepMeters * 0.5f) &&
            yawRate > Mathf.Max(1.0f, maxCalibratedYawRateDegreesPerSecond);

        if (!impossibleTranslation && !impossiblePoseSnap)
            return false;

        reason = string.Format(
            "prevSeq={0} seq={1} dt={2:F3}s dist={3:F2}m speed={4:F1}m/s yawDelta={5:F1}deg yawRate={6:F0}deg/s prev=({7:F2},{8:F2}) curr=({9:F2},{10:F2})",
            previous.Sequence,
            current.Sequence,
            deltaSeconds,
            distance,
            speed,
            yawDelta,
            yawRate,
            previous.ExperimentPosition.x,
            previous.ExperimentPosition.y,
            current.ExperimentPosition.x,
            current.ExperimentPosition.y);
        return true;
    }

    private static Vector2 Rotate2D(Vector2 value, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
    }

    private static float NormalizeDegrees(float degrees)
    {
        degrees %= 360.0f;
        if (degrees > 180.0f)
            degrees -= 360.0f;
        if (degrees < -180.0f)
            degrees += 360.0f;
        return degrees;
    }
}
