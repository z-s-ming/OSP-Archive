using UnityEngine;

public class LiveVRClientPresentationState : MonoBehaviour
{
    [SerializeField] private LiveVRNetworkManager networkManager;
    [SerializeField] private LiveVRClientVirtualViewBinder virtualViewBinder;
    [SerializeField] private LiveVRClientHud clientHud;
    [SerializeField] private LiveVRClientWorldHud worldHud;
    [SerializeField] private LiveVRClientTargetGuide targetGuide;

    private int lastClearedRestartEpoch = -1;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        LiveVRClientVirtualViewBinder newVirtualViewBinder,
        LiveVRClientHud newClientHud,
        LiveVRClientWorldHud newWorldHud,
        LiveVRClientTargetGuide newTargetGuide)
    {
        networkManager = newNetworkManager;
        virtualViewBinder = newVirtualViewBinder;
        clientHud = newClientHud;
        worldHud = newWorldHud;
        targetGuide = newTargetGuide;
    }

    public void ClearForRestart(int restartEpoch, string reason)
    {
        if (restartEpoch >= 0 && restartEpoch == lastClearedRestartEpoch)
            return;

        lastClearedRestartEpoch = restartEpoch;

        if (networkManager != null)
            networkManager.ClearAllClientResetState();
        if (virtualViewBinder != null)
            virtualViewBinder.RestoreInitialViewRootState();
        if (clientHud != null)
            clientHud.ClearResetPrompt();
        if (worldHud != null)
            worldHud.ClearResetPrompt();
        if (targetGuide != null)
            targetGuide.ClearForRestart();

        Debug.Log(string.Format("[LiveVR] Client presentation reset clear epoch={0} reason={1}", restartEpoch, string.IsNullOrEmpty(reason) ? "unspecified" : reason));
    }
}
