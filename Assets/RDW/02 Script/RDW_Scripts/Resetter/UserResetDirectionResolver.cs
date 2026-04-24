using System.Collections.Generic;
using UnityEngine;

public static class UserResetDirectionResolver
{
    private const float EPSILON = 0.0001f;
    private const float APF_SAMPLE_OFFSET = 0.02f;
    private const float APF_C = 0.00897f;
    private const float APF_LAMBDA = 2.656f;

    public static Vector2 ResolveDirection(RedirectedUnit unit, Object2D otherUser)
    {
        if (unit == null || unit.GetRealUser() == null)
            return Vector2.up;

        if (otherUser == null)
            return NormalizeOrFallback(unit.GetLastMovementDirection(), unit.GetRealUser().transform2D.forward);

        Vector2 defaultResetDirection = unit.GetRealUser().transform2D.localPosition - otherUser.transform2D.localPosition;
        return NormalizeOrFallback(defaultResetDirection, unit.GetRealUser().transform2D.forward);
    }

    public static Vector2 ComputeLocalApfDirection(RedirectedUnit unit)
    {
        if (unit == null)
            return Vector2.zero;

        Vector2 apfDirection = ComputeApfDirection(unit);
        return NormalizeOrFallback(apfDirection, unit.GetLastMovementDirection());
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
        RDWSimulationManager manager = RDWSimulationManager.instance;
        if (manager == null || manager.GetRedirectedUnits == null)
            return 0;

        RedirectedUnit[] units = manager.GetRedirectedUnits;
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

        List<Vector2> partitionedSpaceVertices = GetUserResetPolygonVertices(unit);
        if (partitionedSpaceVertices != null && partitionedSpaceVertices.Count >= 3)
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
