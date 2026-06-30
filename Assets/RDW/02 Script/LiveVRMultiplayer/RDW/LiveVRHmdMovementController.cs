using UnityEngine;
using System.Collections.Generic;

public class LiveVRHmdMovementController : MonoBehaviour, IMovementController
{
    [SerializeField] private LiveVRNetworkManager networkManager;
    [SerializeField] private bool hostOnly = true;
    [SerializeField] private int unitIndexToUserIdOffset = 0;
    [SerializeField] private float stalePoseTimeoutSeconds = 0.5f;
    [SerializeField] private bool requireCalibratedPose = true;
    [SerializeField] private bool logMissingPoses = false;
    [SerializeField] private bool driveVirtualUserFromHmdDelta = true;
    [SerializeField] private bool evaluateRdwCoreWhileRunning = true;
    [SerializeField] private bool waitForSimulatedFallbackUntilLiveUserMoves = true;
    [SerializeField] private float liveMovementReleaseDistanceMeters = 0.08f;
    [SerializeField] private LiveVRUserSource[] userSources;

    private float nextMissingPoseLogTime;
    private readonly Dictionary<int, LiveVRPoseSample> previousPoseByUserId = new Dictionary<int, LiveVRPoseSample>();
    private readonly Dictionary<int, Vector2> liveRunStartPositionByUserId = new Dictionary<int, Vector2>();
    private readonly List<RedirectedUnit> unitsWithFreshPose = new List<RedirectedUnit>();
    private readonly List<RedirectedUnit> unitsDrivenBySimulation = new List<RedirectedUnit>();
    private readonly List<RedirectedUnit> gatedFallbackUnits = new List<RedirectedUnit>();
    private readonly LiveRdwWalkingStepper defaultLiveWalkingStepper = new LiveRdwWalkingStepper();
    private ILiveRdwWalkingStepper liveWalkingStepper;
    private bool simulatedFallbackReleased;
    private bool simulatedFallbackWaitLogged;
    private bool wasRdwRunning;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        bool newHostOnly,
        int newUnitIndexToUserIdOffset,
        float newStalePoseTimeoutSeconds,
        bool newRequireCalibratedPose,
        bool newLogMissingPoses,
        bool newWaitForSimulatedFallbackUntilLiveUserMoves,
        float newLiveMovementReleaseDistanceMeters,
        LiveVRUserSource[] newUserSources)
    {
        networkManager = newNetworkManager;
        hostOnly = newHostOnly;
        unitIndexToUserIdOffset = newUnitIndexToUserIdOffset;
        stalePoseTimeoutSeconds = newStalePoseTimeoutSeconds;
        requireCalibratedPose = newRequireCalibratedPose;
        logMissingPoses = newLogMissingPoses;
        waitForSimulatedFallbackUntilLiveUserMoves = newWaitForSimulatedFallbackUntilLiveUserMoves;
        liveMovementReleaseDistanceMeters = Mathf.Max(0.0f, newLiveMovementReleaseDistanceMeters);
        userSources = newUserSources;
    }

    public void Step(RDWSimulationManager simulationManager, RedirectedUnit[] units)
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || units == null)
            return;

        if (hostOnly && !manager.IsHost)
            return;

        bool rdwRunning = simulationManager != null && simulationManager.BStart;
        HandleRunStateTransition(rdwRunning);
        unitsWithFreshPose.Clear();
        unitsDrivenBySimulation.Clear();
        gatedFallbackUnits.Clear();
        for (int unitIndex = 0; unitIndex < units.Length; unitIndex++)
        {
            RedirectedUnit unit = units[unitIndex];
            if (unit == null)
                continue;

            int userId = unitIndex + unitIndexToUserIdOffset;
            LiveVRUserSource source = ResolveConfiguredUserSource(manager, userId);
            if (manager.IsUserRunComplete(userId))
            {
                LiveVRGainCommandService.Clear(userId);
                continue;
            }

            if (manager.ShouldUseSimulatedUser(userId, stalePoseTimeoutSeconds, requireCalibratedPose))
            {
                if (ShouldGateRuntimeFallback(manager, userId))
                    gatedFallbackUnits.Add(unit);
                else
                    unitsDrivenBySimulation.Add(unit);
                continue;
            }

            LiveVRPoseSample sample;
            bool hasLivePose = TryGetValidPose(manager, userId, source != LiveVRUserSource.SimulatedOnly, out sample);
            if (!hasLivePose)
            {
                continue;
            }

            unit.ApplyExternalRealUserPose(sample.ExperimentPosition, sample.YawDegrees);

            if (manager.ConsumePoseReanchorRequest(userId))
            {
                previousPoseByUserId[userId] = sample;
                LiveVRGainCommandService.Clear(userId);
                Debug.Log(string.Format("[LiveVR] User {0}: first fresh pose used as live reanchor.", userId));
                continue;
            }

            if (!rdwRunning)
            {
                previousPoseByUserId[userId] = sample;
                LiveVRGainCommandService.Clear(userId);
                continue;
            }

            TryReleaseSimulatedFallback(userId, sample);

            LiveVRPoseSample previousSample;
            if (!previousPoseByUserId.TryGetValue(userId, out previousSample))
                previousSample = sample;

            defaultLiveWalkingStepper.DriveVirtualUserFromHmdDelta = driveVirtualUserFromHmdDelta;
            LiveVRGainDebugSample gainDebug = ResolveLiveWalkingStepper().StepLiveWalking(unit, sample, previousSample, units);
            if (gainDebug.IsValid)
            {
                LiveVRGainCommandService.UpdateFromRdwSample(userId, gainDebug);
                LiveVRGainDebugState.Record(userId, gainDebug);
            }
            previousPoseByUserId[userId] = sample;
            unitsWithFreshPose.Add(unit);
        }

        if (!evaluateRdwCoreWhileRunning || !rdwRunning)
            return;

        for (int i = 0; i < unitsDrivenBySimulation.Count; i++)
            unitsDrivenBySimulation[i].Simulate(units);

        if (simulatedFallbackReleased)
        {
            for (int i = 0; i < gatedFallbackUnits.Count; i++)
                gatedFallbackUnits[i].Simulate(units);
        }
        else if (gatedFallbackUnits.Count > 0 && !simulatedFallbackWaitLogged)
        {
            simulatedFallbackWaitLogged = true;
            Debug.Log(string.Format(
                "[LiveVR] Holding {0} runtime simulated fallback user(s) until a live user moves at least {1:F2}m.",
                gatedFallbackUnits.Count,
                liveMovementReleaseDistanceMeters));
        }

        for (int i = 0; i < unitsWithFreshPose.Count; i++)
        {
            // LiveUser mode keeps the HMD pose authoritative. Evaluate RDW reset/status
            // without entering RedirectedUnit.Simulate(), whose Move() path is simulation-driven.
            unitsWithFreshPose[i].CheckCurrentStatus(units);
        }
    }

    public void ResetLivePoseAnchor(int userId, LiveVRPoseSample sample)
    {
        previousPoseByUserId[userId] = sample;
    }

    public void ClearPreviousPoseCache()
    {
        previousPoseByUserId.Clear();
        ResetSimulatedFallbackStartGate();
        wasRdwRunning = false;
        LiveVRGainCommandService.ClearAll();
        LiveVRGainDebugState.Clear();
    }

    private void HandleRunStateTransition(bool rdwRunning)
    {
        if (rdwRunning && !wasRdwRunning)
            ResetSimulatedFallbackStartGate();

        wasRdwRunning = rdwRunning;
    }

    private bool ShouldGateRuntimeFallback(LiveVRNetworkManager manager, int userId)
    {
        return waitForSimulatedFallbackUntilLiveUserMoves &&
               !simulatedFallbackReleased &&
               manager != null &&
               manager.IsRuntimeSimulatedFallback(userId);
    }

    private void TryReleaseSimulatedFallback(int userId, LiveVRPoseSample sample)
    {
        if (!waitForSimulatedFallbackUntilLiveUserMoves || simulatedFallbackReleased)
            return;

        Vector2 startPosition;
        if (!liveRunStartPositionByUserId.TryGetValue(userId, out startPosition))
        {
            liveRunStartPositionByUserId[userId] = sample.ExperimentPosition;
            return;
        }

        float movement = Vector2.Distance(startPosition, sample.ExperimentPosition);
        if (movement < liveMovementReleaseDistanceMeters)
            return;

        simulatedFallbackReleased = true;
        Debug.Log(string.Format(
            "[LiveVR] Released runtime simulated fallback users after live user {0} moved {1:F3}m.",
            userId,
            movement));
    }

    private void ResetSimulatedFallbackStartGate()
    {
        liveRunStartPositionByUserId.Clear();
        simulatedFallbackReleased = !waitForSimulatedFallbackUntilLiveUserMoves;
        simulatedFallbackWaitLogged = false;
    }

    private bool TryGetValidPose(LiveVRNetworkManager manager, int userId, bool warnWhenMissing, out LiveVRPoseSample sample)
    {
        if (!manager.TryGetPose(userId, out sample))
        {
            if (warnWhenMissing)
                LogMissingPose(userId, "no pose received");
            return false;
        }

        if (requireCalibratedPose && !sample.IsCalibrated)
        {
            if (warnWhenMissing)
                LogMissingPose(userId, "pose is not calibrated");
            return false;
        }

        if (sample.AgeSeconds > stalePoseTimeoutSeconds)
        {
            if (warnWhenMissing)
                LogMissingPose(userId, string.Format("pose stale ({0:F3}s)", sample.AgeSeconds));
            return false;
        }

        return true;
    }

    private LiveVRUserSource ResolveConfiguredUserSource(LiveVRNetworkManager manager, int userId)
    {
        if (manager != null)
            return manager.GetConfiguredUserSource(userId);

        int index = userId - unitIndexToUserIdOffset;
        if (userSources == null || index < 0 || index >= userSources.Length)
            return LiveVRUserSource.RequiredLiveHmd;

        return userSources[index];
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }

    private ILiveRdwWalkingStepper ResolveLiveWalkingStepper()
    {
        if (liveWalkingStepper == null)
            liveWalkingStepper = defaultLiveWalkingStepper;

        return liveWalkingStepper;
    }

    private void LogMissingPose(int userId, string reason)
    {
        if (!logMissingPoses || Time.unscaledTime < nextMissingPoseLogTime)
            return;

        nextMissingPoseLogTime = Time.unscaledTime + 2.0f;
        Debug.LogWarning(string.Format("[LiveVR] User {0}: {1}.", userId, reason));
    }
}
