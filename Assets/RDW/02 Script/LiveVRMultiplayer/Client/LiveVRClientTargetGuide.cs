using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

[DefaultExecutionOrder(12000)]
public class LiveVRClientTargetGuide : MonoBehaviour
{
    [SerializeField] private LiveVRNetworkManager networkManager;
    [SerializeField] private Transform hmdCamera;
    [SerializeField] private Transform targetParent;
    [SerializeField] private GameObject targetPrefab;
    [SerializeField] private bool enableGuide = true;
    [SerializeField] private bool showOnlyWhileRunning = true;
    [SerializeField] private string targetLayerName = "VirtualWall";
    [SerializeField] private bool useAreaAnchorBounds = true;
    [SerializeField] private string areaAnchorName = "walkingArea";
    [SerializeField] private float targetAreaWidthMeters = 16.0f;
    [SerializeField] private float targetAreaDepthMeters = 16.0f;
    [SerializeField] private float targetHeightMeters = 1.35f;
    [SerializeField] private float targetRadiusMeters = 0.18f;
    [SerializeField] private float reachDistanceMeters = 0.65f;
    [SerializeField] private float resetPromptSuppressSeconds = 1.0f;

    private GameObject targetObject;
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
    private System.Random targetRandom;
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
        string newTargetLayerName,
        bool newUseAreaAnchorBounds,
        string newAreaAnchorName,
        float newTargetAreaWidthMeters,
        float newTargetAreaDepthMeters,
        float newTargetHeightMeters,
        float newTargetRadiusMeters,
        float newReachDistanceMeters)
    {
        networkManager = newNetworkManager;
        hmdCamera = newHmdCamera;
        targetParent = newTargetParent;
        targetPrefab = newTargetPrefab;
        enableGuide = newEnableGuide;
        showOnlyWhileRunning = newShowOnlyWhileRunning;
        targetLayerName = string.IsNullOrEmpty(newTargetLayerName) ? "VirtualWall" : newTargetLayerName;
        useAreaAnchorBounds = newUseAreaAnchorBounds;
        areaAnchorName = string.IsNullOrEmpty(newAreaAnchorName) ? "walkingArea" : newAreaAnchorName;
        targetAreaWidthMeters = Mathf.Max(0.5f, newTargetAreaWidthMeters);
        targetAreaDepthMeters = Mathf.Max(0.5f, newTargetAreaDepthMeters);
        targetHeightMeters = Mathf.Max(0.0f, newTargetHeightMeters);
        targetRadiusMeters = Mathf.Max(0.05f, newTargetRadiusMeters);
        reachDistanceMeters = Mathf.Max(0.1f, newReachDistanceMeters);
        ResetGuide();
    }

    public void ClearResetSuppression()
    {
        wasSuppressedByReset = false;
    }

    public void ClearForRestart()
    {
        wasSuppressedByReset = false;
        ResetGuide();
        if (targetObject != null)
            targetObject.SetActive(false);
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
        targetIndex++;
        targetRandom = new System.Random(BuildTargetSeed(targetIndex));

        bool usedInitialForwardTarget = targetIndex == 0 &&
                                        TryGetInitialForwardTarget(userPosition, out currentTarget);
        bool usedEpisodeTarget = !usedInitialForwardTarget &&
                                 TryGetSimulationEpisodeTarget(userPosition, targetIndex, out currentTarget);
        if (!usedInitialForwardTarget && !usedEpisodeTarget)
            currentTarget = GetRandomTargetAwayFromUser(userPosition);

        if (!IsTargetInsideArea(currentTarget))
            currentTarget = GetRandomTargetAwayFromUser(userPosition);

        currentTargetPathStartDistanceMeters = cumulativeTargetDistanceMeters;
        hasTarget = true;
        targetRandom = null;
    }

    private bool TryGetInitialForwardTarget(Vector2 userPosition, out Vector2 target)
    {
        Vector2 forward = ResolveCurrentForward();
        ResolveEpisodeDistanceRange(out float minDistance, out float maxDistance);
        return TryFindTargetByDistanceRange(
            userPosition,
            forward,
            minDistance,
            maxDistance,
            0.0f,
            0.0f,
            1.0f,
            out target);
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
                return TryFindTargetByDistanceRange(userPosition, forward, 4.0f, 8.0f, -90.0f, 90.0f, 10.0f, out target);
            case EpisodeType.LongRandom:
                return TryFindTargetByDistanceRange(userPosition, forward, 8.0f, 12.0f, -90.0f, 90.0f, 10.0f, out target);
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

    private void ResolveEpisodeDistanceRange(out float minDistance, out float maxDistance)
    {
        minDistance = 4.0f;
        maxDistance = 8.0f;

        UnitSetting unitSetting = ResolveUnitSetting();
        if (unitSetting == null)
            return;

        switch (unitSetting.episodeType)
        {
            case EpisodeType.LongWalk:
                minDistance = 12.0f;
                maxDistance = 12.0f;
                break;
            case EpisodeType.LongRandom:
                minDistance = 8.0f;
                maxDistance = 12.0f;
                break;
            case EpisodeType.NaturalTouring:
                minDistance = 1.0f;
                maxDistance = 3.0f;
                break;
            case EpisodeType.WanderingEpisodeForFixedReset:
                minDistance = 0.2f;
                maxDistance = 0.2f;
                break;
            case EpisodeType.WanderingEpisodeForAnyReset:
                minDistance = 0.5f;
                maxDistance = 0.5f;
                break;
        }
    }

    private Vector2 GetRandomTargetAwayFromUser(Vector2 userPosition)
    {
        Vector2 target = Vector2.zero;
        ResolveEpisodeDistanceRange(out float effectiveMinDistance, out float effectiveMaxDistance);

        for (int attempt = 0; attempt < 96; attempt++)
        {
            float angleRadians = NextRandom01() * Mathf.PI * 2.0f;
            float distance = Mathf.Lerp(effectiveMinDistance, effectiveMaxDistance, NextRandom01());
            target = userPosition + new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * distance;

            if (IsTargetInsideArea(target))
                return target;
        }

        BoxCollider areaCollider;
        if (TryGetAreaAnchorBoxCollider(out areaCollider))
            return ClampTargetToAreaCollider(target, areaCollider);

        float minX = -targetAreaWidthMeters * 0.5f;
        float maxX = targetAreaWidthMeters * 0.5f;
        float minZ = -targetAreaDepthMeters * 0.5f;
        float maxZ = targetAreaDepthMeters * 0.5f;
        target = new Vector2(
            Mathf.Clamp(target.x, minX, maxX),
            Mathf.Clamp(target.y, minZ, maxZ));
        return target;
    }

    private bool IsTargetInsideArea(Vector2 target)
    {
        BoxCollider areaCollider;
        if (TryGetAreaAnchorBoxCollider(out areaCollider))
            return IsTargetInsideAreaCollider(target, areaCollider);

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

    private bool IsTargetInsideAreaCollider(Vector2 target, BoxCollider areaCollider)
    {
        Transform parent = ResolveTargetParent();
        if (parent == null || areaCollider == null)
            return false;

        Vector3 candidateWorld = parent.TransformPoint(new Vector3(target.x, 0.0f, target.y));
        Vector3 candidateLocal = areaCollider.transform.InverseTransformPoint(candidateWorld);
        Vector3 center = areaCollider.center;
        Vector3 halfSize = areaCollider.size * 0.5f;
        Vector3 lossyScale = areaCollider.transform.lossyScale;
        float marginX = targetRadiusMeters / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x));
        float marginZ = targetRadiusMeters / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z));

        return candidateLocal.x >= center.x - halfSize.x + marginX &&
               candidateLocal.x <= center.x + halfSize.x - marginX &&
               candidateLocal.z >= center.z - halfSize.z + marginZ &&
               candidateLocal.z <= center.z + halfSize.z - marginZ;
    }

    private Vector2 ClampTargetToAreaCollider(Vector2 target, BoxCollider areaCollider)
    {
        Transform parent = ResolveTargetParent();
        if (parent == null || areaCollider == null)
            return target;

        Vector3 candidateWorld = parent.TransformPoint(new Vector3(target.x, 0.0f, target.y));
        Vector3 candidateLocal = areaCollider.transform.InverseTransformPoint(candidateWorld);
        Vector3 center = areaCollider.center;
        Vector3 halfSize = areaCollider.size * 0.5f;
        Vector3 lossyScale = areaCollider.transform.lossyScale;
        float marginX = targetRadiusMeters / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.x));
        float marginZ = targetRadiusMeters / Mathf.Max(0.0001f, Mathf.Abs(lossyScale.z));

        candidateLocal.x = Mathf.Clamp(
            candidateLocal.x,
            center.x - halfSize.x + marginX,
            center.x + halfSize.x - marginX);
        candidateLocal.z = Mathf.Clamp(
            candidateLocal.z,
            center.z - halfSize.z + marginZ,
            center.z + halfSize.z - marginZ);
        candidateLocal.y = center.y;

        Vector3 clampedWorld = areaCollider.transform.TransformPoint(candidateLocal);
        Vector3 clampedParentLocal = parent.InverseTransformPoint(clampedWorld);
        return new Vector2(clampedParentLocal.x, clampedParentLocal.z);
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

        if (distanceComplete)
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
        float angleRange = NextRandom01() < 0.2f ? 120.0f : 45.0f;
        for (int attempt = 0; attempt < 96; attempt++)
        {
            float distance = Mathf.Lerp(1.0f, 3.0f, NextRandom01());
            float angle = Mathf.Lerp(-angleRange, angleRange, NextRandom01());
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
            float distance = Mathf.Lerp(safeMin, safeMax, NextRandom01());
            float angle = Mathf.Lerp(minAngle, maxAngle, NextRandom01());
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

    private int BuildTargetSeed(int index)
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        int seed = manager != null && manager.TargetSeed != int.MinValue
            ? manager.TargetSeed
            : StableHash(manager != null ? manager.CurrentRunId : string.Empty);

        unchecked
        {
            seed = (seed * 397) ^ ResolveUserId();
            seed = (seed * 397) ^ Mathf.Max(0, index);
            seed = (seed * 397) ^ (manager != null ? manager.TargetSeedVersion : 0);
        }

        return seed == int.MinValue ? 0 : seed;
    }

    private float NextRandom01()
    {
        if (targetRandom == null)
            return UnityEngine.Random.value;

        return (float)targetRandom.NextDouble();
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            int hash = 23;
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];
            }

            return hash;
        }
    }

    private void ResetGuide()
    {
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

    private bool TryGetAreaAnchorBoxCollider(out BoxCollider areaCollider)
    {
        areaCollider = null;
        if (!useAreaAnchorBounds || string.IsNullOrEmpty(areaAnchorName))
            return false;

        Transform anchor = FindAreaAnchor();
        if (anchor == null)
            return false;

        areaCollider = anchor.GetComponent<BoxCollider>();
        if (areaCollider == null)
            areaCollider = anchor.GetComponentInChildren<BoxCollider>(true);

        return areaCollider != null && areaCollider.enabled;
    }

    private Transform FindAreaAnchor()
    {
        Transform targetRoot = ResolveTargetParent();
        if (targetRoot != null)
        {
            Transform clientEnvironment = FindChildRecursive(targetRoot, "LiveVR Client Visual Environment");
            if (clientEnvironment != null)
            {
                Transform clientArea = FindChildRecursive(clientEnvironment, areaAnchorName);
                if (clientArea != null)
                    return clientArea;
            }

            Transform targetArea = FindChildRecursive(targetRoot, areaAnchorName);
            if (targetArea != null)
                return targetArea;
        }

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
        {
            Vector3 forward = cameraTransform.forward;
            Transform parentTransform = ResolveTargetParent();
            if (parentTransform != null)
                forward = parentTransform.InverseTransformDirection(forward);

            return NormalizeOrFallback(new Vector2(forward.x, forward.z), Vector2.up);
        }

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
