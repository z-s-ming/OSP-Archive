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
    [SerializeField] private string walkableAreaAnchorName = "walkingArea";
    [SerializeField] private bool keepOnlyWalkableEnvironment = true;
    [SerializeField] private string[] walkableEnvironmentRootNamesToKeep = { "walkingArea", "Floor_Tiles", "Terrain" };
    [SerializeField] private string[] walkableEnvironmentRootNamesToHideInside =
    {
        "Trees",
        "Rocks",
        "Props",
        "Mushrooms",
        "Plants",
        "Water",
        "Mountains",
        "obstacle_*",
        "Cube*"
    };

    private GameObject environmentInstance;
    private bool warnedMissingPrefab;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        GameObject newEnvironmentPrefab,
        bool newFallbackToSimulationVirtualSpacePrefab,
        Transform newEnvironmentParent,
        bool newEnableEnvironment,
        string newEnvironmentLayerName,
        bool newApplyVirtualSpaceSettingTransform,
        bool newDisableEnvironmentCameras,
        bool newKeepOnlyWalkableEnvironment,
        string[] newWalkableEnvironmentRootNamesToKeep,
        string[] newWalkableEnvironmentRootNamesToHideInside)
    {
        networkManager = newNetworkManager;
        environmentPrefab = newEnvironmentPrefab;
        fallbackToSimulationVirtualSpacePrefab = newFallbackToSimulationVirtualSpacePrefab;
        environmentParent = newEnvironmentParent;
        enableEnvironment = newEnableEnvironment;
        environmentLayerName = string.IsNullOrEmpty(newEnvironmentLayerName) ? "VirtualWall" : newEnvironmentLayerName;
        applyVirtualSpaceSettingTransform = newApplyVirtualSpaceSettingTransform;
        disableEnvironmentCameras = newDisableEnvironmentCameras;
        keepOnlyWalkableEnvironment = newKeepOnlyWalkableEnvironment;
        walkableEnvironmentRootNamesToKeep = NormalizeKeepNames(newWalkableEnvironmentRootNamesToKeep);
        walkableEnvironmentRootNamesToHideInside = NormalizeHideNames(newWalkableEnvironmentRootNamesToHideInside);
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

        environmentInstance = environmentParent != null
            ? Instantiate(prefab, environmentParent)
            : Instantiate(prefab);
        environmentInstance.name = "LiveVR Client Visual Environment";

        ApplyEnvironmentTransform(environmentInstance.transform);
        AlignWalkableAreaToOrigin(environmentInstance.transform);
        SanitizeWalkableEnvironment(environmentInstance.transform);
        SetLayerRecursively(environmentInstance, ResolveLayer(environmentLayerName));

        if (disableEnvironmentCameras)
            DisableCameras(environmentInstance);
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

    private void SanitizeWalkableEnvironment(Transform environmentRoot)
    {
        if (!keepOnlyWalkableEnvironment || environmentRoot == null)
            return;

        Transform anchor = FindChildRecursive(environmentRoot, walkableAreaAnchorName);
        if (anchor == null)
        {
            Debug.LogWarning("[LiveVR] Cannot sanitize client visual environment because walkable area anchor was not found: " + walkableAreaAnchorName);
            return;
        }

        Bounds walkableBounds;
        if (!TryGetRendererBounds(anchor, out walkableBounds) && !TryGetColliderBounds(anchor, out walkableBounds))
        {
            Debug.LogWarning("[LiveVR] Cannot sanitize client visual environment because walkable area bounds are unavailable: " + walkableAreaAnchorName);
            return;
        }

        string[] keepNames = NormalizeKeepNames(walkableEnvironmentRootNamesToKeep);
        string[] hideNames = NormalizeHideNames(walkableEnvironmentRootNamesToHideInside);
        int hiddenCount = 0;
        for (int i = environmentRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = environmentRoot.GetChild(i);
            if (child == null || ShouldKeepEnvironmentRoot(child.name, keepNames))
                continue;

            if (!ShouldHideEnvironmentRootInsideWalkableArea(child.name, hideNames))
                continue;

            hiddenCount += HideObjectsInsideWalkableBounds(child, walkableBounds, keepNames);
        }

        Debug.Log(string.Format(
            "[LiveVR] Client visual environment sanitized. Hidden objects inside walkable area={0}; protected roots={1}; sanitized roots={2}.",
            hiddenCount,
            string.Join(",", keepNames),
            string.Join(",", hideNames)));
    }

    private static int HideObjectsInsideWalkableBounds(Transform root, Bounds walkableBounds, string[] keepNames)
    {
        if (root == null || ShouldKeepEnvironmentRoot(root.name, keepNames))
            return 0;

        int hiddenCount = 0;
        for (int i = root.childCount - 1; i >= 0; i--)
            hiddenCount += HideObjectsInsideWalkableBounds(root.GetChild(i), walkableBounds, keepNames);

        if (ShouldHideObjectInWalkableArea(root, walkableBounds))
        {
            root.gameObject.SetActive(false);
            hiddenCount++;
        }

        return hiddenCount;
    }

    private static bool ShouldHideObjectInWalkableArea(Transform candidate, Bounds walkableBounds)
    {
        Bounds candidateBounds;
        if (!TryGetOwnRendererOrColliderBounds(candidate, out candidateBounds))
            return false;

        return BoundsOverlapXZ(candidateBounds, walkableBounds) || BoundsCenterInsideXZ(candidateBounds, walkableBounds);
    }

    private static bool TryGetOwnRendererOrColliderBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        if (root == null)
            return false;

        bool initialized = false;
        Renderer[] renderers = root.GetComponents<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
            initialized = EncapsulateBounds(renderers[i] != null ? renderers[i].bounds : bounds, ref bounds, initialized);

        Collider[] colliders = root.GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            initialized = EncapsulateBounds(colliders[i] != null ? colliders[i].bounds : bounds, ref bounds, initialized);

        return initialized;
    }

    private static bool EncapsulateBounds(Bounds value, ref Bounds bounds, bool initialized)
    {
        if (!initialized)
        {
            bounds = value;
            return true;
        }

        bounds.Encapsulate(value);
        return true;
    }

    private static bool BoundsOverlapXZ(Bounds a, Bounds b)
    {
        return a.min.x <= b.max.x &&
               a.max.x >= b.min.x &&
               a.min.z <= b.max.z &&
               a.max.z >= b.min.z;
    }

    private static bool BoundsCenterInsideXZ(Bounds a, Bounds b)
    {
        Vector3 center = a.center;
        return center.x >= b.min.x &&
               center.x <= b.max.x &&
               center.z >= b.min.z &&
               center.z <= b.max.z;
    }

    private static bool ShouldKeepEnvironmentRoot(string rootName, string[] keepNames)
    {
        if (string.IsNullOrEmpty(rootName) || keepNames == null)
            return false;

        for (int i = 0; i < keepNames.Length; i++)
        {
            if (string.Equals(rootName, keepNames[i], System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string[] NormalizeKeepNames(string[] names)
    {
        string[] required = { "walkingArea", "Floor_Tiles", "Terrain" };
        string[] normalized = NormalizeNames(names);
        for (int i = 0; i < required.Length; i++)
        {
            if (!ContainsName(normalized, required[i]))
                normalized = AppendName(normalized, required[i]);
        }

        return normalized;
    }

    private static string[] NormalizeHideNames(string[] names)
    {
        string[] normalized = NormalizeNames(names);
        if (normalized.Length > 0)
            return normalized;

        return new[]
        {
            "Trees",
            "Rocks",
            "Props",
            "Mushrooms",
            "Plants",
            "Water",
            "Mountains",
            "obstacle_*",
            "Cube*"
        };
    }

    private static string[] NormalizeNames(string[] names)
    {
        if (names == null || names.Length == 0)
            return new string[0];

        int validCount = 0;
        for (int i = 0; i < names.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(names[i]))
                validCount++;
        }

        if (validCount == 0)
            return new string[0];

        string[] normalized = new string[validCount];
        int index = 0;
        for (int i = 0; i < names.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(names[i]))
                normalized[index++] = names[i].Trim();
        }

        return normalized;
    }

    private static bool ShouldHideEnvironmentRootInsideWalkableArea(string rootName, string[] hideNames)
    {
        if (string.IsNullOrEmpty(rootName) || hideNames == null)
            return false;

        for (int i = 0; i < hideNames.Length; i++)
        {
            string hideName = hideNames[i];
            if (string.IsNullOrEmpty(hideName))
                continue;

            if (hideName.EndsWith("*", System.StringComparison.Ordinal))
            {
                string prefix = hideName.Substring(0, hideName.Length - 1);
                if (rootName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(rootName, hideName, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsName(string[] names, string value)
    {
        if (names == null || string.IsNullOrEmpty(value))
            return false;

        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], value, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string[] AppendName(string[] names, string value)
    {
        int length = names != null ? names.Length : 0;
        string[] next = new string[length + 1];
        for (int i = 0; i < length; i++)
            next[i] = names[i];
        next[length] = value;
        return next;
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
}
