using UnityEngine;

public class RoadWalkEpisode : Episode
{
    private const float SegmentDistance = 12.0f;
    private const float EndpointBuffer = 1.0f;
    private const float TargetInsideBound = 0.5f;
    private const float MinFallbackSegmentDistance = 1.0f;
    private const float FallbackDistanceStep = 0.5f;

    private bool directionInitialized = false;
    private int travelSign = 1;

    public RoadWalkEpisode() : base()
    {
        episodeLength = int.MaxValue;
    }

    public RoadWalkEpisode(int episodeLength) : base(episodeLength)
    {
        this.episodeLength = int.MaxValue;
    }

    protected override void GenerateEpisode(Transform2D virtualUserTransform, Space2D virtualSpace, Object2D virtualUser)
    {
        if (virtualSpace == null || virtualSpace.spaceObject == null)
        {
            currentTargetPosition = virtualUserTransform.localPosition;
            return;
        }

        Bounds2D bounds = virtualSpace.spaceObject.bound;
        bool roadAlongX = bounds.size.x >= bounds.size.y;
        Vector2 userPosition = virtualUserTransform.localPosition;
        InitializeTravelDirection(virtualUserTransform.forward, roadAlongX);

        Vector2 targetPosition;
        if (TryBuildRoadTarget(virtualSpace, bounds, roadAlongX, userPosition, out targetPosition))
        {
            currentTargetPosition = targetPosition;
            return;
        }

        travelSign *= -1;
        if (TryBuildRoadTarget(virtualSpace, bounds, roadAlongX, userPosition, out targetPosition))
        {
            currentTargetPosition = targetPosition;
            return;
        }

        Debug.LogWarning("RoadWalkEpisode failed to find a reachable road target. Keeping the user in place.");
        currentTargetPosition = userPosition;
    }

    private void InitializeTravelDirection(Vector2 forward, bool roadAlongX)
    {
        if (directionInitialized)
            return;

        float axisForward = roadAlongX ? forward.x : forward.y;
        if (Mathf.Abs(axisForward) > 0.01f)
        {
            travelSign = axisForward >= 0.0f ? 1 : -1;
        }

        directionInitialized = true;
    }

    private bool TryBuildRoadTarget(
        Space2D virtualSpace,
        Bounds2D bounds,
        bool roadAlongX,
        Vector2 userPosition,
        out Vector2 targetPosition)
    {
        float roadMin = roadAlongX ? bounds.min.x : bounds.min.y;
        float roadMax = roadAlongX ? bounds.max.x : bounds.max.y;
        float crossMin = roadAlongX ? bounds.min.y : bounds.min.x;
        float crossMax = roadAlongX ? bounds.max.y : bounds.max.x;
        float userCross = roadAlongX ? userPosition.y : userPosition.x;
        // Road-walk tuning uses a strictly straight lane target: no lateral perturbation.
        float crossTarget = Mathf.Clamp(userCross, crossMin + EndpointBuffer, crossMax - EndpointBuffer);

        float userAxis = roadAlongX ? userPosition.x : userPosition.y;
        float distanceToEnd = travelSign > 0
            ? roadMax - EndpointBuffer - userAxis
            : userAxis - (roadMin + EndpointBuffer);

        if (distanceToEnd < MinFallbackSegmentDistance)
        {
            travelSign *= -1;
            distanceToEnd = travelSign > 0
                ? roadMax - EndpointBuffer - userAxis
                : userAxis - (roadMin + EndpointBuffer);
        }

        float sampledDistance = Mathf.Clamp(SegmentDistance, MinFallbackSegmentDistance, Mathf.Max(MinFallbackSegmentDistance, distanceToEnd));

        for (float distance = sampledDistance; distance >= MinFallbackSegmentDistance; distance -= FallbackDistanceStep)
        {
            float targetAxis = Mathf.Clamp(userAxis + travelSign * distance, roadMin + EndpointBuffer, roadMax - EndpointBuffer);
            Vector2 candidate = roadAlongX
                ? new Vector2(targetAxis, crossTarget)
                : new Vector2(crossTarget, targetAxis);

            if (IsValidTargetCandidate(virtualSpace, userPosition, candidate, TargetInsideBound))
            {
                targetPosition = candidate;
                return true;
            }
        }

        targetPosition = userPosition;
        return false;
    }
}
