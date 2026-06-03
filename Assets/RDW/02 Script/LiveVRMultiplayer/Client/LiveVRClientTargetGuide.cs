using System;
using UnityEngine;

public enum LiveVRClientTargetGuideMode
{
    Random = 0,
    LoopRectangle = 1
}

[DefaultExecutionOrder(12000)]
public class LiveVRClientTargetGuide : MonoBehaviour
{
    [SerializeField] private LiveVRNetworkManager networkManager;
    [SerializeField] private Transform hmdCamera;
    [SerializeField] private Transform targetParent;
    [SerializeField] private GameObject targetPrefab;
    [SerializeField] private bool enableGuide = true;
    [SerializeField] private bool showOnlyWhileRunning = true;
    [SerializeField] private LiveVRClientTargetGuideMode guideMode = LiveVRClientTargetGuideMode.Random;
    [SerializeField] private string targetLayerName = "VirtualWall";
    [SerializeField] private bool useAreaAnchorBounds = true;
    [SerializeField] private string areaAnchorName = "walkingArea";
    [SerializeField] private float targetAreaWidthMeters = 16.0f;
    [SerializeField] private float targetAreaDepthMeters = 16.0f;
    [SerializeField] private float targetHeightMeters = 1.35f;
    [SerializeField] private float targetRadiusMeters = 0.18f;
    [SerializeField] private float reachDistanceMeters = 0.65f;
    [SerializeField] private float minDistanceFromUserMeters = 2.0f;
    [SerializeField] private int baseSeed = 1000;
    [SerializeField] private float resetPromptSuppressSeconds = 1.0f;

    private GameObject targetObject;
    private System.Random random;
    private int targetIndex = -1;
    private Vector2 currentTarget;
    private bool hasTarget;
    private bool wasSuppressedByReset;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        Transform newHmdCamera,
        Transform newTargetParent,
        GameObject newTargetPrefab,
        bool newEnableGuide,
        bool newShowOnlyWhileRunning,
        LiveVRClientTargetGuideMode newGuideMode,
        string newTargetLayerName,
        bool newUseAreaAnchorBounds,
        string newAreaAnchorName,
        float newTargetAreaWidthMeters,
        float newTargetAreaDepthMeters,
        float newTargetHeightMeters,
        float newTargetRadiusMeters,
        float newReachDistanceMeters,
        float newMinDistanceFromUserMeters,
        int newBaseSeed)
    {
        networkManager = newNetworkManager;
        hmdCamera = newHmdCamera;
        targetParent = newTargetParent;
        targetPrefab = newTargetPrefab;
        enableGuide = newEnableGuide;
        showOnlyWhileRunning = newShowOnlyWhileRunning;
        guideMode = newGuideMode;
        targetLayerName = string.IsNullOrEmpty(newTargetLayerName) ? "VirtualWall" : newTargetLayerName;
        useAreaAnchorBounds = newUseAreaAnchorBounds;
        areaAnchorName = string.IsNullOrEmpty(newAreaAnchorName) ? "walkingArea" : newAreaAnchorName;
        targetAreaWidthMeters = Mathf.Max(0.5f, newTargetAreaWidthMeters);
        targetAreaDepthMeters = Mathf.Max(0.5f, newTargetAreaDepthMeters);
        targetHeightMeters = Mathf.Max(0.0f, newTargetHeightMeters);
        targetRadiusMeters = Mathf.Max(0.05f, newTargetRadiusMeters);
        reachDistanceMeters = Mathf.Max(0.1f, newReachDistanceMeters);
        minDistanceFromUserMeters = Mathf.Max(0.0f, newMinDistanceFromUserMeters);
        baseSeed = newBaseSeed;
        ResetGuide();
    }

    private void LateUpdate()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        bool suppressedByReset = manager != null && manager.HasFreshResetPrompt(resetPromptSuppressSeconds);
        bool active = ShouldGuideBeActive(manager) && !suppressedByReset;
        EnsureTargetObject();

        if (targetObject != null)
            targetObject.SetActive(active);

        if (suppressedByReset)
        {
            wasSuppressedByReset = true;
            return;
        }

        if (wasSuppressedByReset)
        {
            wasSuppressedByReset = false;
            ResetGuide();
        }

        if (!active)
            return;

        if (!hasTarget)
            SelectNextTarget(GetUserVirtualPosition());

        if (!hasTarget)
            return;

        ApplyTargetTransform();

        Vector2 userPosition = GetUserVirtualPosition();
        if (Vector2.Distance(userPosition, currentTarget) <= reachDistanceMeters)
            SelectNextTarget(userPosition);
    }

    private bool ShouldGuideBeActive(LiveVRNetworkManager manager)
    {
        if (!enableGuide || manager == null || manager.IsHost || manager.Mode == LiveVRExperimentMode.Disabled)
            return false;

        if (!manager.IsConnectedToHost || !manager.HasCalibration)
            return false;

        if (showOnlyWhileRunning && manager.ExperimentState != LiveVRExperimentState.Running)
            return false;

        return true;
    }

    private void EnsureTargetObject()
    {
        if (targetObject != null)
            return;

        Transform parent = ResolveTargetParent();
        if (targetPrefab != null)
        {
            targetObject = parent != null
                ? Instantiate(targetPrefab, parent)
                : Instantiate(targetPrefab);
        }
        else
        {
            targetObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            targetObject.name = "LiveVR Local Target";
            targetObject.transform.localScale = Vector3.one * (targetRadiusMeters * 2.0f);
            Renderer renderer = targetObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Standard"));
                renderer.material.color = new Color(1.0f, 0.82f, 0.1f, 1.0f);
            }

            if (parent != null)
                targetObject.transform.SetParent(parent, true);
        }

        targetObject.name = "LiveVR Local Target User " + ResolveUserId();
        SetLayerRecursively(targetObject, ResolveLayer(targetLayerName));
        targetObject.SetActive(false);
    }

    private void SelectNextTarget(Vector2 userPosition)
    {
        EnsureRandom();
        targetIndex++;

        if (guideMode == LiveVRClientTargetGuideMode.LoopRectangle)
            currentTarget = GetLoopRectangleTarget(targetIndex);
        else
            currentTarget = GetRandomTargetAwayFromUser(userPosition);

        hasTarget = true;
    }

    private Vector2 GetRandomTargetAwayFromUser(Vector2 userPosition)
    {
        Vector2 target = Vector2.zero;
        Bounds areaBounds;
        bool hasAnchorBounds = TryGetAreaAnchorBounds(out areaBounds);
        float minX = hasAnchorBounds ? areaBounds.min.x : -targetAreaWidthMeters * 0.5f;
        float maxX = hasAnchorBounds ? areaBounds.max.x : targetAreaWidthMeters * 0.5f;
        float minZ = hasAnchorBounds ? areaBounds.min.z : -targetAreaDepthMeters * 0.5f;
        float maxZ = hasAnchorBounds ? areaBounds.max.z : targetAreaDepthMeters * 0.5f;

        for (int attempt = 0; attempt < 32; attempt++)
        {
            target = new Vector2(
                Mathf.Lerp(minX, maxX, (float)random.NextDouble()),
                Mathf.Lerp(minZ, maxZ, (float)random.NextDouble()));

            if (Vector2.Distance(userPosition, target) >= minDistanceFromUserMeters)
                return target;
        }

        return target;
    }

    private Vector2 GetLoopRectangleTarget(int index)
    {
        Bounds areaBounds;
        if (TryGetAreaAnchorBounds(out areaBounds))
        {
            float inset = Mathf.Min(1.0f, Mathf.Min(areaBounds.size.x, areaBounds.size.z) * 0.125f);
            float minX = areaBounds.min.x + inset;
            float maxX = areaBounds.max.x - inset;
            float minZ = areaBounds.min.z + inset;
            float maxZ = areaBounds.max.z - inset;

            switch (Mathf.Abs(index) % 4)
            {
                case 0: return new Vector2(maxX, maxZ);
                case 1: return new Vector2(minX, maxZ);
                case 2: return new Vector2(minX, minZ);
                default: return new Vector2(maxX, minZ);
            }
        }

        float halfWidth = targetAreaWidthMeters * 0.5f;
        float halfDepth = targetAreaDepthMeters * 0.5f;
        float fallbackInset = Mathf.Min(1.0f, Mathf.Min(halfWidth, halfDepth) * 0.25f);
        float x = Mathf.Max(0.1f, halfWidth - fallbackInset);
        float y = Mathf.Max(0.1f, halfDepth - fallbackInset);

        switch (Mathf.Abs(index) % 4)
        {
            case 0: return new Vector2(x, y);
            case 1: return new Vector2(-x, y);
            case 2: return new Vector2(-x, -y);
            default: return new Vector2(x, -y);
        }
    }

    private void ApplyTargetTransform()
    {
        if (targetObject == null || !hasTarget)
            return;

        targetObject.transform.position = new Vector3(currentTarget.x, ResolveTargetHeight(), currentTarget.y);
    }

    private Vector2 GetUserVirtualPosition()
    {
        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform != null)
            return new Vector2(cameraTransform.position.x, cameraTransform.position.z);

        return Vector2.zero;
    }

    private void EnsureRandom()
    {
        if (random != null)
            return;

        random = new System.Random(baseSeed + ResolveUserId() * 9973);
    }

    private void ResetGuide()
    {
        random = null;
        targetIndex = -1;
        hasTarget = false;
        currentTarget = Vector2.zero;
    }

    private int ResolveUserId()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        return manager != null ? manager.LocalUserId : 0;
    }

    private Transform ResolveTargetParent()
    {
        if (targetParent != null)
            return targetParent;

        GameObject virtualSpaceObject = GameObject.Find("Virtual Space");
        if (virtualSpaceObject != null)
            targetParent = virtualSpaceObject.transform;

        return targetParent;
    }

    private float ResolveTargetHeight()
    {
        Bounds areaBounds;
        if (TryGetAreaAnchorBounds(out areaBounds))
            return areaBounds.max.y + targetHeightMeters;

        return targetHeightMeters;
    }

    private bool TryGetAreaAnchorBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        if (!useAreaAnchorBounds || string.IsNullOrEmpty(areaAnchorName))
            return false;

        Transform anchor = FindAreaAnchor();
        if (anchor == null)
            return false;

        if (TryGetRendererBounds(anchor, out bounds))
            return true;

        if (TryGetColliderBounds(anchor, out bounds))
            return true;

        return false;
    }

    private Transform FindAreaAnchor()
    {
        GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindChildRecursive(roots[i].transform, areaAnchorName);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null)
            return null;

        if (string.Equals(root.name, childName, StringComparison.OrdinalIgnoreCase))
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
        return TryEncapsulateBounds(renderers, out bounds);
    }

    private static bool TryGetColliderBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        return TryEncapsulateBounds(colliders, out bounds);
    }

    private static bool TryEncapsulateBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = new Bounds();
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

    private static bool TryEncapsulateBounds(Collider[] colliders, out Bounds bounds)
    {
        bounds = new Bounds();
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

    private Transform ResolveHmdCamera()
    {
        if (hmdCamera != null)
            return hmdCamera;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            hmdCamera = mainCamera.transform;

        return hmdCamera;
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
}
