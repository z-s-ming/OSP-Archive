using UnityEngine;

[DefaultExecutionOrder(11000)]
public class LiveVRClientVirtualViewBinder : MonoBehaviour
{
    [HideInInspector]
    [SerializeField] private LiveVRNetworkManager networkManager;
    [HideInInspector]
    [SerializeField] private Transform clientVirtualViewRoot;
    [HideInInspector]
    [SerializeField] private Transform hmdCamera;
    [HideInInspector]
    [SerializeField] private bool applyVirtualPose = true;
    [HideInInspector]
    [SerializeField] private bool preserveTrackedHeadLocalOffset = true;
    [HideInInspector]
    [SerializeField] private bool requireExplicitViewRoot = true;
    [HideInInspector]
    [SerializeField] private float viewHeightFallbackMeters = 1.6f;
    [HideInInspector]
    [SerializeField] private float staleVirtualPoseTimeoutSeconds = 0.5f;
    [HideInInspector]
    [SerializeField] private bool applyOnlyWhileRunning = true;
    [HideInInspector]
    [SerializeField] private float maxGainApplyDeltaTimeSeconds = 1.0f / 60.0f;

    private uint lastAppliedSequence;
    private uint lastGainCommandSequence;
    private long lastVirtualPoseHostTime;
    private bool hasLastHostVirtualPose;
    private Vector3 lastHostVirtualWorldPosition;
    private float lastHostVirtualYawDegrees;
    private bool hasInitialViewRootPose;
    private Vector3 initialViewRootPosition;
    private Quaternion initialViewRootRotation;
    private Vector3 initialViewRootScale;
    private bool warnedMissingViewRoot;
    private bool warnedInvalidViewRoot;
    private float nextResetDebugLogTime;
    private int activeLocalResetEventId = -1;
    private int completedLocalResetEventId = -1;
    private bool hasLocalResetMapping;
    private bool localResetDoneSent;
    private float localResetInitialPhysicalYaw;
    private float localResetPreviousPhysicalYaw;
    private float localResetDesiredPhysicalTurn;
    private float localResetInjectRate;
    private float localResetAccumulatedPhysicalTurn;
    private float localResetDesiredInjectedRootTurn;
    private float localResetAppliedInjectedRootTurn;
    private float localResetLastAppliedInjectedRootTurn;
    private GainType activeHostGainType = GainType.Undefined;
    private float activeHostGainRateDegreesPerSecond;
    private float activeHostGainExpiresAtTime;
    private float applyGainWindowStartTime;
    private float nextApplyGainSummaryLogTime;
    private int applyGainLateUpdateFrames;
    private int applyGainActiveFrames;
    private int applyGainAppliedFrames;
    private int applyGainExpiredFrames;
    private int applyGainMissingCameraFrames;
    private float applyGainAccumulatedYaw;
    private bool applyGainHasRootYawStart;
    private float applyGainRootYawStart;
    private float applyGainRootYawEnd;
    private GainType applyGainLastType = GainType.Undefined;
    private float applyGainLastRate;
    private float applyGainLastDelta;
    private float applyGainLastExpiresIn;
    private float nextApplyGainSkipLogTime;
    private string lastApplyGainSkipReason;
    private int applyGainSkipFrames;
    private float lastVirtualPoseReceiveAgeSeconds = float.PositiveInfinity;
    private uint lastVirtualPoseSequenceForDiagnostics;
    private GainType lastVirtualPoseGainTypeForDiagnostics = GainType.Undefined;
    private float lastVirtualPoseGainRateForDiagnostics;
    private float lastVirtualPoseGainValidForDiagnostics;

    public float LastVirtualPoseAgeSeconds
    {
        get
        {
            if (lastVirtualPoseHostTime <= 0)
                return float.PositiveInfinity;

            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Mathf.Max(0.0f, (now - lastVirtualPoseHostTime) / 1000.0f);
        }
    }

    public void ClearLocalResetState()
    {
        ClearActiveHostGain();
        activeLocalResetEventId = -1;
        completedLocalResetEventId = -1;
        hasLocalResetMapping = false;
        localResetDoneSent = false;
        localResetInitialPhysicalYaw = 0.0f;
        localResetPreviousPhysicalYaw = 0.0f;
        localResetDesiredPhysicalTurn = 0.0f;
        localResetInjectRate = 0.0f;
        localResetAccumulatedPhysicalTurn = 0.0f;
        localResetDesiredInjectedRootTurn = 0.0f;
        localResetAppliedInjectedRootTurn = 0.0f;
        localResetLastAppliedInjectedRootTurn = 0.0f;
        nextResetDebugLogTime = 0.0f;
    }

    public void RestoreInitialViewRootState()
    {
        ClearLocalResetState();
        hasLastHostVirtualPose = false;
        lastHostVirtualWorldPosition = Vector3.zero;
        lastHostVirtualYawDegrees = 0.0f;
        lastAppliedSequence = 0;
        lastVirtualPoseHostTime = 0;

        Transform root = ResolveViewRoot();
        if (root == null)
            return;

        CaptureInitialViewRootPose(root);
        root.position = initialViewRootPosition;
        root.rotation = initialViewRootRotation;
        root.localScale = initialViewRootScale;
    }

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        Transform newClientVirtualViewRoot,
        Transform newHmdCamera,
        bool newApplyVirtualPose,
        bool newPreserveTrackedHeadLocalOffset,
        bool newRequireExplicitViewRoot,
        float newViewHeightFallbackMeters,
        float newStaleVirtualPoseTimeoutSeconds,
        bool newApplyOnlyWhileRunning)
    {
        networkManager = newNetworkManager;
        clientVirtualViewRoot = newClientVirtualViewRoot;
        hmdCamera = newHmdCamera;
        applyVirtualPose = newApplyVirtualPose;
        preserveTrackedHeadLocalOffset = newPreserveTrackedHeadLocalOffset;
        requireExplicitViewRoot = newRequireExplicitViewRoot;
        viewHeightFallbackMeters = newViewHeightFallbackMeters;
        staleVirtualPoseTimeoutSeconds = newStaleVirtualPoseTimeoutSeconds;
        applyOnlyWhileRunning = newApplyOnlyWhileRunning;
    }

    private void LateUpdate()
    {
        if (!applyVirtualPose)
        {
            LogApplyGainSkip("apply_virtual_pose_disabled", null, null);
            return;
        }

        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null)
        {
            LogApplyGainSkip("no_network_manager", null, null);
            return;
        }

        if (manager.Mode == LiveVRExperimentMode.Disabled)
        {
            ClearActiveHostGain();
            return;
        }

        if (manager.IsHost)
        {
            LogApplyGainSkip("host_mode", manager, null);
            return;
        }

        Transform root = ResolveViewRoot();
        if (root == null)
        {
            LogApplyGainSkip("no_view_root", manager, null);
            return;
        }

        if (TryApplyLocalResetInjection(root, manager))
        {
            ClearActiveHostGain();
            LogApplyGainSkip("reset_injection_active", manager, root);
            return;
        }

        if (applyOnlyWhileRunning && manager.ExperimentState != LiveVRExperimentState.Running)
        {
            ClearActiveHostGain();
            LogApplyGainSkip("not_running", manager, root);
            return;
        }

        LiveVRVirtualPoseMessage virtualPose;
        long virtualPoseReceiveUnixMilliseconds;
        bool hasVirtualPose = manager.TryGetLatestVirtualPose(out virtualPose, out virtualPoseReceiveUnixMilliseconds);

        bool hasFreshVirtualPose = false;
        if (hasVirtualPose)
        {
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            float ageSeconds = virtualPoseReceiveUnixMilliseconds > 0
                ? Mathf.Max(0.0f, (now - virtualPoseReceiveUnixMilliseconds) / 1000.0f)
                : float.PositiveInfinity;
            lastVirtualPoseReceiveAgeSeconds = ageSeconds;
            lastVirtualPoseSequenceForDiagnostics = virtualPose.Sequence;
            lastVirtualPoseGainTypeForDiagnostics = virtualPose.GainType;
            lastVirtualPoseGainRateForDiagnostics = virtualPose.GainRateDegreesPerSecond;
            lastVirtualPoseGainValidForDiagnostics = virtualPose.GainValidSeconds;
            hasFreshVirtualPose = ageSeconds <= staleVirtualPoseTimeoutSeconds;
            UpdateActiveHostGain(virtualPose, ageSeconds);
        }
        else
        {
            lastVirtualPoseReceiveAgeSeconds = float.PositiveInfinity;
            LogApplyGainSkip("no_virtual_pose", manager, root);
        }

        if (hasFreshVirtualPose)
        {
            lastHostVirtualWorldPosition = Utility.CastVector2Dto3D(virtualPose.VirtualPosition, 0.0f);
            lastHostVirtualYawDegrees = virtualPose.VirtualYawDegrees;
            hasLastHostVirtualPose = true;
        }

        if (hasFreshVirtualPose && virtualPose.Sequence != lastAppliedSequence)
        {
            ApplyVirtualPose(root, hasFreshVirtualPose, virtualPose, manager);
            lastAppliedSequence = virtualPose.Sequence;
            lastVirtualPoseHostTime = virtualPose.HostUnixMilliseconds;
        }
        else if (hasVirtualPose && !hasFreshVirtualPose)
        {
            LogApplyGainSkip("stale_virtual_pose", manager, root);
        }

        ApplyActiveHostGain(root, manager);
    }

    private void LogApplyGainSkip(string reason, LiveVRNetworkManager manager, Transform root)
    {
        applyGainSkipFrames++;
        lastApplyGainSkipReason = reason;

        if (Time.unscaledTime < nextApplyGainSkipLogTime)
            return;

        nextApplyGainSkipLogTime = Time.unscaledTime + 1.0f;
        string state = manager != null ? manager.ExperimentState.ToString() : "none";
        string rootName = root != null ? root.name : "null";
        float expiresIn = activeHostGainExpiresAtTime - Time.unscaledTime;
        Debug.Log(string.Format(
            "[LiveVR] ApplyGainSkip reason={0} lastReason={1} frames={2} state={3} root={4} activeType={5} activeRate={6:F2}/s expiresIn={7:F3}s poseSeq={8} poseAge={9:F3}s poseGain={10} poseRate={11:F2}/s poseValid={12:F3}s",
            reason,
            lastApplyGainSkipReason,
            applyGainSkipFrames,
            state,
            rootName,
            activeHostGainType,
            activeHostGainRateDegreesPerSecond,
            expiresIn,
            lastVirtualPoseSequenceForDiagnostics,
            lastVirtualPoseReceiveAgeSeconds,
            lastVirtualPoseGainTypeForDiagnostics,
            lastVirtualPoseGainRateForDiagnostics,
            lastVirtualPoseGainValidForDiagnostics));
        applyGainSkipFrames = 0;
    }

    private void ApplyVirtualPose(Transform root, bool hasFreshVirtualPose, LiveVRVirtualPoseMessage virtualPose, LiveVRNetworkManager manager)
    {
        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform != null && cameraTransform != root)
        {
            ApplyInjectedYawFromHost(root, cameraTransform, virtualPose);
            return;
        }

        // Fallback for non-XR debug cameras only. Formal XR clients should provide a VirtualWorldRoot.
        if (hasFreshVirtualPose)
        {
            Vector3 targetWorldPosition = Utility.CastVector2Dto3D(virtualPose.VirtualPosition, 0.0f);
            Quaternion targetWorldRotation = Utility.CastRotation2Dto3D(NormalizeDegrees(virtualPose.VirtualYawDegrees));
            root.position = targetWorldPosition + Vector3.up * viewHeightFallbackMeters;
            root.rotation = targetWorldRotation;
        }
    }

    private void ApplyInjectedYawFromHost(
        Transform root,
        Transform cameraTransform,
        LiveVRVirtualPoseMessage virtualPose)
    {
        float injectedYawDelta = virtualPose.InjectedYawDeltaDegrees;
        if (Mathf.Abs(injectedYawDelta) <= Mathf.Epsilon)
            return;

        // OpenRDW-style display injection: keep XR tracking authoritative and
        // rotate only the virtual world around the user's current HMD position.
        Vector3 pivot = FlattenedPivot(cameraTransform.position, root.position.y);
        root.RotateAround(pivot, Vector3.up, injectedYawDelta);
    }

    private void UpdateActiveHostGain(LiveVRVirtualPoseMessage virtualPose, float receiveAgeSeconds)
    {
        if (virtualPose.Sequence == lastGainCommandSequence)
            return;

        lastGainCommandSequence = virtualPose.Sequence;

        if (virtualPose.GainType == GainType.Undefined ||
            Mathf.Abs(virtualPose.GainRateDegreesPerSecond) <= Mathf.Epsilon ||
            virtualPose.GainValidSeconds <= 0.0f)
        {
            ClearActiveHostGain();
            return;
        }

        float remainingValidSeconds = virtualPose.GainValidSeconds - Mathf.Max(0.0f, receiveAgeSeconds);
        if (remainingValidSeconds <= 0.0f)
        {
            ClearActiveHostGain();
            return;
        }

        activeHostGainType = virtualPose.GainType;
        activeHostGainRateDegreesPerSecond = virtualPose.GainRateDegreesPerSecond;
        activeHostGainExpiresAtTime = Time.unscaledTime + remainingValidSeconds;
    }

    private void ApplyActiveHostGain(Transform root, LiveVRNetworkManager manager)
    {
        RecordApplyGainFrame();

        if (activeHostGainType == GainType.Undefined ||
            Mathf.Abs(activeHostGainRateDegreesPerSecond) <= Mathf.Epsilon)
        {
            LogApplyGainSkip("no_active_gain", manager, root);
            LogApplyGainSummaryIfNeeded(root);
            return;
        }

        applyGainActiveFrames++;
        applyGainLastType = activeHostGainType;
        applyGainLastRate = activeHostGainRateDegreesPerSecond;
        applyGainLastExpiresIn = activeHostGainExpiresAtTime - Time.unscaledTime;

        if (Time.unscaledTime > activeHostGainExpiresAtTime)
        {
            applyGainExpiredFrames++;
            ClearActiveHostGain();
            LogApplyGainSummaryIfNeeded(root);
            return;
        }

        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform == null || cameraTransform == root)
        {
            applyGainMissingCameraFrames++;
            LogApplyGainSummaryIfNeeded(root);
            return;
        }

        float applyDeltaTime = Mathf.Min(Time.deltaTime, Mathf.Max(0.001f, maxGainApplyDeltaTimeSeconds));
        float injectedYawDelta = activeHostGainRateDegreesPerSecond * applyDeltaTime;
        if (Mathf.Abs(injectedYawDelta) <= Mathf.Epsilon)
        {
            LogApplyGainSummaryIfNeeded(root);
            return;
        }

        Vector3 pivot = FlattenedPivot(cameraTransform.position, root.position.y);
        float rootYawBefore = ToProjectYaw(root.rotation);
        if (!applyGainHasRootYawStart)
        {
            applyGainHasRootYawStart = true;
            applyGainRootYawStart = rootYawBefore;
        }

        root.RotateAround(pivot, Vector3.up, injectedYawDelta);
        applyGainRootYawEnd = ToProjectYaw(root.rotation);
        applyGainAppliedFrames++;
        applyGainAccumulatedYaw += injectedYawDelta;
        applyGainLastDelta = injectedYawDelta;
        LogApplyGainSummaryIfNeeded(root);
    }

    private void ClearActiveHostGain()
    {
        activeHostGainType = GainType.Undefined;
        activeHostGainRateDegreesPerSecond = 0.0f;
        activeHostGainExpiresAtTime = 0.0f;
    }

    private void RecordApplyGainFrame()
    {
        if (applyGainWindowStartTime <= 0.0f)
        {
            applyGainWindowStartTime = Time.unscaledTime;
            nextApplyGainSummaryLogTime = Time.unscaledTime + 1.0f;
        }

        applyGainLateUpdateFrames++;
    }

    private void LogApplyGainSummaryIfNeeded(Transform root)
    {
        if (Time.unscaledTime < nextApplyGainSummaryLogTime)
            return;

        if (applyGainActiveFrames <= 0 &&
            applyGainAppliedFrames <= 0 &&
            applyGainExpiredFrames <= 0 &&
            applyGainMissingCameraFrames <= 0)
        {
            ResetApplyGainSummaryWindow();
            return;
        }

        float duration = Mathf.Max(0.001f, Time.unscaledTime - applyGainWindowStartTime);
        float actualRootYawDelta = applyGainHasRootYawStart
            ? Mathf.DeltaAngle(applyGainRootYawStart, applyGainRootYawEnd)
            : 0.0f;
        float expectedRootYawDelta = applyGainAccumulatedYaw;
        float expectedPerceivedYawDelta = -applyGainAccumulatedYaw;
        float yawError = Mathf.DeltaAngle(expectedRootYawDelta, actualRootYawDelta);
        Debug.Log(string.Format(
            "[LiveVR] ApplyGainCheck hz={0:F1} frames={1} activeFrames={2} appliedFrames={3} expired={4} missingCamera={5} expectedRootYawDelta={6:F2} expectedPerceivedYawDelta={7:F2} actualRootYawDelta={8:F2} error={9:F2} rootYawStart={10:F1} rootYawEnd={11:F1} lastType={12} lastRate={13:F2}/s lastDelta={14:F3} expiresIn={15:F3}s",
            applyGainAppliedFrames / duration,
            applyGainLateUpdateFrames,
            applyGainActiveFrames,
            applyGainAppliedFrames,
            applyGainExpiredFrames,
            applyGainMissingCameraFrames,
            expectedRootYawDelta,
            expectedPerceivedYawDelta,
            actualRootYawDelta,
            yawError,
            applyGainHasRootYawStart ? applyGainRootYawStart : ToProjectYaw(root.rotation),
            ToProjectYaw(root.rotation),
            applyGainLastType,
            applyGainLastRate,
            applyGainLastDelta,
            applyGainLastExpiresIn));
        ResetApplyGainSummaryWindow();
    }

    private void ResetApplyGainSummaryWindow()
    {
        applyGainWindowStartTime = Time.unscaledTime;
        nextApplyGainSummaryLogTime = Time.unscaledTime + 1.0f;
        applyGainLateUpdateFrames = 0;
        applyGainActiveFrames = 0;
        applyGainAppliedFrames = 0;
        applyGainExpiredFrames = 0;
        applyGainMissingCameraFrames = 0;
        applyGainAccumulatedYaw = 0.0f;
        applyGainHasRootYawStart = false;
        applyGainRootYawStart = 0.0f;
        applyGainRootYawEnd = 0.0f;
        applyGainLastType = GainType.Undefined;
        applyGainLastRate = 0.0f;
        applyGainLastDelta = 0.0f;
        applyGainLastExpiresIn = 0.0f;
    }

    private bool TryApplyLocalResetInjection(Transform root, LiveVRNetworkManager manager)
    {
        LiveVRResetStartMessage resetStart;
        if (manager == null || !manager.TryGetLatestResetStart(out resetStart))
        {
            hasLocalResetMapping = false;
            localResetDoneSent = false;
            return false;
        }

        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform == null || cameraTransform == root)
        {
            if (Time.unscaledTime >= nextResetDebugLogTime)
            {
                nextResetDebugLogTime = Time.unscaledTime + 0.5f;
                Debug.LogWarning(string.Format(
                    "[LiveVR] Client reset injection skipped event={0}: camera/root invalid camera={1} root={2}",
                    resetStart.EventId,
                    cameraTransform != null ? cameraTransform.name : "null",
                    root != null ? root.name : "null"));
            }
            return false;
        }

        float localHeadYaw = ResolveLocalHeadYaw(root, cameraTransform);
        bool isCurrentActiveReset = hasLocalResetMapping && activeLocalResetEventId == resetStart.EventId;
        if (!isCurrentActiveReset)
        {
            if (completedLocalResetEventId == resetStart.EventId)
                return false;

            if (!manager.HasFreshResetStart(3.0f) || Mathf.Abs(resetStart.PhysicalTurnDegrees) <= 0.1f)
                return false;

            activeLocalResetEventId = resetStart.EventId;
            hasLocalResetMapping = true;
            localResetDoneSent = false;
            localResetInitialPhysicalYaw = localHeadYaw;
            localResetPreviousPhysicalYaw = localHeadYaw;
            localResetDesiredPhysicalTurn = resetStart.PhysicalTurnDegrees;
            localResetInjectRate = CalculateInjectionRate(resetStart.PhysicalTurnDegrees);
            localResetAccumulatedPhysicalTurn = 0.0f;
            localResetDesiredInjectedRootTurn = resetStart.InjectedTurnDegrees;
            localResetAppliedInjectedRootTurn = 0.0f;
            localResetLastAppliedInjectedRootTurn = 0.0f;
        }

        if (localResetDoneSent)
        {
            return true;
        }

        float physicalYawDelta = Mathf.DeltaAngle(localResetPreviousPhysicalYaw, localHeadYaw);
        localResetPreviousPhysicalYaw = localHeadYaw;
        float nextAccumulatedPhysicalTurn = AccumulatePhysicalTurnTowardPlan(
            localResetAccumulatedPhysicalTurn,
            physicalYawDelta,
            localResetDesiredPhysicalTurn);
        localResetAccumulatedPhysicalTurn = nextAccumulatedPhysicalTurn;
        localResetAppliedInjectedRootTurn = localResetAccumulatedPhysicalTurn * localResetInjectRate;

        float physicalTurn = localResetAccumulatedPhysicalTurn;
        float progress = Mathf.Abs(localResetDesiredPhysicalTurn) <= Mathf.Epsilon
            ? 1.0f
            : Mathf.Clamp01(Mathf.Abs(physicalTurn) / Mathf.Abs(localResetDesiredPhysicalTurn));
        ApplyResetVirtualMapping(root, cameraTransform);

        if (progress >= 0.995f)
        {
            completedLocalResetEventId = activeLocalResetEventId;
            localResetAccumulatedPhysicalTurn = localResetDesiredPhysicalTurn;
            localResetAppliedInjectedRootTurn = localResetDesiredInjectedRootTurn;
            ApplyResetVirtualMapping(root, cameraTransform);
            if (!localResetDoneSent)
                localResetDoneSent = true;
        }

        return true;
    }

    private static float AccumulatePhysicalTurnTowardPlan(float currentTurn, float physicalYawDelta, float desiredTurn)
    {
        float desiredMagnitude = Mathf.Abs(desiredTurn);
        if (desiredMagnitude <= Mathf.Epsilon)
            return 0.0f;

        float directionSign = Mathf.Sign(desiredTurn);
        float currentMagnitude = Mathf.Clamp(Mathf.Abs(currentTurn), 0.0f, desiredMagnitude);
        float deltaAlongPlan = physicalYawDelta * directionSign;
        float nextMagnitude = Mathf.Clamp(currentMagnitude + deltaAlongPlan, 0.0f, desiredMagnitude);
        return directionSign * nextMagnitude;
    }

    private void ApplyResetVirtualMapping(Transform root, Transform cameraTransform)
    {
        float deltaInjectedTurn = localResetAppliedInjectedRootTurn - localResetLastAppliedInjectedRootTurn;
        if (Mathf.Abs(deltaInjectedTurn) <= Mathf.Epsilon)
            return;

        // OpenRDW-style injection: rotate the virtual world around the current HMD
        // horizontal position, not around the scene origin or the XR Origin.
        Vector3 pivot = FlattenedPivot(cameraTransform.position, root.position.y);
        float rootProjectYawDelta = -deltaInjectedTurn;
        root.RotateAround(pivot, Vector3.up, -rootProjectYawDelta);
        localResetLastAppliedInjectedRootTurn = localResetAppliedInjectedRootTurn;
    }

    private static float CalculateInjectionRate(float physicalTurnDegrees)
    {
        float absPhysicalTurn = Mathf.Abs(physicalTurnDegrees);
        if (absPhysicalTurn <= 0.1f)
            return 0.0f;

        return (360.0f - absPhysicalTurn) / absPhysicalTurn;
    }

    private static float ResolveLocalHeadYaw(Transform root, Transform cameraTransform)
    {
        // Reset progress must be based on the tracked HMD world yaw. If the
        // VirtualWorldRoot yaw is included here, world rotation feeds back into
        // the physical progress estimate.
        return ToProjectYaw(cameraTransform.rotation);
    }

    private Transform ResolveViewRoot()
    {
        if (clientVirtualViewRoot != null)
            return IsValidViewRoot(clientVirtualViewRoot) ? clientVirtualViewRoot : null;

        if (requireExplicitViewRoot)
        {
            if (!warnedMissingViewRoot)
            {
                warnedMissingViewRoot = true;
                Debug.LogWarning("[LiveVR] Client Virtual View Root is not assigned. Assign the VirtualWorldRoot/client visual environment root; LiveVR will not move Main Camera directly.");
            }
            return null;
        }

        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform == null)
            return null;

        if (cameraTransform.parent == null)
        {
            if (!warnedMissingViewRoot)
            {
                warnedMissingViewRoot = true;
                Debug.LogWarning("[LiveVR] Client virtual view root is missing and Main Camera has no parent. Assign the VirtualWorldRoot/client visual environment root; the camera transform will not be modified directly.");
            }
            return null;
        }

        if (!warnedMissingViewRoot)
        {
            warnedMissingViewRoot = true;
            Debug.LogWarning("[LiveVR] Client Virtual View Root must be assigned explicitly when using VirtualWorldRoot redirection; refusing to fall back to the camera parent.");
        }
        return null;
    }

    private bool IsValidViewRoot(Transform root)
    {
        if (root == null)
            return false;

        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform != null && root == cameraTransform)
        {
            WarnInvalidViewRoot("[LiveVR] Client Virtual View Root points to the HMD/Main Camera itself. Assign the VirtualWorldRoot/client visual environment root instead; camera transform will not be modified directly.");
            return false;
        }

        if (cameraTransform != null && cameraTransform.IsChildOf(root))
        {
            WarnInvalidViewRoot("[LiveVR] Client Virtual View Root contains the HMD/Main Camera. Assign a VirtualWorldRoot that is a sibling of the XR Origin, not the XR Origin or Camera Offset.");
            return false;
        }

        if (root.GetComponent<Camera>() != null)
        {
            WarnInvalidViewRoot("[LiveVR] Client Virtual View Root has a Camera component. Assign the VirtualWorldRoot/client visual environment root instead; camera transform will not be modified directly.");
            return false;
        }

        CaptureInitialViewRootPose(root);
        return true;
    }

    private void CaptureInitialViewRootPose(Transform root)
    {
        if (hasInitialViewRootPose || root == null)
            return;

        hasInitialViewRootPose = true;
        initialViewRootPosition = root.position;
        initialViewRootRotation = root.rotation;
        initialViewRootScale = root.localScale;
    }

    private void WarnInvalidViewRoot(string message)
    {
        if (warnedInvalidViewRoot)
            return;

        warnedInvalidViewRoot = true;
        Debug.LogWarning(message);
    }

    private Transform ResolveHmdCamera()
    {
        if (hmdCamera != null)
            return hmdCamera;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            hmdCamera = mainCamera.transform;

        return hmdCamera;
    }

    private static float ExtractYawDegrees(Quaternion rotation)
    {
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0.0f;
        if (forward.sqrMagnitude <= Mathf.Epsilon)
            return 0.0f;

        forward.Normalize();
        return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    private static float ToProjectYaw(Quaternion rotation)
    {
        return NormalizeDegrees(-rotation.eulerAngles.y);
    }

    private static Vector3 FlattenedPivot(Vector3 position, float y)
    {
        return new Vector3(position.x, y, position.z);
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

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }
}
