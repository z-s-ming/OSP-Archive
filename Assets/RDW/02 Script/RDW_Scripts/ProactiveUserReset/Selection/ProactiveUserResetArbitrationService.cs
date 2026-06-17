using System.Collections.Generic;
using UnityEngine;

public static class ProactiveUserResetArbitrationService
{
    private const float EPSILON = 0.0001f;
    private const float RAY_MAX_DISTANCE = 200.0f;

    public struct ArbitrationResult
    {
        public int SelectedUnitIndex;
        public int OtherUnitIndex;
        public Vector2 SelectedResetDirection;
        public float KeepMargin;
        public float SelectedM;
        public float SelectedCSelf;
        public float SelectedScore;
    }

    public static bool TryArbitratePair(
        RedirectedUnit unitA,
        int indexA,
        RedirectedUnit unitB,
        int indexB,
        float mEpsilon,
        float cEpsilon,
        bool useScoredArbitration,
        float scoreAlpha,
        float scoreBeta,
        float scoreGamma,
        float scoreD0Meters,
        float scoreEpsilonMeters,
        float scoreTieEpsilon,
        ProactiveResetDirectionMode directionMode,
        out ArbitrationResult result)
    {
        result = new ArbitrationResult
        {
            SelectedUnitIndex = -1,
            OtherUnitIndex = -1,
            SelectedResetDirection = Vector2.zero,
            KeepMargin = float.PositiveInfinity,
            SelectedM = 0.0f,
            SelectedCSelf = 0.0f,
            SelectedScore = float.NegativeInfinity
        };

        if (unitA == null || unitB == null || unitA.GetRealUser() == null || unitB.GetRealUser() == null)
            return false;

        Vector2 keepDirectionA = ResolveKeepDirection(unitA);
        Vector2 keepDirectionB = ResolveKeepDirection(unitB);
        Vector2 resetDirectionA = ResolveResetDirection(unitA, unitB, directionMode);
        Vector2 resetDirectionB = ResolveResetDirection(unitB, unitA, directionMode);

        bool hasValidResetA = resetDirectionA.sqrMagnitude > EPSILON;
        bool hasValidResetB = resetDirectionB.sqrMagnitude > EPSILON;
        if (!hasValidResetA && !hasValidResetB)
            return false;

        float keepDistanceA = ComputePhysicalRemainingDistance(unitA, keepDirectionA);
        float keepDistanceB = ComputePhysicalRemainingDistance(unitB, keepDirectionB);
        float resetDistanceA = hasValidResetA ? ComputePhysicalRemainingDistance(unitA, resetDirectionA) : keepDistanceA;
        float resetDistanceB = hasValidResetB ? ComputePhysicalRemainingDistance(unitB, resetDirectionB) : keepDistanceB;

        float cSelfA = hasValidResetA ? (keepDistanceA - resetDistanceA) : float.PositiveInfinity;
        float cSelfB = hasValidResetB ? (keepDistanceB - resetDistanceB) : float.PositiveInfinity;

        float keepWorstDistance = Mathf.Min(keepDistanceA, keepDistanceB);
        float resetAWorstDistance = hasValidResetA ? Mathf.Min(resetDistanceA, keepDistanceB) : float.NegativeInfinity;
        float resetBWorstDistance = hasValidResetB ? Mathf.Min(resetDistanceB, keepDistanceA) : float.NegativeInfinity;
        float resetAScore = hasValidResetA
            ? (useScoredArbitration
                ? ComputeArbitrationScore(
                    resetDistanceA,
                    keepDistanceB,
                    scoreAlpha,
                    scoreBeta,
                    scoreGamma,
                    scoreD0Meters,
                    scoreEpsilonMeters)
                : resetAWorstDistance)
            : float.NegativeInfinity;
        float resetBScore = hasValidResetB
            ? (useScoredArbitration
                ? ComputeArbitrationScore(
                    resetDistanceB,
                    keepDistanceA,
                    scoreAlpha,
                    scoreBeta,
                    scoreGamma,
                    scoreD0Meters,
                    scoreEpsilonMeters)
                : resetBWorstDistance)
            : float.NegativeInfinity;

        bool selectA;
        if (useScoredArbitration && Mathf.Abs(resetAScore - resetBScore) > scoreTieEpsilon)
        {
            selectA = resetAScore > resetBScore;
        }
        else if (Mathf.Abs(resetAWorstDistance - resetBWorstDistance) > mEpsilon)
        {
            selectA = resetAWorstDistance > resetBWorstDistance;
        }
        else if (Mathf.Abs(cSelfA - cSelfB) > cEpsilon)
        {
            selectA = cSelfA < cSelfB;
        }
        else
        {
            selectA = indexA <= indexB;
        }

        if (selectA)
        {
            result.SelectedUnitIndex = indexA;
            result.OtherUnitIndex = indexB;
            result.SelectedResetDirection = resetDirectionA;
            result.SelectedM = resetAWorstDistance;
            result.SelectedCSelf = cSelfA;
            result.SelectedScore = resetAScore;
        }
        else
        {
            result.SelectedUnitIndex = indexB;
            result.OtherUnitIndex = indexA;
            result.SelectedResetDirection = resetDirectionB;
            result.SelectedM = resetBWorstDistance;
            result.SelectedCSelf = cSelfB;
            result.SelectedScore = resetBScore;
        }

        result.KeepMargin = keepWorstDistance;
        return true;
    }

    private static float ComputeArbitrationScore(
        float selectedDistance,
        float otherDistance,
        float scoreAlpha,
        float scoreBeta,
        float scoreGamma,
        float scoreD0Meters,
        float scoreEpsilonMeters)
    {
        float distanceSum = Mathf.Max(0.0f, selectedDistance) + Mathf.Max(0.0f, otherDistance);
        float minimumDistance = Mathf.Max(0.0f, Mathf.Min(selectedDistance, otherDistance));
        float d0 = Mathf.Max(EPSILON, scoreD0Meters);
        float epsilonMeters = Mathf.Max(EPSILON, scoreEpsilonMeters);
        float shortfall = Mathf.Max(0.0f, d0 - minimumDistance);
        float shortDistancePenalty = Mathf.Max(0.0f, scoreGamma) *
                                     Mathf.Pow(shortfall / (minimumDistance + epsilonMeters), 2.0f);

        return (Mathf.Max(0.0f, scoreAlpha) * distanceSum / (2.0f * d0)) +
               (Mathf.Max(0.0f, scoreBeta) * minimumDistance / d0) -
               shortDistancePenalty;
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

    private static Vector2 ResolveResetDirection(
        RedirectedUnit selectedUnit,
        RedirectedUnit otherUnit,
        ProactiveResetDirectionMode directionMode)
    {
        if (selectedUnit == null || otherUnit == null || selectedUnit.GetRealUser() == null || otherUnit.GetRealUser() == null)
            return Vector2.zero;

        if (directionMode == ProactiveResetDirectionMode.LocalAPF)
        {
            Vector2 apfDirection = UserResetDirectionResolver.ComputeLocalApfDirection(selectedUnit);
            if (apfDirection.sqrMagnitude > EPSILON)
                return apfDirection.normalized;
        }

        if (directionMode == ProactiveResetDirectionMode.MaxPhysicalRemainingDistance)
        {
            Vector2 bestDirection = ResolveMaxPhysicalRemainingDistanceDirection(selectedUnit, otherUnit);
            if (bestDirection.sqrMagnitude > EPSILON)
                return bestDirection.normalized;
        }

        Vector2 awayFromPairUser = selectedUnit.GetRealUser().transform2D.localPosition -
                                   otherUnit.GetRealUser().transform2D.localPosition;
        if (awayFromPairUser.sqrMagnitude > EPSILON)
            return awayFromPairUser.normalized;

        return ResolveKeepDirection(selectedUnit);
    }

    private static Vector2 ResolveMaxPhysicalRemainingDistanceDirection(RedirectedUnit selectedUnit, RedirectedUnit otherUnit)
    {
        if (selectedUnit == null || selectedUnit.GetRealUser() == null)
            return Vector2.zero;

        Vector2 awayFromPairUser = Vector2.zero;
        if (otherUnit != null && otherUnit.GetRealUser() != null)
        {
            awayFromPairUser = selectedUnit.GetRealUser().transform2D.localPosition -
                               otherUnit.GetRealUser().transform2D.localPosition;
        }

        Vector2 keepDirection = ResolveKeepDirection(selectedUnit);
        Vector2 apfDirection = UserResetDirectionResolver.ComputeLocalApfDirection(selectedUnit);
        Vector2[] candidates =
        {
            awayFromPairUser,
            apfDirection,
            keepDirection,
            -keepDirection,
            Vector2.up,
            Vector2.down,
            Vector2.left,
            Vector2.right,
            new Vector2(1.0f, 1.0f),
            new Vector2(1.0f, -1.0f),
            new Vector2(-1.0f, 1.0f),
            new Vector2(-1.0f, -1.0f)
        };

        float bestDistance = float.NegativeInfinity;
        Vector2 bestDirection = Vector2.zero;
        for (int i = 0; i < candidates.Length; i++)
        {
            Vector2 candidate = candidates[i];
            if (candidate.sqrMagnitude <= EPSILON)
                continue;

            candidate.Normalize();
            float remainingDistance = ComputePhysicalRemainingDistance(selectedUnit, candidate);
            if (remainingDistance > bestDistance)
            {
                bestDistance = remainingDistance;
                bestDirection = candidate;
            }
        }

        return bestDirection;
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

    private static float Cross(Vector2 lhs, Vector2 rhs)
    {
        return lhs.x * rhs.y - lhs.y * rhs.x;
    }
}
