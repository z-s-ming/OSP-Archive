using UnityEngine;

public class LiveVRClientEnvironmentLoader : MonoBehaviour
{
    [SerializeField] private LiveVRNetworkManager networkManager;
    [SerializeField] private GameObject environmentPrefab;
    [SerializeField] private bool fallbackToSimulationVirtualSpacePrefab = true;
    [SerializeField] private Transform environmentParent;
    [SerializeField] private bool enableEnvironment = true;
    [SerializeField] private string environmentLayerName = "VirtualWall";
    [SerializeField] private bool applyVirtualSpaceSettingTransform = true;
    [SerializeField] private bool disableEnvironmentCameras = true;
    [SerializeField] private bool hideSimulationVirtualSpaceOnClient = true;
    [SerializeField] private string walkableAreaAnchorName = "walkingArea";

    private GameObject environmentInstance;
    private bool warnedMissingPrefab;
    private bool warnedEnvironmentParent;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        GameObject newEnvironmentPrefab,
        bool newFallbackToSimulationVirtualSpacePrefab,
        Transform newEnvironmentParent,
        bool newEnableEnvironment,
        string newEnvironmentLayerName,
        bool newApplyVirtualSpaceSettingTransform,
        bool newDisableEnvironmentCameras)
    {
        networkManager = newNetworkManager;
        environmentPrefab = newEnvironmentPrefab;
        fallbackToSimulationVirtualSpacePrefab = newFallbackToSimulationVirtualSpacePrefab;
        environmentParent = newEnvironmentParent;
        enableEnvironment = newEnableEnvironment;
        environmentLayerName = string.IsNullOrEmpty(newEnvironmentLayerName) ? "VirtualWall" : newEnvironmentLayerName;
        applyVirtualSpaceSettingTransform = newApplyVirtualSpaceSettingTransform;
        disableEnvironmentCameras = newDisableEnvironmentCameras;
    }

    private void Start()
    {
        TryLoadEnvironment();
    }

    private void LateUpdate()
    {
        if (environmentInstance == null)
            TryLoadEnvironment();
    }

    private void TryLoadEnvironment()
    {
        if (!enableEnvironment)
            return;

        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || manager.Mode == LiveVRExperimentMode.Disabled || manager.Mode == LiveVRExperimentMode.HostOnly)
            return;

        if (environmentInstance != null)
            return;

        GameObject prefab = ResolveEnvironmentPrefab();
        if (prefab == null)
        {
            WarnMissingPrefab();
            return;
        }

        Transform resolvedParent = ResolveEnvironmentParent();
        environmentInstance = Instantiate(prefab);
        if (resolvedParent != null)
            environmentInstance.transform.SetParent(resolvedParent, false);
        environmentInstance.name = "LiveVR Client Visual Environment";

        ApplyEnvironmentTransform(environmentInstance.transform);
        AlignWalkableAreaToOrigin(environmentInstance.transform);
        // Visual cleanup is now handled in the environment prefab itself.
        // Runtime partial hiding created inconsistent visual layers during reset rotation.
        SetLayerRecursively(environmentInstance, ResolveLayer(environmentLayerName));

        if (disableEnvironmentCameras)
            DisableCameras(environmentInstance);

        if (hideSimulationVirtualSpaceOnClient)
            HideOriginalSimulationVirtualSpace(environmentInstance.transform);
    }

    private GameObject ResolveEnvironmentPrefab()
    {
        if (environmentPrefab != null)
            return environmentPrefab;

        if (!fallbackToSimulationVirtualSpacePrefab)
            return null;

        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager == null ||
            simulationManager.simulationSetting == null ||
            simulationManager.simulationSetting.virtualSpaceSetting == null)
        {
            return null;
        }

        return simulationManager.simulationSetting.virtualSpaceSetting.predefinedSpace;
    }

    private Transform ResolveEnvironmentParent()
    {
        Transform virtualWorldRoot = FindVirtualWorldRoot();
        if (environmentParent == null)
        {
            if (virtualWorldRoot != null)
            {
                environmentParent = virtualWorldRoot;
                return environmentParent;
            }

            WarnEnvironmentParent("[LiveVR] Client visual environment parent is missing and VirtualWorldRoot was not found. The environment will be instantiated at scene root, so RDW visual injection will not rotate it.");
            return null;
        }

        if (virtualWorldRoot != null && !IsSameOrChildOf(environmentParent, virtualWorldRoot))
        {
            WarnEnvironmentParent(string.Format(
                "[LiveVR] Client visual environment parent '{0}' is not under VirtualWorldRoot. All client visual environment should be under VirtualWorldRoot so RDW injection moves one world.",
                environmentParent.name));
        }

        return environmentParent;
    }

    private static Transform FindVirtualWorldRoot()
    {
        GameObject root = GameObject.Find("VirtualWorldRoot");
        return root != null ? root.transform : null;
    }

    private void ApplyEnvironmentTransform(Transform instanceTransform)
    {
        if (instanceTransform == null)
            return;

        if (!applyVirtualSpaceSettingTransform)
        {
            instanceTransform.localPosition = Vector3.zero;
            instanceTransform.localRotation = Quaternion.identity;
            return;
        }

        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager == null ||
            simulationManager.simulationSetting == null ||
            simulationManager.simulationSetting.virtualSpaceSetting == null)
        {
            instanceTransform.localPosition = Vector3.zero;
            instanceTransform.localRotation = Quaternion.identity;
            return;
        }

        SpaceSetting setting = simulationManager.simulationSetting.virtualSpaceSetting;
        instanceTransform.localPosition = Utility.CastVector2Dto3D(setting.position, 0.0f);
        instanceTransform.localRotation = Utility.CastRotation2Dto3D(setting.rotation);
    }

    private void AlignWalkableAreaToOrigin(Transform environmentRoot)
    {
        if (environmentRoot == null || string.IsNullOrEmpty(walkableAreaAnchorName))
            return;

        Transform anchor = FindChildRecursive(environmentRoot, walkableAreaAnchorName);
        if (anchor == null)
        {
            Debug.LogWarning("[LiveVR] Client visual environment loaded, but walkable area anchor was not found: " + walkableAreaAnchorName);
            return;
        }

        Bounds anchorBounds;
        if (!TryGetRendererBounds(anchor, out anchorBounds) && !TryGetColliderBounds(anchor, out anchorBounds))
        {
            Debug.LogWarning("[LiveVR] Walkable area anchor has no Renderer or Collider bounds: " + walkableAreaAnchorName);
            return;
        }

        Vector3 offset = new Vector3(-anchorBounds.center.x, -anchorBounds.max.y, -anchorBounds.center.z);
        environmentRoot.position += offset;
        Debug.Log(string.Format(
            "[LiveVR] Aligned client visual environment using '{0}'. center=({1:F2},{2:F2},{3:F2}) topY={4:F2}",
            walkableAreaAnchorName,
            anchorBounds.center.x,
            anchorBounds.center.y,
            anchorBounds.center.z,
            anchorBounds.max.y));
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, childName, System.StringComparison.OrdinalIgnoreCase))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;

            if (!initialized)
            {
                bounds = renderers[i].bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }

        return initialized;
    }

    private static bool TryGetColliderBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        bool initialized = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;

            if (!initialized)
            {
                bounds = colliders[i].bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(colliders[i].bounds);
            }
        }

        return initialized;
    }

    private void WarnMissingPrefab()
    {
        if (warnedMissingPrefab)
            return;

        warnedMissingPrefab = true;
        Debug.LogWarning("[LiveVR] Client visual environment is enabled, but no prefab is assigned and SimulationSetting.virtualSpaceSetting.predefinedSpace is empty.");
    }

    private void WarnEnvironmentParent(string message)
    {
        if (warnedEnvironmentParent)
            return;

        warnedEnvironmentParent = true;
        Debug.LogWarning(message);
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }

    private int ResolveLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? layer : 0;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null)
            return;

        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
    }

    private static void DisableCameras(GameObject root)
    {
        Camera[] cameras = root.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
            cameras[i].enabled = false;

        AudioListener[] listeners = root.GetComponentsInChildren<AudioListener>(true);
        for (int i = 0; i < listeners.Length; i++)
            listeners[i].enabled = false;
    }

    private static void HideOriginalSimulationVirtualSpace(Transform clientEnvironmentRoot)
    {
        Transform[] transforms = GameObject.FindObjectsOfType<Transform>(true);
        int hiddenRendererCount = 0;
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null ||
                !string.Equals(candidate.name, "Virtual Space", System.StringComparison.OrdinalIgnoreCase) ||
                IsSameOrChildOf(candidate, clientEnvironmentRoot))
            {
                continue;
            }

            Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null || !renderers[r].enabled)
                    continue;

                renderers[r].enabled = false;
                hiddenRendererCount++;
            }
        }

        if (hiddenRendererCount > 0)
            Debug.Log(string.Format("[LiveVR] Hid original simulation Virtual Space renderers on client: {0}", hiddenRendererCount));
    }

    private static bool IsSameOrChildOf(Transform candidate, Transform root)
    {
        if (candidate == null || root == null)
            return false;

        Transform current = candidate;
        while (current != null)
        {
            if (current == root)
                return true;

            current = current.parent;
        }

        return false;
    }
}
