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
    [SerializeField] private LiveVRUserSource[] userSources;

    private float nextMissingPoseLogTime;
    private readonly Dictionary<int, LiveVRPoseSample> previousPoseByUserId = new Dictionary<int, LiveVRPoseSample>();
    private readonly List<RedirectedUnit> unitsWithFreshPose = new List<RedirectedUnit>();
    private readonly List<RedirectedUnit> unitsDrivenBySimulation = new List<RedirectedUnit>();
    private readonly LiveRdwWalkingStepper defaultLiveWalkingStepper = new LiveRdwWalkingStepper();
    private ILiveRdwWalkingStepper liveWalkingStepper;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        bool newHostOnly,
        int newUnitIndexToUserIdOffset,
        float newStalePoseTimeoutSeconds,
        bool newRequireCalibratedPose,
        bool newLogMissingPoses,
        LiveVRUserSource[] newUserSources)
    {
        networkManager = newNetworkManager;
        hostOnly = newHostOnly;
        unitIndexToUserIdOffset = newUnitIndexToUserIdOffset;
        stalePoseTimeoutSeconds = newStalePoseTimeoutSeconds;
        requireCalibratedPose = newRequireCalibratedPose;
        logMissingPoses = newLogMissingPoses;
        userSources = newUserSources;
    }

    public void Step(RDWSimulationManager simulationManager, RedirectedUnit[] units)
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || units == null)
            return;

        if (hostOnly && !manager.IsHost)
            return;

        unitsWithFreshPose.Clear();
        unitsDrivenBySimulation.Clear();
        for (int unitIndex = 0; unitIndex < units.Length; unitIndex++)
        {
            RedirectedUnit unit = units[unitIndex];
            if (unit == null)
                continue;

            int userId = unitIndex + unitIndexToUserIdOffset;
            LiveVRUserSource source = ResolveConfiguredUserSource(manager, userId);
            if (manager.ShouldUseSimulatedUser(userId, stalePoseTimeoutSeconds, requireCalibratedPose))
            {
                unitsDrivenBySimulation.Add(unit);
                continue;
            }

            LiveVRPoseSample sample;
            bool hasLivePose = TryGetValidPose(manager, userId, source != LiveVRUserSource.SimulatedOnly, out sample);
            if (!hasLivePose)
            {
                continue;
            }

            LiveVRPoseSample previousSample;
            if (!previousPoseByUserId.TryGetValue(userId, out previousSample))
                previousSample = sample;

            defaultLiveWalkingStepper.DriveVirtualUserFromHmdDelta = driveVirtualUserFromHmdDelta;
            ResolveLiveWalkingStepper().StepLiveWalking(unit, sample, previousSample, units);
            previousPoseByUserId[userId] = sample;
            unitsWithFreshPose.Add(unit);
        }

        if (!evaluateRdwCoreWhileRunning || simulationManager == null || !simulationManager.BStart)
            return;

        for (int i = 0; i < unitsDrivenBySimulation.Count; i++)
            unitsDrivenBySimulation[i].Simulate(units);

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
