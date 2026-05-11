using System.Collections.Generic;
using UnityEngine;

public static class ProactiveResetTriggerDetectorFactory
{
    public static IProactiveResetTriggerDetector Create(ProactiveUserResetJudgeMode judgeMode)
    {
        if (judgeMode == ProactiveUserResetJudgeMode.Simple)
            return new SimpleDistanceProactiveResetTriggerDetector();
        if (judgeMode == ProactiveUserResetJudgeMode.TTC)
            return new TtcProactiveResetTriggerDetector();
        if (judgeMode == ProactiveUserResetJudgeMode.VoronoiBoundary)
            return new VoronoiBoundaryProactiveResetTriggerDetector();

        return new RecoverabilityProactiveResetTriggerDetector();
    }
}

public class RecoverabilityProactiveResetTriggerDetector : IProactiveResetTriggerDetector
{
    public bool TryCreateTrigger(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        out ProactiveResetTriggerEvent triggerEvent)
    {
        triggerEvent = default;
        BidirectionalCollisionRecoverabilityAssessment assessment = BidirectionalCollisionRecoverabilityEvaluator.Evaluate(
            pairContext.UnitA,
            pairContext.UnitB,
            pairContext.PredictionHorizonSeconds,
            pairContext.PredictionSampleCount,
            true);

        if (!assessment.RiskConfirmed)
            return false;

        triggerEvent = BuildTriggerEvent(context, pairContext);
        return true;
    }

    internal static ProactiveResetTriggerEvent BuildTriggerEvent(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext)
    {
        return new ProactiveResetTriggerEvent
        {
            UnitA = pairContext.UnitA,
            UnitB = pairContext.UnitB,
            UnitAId = pairContext.UnitAId,
            UnitBId = pairContext.UnitBId,
            JudgeMode = context.Settings.judgeMode.ToString(),
            HorizonSeconds = pairContext.PredictionHorizonSeconds,
            TriggerDistance = pairContext.OffsetAB.magnitude,
            ClosingSpeed = pairContext.ClosingSpeed
        };
    }
}

public class VoronoiBoundaryProactiveResetTriggerDetector : IProactiveResetTriggerDetector
{
    private const float EPSILON = 0.0001f;
    private const float TREND_EPSILON = 0.005f;
    private const float RAY_MAX_DISTANCE = 200.0f;
    private static readonly Dictionary<long, PairTrendState> trendStatesByPair = new Dictionary<long, PairTrendState>();

    private struct PairTrendState
    {
        public bool HasLastDistance;
        public float LastDistance;
        public int DecreaseMask;
        public int SampleCount;
        public int LastFrameIndex;
    }

    private struct BoundaryMetrics
    {
        public float DistanceA;
        public float DistanceB;
        public float PairDistance;
        public float ReverseWallA;
        public float ReverseWallB;
        public int TrendHitCount;
        public int TrendWindowFrames;
    }

    public bool TryCreateTrigger(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        out ProactiveResetTriggerEvent triggerEvent)
    {
        triggerEvent = default;
        if (!TryEvaluateBoundaryMetrics(context, pairContext, out BoundaryMetrics metrics))
            return false;

        ProactiveUserResetSettings settings = context.Settings;
        float distanceThreshold = Mathf.Max(0.0f, settings.voronoiBoundaryDistanceThreshold);
        float reverseWallThreshold = Mathf.Max(0.0f, settings.voronoiBoundaryReverseWallDistanceThreshold);
        int trendWindow = Mathf.Clamp(settings.voronoiBoundaryTrendWindowFrames, 2, 30);
        int requiredTrendHits = Mathf.Clamp(settings.voronoiBoundaryTrendRequiredFrames, 1, trendWindow);

        bool hasBoundaryReverseWallPressure =
            (metrics.DistanceA <= distanceThreshold && metrics.ReverseWallA <= reverseWallThreshold) ||
            (metrics.DistanceB <= distanceThreshold && metrics.ReverseWallB <= reverseWallThreshold);
        bool trendConfirmed = metrics.TrendWindowFrames >= trendWindow &&
                              metrics.TrendHitCount >= requiredTrendHits;
        if (!hasBoundaryReverseWallPressure || !trendConfirmed)
            return false;

        triggerEvent = RecoverabilityProactiveResetTriggerDetector.BuildTriggerEvent(context, pairContext);
        triggerEvent.TriggerDistance = metrics.PairDistance;
        triggerEvent.ConflictBoundaryDistanceA = metrics.DistanceA;
        triggerEvent.ConflictBoundaryDistanceB = metrics.DistanceB;
        triggerEvent.ConflictBoundaryDistancePair = metrics.PairDistance;
        triggerEvent.ReverseWallDistanceA = metrics.ReverseWallA;
        triggerEvent.ReverseWallDistanceB = metrics.ReverseWallB;
        triggerEvent.ConflictBoundaryTrendHitCount = metrics.TrendHitCount;
        triggerEvent.ConflictBoundaryTrendWindowFrames = metrics.TrendWindowFrames;
        return true;
    }

    public static void ResetTemporalState()
    {
        trendStatesByPair.Clear();
    }

    private static bool TryEvaluateBoundaryMetrics(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        out BoundaryMetrics metrics)
    {
        metrics = default;
        if (context == null ||
            context.PartitionResult == null ||
            context.PartitionResult.SeedPoints == null ||
            context.Settings == null ||
            pairContext.UnitA == null ||
            pairContext.UnitB == null ||
            pairContext.UnitA.GetRealUser() == null ||
            pairContext.UnitB.GetRealUser() == null)
        {
            return false;
        }

        if (pairContext.UnitAId < 0 ||
            pairContext.UnitBId < 0 ||
            pairContext.UnitAId >= context.PartitionResult.SeedPoints.Count ||
            pairContext.UnitBId >= context.PartitionResult.SeedPoints.Count)
        {
            return false;
        }

        Vector2 seedA = ToVector2(context.PartitionResult.SeedPoints[pairContext.UnitAId]);
        Vector2 seedB = ToVector2(context.PartitionResult.SeedPoints[pairContext.UnitBId]);
        Vector2 seedOffset = seedB - seedA;
        if (seedOffset.sqrMagnitude <= EPSILON)
            return false;

        Vector2 boundaryNormal = seedOffset.normalized;
        Vector2 boundaryPoint = (seedA + seedB) * 0.5f;
        Vector2 positionA = pairContext.UnitA.GetRealUser().transform2D.localPosition;
        Vector2 positionB = pairContext.UnitB.GetRealUser().transform2D.localPosition;
        float signedA = Vector2.Dot(positionA - boundaryPoint, boundaryNormal);
        float signedB = Vector2.Dot(positionB - boundaryPoint, boundaryNormal);
        float distanceA = Mathf.Abs(signedA);
        float distanceB = Mathf.Abs(signedB);
        float pairDistance = Mathf.Min(distanceA, distanceB);

        Vector2 reverseA = ResolveReverseMovementDirection(pairContext.UnitA, positionA, positionB, boundaryNormal, signedA);
        Vector2 reverseB = ResolveReverseMovementDirection(pairContext.UnitB, positionB, positionA, boundaryNormal, signedB);
        float reverseWallA = ComputePhysicalRemainingDistance(pairContext.UnitA, reverseA);
        float reverseWallB = ComputePhysicalRemainingDistance(pairContext.UnitB, reverseB);

        UpdateTrendState(
            pairContext.UnitAId,
            pairContext.UnitBId,
            pairDistance,
            Mathf.Clamp(context.Settings.voronoiBoundaryTrendWindowFrames, 2, 30),
            context.FrameIndex,
            out int trendHitCount,
            out int trendWindowFrames);

        metrics = new BoundaryMetrics
        {
            DistanceA = distanceA,
            DistanceB = distanceB,
            PairDistance = pairDistance,
            ReverseWallA = reverseWallA,
            ReverseWallB = reverseWallB,
            TrendHitCount = trendHitCount,
            TrendWindowFrames = trendWindowFrames
        };
        return true;
    }

    private static Vector2 ToVector2(Vector2f value)
    {
        return new Vector2(value.x, value.y);
    }

    private static Vector2 ResolveReverseMovementDirection(
        RedirectedUnit unit,
        Vector2 position,
        Vector2 otherPosition,
        Vector2 boundaryNormal,
        float signedDistance)
    {
        if (unit != null)
        {
            Vector2 movement = unit.GetLastMovementDirection();
            if (movement.sqrMagnitude > EPSILON)
                return -movement.normalized;
        }

        if (Mathf.Abs(signedDistance) > EPSILON)
            return signedDistance >= 0.0f ? boundaryNormal : -boundaryNormal;

        Vector2 awayFromOther = position - otherPosition;
        if (awayFromOther.sqrMagnitude > EPSILON)
            return awayFromOther.normalized;

        return boundaryNormal;
    }

    private static void UpdateTrendState(
        int unitAId,
        int unitBId,
        float pairDistance,
        int trendWindow,
        int frameIndex,
        out int trendHitCount,
        out int trendWindowFrames)
    {
        long key = BuildPairKey(unitAId, unitBId);
        PairTrendState state;
        if (!trendStatesByPair.TryGetValue(key, out state))
            state = new PairTrendState();
        else if (state.LastFrameIndex > 0 && frameIndex - state.LastFrameIndex > 1)
            state = new PairTrendState();

        bool decreased = state.HasLastDistance &&
                         pairDistance < state.LastDistance - TREND_EPSILON;
        int windowMask = (1 << trendWindow) - 1;
        state.DecreaseMask = ((state.DecreaseMask << 1) | (decreased ? 1 : 0)) & windowMask;
        state.SampleCount = Mathf.Min(state.SampleCount + 1, trendWindow);
        state.LastDistance = pairDistance;
        state.HasLastDistance = true;
        state.LastFrameIndex = frameIndex;
        trendStatesByPair[key] = state;

        trendHitCount = CountBits(state.DecreaseMask);
        trendWindowFrames = state.SampleCount;
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }

    private static long BuildPairKey(int idA, int idB)
    {
        int min = Mathf.Min(idA, idB);
        int max = Mathf.Max(idA, idB);
        return ((long)min << 32) ^ (uint)max;
    }

    private static float ComputePhysicalRemainingDistance(RedirectedUnit unit, Vector2 direction)
    {
        if (unit == null ||
            unit.GetRealUser() == null ||
            unit.GetRealSpace() == null ||
            direction.sqrMagnitude <= EPSILON)
        {
            return 0.0f;
        }

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

        if (t1 > EPSILON)
            best = t1;
        if (t2 > EPSILON && t2 < best)
            best = t2;
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

public class SimpleDistanceProactiveResetTriggerDetector : IProactiveResetTriggerDetector
{
    private const float DirectionEpsilon = 0.0001f;

    public bool TryCreateTrigger(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        out ProactiveResetTriggerEvent triggerEvent)
    {
        triggerEvent = default;
        ProactiveUserResetSettings settings = context.Settings;
        float distance = pairContext.OffsetAB.magnitude;
        float safeTriggerDistance = Mathf.Max(settings.simpleTriggerDistanceMeters, 0.1f);
        if (distance <= DirectionEpsilon || distance > safeTriggerDistance)
            return false;

        if (pairContext.ClosingSpeed <= Mathf.Max(0.0f, settings.simpleClosingSpeedThreshold))
            return false;

        triggerEvent = RecoverabilityProactiveResetTriggerDetector.BuildTriggerEvent(context, pairContext);
        return true;
    }
}

public class TtcProactiveResetTriggerDetector : IProactiveResetTriggerDetector
{
    public bool TryCreateTrigger(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        out ProactiveResetTriggerEvent triggerEvent)
    {
        triggerEvent = default;
        ProactiveUserResetSettings settings = context.Settings;
        int safeSampleCount = Mathf.Max(pairContext.PredictionSampleCount, 2);
        float safeHorizon = Mathf.Max(pairContext.PredictionHorizonSeconds, 0.01f);
        float safeCollisionDistance = Mathf.Max(settings.ttcCollisionDistanceMeters, 0.1f);
        float safeMinTimeToHit = Mathf.Max(0.0f, settings.ttcMinTimeToHitSeconds);
        Vector2 initialA = pairContext.UnitA.GetRealUser().transform2D.localPosition;
        Vector2 initialB = pairContext.UnitB.GetRealUser().transform2D.localPosition;

        for (int i = 0; i <= safeSampleCount; i++)
        {
            float t = safeHorizon * i / safeSampleCount;
            Vector2 pA = initialA + pairContext.VelocityA * t;
            Vector2 pB = initialB + pairContext.VelocityB * t;
            float distance = Vector2.Distance(pA, pB);
            if (distance < safeCollisionDistance)
            {
                if (t < safeMinTimeToHit)
                    return false;

                triggerEvent = RecoverabilityProactiveResetTriggerDetector.BuildTriggerEvent(context, pairContext);
                triggerEvent.HorizonSeconds = safeHorizon;
                return true;
            }
        }

        return false;
    }
}
