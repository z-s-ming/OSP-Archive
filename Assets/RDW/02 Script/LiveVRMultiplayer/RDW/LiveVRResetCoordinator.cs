using System.Collections.Generic;
using UnityEngine;

public class LiveVRResetCoordinator : MonoBehaviour, IRdwResetExecutionCoordinator
{
    private class ActiveReset
    {
        public RedirectedUnit Unit;
        public ResetPlan Plan;
        public float StableSeconds;
        public float ElapsedSeconds;
        public float NextPromptTime;
        public bool HasPreviousYaw;
        public float PreviousYawDegrees;
        public bool HasResetMapping;
        public float InitialPhysicalYawDegrees;
        public float InitialVirtualYawDegrees;
        public float DesiredPhysicalRotationDegrees;
        public float DesiredVirtualRotationDegrees;
        public float AccumulatedPhysicalRotationDegrees;
        public float ResetProgress;
        public float NextInjectionLogTime;
        public bool HasSentResetStart;
        public Vector2 FrozenVirtualPosition;
        public float FrozenVirtualYawDegrees;
    }

    [SerializeField] private LiveVRNetworkManager networkManager;
    [SerializeField] private LiveVRHmdMovementController hmdMovementController;
    [SerializeField] private bool enableCoordinator = true;
    [SerializeField] private bool requireFreshCalibratedPose = true;
    [SerializeField] private float stalePoseTimeoutSeconds = 0.5f;
    [SerializeField] private float yawErrorThresholdDegrees = 15.0f;
    [SerializeField] private bool requirePositionAlignment = true;
    [SerializeField] private float positionToleranceMeters = 0.75f;
    [SerializeField] private bool requireInsideRealSpaceForCompletion = false;
    [SerializeField] private float stableDurationSeconds = 0.3f;
    [SerializeField] private float timeoutSeconds = 0.0f;
    [SerializeField] private float promptRepeatSeconds = 0.5f;
    [SerializeField] private float resetVirtualRotationScale = 1.0f;
    [SerializeField] private float minimumPhysicalTurnDegreesForVirtualReset = 3.0f;

    private readonly Dictionary<int, ActiveReset> activeByPlanId = new Dictionary<int, ActiveReset>();
    private readonly Dictionary<int, int> activePlanByUserId = new Dictionary<int, int>();
    private readonly List<int> completedPlanIds = new List<int>();

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        bool newEnableCoordinator,
        float newStalePoseTimeoutSeconds,
        float newYawErrorThresholdDegrees,
        bool newRequirePositionAlignment,
        float newPositionToleranceMeters,
        bool newRequireInsideRealSpaceForCompletion,
        float newStableDurationSeconds,
        float newTimeoutSeconds,
        float newPromptRepeatSeconds)
    {
        networkManager = newNetworkManager;
        enableCoordinator = newEnableCoordinator;
        stalePoseTimeoutSeconds = newStalePoseTimeoutSeconds;
        yawErrorThresholdDegrees = newYawErrorThresholdDegrees;
        requirePositionAlignment = newRequirePositionAlignment;
        positionToleranceMeters = newPositionToleranceMeters;
        requireInsideRealSpaceForCompletion = newRequireInsideRealSpaceForCompletion;
        stableDurationSeconds = newStableDurationSeconds;
        timeoutSeconds = newTimeoutSeconds;
        promptRepeatSeconds = newPromptRepeatSeconds;
    }

    private void OnEnable()
    {
        RdwResetExecutionRegistry.Register(this);
    }

    private void OnDisable()
    {
        RdwResetExecutionRegistry.Unregister(this);
        activeByPlanId.Clear();
        activePlanByUserId.Clear();
    }

    public bool TryBeginReset(RedirectedUnit unit, ResetPlan plan)
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (!CanCoordinate(manager) || unit == null || plan.UserId < 0)
            return false;

        if (manager.ShouldUseSimulatedUser(plan.UserId, stalePoseTimeoutSeconds, requireFreshCalibratedPose))
            return false;

        int existingPlanId;
        if (activePlanByUserId.TryGetValue(plan.UserId, out existingPlanId))
            return existingPlanId == plan.PlanId;

        ActiveReset activeReset = new ActiveReset
        {
            Unit = unit,
            Plan = plan,
            StableSeconds = 0.0f,
            ElapsedSeconds = 0.0f,
            NextPromptTime = 0.0f,
            HasPreviousYaw = false,
            PreviousYawDegrees = 0.0f,
            HasResetMapping = false,
            InitialPhysicalYawDegrees = 0.0f,
            InitialVirtualYawDegrees = 0.0f,
            DesiredPhysicalRotationDegrees = 0.0f,
            DesiredVirtualRotationDegrees = 0.0f,
            AccumulatedPhysicalRotationDegrees = 0.0f,
            ResetProgress = 0.0f,
            NextInjectionLogTime = 0.0f,
            HasSentResetStart = false,
            FrozenVirtualPosition = Vector2.zero,
            FrozenVirtualYawDegrees = 0.0f
        };

        activeByPlanId[plan.PlanId] = activeReset;
        activePlanByUserId[plan.UserId] = plan.PlanId;
        LiveVRPoseSample sample;
        if (TryGetPoseForCompletion(manager, plan.UserId, out sample))
            EnsureResetMapping(manager, activeReset, sample);
        SendPrompt(manager, activeReset);
        return true;
    }

    private void FixedUpdate()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (!CanCoordinate(manager) || activeByPlanId.Count == 0)
            return;

        completedPlanIds.Clear();
        foreach (KeyValuePair<int, ActiveReset> item in activeByPlanId)
        {
            ActiveReset activeReset = item.Value;
            TickActiveReset(manager, activeReset);

            if (IsLiveHmdReset(manager, activeReset))
            {
                LiveVRPoseSample finalSample;
                if (TryGetPoseForCompletion(manager, activeReset.Plan.UserId, out finalSample) &&
                    CommitLiveResetDone(
                        activeReset.Plan.UserId,
                        activeReset.Plan.PlanId,
                        finalSample.ExperimentPosition,
                        finalSample.YawDegrees))
                {
                    completedPlanIds.Add(activeReset.Plan.PlanId);
                }
            }
            else if (ShouldCompleteReset(manager, activeReset))
            {
                activeReset.Unit.CompleteExternalReset(activeReset.Plan.PlanId);
                completedPlanIds.Add(activeReset.Plan.PlanId);
            }
        }

        for (int i = 0; i < completedPlanIds.Count; i++)
        {
            int planId = completedPlanIds[i];
            ActiveReset activeReset;
            if (!activeByPlanId.TryGetValue(planId, out activeReset))
                continue;

            activeByPlanId.Remove(planId);
            activePlanByUserId.Remove(activeReset.Plan.UserId);
        }
    }

    private void TickActiveReset(LiveVRNetworkManager manager, ActiveReset activeReset)
    {
        activeReset.ElapsedSeconds += Time.fixedDeltaTime;
        TrackPhysicalResetProgress(manager, activeReset);
        ApplyFrozenVirtualPose(activeReset);

        if (Time.unscaledTime >= activeReset.NextPromptTime)
            SendPrompt(manager, activeReset);
    }

    private void TrackPhysicalResetProgress(LiveVRNetworkManager manager, ActiveReset activeReset)
    {
        LiveVRPoseSample sample;
        if (!TryGetPoseForCompletion(manager, activeReset.Plan.UserId, out sample))
            return;

        if (!EnsureResetMapping(manager, activeReset, sample))
            return;

        if (!activeReset.HasPreviousYaw)
        {
            activeReset.PreviousYawDegrees = sample.YawDegrees;
            activeReset.HasPreviousYaw = true;
            return;
        }

        float physicalYawDelta = Mathf.DeltaAngle(activeReset.PreviousYawDegrees, sample.YawDegrees);
        activeReset.PreviousYawDegrees = sample.YawDegrees;

        activeReset.AccumulatedPhysicalRotationDegrees += physicalYawDelta;
        activeReset.ResetProgress = CalculateResetProgress(activeReset);

        if (Time.unscaledTime >= activeReset.NextInjectionLogTime)
        {
            activeReset.NextInjectionLogTime = Time.unscaledTime + 0.5f;
            Debug.Log(string.Format(
                "[LiveVR] Reset progress user={0} event={1} physical={2:F1}/{3:F1} clientVirtualPlan={4:F1}/{5:F1} progress={6:P0}",
                activeReset.Plan.UserId,
                activeReset.Plan.PlanId,
                activeReset.AccumulatedPhysicalRotationDegrees,
                activeReset.DesiredPhysicalRotationDegrees,
                activeReset.DesiredVirtualRotationDegrees * activeReset.ResetProgress,
                activeReset.DesiredVirtualRotationDegrees,
                activeReset.ResetProgress));
        }
    }

    private bool EnsureResetMapping(LiveVRNetworkManager manager, ActiveReset activeReset, LiveVRPoseSample sample)
    {
        if (activeReset.HasResetMapping)
            return true;

        Object2D virtualUser = activeReset.Unit != null ? activeReset.Unit.GetVirtualUser() : null;
        if (virtualUser == null || virtualUser.transform2D == null)
            return false;

        Vector2 currentForward = NormalizeOrFallback(Utility.RotateVector2(Vector2.up, sample.YawDegrees), Vector2.up);
        Vector2 targetDirection = NormalizeOrFallback(activeReset.Plan.TargetDirection, currentForward);
        float targetAngle = Vector2.SignedAngle(currentForward, targetDirection);

        activeReset.InitialPhysicalYawDegrees = sample.YawDegrees;
        activeReset.InitialVirtualYawDegrees = virtualUser.transform2D.localRotation;
        activeReset.FrozenVirtualPosition = virtualUser.transform2D.localPosition;
        activeReset.FrozenVirtualYawDegrees = virtualUser.transform2D.localRotation;
        activeReset.DesiredPhysicalRotationDegrees = targetAngle;
        if (Mathf.Abs(activeReset.DesiredPhysicalRotationDegrees) < Mathf.Max(0.1f, minimumPhysicalTurnDegreesForVirtualReset))
        {
            activeReset.DesiredPhysicalRotationDegrees = 0.0f;
            activeReset.DesiredVirtualRotationDegrees = 0.0f;
            activeReset.ResetProgress = 1.0f;
        }
        else
        {
            // Host only defines the plan. The Quest client injects the virtual-from-physical
            // rotation locally from frame-to-frame HMD yaw deltas.
            activeReset.DesiredVirtualRotationDegrees = Mathf.Sign(activeReset.DesiredPhysicalRotationDegrees) * 360.0f * Mathf.Max(0.01f, resetVirtualRotationScale);
            activeReset.ResetProgress = 0.0f;
        }
        activeReset.AccumulatedPhysicalRotationDegrees = 0.0f;
        activeReset.NextInjectionLogTime = 0.0f;
        activeReset.HasResetMapping = true;

        Debug.Log(string.Format(
            "[LiveVR] Reset mapping start user={0} event={1} targetAngle={2:F1} physicalPlan={3:F1} virtualPlan={4:F1} extraInjected={5:F1} initialPhysicalYaw={6:F1} initialVirtualYaw={7:F1}",
            activeReset.Plan.UserId,
            activeReset.Plan.PlanId,
            targetAngle,
            activeReset.DesiredPhysicalRotationDegrees,
            activeReset.DesiredVirtualRotationDegrees,
            Mathf.Max(0.0f, Mathf.Abs(activeReset.DesiredVirtualRotationDegrees) - Mathf.Abs(activeReset.DesiredPhysicalRotationDegrees)),
            activeReset.InitialPhysicalYawDegrees,
            activeReset.InitialVirtualYawDegrees));
        return true;
    }

    public bool CommitLiveResetDone(int userId, int resetEventId, Vector2 finalPhysicalPose, float finalPhysicalYaw)
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (!CanCoordinate(manager))
            return false;

        int activePlanId;
        if (!activePlanByUserId.TryGetValue(userId, out activePlanId) || activePlanId != resetEventId)
            return false;

        ActiveReset activeReset;
        if (!activeByPlanId.TryGetValue(resetEventId, out activeReset))
            return false;

        LiveVRResetDoneMessage resetDone;
        if (!manager.TryGetClientResetDone(userId, resetEventId, out resetDone))
            return false;

        if (!IsFinalYawAligned(activeReset, finalPhysicalYaw))
            return false;

        if (!IsFinalPositionSafe(activeReset, finalPhysicalPose))
            return false;

        ApplyFrozenVirtualPose(activeReset);
        if (!activeReset.Unit.CommitLiveExternalReset(
                resetEventId,
                finalPhysicalPose,
                finalPhysicalYaw,
                activeReset.FrozenVirtualPosition,
                activeReset.FrozenVirtualYawDegrees))
        {
            return false;
        }

        LiveVRPoseSample finalSample;
        if (manager.TryGetPose(userId, out finalSample))
        {
            finalSample.ExperimentPosition = finalPhysicalPose;
            finalSample.YawDegrees = finalPhysicalYaw;
            ResolveHmdMovementController()?.ResetLivePoseAnchor(userId, finalSample);
        }

        manager.SendResetEnd(userId, resetEventId);
        Debug.Log(string.Format(
            "[LiveVR] CommitLiveResetDone user={0} event={1} physical=({2:F2},{3:F2}) yaw={4:F1} frozenVirtual=({5:F2},{6:F2})/{7:F1}",
            userId,
            resetEventId,
            finalPhysicalPose.x,
            finalPhysicalPose.y,
            finalPhysicalYaw,
            activeReset.FrozenVirtualPosition.x,
            activeReset.FrozenVirtualPosition.y,
            activeReset.FrozenVirtualYawDegrees));
        return true;
    }

    private static float CalculateResetProgress(ActiveReset activeReset)
    {
        if (Mathf.Abs(activeReset.DesiredPhysicalRotationDegrees) <= Mathf.Epsilon)
            return 1.0f;

        return Mathf.Clamp01(activeReset.AccumulatedPhysicalRotationDegrees / activeReset.DesiredPhysicalRotationDegrees);
    }

    private bool ShouldCompleteReset(LiveVRNetworkManager manager, ActiveReset activeReset)
    {
        if (manager.HasClientReportedResetDone(activeReset.Plan.UserId, activeReset.Plan.PlanId))
            return true;

        LiveVRPoseSample sample;
        if (!TryGetPoseForCompletion(manager, activeReset.Plan.UserId, out sample))
        {
            activeReset.StableSeconds = 0.0f;
            return false;
        }

        Vector2 targetDirection = NormalizeOrFallback(activeReset.Plan.TargetDirection, Vector2.up);
        Vector2 currentForward = NormalizeOrFallback(Utility.RotateVector2(Vector2.up, sample.YawDegrees), Vector2.up);
        float yawError = Mathf.Abs(Vector2.SignedAngle(currentForward, targetDirection));
        bool yawAligned = yawError <= Mathf.Max(1.0f, yawErrorThresholdDegrees);
        bool positionAligned = !requirePositionAlignment ||
                               !activeReset.Plan.HasTargetPosition ||
                               Vector2.Distance(sample.ExperimentPosition, activeReset.Plan.TargetPosition) <= Mathf.Max(0.0f, positionToleranceMeters);
        bool safetyOk = !requireInsideRealSpaceForCompletion || IsInsideRealSpace(activeReset.Unit);

        bool resetMappingComplete = !activeReset.HasResetMapping || activeReset.ResetProgress >= 0.98f;

        if ((yawAligned || resetMappingComplete) && positionAligned && safetyOk && resetMappingComplete)
        {
            activeReset.StableSeconds += Time.fixedDeltaTime;
        }
        else
        {
            activeReset.StableSeconds = 0.0f;
        }

        bool completedByPose = activeReset.StableSeconds >= Mathf.Max(0.0f, stableDurationSeconds);
        bool completedByTimeout = timeoutSeconds > 0.0f && activeReset.ElapsedSeconds >= timeoutSeconds;
        return completedByPose || completedByTimeout;
    }

    private bool IsLiveHmdReset(LiveVRNetworkManager manager, ActiveReset activeReset)
    {
        return manager != null &&
               activeReset != null &&
               !manager.ShouldUseSimulatedUser(activeReset.Plan.UserId, stalePoseTimeoutSeconds, requireFreshCalibratedPose);
    }

    private void ApplyFrozenVirtualPose(ActiveReset activeReset)
    {
        if (activeReset == null || !activeReset.HasResetMapping || activeReset.Unit == null)
            return;

        Object2D virtualUser = activeReset.Unit.GetVirtualUser();
        if (virtualUser == null || virtualUser.transform2D == null)
            return;

        virtualUser.transform2D.localPosition = activeReset.FrozenVirtualPosition;
        virtualUser.transform2D.localRotation = activeReset.FrozenVirtualYawDegrees;
        if (activeReset.Unit.controller != null)
            activeReset.Unit.controller.ResetCurrentState(virtualUser.transform2D);
    }

    private bool IsFinalYawAligned(ActiveReset activeReset, float finalPhysicalYaw)
    {
        Vector2 targetDirection = NormalizeOrFallback(activeReset.Plan.TargetDirection, Vector2.up);
        Vector2 finalForward = NormalizeOrFallback(Utility.RotateVector2(Vector2.up, finalPhysicalYaw), Vector2.up);
        float yawError = Mathf.Abs(Vector2.SignedAngle(finalForward, targetDirection));
        if (yawError <= Mathf.Max(1.0f, yawErrorThresholdDegrees))
            return true;

        Debug.LogWarning(string.Format(
            "[LiveVR] Reject reset commit user={0} event={1}: final yaw error {2:F1} deg exceeds threshold.",
            activeReset.Plan.UserId,
            activeReset.Plan.PlanId,
            yawError));
        return false;
    }

    private bool IsFinalPositionSafe(ActiveReset activeReset, Vector2 finalPhysicalPose)
    {
        bool positionAligned = !requirePositionAlignment ||
                               !activeReset.Plan.HasTargetPosition ||
                               Vector2.Distance(finalPhysicalPose, activeReset.Plan.TargetPosition) <= Mathf.Max(0.0f, positionToleranceMeters);
        bool safetyOk = !requireInsideRealSpaceForCompletion || IsInsideRealSpace(activeReset.Unit);
        if (positionAligned && safetyOk)
            return true;

        Debug.LogWarning(string.Format(
            "[LiveVR] Reject reset commit user={0} event={1}: positionAligned={2}, safetyOk={3}.",
            activeReset.Plan.UserId,
            activeReset.Plan.PlanId,
            positionAligned,
            safetyOk));
        return false;
    }

    private bool TryGetPoseForCompletion(LiveVRNetworkManager manager, int userId, out LiveVRPoseSample sample)
    {
        if (!manager.TryGetPose(userId, out sample))
            return false;

        if (requireFreshCalibratedPose && !sample.IsCalibrated)
            return false;

        return sample.AgeSeconds <= Mathf.Max(0.0f, stalePoseTimeoutSeconds);
    }

    private bool IsInsideRealSpace(RedirectedUnit unit)
    {
        if (unit == null || unit.GetRealSpace() == null || unit.GetRealSpace().spaceObject == null || unit.GetRealUser() == null)
            return false;

        return unit.GetRealSpace().spaceObject.IsInside(unit.GetRealUser().transform2D.position, Space.World, 0.5f);
    }

    private void SendPrompt(LiveVRNetworkManager manager, ActiveReset activeReset)
    {
        ResetPlan plan = activeReset.Plan;
        bool hasTurnInstruction = activeReset.HasResetMapping;
        int turnDirectionSign = hasTurnInstruction ? (activeReset.DesiredPhysicalRotationDegrees >= 0.0f ? 1 : -1) : 0;
        float totalTurnDegrees = hasTurnInstruction ? Mathf.Abs(activeReset.DesiredPhysicalRotationDegrees) : 0.0f;
        float remainingTurnDegrees = hasTurnInstruction
            ? Mathf.Max(0.0f, totalTurnDegrees * (1.0f - activeReset.ResetProgress))
            : 0.0f;
        if (hasTurnInstruction && !activeReset.HasSentResetStart)
        {
            float injectedTurnDegrees = turnDirectionSign *
                                        Mathf.Max(0.0f, 360.0f - Mathf.Abs(activeReset.DesiredPhysicalRotationDegrees));
            manager.SendResetStart(
                plan.UserId,
                plan.ResetTypeName,
                plan.TargetDirection,
                plan.PlanId,
                plan.HasTargetPosition,
                plan.TargetPosition,
                turnDirectionSign,
                activeReset.DesiredPhysicalRotationDegrees,
                injectedTurnDegrees);
            activeReset.HasSentResetStart = true;
        }

        manager.SendResetPrompt(
            plan.UserId,
            plan.ResetTypeName,
            plan.TargetDirection,
            plan.PlanId,
            plan.HasTargetPosition,
            plan.TargetPosition,
            hasTurnInstruction,
            turnDirectionSign,
            totalTurnDegrees,
            remainingTurnDegrees,
            activeReset.ResetProgress);
        activeReset.NextPromptTime = Time.unscaledTime + Mathf.Max(0.1f, promptRepeatSeconds);
    }

    private bool CanCoordinate(LiveVRNetworkManager manager)
    {
        if (!enableCoordinator || manager == null || !manager.IsHost)
            return false;

        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager == null || simulationManager.simulationSetting == null)
            return false;

        SimulationSetting setting = simulationManager.simulationSetting;
        return setting.experimentProfile == ExperimentProfile.LiveUser || setting.useLiveVRPhysicalUserInput;
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }

    private LiveVRHmdMovementController ResolveHmdMovementController()
    {
        if (hmdMovementController == null)
            hmdMovementController = GetComponent<LiveVRHmdMovementController>();
        if (hmdMovementController == null)
            hmdMovementController = FindObjectOfType<LiveVRHmdMovementController>();

        return hmdMovementController;
    }

    private static Vector2 NormalizeOrFallback(Vector2 value, Vector2 fallback)
    {
        if (value.sqrMagnitude > Mathf.Epsilon)
            return value.normalized;

        if (fallback.sqrMagnitude > Mathf.Epsilon)
            return fallback.normalized;

        return Vector2.up;
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
