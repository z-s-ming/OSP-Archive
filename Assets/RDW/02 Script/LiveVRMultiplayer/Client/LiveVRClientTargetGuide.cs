using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    [SerializeField] private bool useSimulationEpisodeTargetMode = true;
    [SerializeField] private string targetLayerName = "VirtualWall";
    [SerializeField] private bool useAreaAnchorBounds = true;
    [SerializeField] private string areaAnchorName = "walkingArea";
    [SerializeField] private float targetAreaWidthMeters = 16.0f;
    [SerializeField] private float targetAreaDepthMeters = 16.0f;
    [SerializeField] private float targetHeightMeters = 1.35f;
    [SerializeField] private float targetRadiusMeters = 0.18f;
    [SerializeField] private float reachDistanceMeters = 0.65f;
    [SerializeField] private float minDistanceFromUserMeters = 2.0f;
    [SerializeField] private float minSpawnDistanceMeters = 4.0f;
    [SerializeField] private float maxSpawnDistanceMeters = 8.0f;
    [SerializeField] private int targetsPerRun = 1;
    [SerializeField] private int baseSeed = 1000;
    [SerializeField] private float resetPromptSuppressSeconds = 1.0f;

    private GameObject targetObject;
    private System.Random random;
    private int targetIndex = -1;
    private Vector2 currentTarget;
    private float currentTargetPathStartDistanceMeters;
    private Vector2 previousVirtualPosition;
    private bool hasPreviousVirtualPosition;
    private bool hasTarget;
    private bool wasSuppressedByReset;
    private bool runCompletedLocally;
    private string completedRunId = string.Empty;
    private float cumulativeTargetDistanceMeters;
    private float nextSimulationTargetSuppressionTime;
    private readonly List<Vector2> predefinedTargets = new List<Vector2>();

    public bool IsLocalRunComplete
    {
        get
        {
            LiveVRNetworkManager manager = ResolveNetworkManager();
            string currentRunId = manager != null ? manager.CurrentRunId : string.Empty;
            return runCompletedLocally && string.Equals(completedRunId, currentRunId, StringComparison.Ordinal);
        }
    }

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
        float newMinSpawnDistanceMeters,
        float newMaxSpawnDistanceMeters,
        int newTargetsPerRun,
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
        minSpawnDistanceMeters = Mathf.Max(0.0f, newMinSpawnDistanceMeters);
        maxSpawnDistanceMeters = Mathf.Max(minSpawnDistanceMeters + 0.1f, newMaxSpawnDistanceMeters);
        targetsPerRun = Mathf.Max(1, newTargetsPerRun);
        baseSeed = newBaseSeed;
        ResetGuide();
    }

    public void ClearResetSuppression()
    {
        wasSuppressedByReset = false;
    }

    private void LateUpdate()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        SuppressSimulationEpisodeTargetVisuals(manager);

        bool suppressedByReset = manager != null && manager.HasFreshResetPrompt(resetPromptSuppressSeconds);
        bool active = ShouldGuideBeActive(manager) && !suppressedByReset;
        EnsureTargetObject();

        string currentRunId = manager != null ? manager.CurrentRunId : string.Empty;
        if (runCompletedLocally && !string.Equals(completedRunId, currentRunId, StringComparison.Ordinal))
            ResetGuide();

        if (targetObject != null)
            targetObject.SetActive(active && !runCompletedLocally);

        if (suppressedByReset)
        {
            wasSuppressedByReset = true;
            hasPreviousVirtualPosition = false;
            return;
        }

        if (wasSuppressedByReset)
            wasSuppressedByReset = false;

        if (!active)
        {
            hasPreviousVirtualPosition = false;
            return;
        }

        if (runCompletedLocally)
            return;

        Vector2 userPosition = GetUserVirtualPosition();
        UpdateCumulativeVirtualDistance(userPosition);

        if (TryCompleteByVirtualDistance(manager, userPosition))
            return;

        if (!hasTarget)
            SelectNextTarget(userPosition);

        if (!hasTarget)
            return;

        ApplyTargetTransform();

        if (Vector2.Distance(userPosition, currentTarget) <= reachDistanceMeters)
            HandleTargetReached(manager, userPosition);
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
                targetObject.transform.SetParent(parent, false);
        }

        targetObject.name = "LiveVR Local Target User " + ResolveUserId();
        SetLayerRecursively(targetObject, ResolveLayer(targetLayerName));
        targetObject.SetActive(false);
    }

    private void SelectNextTarget(Vector2 userPosition)
    {
        EnsureRandom();
        targetIndex++;

        bool usedEpisodeTarget = useSimulationEpisodeTargetMode &&
                                 TryGetSimulationEpisodeTarget(userPosition, targetIndex, out currentTarget);
        if (!usedEpisodeTarget && guideMode == LiveVRClientTargetGuideMode.LoopRectangle)
            currentTarget = GetLoopRectangleTarget(targetIndex);
        else if (!usedEpisodeTarget)
            currentTarget = GetRandomTargetAwayFromUser(userPosition);

        if (!IsTargetInsideArea(currentTarget))
            currentTarget = GetRandomTargetAwayFromUser(userPosition);

        currentTargetPathStartDistanceMeters = cumulativeTargetDistanceMeters;
        hasTarget = true;
    }

    private bool TryGetSimulationEpisodeTarget(Vector2 userPosition, int index, out Vector2 target)
    {
        target = Vector2.zero;
        UnitSetting unitSetting = ResolveUnitSetting();
        if (unitSetting == null)
            return false;

        Vector2 forward = ResolveCurrentForward();
        switch (unitSetting.episodeType)
        {
            case EpisodeType.LongWalk:
                return TryFindTargetByDistanceRange(userPosition, forward, 12.0f, 12.0f, -180.0f, 180.0f, 10.0f, out target);
            case EpisodeType.Random:
                return TryFindTargetByDistanceRange(userPosition, forward, minSpawnDistanceMeters, maxSpawnDistanceMeters, -90.0f, 90.0f, 10.0f, out target);
            case EpisodeType.NaturalTouring:
                return TryFindNaturalTouringTarget(userPosition, forward, out target);
            case EpisodeType.PreDefined:
                return TryGetPredefinedTarget(unitSetting.episodeFileName, index, userPosition, out target);
            case EpisodeType.WanderingEpisodeForFixedReset:
                return TryFindTargetByDistanceRange(userPosition, forward, 0.2f, 0.2f, -180.0f, 180.0f, 10.0f, out target);
            case EpisodeType.WanderingEpisodeForAnyReset:
                return TryFindTargetByDistanceRange(userPosition, forward, 0.5f, 0.5f, -180.0f, 180.0f, 10.0f, out target);
            default:
                return false;
        }
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
        float effectiveMinDistance = Mathf.Max(minDistanceFromUserMeters, minSpawnDistanceMeters);
        float effectiveMaxDistance = Mathf.Max(effectiveMinDistance + 0.1f, maxSpawnDistanceMeters);

        for (int attempt = 0; attempt < 96; attempt++)
        {
            float angleRadians = (float)random.NextDouble() * Mathf.PI * 2.0f;
            float distance = Mathf.Lerp(effectiveMinDistance, effectiveMaxDistance, (float)random.NextDouble());
            target = userPosition + new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * distance;

            if (target.x >= minX && target.x <= maxX && target.y >= minZ && target.y <= maxZ)
                return target;
        }

        target = new Vector2(
            Mathf.Clamp(target.x, minX, maxX),
            Mathf.Clamp(target.y, minZ, maxZ));
        return target;
    }

    private bool IsTargetInsideArea(Vector2 target)
    {
        Bounds areaBounds;
        if (TryGetAreaAnchorBounds(out areaBounds))
        {
            if (target.x < areaBounds.min.x ||
                target.x > areaBounds.max.x ||
                target.y < areaBounds.min.z ||
                target.y > areaBounds.max.z)
            {
                return false;
            }
        }
        else
        {
            float halfWidth = targetAreaWidthMeters * 0.5f;
            float halfDepth = targetAreaDepthMeters * 0.5f;
            if (target.x < -halfWidth || target.x > halfWidth || target.y < -halfDepth || target.y > halfDepth)
                return false;
        }

        return true;
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

        Transform parent = targetObject.transform.parent;
        if (parent != null)
        {
            targetObject.transform.localPosition = new Vector3(
                currentTarget.x,
                ResolveTargetLocalHeight(parent),
                currentTarget.y);
            return;
        }

        targetObject.transform.position = new Vector3(currentTarget.x, ResolveTargetHeight(), currentTarget.y);
    }

    private void HandleTargetReached(LiveVRNetworkManager manager, Vector2 userPosition)
    {
        int reachedIndex = targetIndex;
        Vector2 reachedTarget = currentTarget;
        float travelledSinceTargetSpawn = Mathf.Max(0.0f, cumulativeTargetDistanceMeters - currentTargetPathStartDistanceMeters);
        float targetDistancePerRun = ResolveTargetDistancePerRun();
        bool hasDistanceGoal = !float.IsInfinity(targetDistancePerRun) && targetDistancePerRun > 0.0f;
        bool distanceComplete = hasDistanceGoal && cumulativeTargetDistanceMeters >= targetDistancePerRun;
        int targetCountGoal = ResolveTargetCountPerRun(hasDistanceGoal);
        bool countComplete = targetCountGoal > 0 && targetIndex + 1 >= targetCountGoal;

        if (distanceComplete || countComplete)
        {
            runCompletedLocally = true;
            completedRunId = manager != null ? manager.CurrentRunId : string.Empty;
            hasTarget = false;
            if (targetObject != null)
                targetObject.SetActive(false);

            if (manager != null)
                manager.SendTargetReached(reachedIndex, reachedTarget, userPosition, travelledSinceTargetSpawn, cumulativeTargetDistanceMeters, true);
            return;
        }

        if (manager != null)
            manager.SendTargetReached(reachedIndex, reachedTarget, userPosition, travelledSinceTargetSpawn, cumulativeTargetDistanceMeters, false);

        SelectNextTarget(userPosition);
    }

    private void UpdateCumulativeVirtualDistance(Vector2 userPosition)
    {
        if (!hasPreviousVirtualPosition)
        {
            previousVirtualPosition = userPosition;
            hasPreviousVirtualPosition = true;
            return;
        }

        float delta = Vector2.Distance(previousVirtualPosition, userPosition);
        previousVirtualPosition = userPosition;

        if (delta <= 0.0001f)
            return;

        cumulativeTargetDistanceMeters += delta;
    }

    private bool TryCompleteByVirtualDistance(LiveVRNetworkManager manager, Vector2 userPosition)
    {
        float targetDistancePerRun = ResolveTargetDistancePerRun();
        bool hasDistanceGoal = !float.IsInfinity(targetDistancePerRun) && targetDistancePerRun > 0.0f;
        if (!hasDistanceGoal || cumulativeTargetDistanceMeters < targetDistancePerRun)
            return false;

        int reachedIndex = Mathf.Max(0, targetIndex);
        Vector2 target = hasTarget ? currentTarget : userPosition;
        float travelledSinceTargetSpawn = Mathf.Max(0.0f, cumulativeTargetDistanceMeters - currentTargetPathStartDistanceMeters);
        runCompletedLocally = true;
        completedRunId = manager != null ? manager.CurrentRunId : string.Empty;
        hasTarget = false;
        if (targetObject != null)
            targetObject.SetActive(false);

        if (manager != null)
            manager.SendTargetReached(reachedIndex, target, userPosition, travelledSinceTargetSpawn, cumulativeTargetDistanceMeters, true);

        return true;
    }

    private bool TryFindNaturalTouringTarget(Vector2 userPosition, Vector2 forward, out Vector2 target)
    {
        target = Vector2.zero;
        float angleRange = UnityEngine.Random.value < 0.2f ? 120.0f : 45.0f;
        for (int attempt = 0; attempt < 96; attempt++)
        {
            float distance = Mathf.Lerp(1.0f, 3.0f, (float)random.NextDouble());
            float angle = Mathf.Lerp(-angleRange, angleRange, (float)random.NextDouble());
            Vector2 candidate = userPosition + Utility.RotateVector2(forward, angle) * distance;
            if (IsTargetInsideArea(candidate))
            {
                target = candidate;
                return true;
            }
        }

        return TryFindTargetByDistanceRange(userPosition, forward, 1.0f, 3.0f, -180.0f, 180.0f, 10.0f, out target);
    }

    private bool TryFindTargetByDistanceRange(
        Vector2 userPosition,
        Vector2 forward,
        float minDistance,
        float maxDistance,
        float minAngle,
        float maxAngle,
        float angleStep,
        out Vector2 target)
    {
        target = Vector2.zero;
        float safeMin = Mathf.Max(0.0f, minDistance);
        float safeMax = Mathf.Max(safeMin, maxDistance);

        for (int attempt = 0; attempt < 96; attempt++)
        {
            float distance = Mathf.Lerp(safeMin, safeMax, (float)random.NextDouble());
            float angle = Mathf.Lerp(minAngle, maxAngle, (float)random.NextDouble());
            Vector2 candidate = userPosition + Utility.RotateVector2(forward, angle) * distance;
            if (IsTargetInsideArea(candidate))
            {
                target = candidate;
                return true;
            }
        }

        for (float distance = safeMax; distance >= safeMin; distance -= 0.25f)
        {
            for (float angle = minAngle; angle <= maxAngle; angle += Mathf.Max(1.0f, angleStep))
            {
                Vector2 candidate = userPosition + Utility.RotateVector2(forward, angle) * distance;
                if (IsTargetInsideArea(candidate))
                {
                    target = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryGetPredefinedTarget(string episodeFileName, int index, Vector2 userPosition, out Vector2 target)
    {
        target = Vector2.zero;
        EnsurePredefinedTargetsLoaded(episodeFileName);
        if (predefinedTargets.Count == 0)
            return false;

        target = predefinedTargets[Mathf.Abs(index) % predefinedTargets.Count];
        if (IsTargetInsideArea(target))
            return true;

        target = userPosition;
        return false;
    }

    private void EnsurePredefinedTargetsLoaded(string episodeFileName)
    {
        if (predefinedTargets.Count > 0 || string.IsNullOrEmpty(episodeFileName))
            return;

        string path = Path.Combine(Application.dataPath, "Resources/" + episodeFileName + ".txt");
        if (!File.Exists(path))
            return;

        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            string[] parts = lines[i].Split(',');
            if (parts.Length < 2)
                continue;

            float x;
            float y;
            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            {
                predefinedTargets.Add(new Vector2(x, y));
            }
        }
    }

    private float ResolveTargetDistancePerRun()
    {
        if (_GCM.GlobalCoordinationManager.instance != null)
            return Mathf.Max(0.0f, _GCM.GlobalCoordinationManager.instance.TargetDistancePerUser);

        return float.PositiveInfinity;
    }

    private int ResolveTargetCountPerRun(bool hasDistanceGoal)
    {
        UnitSetting unitSetting = ResolveUnitSetting();
        if (unitSetting != null && unitSetting.episodeLength > 0 && unitSetting.episodeLength < int.MaxValue)
            return unitSetting.episodeLength;

        if (predefinedTargets.Count > 0)
            return predefinedTargets.Count;

        int configuredTargetCount = Mathf.Max(1, targetsPerRun);
        if (hasDistanceGoal && configuredTargetCount <= 1)
            return -1;

        return configuredTargetCount;
    }

    private Vector2 GetUserVirtualPosition()
    {
        Transform cameraTransform = ResolveHmdCamera();
        Transform parentTransform = ResolveTargetParent();
        if (cameraTransform != null && parentTransform != null)
        {
            Vector3 localPosition = parentTransform.InverseTransformPoint(cameraTransform.position);
            return new Vector2(localPosition.x, localPosition.z);
        }

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
        currentTargetPathStartDistanceMeters = 0.0f;
        previousVirtualPosition = Vector2.zero;
        hasPreviousVirtualPosition = false;
        runCompletedLocally = false;
        completedRunId = string.Empty;
        cumulativeTargetDistanceMeters = 0.0f;
        nextSimulationTargetSuppressionTime = 0.0f;
        predefinedTargets.Clear();
    }

    private void SuppressSimulationEpisodeTargetVisuals(LiveVRNetworkManager manager)
    {
        if (manager == null || manager.IsHost || manager.Mode == LiveVRExperimentMode.Disabled)
            return;

        if (Time.unscaledTime < nextSimulationTargetSuppressionTime)
            return;

        nextSimulationTargetSuppressionTime = Time.unscaledTime + 0.5f;

        GameObject simulationRoot = GameObject.Find("RDWSimulation");
        if (simulationRoot == null)
            return;

        Transform virtualSpace = FindChildRecursive(simulationRoot.transform, "Virtual Space");
        if (virtualSpace == null)
            return;

        Renderer[] renderers = virtualSpace.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (IsSimulationTargetTransform(renderer.transform, virtualSpace))
                renderer.enabled = false;
        }
    }

    private static bool IsSimulationTargetTransform(Transform transform, Transform virtualSpace)
    {
        Transform current = transform;
        while (current != null && current != virtualSpace)
        {
            if (current.name.StartsWith("target", StringComparison.OrdinalIgnoreCase))
                return true;

            current = current.parent;
        }

        return false;
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

        GameObject virtualWorldRoot = GameObject.Find("VirtualWorldRoot");
        if (virtualWorldRoot != null)
        {
            targetParent = virtualWorldRoot.transform;
            return targetParent;
        }

        GameObject clientEnvironment = GameObject.Find("LiveVR Client Visual Environment");
        if (clientEnvironment != null)
        {
            targetParent = clientEnvironment.transform;
            return targetParent;
        }

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

    private float ResolveTargetLocalHeight(Transform parent)
    {
        Bounds areaBounds;
        if (parent != null && TryGetAreaAnchorBounds(out areaBounds))
        {
            Vector3 topWorld = new Vector3(areaBounds.center.x, areaBounds.max.y, areaBounds.center.z);
            return parent.InverseTransformPoint(topWorld).y + targetHeightMeters;
        }

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

    private UnitSetting ResolveUnitSetting()
    {
        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        if (simulationManager == null || simulationManager.simulationSetting == null || simulationManager.simulationSetting.unitSettings == null)
            return null;

        int userId = ResolveUserId();
        UnitSetting[] settings = simulationManager.simulationSetting.unitSettings;
        if (userId < 0 || userId >= settings.Length)
            return settings.Length > 0 ? settings[0] : null;

        return settings[userId];
    }

    private Vector2 ResolveCurrentForward()
    {
        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform != null)
            return NormalizeOrFallback(new Vector2(cameraTransform.forward.x, cameraTransform.forward.z), Vector2.up);

        return Vector2.up;
    }

    private static Vector2 NormalizeOrFallback(Vector2 value, Vector2 fallback)
    {
        if (value.sqrMagnitude > Mathf.Epsilon)
            return value.normalized;

        if (fallback.sqrMagnitude > Mathf.Epsilon)
            return fallback.normalized;

        return Vector2.up;
    }
}
