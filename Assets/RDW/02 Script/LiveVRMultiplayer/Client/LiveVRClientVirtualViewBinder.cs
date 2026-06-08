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

    private uint lastAppliedSequence;
    private long lastVirtualPoseHostTime;
    private bool hasLastHostVirtualPose;
    private Vector3 lastHostVirtualWorldPosition;
    private float lastHostVirtualYawDegrees;
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
    private Vector3 localResetStartCameraWorldPosition;
    private float localResetStartCameraWorldYaw;

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
        localResetStartCameraWorldPosition = Vector3.zero;
        localResetStartCameraWorldYaw = 0.0f;
        nextResetDebugLogTime = 0.0f;
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
            return;

        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || manager.IsHost)
            return;

        Transform root = ResolveViewRoot();
        if (root == null)
            return;

        if (TryApplyLocalResetInjection(root, manager))
            return;

        if (applyOnlyWhileRunning && manager.ExperimentState != LiveVRExperimentState.Running)
            return;

        LiveVRVirtualPoseMessage virtualPose;
        bool hasVirtualPose = manager.TryGetLatestVirtualPose(out virtualPose);

        bool hasFreshVirtualPose = false;
        if (hasVirtualPose)
        {
            long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            float ageSeconds = Mathf.Max(0.0f, (now - virtualPose.HostUnixMilliseconds) / 1000.0f);
            hasFreshVirtualPose = ageSeconds <= staleVirtualPoseTimeoutSeconds;
        }

        if (hasFreshVirtualPose)
        {
            lastHostVirtualWorldPosition = Utility.CastVector2Dto3D(virtualPose.VirtualPosition, 0.0f);
            lastHostVirtualYawDegrees = virtualPose.VirtualYawDegrees;
            hasLastHostVirtualPose = true;
        }

        if (!hasFreshVirtualPose || virtualPose.Sequence == lastAppliedSequence)
            return;

        ApplyVirtualPose(root, hasFreshVirtualPose, virtualPose, manager);
        if (hasFreshVirtualPose)
        {
            lastAppliedSequence = virtualPose.Sequence;
            lastVirtualPoseHostTime = virtualPose.HostUnixMilliseconds;
        }
    }

    private void ApplyVirtualPose(Transform root, bool hasFreshVirtualPose, LiveVRVirtualPoseMessage virtualPose, LiveVRNetworkManager manager)
    {
        Vector3 targetWorldPosition = hasFreshVirtualPose
            ? Utility.CastVector2Dto3D(virtualPose.VirtualPosition, 0.0f)
            : (hasLastHostVirtualPose ? lastHostVirtualWorldPosition : root.position);
        float hostVirtualYaw = hasFreshVirtualPose
            ? virtualPose.VirtualYawDegrees
            : (hasLastHostVirtualPose ? lastHostVirtualYawDegrees : ExtractYawDegrees(root.rotation));

        float desiredCameraWorldYaw = NormalizeDegrees(hostVirtualYaw);
        Quaternion targetWorldRotation = Utility.CastRotation2Dto3D(desiredCameraWorldYaw);

        if (preserveTrackedHeadLocalOffset)
        {
            Transform cameraTransform = ResolveHmdCamera();
            if (cameraTransform != null && cameraTransform != root)
            {
                ApplyVirtualFromPhysical(root, cameraTransform, targetWorldPosition, desiredCameraWorldYaw, hasFreshVirtualPose, virtualPose, manager);
                return;
            }
        }

        // Fallback for non-XR debug cameras only. Formal XR clients should provide an XR Origin/root.
        if (hasFreshVirtualPose)
        {
            root.position = targetWorldPosition + Vector3.up * viewHeightFallbackMeters;
            root.rotation = targetWorldRotation;
        }
    }

    private void ApplyVirtualFromPhysical(
        Transform root,
        Transform cameraTransform,
        Vector3 targetWorldPosition,
        float desiredCameraWorldYaw,
        bool hasFreshVirtualPose,
        LiveVRVirtualPoseMessage virtualPose,
        LiveVRNetworkManager manager)
    {
        Vector3 rootSpaceHeadOffset = root.InverseTransformPoint(cameraTransform.position);
        Vector3 planarRootSpaceHeadOffset = new Vector3(rootSpaceHeadOffset.x, 0.0f, rootSpaceHeadOffset.z);
        float localHeadYaw = ResolveLocalHeadYaw(root, cameraTransform);
        float rootYaw = NormalizeDegrees(desiredCameraWorldYaw - localHeadYaw);
        Quaternion rootRotation = Utility.CastRotation2Dto3D(rootYaw);

        root.rotation = rootRotation;
        root.position = targetWorldPosition - (rootRotation * planarRootSpaceHeadOffset);
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
            localResetStartCameraWorldPosition = cameraTransform.position;
            localResetStartCameraWorldYaw = ToProjectYaw(cameraTransform.rotation);
        }

        if (localResetDoneSent)
        {
            ApplyResetVirtualMapping(root, cameraTransform, CalculateResetDesiredCameraWorldYaw());
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
        float desiredCameraWorldYaw = CalculateResetDesiredCameraWorldYaw();
        ApplyResetVirtualMapping(root, cameraTransform, desiredCameraWorldYaw);

        if (progress >= 0.995f)
        {
            completedLocalResetEventId = activeLocalResetEventId;
            localResetAccumulatedPhysicalTurn = localResetDesiredPhysicalTurn;
            localResetAppliedInjectedRootTurn = localResetDesiredInjectedRootTurn;
            desiredCameraWorldYaw = CalculateResetDesiredCameraWorldYaw();
            ApplyResetVirtualMapping(root, cameraTransform, desiredCameraWorldYaw);
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

    private float CalculateResetDesiredCameraWorldYaw()
    {
        return NormalizeDegrees(localResetStartCameraWorldYaw + localResetAccumulatedPhysicalTurn + localResetAppliedInjectedRootTurn);
    }

    private void ApplyResetVirtualMapping(Transform root, Transform cameraTransform, float desiredCameraWorldYaw)
    {
        Vector3 rootSpaceHeadOffset = root.InverseTransformPoint(cameraTransform.position);
        float localHeadYaw = ResolveLocalHeadYaw(root, cameraTransform);
        float rootYaw = NormalizeDegrees(desiredCameraWorldYaw - localHeadYaw);
        Quaternion rootRotation = Utility.CastRotation2Dto3D(rootYaw);

        root.rotation = rootRotation;
        root.position = localResetStartCameraWorldPosition - (rootRotation * rootSpaceHeadOffset);
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
        // For reset injection this must use the same project-yaw convention as
        // LiveVRNetworkManager pose packets: positive degrees rotate Vector2.up to the left.
        // Using the opposite Unity yaw convention makes the client finish condition fight
        // the Host reset target and delays RESET_END.
        return ToProjectYaw(cameraTransform.localRotation);
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
                Debug.LogWarning("[LiveVR] Client Virtual View Root is not assigned. Assign the XR Origin or camera rig root; LiveVR will not move Main Camera directly.");
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
                Debug.LogWarning("[LiveVR] Client virtual view root is missing and Main Camera has no parent. Assign the XR Origin/root to Client Virtual View Root; the camera transform will not be modified directly.");
            }
            return null;
        }

        clientVirtualViewRoot = cameraTransform.parent;
        return IsValidViewRoot(clientVirtualViewRoot) ? clientVirtualViewRoot : null;
    }

    private bool IsValidViewRoot(Transform root)
    {
        if (root == null)
            return false;

        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform != null && root == cameraTransform)
        {
            WarnInvalidViewRoot("[LiveVR] Client Virtual View Root points to the HMD/Main Camera itself. Assign the XR Origin or camera rig root instead; camera transform will not be modified directly.");
            return false;
        }

        if (root.GetComponent<Camera>() != null)
        {
            WarnInvalidViewRoot("[LiveVR] Client Virtual View Root has a Camera component. Assign the XR Origin or camera rig root instead; camera transform will not be modified directly.");
            return false;
        }

        return true;
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
