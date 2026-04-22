using System.Collections.Generic;
using UnityEngine;

public static class ProactiveUserResetArbitrationService
{
    private const float EPSILON = 0.0001f;
    private const float DEFAULT_SAFE_BUFFER = 0.1f;
    private const float DEFAULT_HORIZON_SECONDS = 1.5f;
    private const int DEFAULT_SAMPLE_COUNT = 60;
    private const float RAY_MAX_DISTANCE = 200.0f;

    public struct ArbitrationResult
    {
        public int SelectedUnitIndex;
        public int OtherUnitIndex;
        public Vector2 SelectedResetDirection;
        public float KeepMargin;
        public float SelectedM;
        public float SelectedCSelf;
    }

    public static bool TryArbitratePair(
        RedirectedUnit unitA,
        int indexA,
        RedirectedUnit unitB,
        int indexB,
        float mEpsilon,
        float cEpsilon,
        out ArbitrationResult result)
    {
        result = new ArbitrationResult
        {
            SelectedUnitIndex = -1,
            OtherUnitIndex = -1,
            SelectedResetDirection = Vector2.zero,
            KeepMargin = float.PositiveInfinity,
            SelectedM = 0.0f,
            SelectedCSelf = 0.0f
        };

        if (unitA == null || unitB == null || unitA.GetRealUser() == null || unitB.GetRealUser() == null)
            return false;

        Vector2 keepDirectionA = ResolveKeepDirection(unitA);
        Vector2 keepDirectionB = ResolveKeepDirection(unitB);
        Vector2 resetDirectionA = ResolveResetDirection(unitA, keepDirectionA);
        Vector2 resetDirectionB = ResolveResetDirection(unitB, keepDirectionB);

        float keepDistanceA = ComputePhysicalRemainingDistance(unitA, keepDirectionA);
        float keepDistanceB = ComputePhysicalRemainingDistance(unitB, keepDirectionB);
        float resetDistanceA = ComputePhysicalRemainingDistance(unitA, resetDirectionA);
        float resetDistanceB = ComputePhysicalRemainingDistance(unitB, resetDirectionB);

        float cSelfA = keepDistanceA - resetDistanceA;
        float cSelfB = keepDistanceB - resetDistanceB;

        float keepMargin = EvaluatePairMargin(unitA, keepDirectionA, unitB, keepDirectionB, DEFAULT_HORIZON_SECONDS, DEFAULT_SAMPLE_COUNT);
        float marginAReset = EvaluatePairMargin(unitA, resetDirectionA, unitB, keepDirectionB, DEFAULT_HORIZON_SECONDS, DEFAULT_SAMPLE_COUNT);
        float marginBReset = EvaluatePairMargin(unitA, keepDirectionA, unitB, resetDirectionB, DEFAULT_HORIZON_SECONDS, DEFAULT_SAMPLE_COUNT);

        float mA = marginAReset - keepMargin;
        float mB = marginBReset - keepMargin;

        bool selectA;
        if (Mathf.Abs(mA - mB) > mEpsilon)
        {
            selectA = mA > mB;
        }
        else if (Mathf.Abs(cSelfA - cSelfB) > cEpsilon)
        {
            selectA = cSelfA < cSelfB;
        }
        else
        {
            selectA = unitA.GetID() <= unitB.GetID();
        }

        if (selectA)
        {
            result.SelectedUnitIndex = indexA;
            result.OtherUnitIndex = indexB;
            result.SelectedResetDirection = resetDirectionA;
            result.SelectedM = mA;
            result.SelectedCSelf = cSelfA;
        }
        else
        {
            result.SelectedUnitIndex = indexB;
            result.OtherUnitIndex = indexA;
            result.SelectedResetDirection = resetDirectionB;
            result.SelectedM = mB;
            result.SelectedCSelf = cSelfB;
        }

        result.KeepMargin = keepMargin;
        return true;
    }

    private static Vector2 ResolveKeepDirection(RedirectedUnit unit)
    {
        if (unit == null || unit.GetRealUser() == null)
            return Vector2.up;

        Vector2 movement = unit.GetLastMovementDirection();
        if (movement.sqrMagnitude > EPSILON)
            return movement.normalized;

        Vector2 forward = unit.GetRealUser().transform2D.forward;
        if (forward.sqrMagnitude > EPSILON)
            return forward.normalized;

        return Vector2.up;
    }

    private static Vector2 ResolveResetDirection(RedirectedUnit unit, Vector2 fallbackDirection)
    {
        Vector2 apfDirection = UserResetDirectionResolver.ComputeLocalApfDirection(unit);
        if (apfDirection.sqrMagnitude > EPSILON)
            return apfDirection.normalized;

        if (fallbackDirection.sqrMagnitude > EPSILON)
            return fallbackDirection.normalized;

        return Vector2.up;
    }

    private static float ComputePhysicalRemainingDistance(RedirectedUnit unit, Vector2 direction)
    {
        if (unit == null || unit.GetRealUser() == null || unit.GetRealSpace() == null || direction.sqrMagnitude <= EPSILON)
            return 0.0f;

        Vector2 origin = unit.GetRealUser().transform2D.position;
        Vector2 normalizedDirection = direction.normalized;
        float bestDistance = RAY_MAX_DISTANCE;
        Space2D realSpace = unit.GetRealSpace();

        UpdateBestDistanceAgainstObject(realSpace.spaceObject, origin, normalizedDirection, ref bestDistance);

        List<Object2D> obstacles = realSpace.obstacles;
        if (obstacles != null)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                UpdateBestDistanceAgainstObject(obstacles[i], origin, normalizedDirection, ref bestDistance);
            }
        }

        return Mathf.Max(0.0f, bestDistance);
    }

    private static void UpdateBestDistanceAgainstObject(Object2D obj, Vector2 origin, Vector2 direction, ref float bestDistance)
    {
        if (obj == null)
            return;

        if (obj is Polygon2D polygon)
        {
            List<Vector2> vertices = polygon.GetVertices();
            if (vertices == null || vertices.Count < 2)
                return;

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector2 p1 = polygon.GetVertex(i, Space.World);
                Vector2 p2 = polygon.GetVertex(i + 1, Space.World);
                if (TryIntersectRayWithSegment(origin, direction, p1, p2, out float distance) &&
                    distance < bestDistance)
                {
                    bestDistance = distance;
                }
            }
            return;
        }

        if (obj is Circle2D circle)
        {
            if (TryIntersectRayWithCircle(origin, direction, circle.transform2D.position, circle.GetRadius(), out float distance) &&
                distance < bestDistance)
            {
                bestDistance = distance;
            }
            return;
        }

        if (obj is LineSegment2D line)
        {
            Edge2D edge = line.ChangeToEdge(Space.World);
            if (TryIntersectRayWithSegment(origin, direction, edge.p1, edge.p2, out float distance) &&
                distance < bestDistance)
            {
                bestDistance = distance;
            }
        }
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

    private static bool TryIntersectRayWithCircle(Vector2 rayOrigin, Vector2 rayDirection, Vector2 circleCenter, float radius, out float distance)
    {
        distance = 0.0f;
        Vector2 toCenter = rayOrigin - circleCenter;
        float a = Vector2.Dot(rayDirection, rayDirection);
        float b = 2.0f * Vector2.Dot(rayDirection, toCenter);
        float c = Vector2.Dot(toCenter, toCenter) - (radius * radius);
        float discriminant = (b * b) - (4.0f * a * c);
        if (discriminant < 0.0f)
            return false;

        float sqrtDisc = Mathf.Sqrt(discriminant);
        float t1 = (-b - sqrtDisc) / (2.0f * a);
        float t2 = (-b + sqrtDisc) / (2.0f * a);
        float best = float.PositiveInfinity;

        if (t1 > EPSILON) best = t1;
        if (t2 > EPSILON && t2 < best) best = t2;
        if (float.IsInfinity(best))
            return false;

        distance = best;
        return true;
    }

    private static float EvaluatePairMargin(
        RedirectedUnit unitA,
        Vector2 directionA,
        RedirectedUnit unitB,
        Vector2 directionB,
        float horizonSeconds,
        int sampleCount)
    {
        Object2D userA = unitA.GetRealUser();
        Object2D userB = unitB.GetRealUser();

        float speedA = Mathf.Max(unitA.GetResetter().GetTranslationSpeed(), EPSILON);
        float speedB = Mathf.Max(unitB.GetResetter().GetTranslationSpeed(), EPSILON);
        float safeDistance = ResolveSafeDistance(userA, userB);
        Vector2 normalizedA = directionA.sqrMagnitude > EPSILON ? directionA.normalized : ResolveKeepDirection(unitA);
        Vector2 normalizedB = directionB.sqrMagnitude > EPSILON ? directionB.normalized : ResolveKeepDirection(unitB);
        int safeSampleCount = Mathf.Max(sampleCount, 2);

        float minMargin = float.PositiveInfinity;
        for (int i = 0; i <= safeSampleCount; i++)
        {
            float t = horizonSeconds * i / safeSampleCount;
            Vector2 pA = userA.transform2D.localPosition + normalizedA * speedA * t;
            Vector2 pB = userB.transform2D.localPosition + normalizedB * speedB * t;
            float margin = Vector2.Distance(pA, pB) - safeDistance;
            if (margin < minMargin)
                minMargin = margin;
        }

        return minMargin;
    }

    private static float ResolveSafeDistance(Object2D userA, Object2D userB)
    {
        float radiusA = ResolveUserRadius(userA);
        float radiusB = ResolveUserRadius(userB);
        return radiusA + radiusB + DEFAULT_SAFE_BUFFER;
    }

    private static float ResolveUserRadius(Object2D user)
    {
        if (user is Circle2D circle)
            return circle.GetRadius();

        return 0.5f;
    }

    private static float Cross(Vector2 lhs, Vector2 rhs)
    {
        return lhs.x * rhs.y - lhs.y * rhs.x;
    }
}
