using UnityEngine;

[DefaultExecutionOrder(-10000)]
public class LiveVRRdwPoseBridge : MonoBehaviour
{
    [HideInInspector]
    [SerializeField] private LiveVRNetworkManager networkManager;
    [HideInInspector]
    [SerializeField] private bool hostOnly = true;
    [HideInInspector]
    [SerializeField] private int unitIndexToUserIdOffset = 0;
    [HideInInspector]
    [SerializeField] private float stalePoseTimeoutSeconds = 0.5f;
    [HideInInspector]
    [SerializeField] private bool requireCalibratedPose = true;
    [HideInInspector]
    [SerializeField] private bool applyInFixedUpdate = true;
    [HideInInspector]
    [SerializeField] private bool applyInLateUpdate = false;
    [HideInInspector]
    [SerializeField] private bool logMissingPoses = false;

    private float nextMissingPoseLogTime;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        bool newHostOnly,
        int newUnitIndexToUserIdOffset,
        float newStalePoseTimeoutSeconds,
        bool newRequireCalibratedPose,
        bool newApplyInFixedUpdate,
        bool newApplyInLateUpdate,
        bool newLogMissingPoses)
    {
        networkManager = newNetworkManager;
        hostOnly = newHostOnly;
        unitIndexToUserIdOffset = newUnitIndexToUserIdOffset;
        stalePoseTimeoutSeconds = newStalePoseTimeoutSeconds;
        requireCalibratedPose = newRequireCalibratedPose;
        applyInFixedUpdate = newApplyInFixedUpdate;
        applyInLateUpdate = newApplyInLateUpdate;
        logMissingPoses = newLogMissingPoses;
    }

    private void FixedUpdate()
    {
        if (applyInFixedUpdate)
            ApplyLivePosesToRdwUsers();
    }

    private void LateUpdate()
    {
        if (applyInLateUpdate)
            ApplyLivePosesToRdwUsers();
    }

    public void ApplyLivePosesToRdwUsers()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null)
            return;

        if (hostOnly && !manager.IsHost)
            return;

        if (RDWSimulationManager.instance == null)
            return;

        RedirectedUnit[] units = RDWSimulationManager.instance.GetRedirectedUnits;
        if (units == null)
            return;

        for (int unitIndex = 0; unitIndex < units.Length; unitIndex++)
        {
            RedirectedUnit unit = units[unitIndex];
            if (unit == null || unit.GetRealUser() == null || unit.GetRealUser().transform2D == null)
                continue;

            int userId = unitIndex + unitIndexToUserIdOffset;
            LiveVRPoseSample sample;
            if (!manager.TryGetPose(userId, out sample))
            {
                LogMissingPose(userId, "no pose received");
                continue;
            }

            if (requireCalibratedPose && !sample.IsCalibrated)
            {
                LogMissingPose(userId, "pose is not calibrated");
                continue;
            }

            if (sample.AgeSeconds > stalePoseTimeoutSeconds)
            {
                LogMissingPose(userId, string.Format("pose stale ({0:F3}s)", sample.AgeSeconds));
                continue;
            }

            ApplyPose(unit, sample);
        }
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }

    private void ApplyPose(RedirectedUnit unit, LiveVRPoseSample sample)
    {
        unit.ApplyExternalRealUserPose(sample.ExperimentPosition, sample.YawDegrees);
    }

    private void LogMissingPose(int userId, string reason)
    {
        if (!logMissingPoses || Time.unscaledTime < nextMissingPoseLogTime)
            return;

        nextMissingPoseLogTime = Time.unscaledTime + 2.0f;
        Debug.LogWarning(string.Format("[LiveVR] User {0}: {1}.", userId, reason));
    }
}
