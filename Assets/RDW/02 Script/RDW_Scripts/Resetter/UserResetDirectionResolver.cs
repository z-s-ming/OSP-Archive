using System.Collections.Generic;
using UnityEngine;

public static class UserResetDirectionResolver
{
    private const float EPSILON = 0.0001f;
    private const float DISTANCE_TIE_THRESHOLD = 0.25f;
    private const float BOUNDARY_DIRECTION_DOT_THRESHOLD = 0.3f;
    private const float APF_SAMPLE_OFFSET = 0.02f;
    private const float APF_C = 0.00897f;
    private const float APF_LAMBDA = 2.656f;

    private struct RayHitInfo
    {
        public bool HasHit;
        public float Distance;
        public Vector2 InwardNormal;
    }

    public static Vector2 ResolveDirection(RedirectedUnit unit, Object2D otherUser)
    {
        if (unit == null || unit.GetRealUser() == null)
            return Vector2.up;

        if (otherUser == null)
            return NormalizeOrFallback(unit.GetLastMovementDirection(), unit.GetRealUser().transform2D.forward);

        Vector2 defaultResetDirection = GetDefaultResetDirection(unit.GetRealUser(), otherUser);
        if (!ShouldUseHybridStrategy())
            return defaultResetDirection;

        RedirectedUnit otherUnit = FindUnitByRealUser(otherUser);
        if (otherUnit == null || !IsBidirectionalReset(unit, otherUnit))
            return defaultResetDirection;

        Vector2 otherDefaultResetDirection = GetDefaultResetDirection(otherUnit.GetRealUser(), unit.GetRealUser());
        float selfRemainingDistance = ComputeRemainingDistance(unit, defaultResetDirection);
        float otherRemainingDistance = ComputeRemainingDistance(otherUnit, otherDefaultResetDirection);

        if (!IsComparable(selfRemainingDistance, otherRemainingDistance))
            return defaultResetDirection;

        bool selfBoundaryDirection = IsBoundaryDirection(unit, defaultResetDirection);
        bool otherBoundaryDirection = IsBoundaryDirection(otherUnit, otherDefaultResetDirection);

        if (selfRemainingDistance + DISTANCE_TIE_THRESHOLD < otherRemainingDistance && selfBoundaryDirection)
            return NormalizeOrFallback(unit.GetLastMovementDirection(), defaultResetDirection);

        if (otherRemainingDistance + DISTANCE_TIE_THRESHOLD < selfRemainingDistance && otherBoundaryDirection)
            return NormalizeOrFallback(ComputeApfDirection(unit), defaultResetDirection);

        return defaultResetDirection;
    }

    private static bool ShouldUseHybridStrategy()
    {
        RDWSimulationManager manager = RDWSimulationManager.instance;
        return manager != null &&
               manager.simulationSetting != null &&
               manager.simulationSetting.useHybridApfUserResetDirection;
    }

    private static bool IsComparable(float selfRemainingDistance, float otherRemainingDistance)
    {
        return !float.IsNaN(selfRemainingDistance) &&
               !float.IsNaN(otherRemainingDistance) &&
               !float.IsInfinity(selfRemainingDistance) &&
               !float.IsInfinity(otherRemainingDistance);
    }

    private static bool IsBidirectionalReset(RedirectedUnit unit, RedirectedUnit otherUnit)
    {
        Vector2 selfMovementDirection = unit.GetLastMovementDirection();
        Vector2 otherMovementDirection = otherUnit.GetLastMovementDirection();
        return Vector2.Dot(selfMovementDirection, otherMovementDirection) < 0.0f;
    }

    private static RedirectedUnit FindUnitByRealUser(Object2D realUser)
    {
        RedirectedUnit[] units = RDWSimulationManager.instance.GetRedirectedUnits;
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] != null && units[i].GetRealUser() == realUser)
                return units[i];
        }

        return null;
    }

    private static Vector2 GetDefaultResetDirection(Object2D selfUser, Object2D otherUser)
    {
        Vector2 direction = selfUser.transform2D.localPosition - otherUser.transform2D.localPosition;
        return NormalizeOrFallback(direction, selfUser.transform2D.forward);
    }

    private static float ComputeRemainingDistance(RedirectedUnit unit, Vector2 direction)
    {
        List<Vector2> polygonVertices = GetUserResetPolygonVertices(unit);
        RayHitInfo hitInfo = FindClosestBoundaryHit(unit.GetRealUser().transform2D.localPosition, direction, polygonVertices);
        return hitInfo.HasHit ? hitInfo.Distance : float.PositiveInfinity;
    }

    private static bool IsBoundaryDirection(RedirectedUnit unit, Vector2 direction)
    {
        List<Vector2> polygonVertices = GetUserResetPolygonVertices(unit);
        RayHitInfo hitInfo = FindClosestBoundaryHit(unit.GetRealUser().transform2D.localPosition, direction, polygonVertices);
        if (!hitInfo.HasHit)
            return false;

        return Vector2.Dot(direction.normalized, hitInfo.InwardNormal) < -BOUNDARY_DIRECTION_DOT_THRESHOLD;
    }

    private static RayHitInfo FindClosestBoundaryHit(Vector2 origin, Vector2 direction, List<Vector2> polygonVertices)
    {
        RayHitInfo bestHit = new RayHitInfo
        {
            HasHit = false,
            Distance = float.PositiveInfinity,
            InwardNormal = Vector2.zero
        };

        if (polygonVertices == null || polygonVertices.Count < 3 || direction.sqrMagnitude <= EPSILON)
            return bestHit;

        Vector2 normalizedDirection = direction.normalized;
        Vector2 centroid = ComputeCentroid(polygonVertices);

        for (int i = 0; i < polygonVertices.Count; i++)
        {
            Vector2 p1 = polygonVertices[i];
            Vector2 p2 = polygonVertices[(i + 1) % polygonVertices.Count];

            if (!TryIntersectRayWithSegment(origin, normalizedDirection, p1, p2, out float distance))
                continue;

            if (distance >= bestHit.Distance)
                continue;

            Vector2 edgeDirection = (p2 - p1).normalized;
            Vector2 edgeMidpoint = (p1 + p2) * 0.5f;
            Vector2 candidateNormal = Utility.RotateVector2(edgeDirection, -90f).normalized;
            Vector2 inwardNormal = Vector2.Dot(candidateNormal, centroid - edgeMidpoint) >= 0.0f
                ? candidateNormal
                : -candidateNormal;

            bestHit.HasHit = true;
            bestHit.Distance = distance;
            bestHit.InwardNormal = inwardNormal;
        }

        return bestHit;
    }

    private static bool TryIntersectRayWithSegment(Vector2 rayOrigin, Vector2 rayDirection, Vector2 p1, Vector2 p2, out float distance)
    {
        distance = 0.0f;

        Vector2 segmentDirection = p2 - p1;
        float denominator = Cross(rayDirection, segmentDirection);
        if (Mathf.Abs(denominator) <= EPSILON)
            return false;

        Vector2 originToSegment = p1 - rayOrigin;
        float rayDistance = Cross(originToSegment, segmentDirection) / denominator;
        float segmentDistance = Cross(originToSegment, rayDirection) / denominator;

        if (rayDistance <= EPSILON || segmentDistance < -EPSILON || segmentDistance > 1.0f + EPSILON)
            return false;

        distance = rayDistance;
        return true;
    }

    private static float Cross(Vector2 lhs, Vector2 rhs)
    {
        return lhs.x * rhs.y - lhs.y * rhs.x;
    }

    private static Vector2 ComputeCentroid(List<Vector2> vertices)
    {
        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < vertices.Count; i++)
        {
            centroid += vertices[i];
        }

        return centroid / vertices.Count;
    }

    private static List<Vector2> GetUserResetPolygonVertices(RedirectedUnit unit)
    {
        int redirectedUnitIndex = GetRedirectedUnitIndex(unit);
        List<Vector2> partitionedSpaceVertices;
        if (_GCM.GlobalCoordinationManager.instance != null &&
            _GCM.GlobalCoordinationManager.instance.dic_AreaSegmentsVertex.TryGetValue(redirectedUnitIndex, out partitionedSpaceVertices) &&
            partitionedSpaceVertices != null &&
            partitionedSpaceVertices.Count >= 3)
        {
            return new List<Vector2>(partitionedSpaceVertices);
        }

        Polygon2D realPolygonObject = unit.GetRealSpace().spaceObject as Polygon2D;
        return realPolygonObject != null ? new List<Vector2>(realPolygonObject.GetVertices()) : new List<Vector2>();
    }

    private static int GetRedirectedUnitIndex(RedirectedUnit targetUnit)
    {
        RedirectedUnit[] units = RDWSimulationManager.instance.GetRedirectedUnits;
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] == targetUnit)
                return i;
        }

        return 0;
    }

    private static Vector2 ComputeApfDirection(RedirectedUnit unit)
    {
        Object2D realUser = unit.GetRealUser();
        Space2D realSpace = unit.GetRealSpace();
        Polygon2D polygonForApf = null;
        bool destroyTemporaryPolygon = false;
        int redirectedUnitIndex = GetRedirectedUnitIndex(unit);

        List<Vector2> partitionedSpaceVertices;
        if (_GCM.GlobalCoordinationManager.instance != null &&
            _GCM.GlobalCoordinationManager.instance.dic_AreaSegmentsVertex.TryGetValue(redirectedUnitIndex, out partitionedSpaceVertices) &&
            partitionedSpaceVertices != null &&
            partitionedSpaceVertices.Count >= 3)
        {
            Object2D spaceObject = new Polygon2DBuilder()
                .SetName("UserResetAPF_" + redirectedUnitIndex)
                .SetPrefab(null)
                .SetLocalPosition(Vector2.zero)
                .SetLocalRotation(0.0f)
                .SetMode(false)
                .SetSize(1.0f)
                .SetCount(4)
                .SetVertices(partitionedSpaceVertices)
                .Build();

            Space2D partitionedSpace = new Space2DBuilder()
                .SetName("UserResetPS_" + redirectedUnitIndex)
                .SetSpaceObject(spaceObject)
                .SetObstacles(new List<Object2D>())
                .Build();

            polygonForApf = partitionedSpace.spaceObject as Polygon2D;
            destroyTemporaryPolygon = polygonForApf != null;
        }
        else
        {
            polygonForApf = realSpace.spaceObject as Polygon2D;
        }

        if (polygonForApf == null)
            return Vector2.zero;

        Vector2 userForward = realUser.transform2D.forward.sqrMagnitude > EPSILON
            ? realUser.transform2D.forward.normalized
            : unit.GetLastMovementDirection();
        Vector2 userPosition = realUser.transform2D.localPosition - APF_SAMPLE_OFFSET * userForward;

        Vector2 apfDirection = Vector2.zero;
        for (int i = 0; i < polygonForApf.segmentedVertices.Count; i++)
        {
            Vector2 d = userPosition - polygonForApf.segmentedVertices[i];
            float distance = d.magnitude;
            if (distance <= EPSILON)
                continue;

            Vector2 normalizedD = d / distance;
            if (Vector2.Dot(polygonForApf.segmentNormalVectors[i], normalizedD) <= 0.0f)
                continue;

            float inverseDistance = Mathf.Pow(1.0f / distance, APF_LAMBDA);
            apfDirection += APF_C * polygonForApf.segmentedEdgeLengths[i] * normalizedD * inverseDistance;
        }

        if (destroyTemporaryPolygon)
            polygonForApf.Destroy(0.02f);

        return apfDirection;
    }

    private static Vector2 NormalizeOrFallback(Vector2 direction, Vector2 fallbackDirection)
    {
        if (direction.sqrMagnitude > EPSILON)
            return direction.normalized;

        if (fallbackDirection.sqrMagnitude > EPSILON)
            return fallbackDirection.normalized;

        return Vector2.up;
    }
}
