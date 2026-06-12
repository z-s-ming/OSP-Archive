using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(10000)]
public class LiveVRVirtualPoseBroadcaster : MonoBehaviour
{
    [HideInInspector]
    [SerializeField] private LiveVRNetworkManager networkManager;
    [HideInInspector]
    [SerializeField] private bool hostOnly = true;
    [HideInInspector]
    [SerializeField] private int unitIndexToUserIdOffset = 0;
    [HideInInspector]
    [SerializeField] private float sendRateHz = 60.0f;
    [HideInInspector]
    [SerializeField] private bool sendOnlyWhileRunning = false;

    private float nextSendTime;
    private readonly Dictionary<int, float> nextGainSendLogTimeByUserId = new Dictionary<int, float>();

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        bool newHostOnly,
        int newUnitIndexToUserIdOffset,
        float newSendRateHz,
        bool newSendOnlyWhileRunning)
    {
        networkManager = newNetworkManager;
        hostOnly = newHostOnly;
        unitIndexToUserIdOffset = newUnitIndexToUserIdOffset;
        sendRateHz = newSendRateHz;
        sendOnlyWhileRunning = newSendOnlyWhileRunning;
    }

    private void LateUpdate()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null)
            return;

        if (hostOnly && !manager.IsHost)
            return;

        if (sendOnlyWhileRunning && manager.ExperimentState != LiveVRExperimentState.Running)
            return;

        if (Time.unscaledTime < nextSendTime)
            return;

        float interval = sendRateHz > 0.0f ? 1.0f / sendRateHz : 0.033f;
        nextSendTime = Time.unscaledTime + interval;
        BroadcastVirtualPoses(manager);
    }

    private void BroadcastVirtualPoses(LiveVRNetworkManager manager)
    {
        if (RDWSimulationManager.instance == null)
            return;

        RedirectedUnit[] units = RDWSimulationManager.instance.GetRedirectedUnits;
        if (units == null)
            return;

        for (int unitIndex = 0; unitIndex < units.Length; unitIndex++)
        {
            RedirectedUnit unit = units[unitIndex];
            if (unit == null || unit.GetVirtualUser() == null || unit.GetVirtualUser().transform2D == null)
                continue;

            int userId = unitIndex + unitIndexToUserIdOffset;
            Transform2D virtualTransform = unit.GetVirtualUser().transform2D;
            float injectedYawDelta = 0.0f;
            float gainRateDegreesPerSecond = 0.0f;
            float gainValidSeconds = 0.0f;
            GainType gainType = GainType.Undefined;

            LiveVRGainCommand command;
            if (LiveVRGainCommandService.TryGetCommand(userId, out command))
            {
                gainType = command.GainType;
                gainRateDegreesPerSecond = command.GainRateDegreesPerSecond;
                gainValidSeconds = command.ClientValidSeconds;
            }

            manager.SendVirtualPose(
                userId,
                virtualTransform.localPosition,
                virtualTransform.localRotation,
                injectedYawDelta,
                gainType,
                gainRateDegreesPerSecond,
                gainValidSeconds);

            LogGainSendIfNeeded(userId, virtualTransform, gainType, gainRateDegreesPerSecond, gainValidSeconds);
        }
    }

    private void LogGainSendIfNeeded(
        int userId,
        Transform2D virtualTransform,
        GainType gainType,
        float gainRateDegreesPerSecond,
        float gainValidSeconds)
    {
        float nextLogTime;
        if (!nextGainSendLogTimeByUserId.TryGetValue(userId, out nextLogTime))
            nextLogTime = 0.0f;

        if (Time.unscaledTime < nextLogTime)
            return;

        nextGainSendLogTimeByUserId[userId] = Time.unscaledTime + 0.5f;
        Debug.Log(string.Format(
            "[LiveVR] SendGain user={0} type={1} rate={2:F2}/s valid={3:F3}s virtual=({4:F2},{5:F2})/{6:F1}",
            userId,
            gainType,
            gainRateDegreesPerSecond,
            gainValidSeconds,
            virtualTransform.localPosition.x,
            virtualTransform.localPosition.y,
            virtualTransform.localRotation));
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }
}
