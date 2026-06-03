using UnityEngine;

public class LiveVRClientCameraIsolation : MonoBehaviour
{
    [SerializeField] private LiveVRNetworkManager networkManager;
    [SerializeField] private Camera hmdCamera;
    [SerializeField] private bool isolateClientCameras = true;
    [SerializeField] private bool tagHmdCameraAsMainCamera = true;
    [SerializeField] private bool applyClientCullingMask = true;
    [SerializeField] private string[] visibleLayerNames = { "VirtualWall", "UI" };

    private bool hasApplied;
    private bool warnedMissingHmdCamera;
    private bool warnedMissingLayer;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        Transform newHmdCameraTransform,
        bool newIsolateClientCameras,
        bool newTagHmdCameraAsMainCamera,
        bool newApplyClientCullingMask,
        string[] newVisibleLayerNames)
    {
        networkManager = newNetworkManager;
        hmdCamera = newHmdCameraTransform != null ? newHmdCameraTransform.GetComponent<Camera>() : null;
        isolateClientCameras = newIsolateClientCameras;
        tagHmdCameraAsMainCamera = newTagHmdCameraAsMainCamera;
        applyClientCullingMask = newApplyClientCullingMask;
        visibleLayerNames = newVisibleLayerNames != null && newVisibleLayerNames.Length > 0
            ? newVisibleLayerNames
            : new[] { "VirtualWall", "UI" };
        hasApplied = false;
    }

    private void LateUpdate()
    {
        if (hasApplied || !isolateClientCameras)
            return;

        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || manager.Mode == LiveVRExperimentMode.Disabled || manager.Mode == LiveVRExperimentMode.HostOnly)
            return;

        if (hmdCamera == null)
        {
            WarnMissingHmdCamera();
            return;
        }

        Camera[] cameras = FindObjectsOfType<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null)
                continue;

            bool isHmdCamera = camera == hmdCamera;
            camera.enabled = isHmdCamera;

            if (tagHmdCameraAsMainCamera && !isHmdCamera && camera.CompareTag("MainCamera"))
                camera.gameObject.tag = "Untagged";
        }

        hmdCamera.enabled = true;
        if (applyClientCullingMask)
            hmdCamera.cullingMask = BuildCullingMask();

        if (tagHmdCameraAsMainCamera)
            hmdCamera.gameObject.tag = "MainCamera";

        hasApplied = true;
    }

    private int BuildCullingMask()
    {
        int mask = 0;
        for (int i = 0; i < visibleLayerNames.Length; i++)
        {
            string layerName = visibleLayerNames[i];
            if (string.IsNullOrEmpty(layerName))
                continue;

            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                WarnMissingLayer(layerName);
                continue;
            }

            mask |= 1 << layer;
        }

        return mask;
    }

    private void WarnMissingHmdCamera()
    {
        if (warnedMissingHmdCamera)
            return;

        warnedMissingHmdCamera = true;
        Debug.LogWarning("[LiveVR] Client camera isolation is enabled, but Client Hmd Camera is not assigned or has no Camera component. Assign XR HMD Camera in LiveVRExperimentSetup.");
    }

    private void WarnMissingLayer(string layerName)
    {
        if (warnedMissingLayer)
            return;

        warnedMissingLayer = true;
        Debug.LogWarning("[LiveVR] Client camera culling mask references a missing layer: " + layerName);
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }
}
