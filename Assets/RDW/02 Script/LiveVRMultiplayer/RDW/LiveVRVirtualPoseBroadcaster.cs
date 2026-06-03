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
    [SerializeField] private float sendRateHz = 30.0f;
    [HideInInspector]
    [SerializeField] private bool sendOnlyWhileRunning = false;

    private float nextSendTime;

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
            manager.SendVirtualPose(userId, virtualTransform.localPosition, virtualTransform.localRotation);
        }
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }
}
