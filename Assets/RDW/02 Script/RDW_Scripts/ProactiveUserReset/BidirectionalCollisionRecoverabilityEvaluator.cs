using System.Collections.Generic;
using UnityEngine;

public static class BidirectionalCollisionRecoverabilityEvaluator
{
    private const float EPSILON = 0.0001f;
    private const float DEFAULT_SAFE_BUFFER = 0.1f;
    private const float CLOSING_SPEED_THRESHOLD = 0.05f;
    private const float APPROACHING_DISTANCE_MARGIN = 0.01f;
    private const int CONTINUOUS_FRAMES_THRESHOLD = 3;

    private struct PairTemporalState
    {
        public int IrrecoverableConsecutiveFrames;
        public int PersistentConsecutiveFrames;
    }

    private struct PairEvaluation
    {
        public float Margin;
        public float WorstTime;
        public int SigmaA;
        public int SigmaB;
    }

    private static readonly Dictionary<long, PairTemporalState> temporalStates = new Dictionary<long, PairTemporalState>();

    public static BidirectionalCollisionRecoverabilityAssessment Evaluate(
        RedirectedUnit unitA,
        RedirectedUnit unitB,
        float horizonSeconds,
        int sampleCount,
        bool isAdjacentCellCandidate = false)
    {
        Object2D userA = unitA.GetRealUser();
        Object2D userB = unitB.GetRealUser();
        int unitAId = unitA.GetID();
        int unitBId = unitB.GetID();
        long pairKey = BuildPairKey(unitAId, unitBId);

        BidirectionalCollisionRecoverabilityAssessment assessment = new BidirectionalCollisionRecoverabilityAssessment
        {
            PositionA = userA.transform2D.localPosition,
            PositionB = userB.transform2D.localPosition,
            IsAdjacentCellCandidate = isAdjacentCellCandidate
        };

        float speedA = Mathf.Max(unitA.GetLastInstantaneousSpeed(), 0.0f);
        float speedB = Mathf.Max(unitB.GetLastInstantaneousSpeed(), 0.0f);
        float kappaA = ResolveMaxCurvature(unitA.GetResetter(), speedA);
        float kappaB = ResolveMaxCurvature(unitB.GetResetter(), speedB);
        float headingA = Mathf.Atan2(userA.transform2D.forward.y, userA.transform2D.forward.x);
        float headingB = Mathf.Atan2(userB.transform2D.forward.y, userB.transform2D.forward.x);
        float safeDistance = ResolveSafeDistance(userA, userB);
        int safeSampleCount = Mathf.Max(sampleCount, 2);
        float safeHorizon = Mathf.Max(horizonSeconds, 0.01f);

        PairEvaluation ll = EvaluatePair(userA.transform2D.localPosition, headingA, speedA, kappaA, +1,
                                         userB.transform2D.localPosition, headingB, speedB, kappaB, +1,
                                         safeDistance, safeHorizon, safeSampleCount);
        PairEvaluation lr = EvaluatePair(userA.transform2D.localPosition, headingA, speedA, kappaA, +1,
                                         userB.transform2D.localPosition, headingB, speedB, kappaB, -1,
                                         safeDistance, safeHorizon, safeSampleCount);
        PairEvaluation rl = EvaluatePair(userA.transform2D.localPosition, headingA, speedA, kappaA, -1,
                                         userB.transform2D.localPosition, headingB, speedB, kappaB, +1,
                                         safeDistance, safeHorizon, safeSampleCount);
        PairEvaluation rr = EvaluatePair(userA.transform2D.localPosition, headingA, speedA, kappaA, -1,
                                         userB.transform2D.localPosition, headingB, speedB, kappaB, -1,
                                         safeDistance, safeHorizon, safeSampleCount);

        PairEvaluation best = ll;
        if (lr.Margin > best.Margin) best = lr;
        if (rl.Margin > best.Margin) best = rl;
        if (rr.Margin > best.Margin) best = rr;

        Vector2 movementA = NormalizeOrZero(unitA.GetLastMovementDirection());
        Vector2 movementB = NormalizeOrZero(unitB.GetLastMovementDirection());
        Vector2 velocityA = movementA * speedA;
        Vector2 velocityB = movementB * speedB;
        Vector2 offsetAB = userB.transform2D.localPosition - userA.transform2D.localPosition;
        float currentDistance = offsetAB.magnitude;
        float predictedMinDistance = best.Margin + safeDistance;
        float closingSpeedNow = ResolveClosingSpeed(offsetAB, velocityA, velocityB);
        bool approachingNow = closingSpeedNow > CLOSING_SPEED_THRESHOLD &&
                              predictedMinDistance < currentDistance - APPROACHING_DISTANCE_MARGIN;
        bool irrecoverableNow = best.Margin < 0.0f;

        PairTemporalState temporalState = GetTemporalState(pairKey);
        if (irrecoverableNow)
            temporalState.IrrecoverableConsecutiveFrames++;
        else
            temporalState.IrrecoverableConsecutiveFrames = 0;

        if (irrecoverableNow && approachingNow)
            temporalState.PersistentConsecutiveFrames++;
        else
            temporalState.PersistentConsecutiveFrames = 0;

        temporalStates[pairKey] = temporalState;

        assessment.MarginLL = ll.Margin;
        assessment.MarginLR = lr.Margin;
        assessment.MarginRL = rl.Margin;
        assessment.MarginRR = rr.Margin;
        assessment.MaxSeparationMargin = best.Margin;
        assessment.BestSigmaA = best.SigmaA;
        assessment.BestSigmaB = best.SigmaB;
        assessment.WorstTimeOnBestPair = best.WorstTime;
        assessment.Recoverable = best.Margin >= 0.0f;
        assessment.CurrentDistance = currentDistance;
        assessment.PredictedMinDistance = predictedMinDistance;
        assessment.ClosingSpeedNow = closingSpeedNow;
        assessment.IsApproachingCandidate = approachingNow;
        assessment.IrrecoverableStreak = temporalState.IrrecoverableConsecutiveFrames;
        assessment.PersistentStreak = temporalState.PersistentConsecutiveFrames;
        assessment.IsIrrecoverable = temporalState.IrrecoverableConsecutiveFrames >= CONTINUOUS_FRAMES_THRESHOLD;
        assessment.IsApproaching = approachingNow;
        assessment.IsPersistent = temporalState.PersistentConsecutiveFrames >= CONTINUOUS_FRAMES_THRESHOLD;
        assessment.RiskConfirmed = assessment.IsAdjacentCellCandidate &&
                                   assessment.IsIrrecoverable &&
                                   assessment.IsApproaching &&
                                   assessment.IsPersistent;

        if (assessment.RiskConfirmed)
        {
            BidirectionalCollisionDebugVisualizer.TryStartTrace(unitA, unitB, assessment);
        }

        return assessment;
    }

    public static void ResetTemporalState()
    {
        temporalStates.Clear();
    }

    public static void SetDebugVisualizationEnabled(bool enabled)
    {
        BidirectionalCollisionDebugVisualizer.SetEnabled(enabled);
    }

    private static PairEvaluation EvaluatePair(
        Vector2 pA0,
        float headingA,
        float speedA,
        float kappaA,
        int sigmaA,
        Vector2 pB0,
        float headingB,
        float speedB,
        float kappaB,
        int sigmaB,
        float safeDistance,
        float horizonSeconds,
        int sampleCount)
    {
        float minMargin = float.PositiveInfinity;
        float minMarginTime = 0.0f;

        for (int i = 0; i <= sampleCount; i++)
        {
            float t = horizonSeconds * i / sampleCount;
            Vector2 pA = PredictPosition(pA0, headingA, speedA, kappaA, sigmaA, t);
            Vector2 pB = PredictPosition(pB0, headingB, speedB, kappaB, sigmaB, t);
            float margin = Vector2.Distance(pA, pB) - safeDistance;
            if (margin < minMargin)
            {
                minMargin = margin;
                minMarginTime = t;
            }
        }

        return new PairEvaluation
        {
            Margin = minMargin,
            WorstTime = minMarginTime,
            SigmaA = sigmaA,
            SigmaB = sigmaB
        };
    }

    private static Vector2 PredictPosition(
        Vector2 p0,
        float heading,
        float speed,
        float maxCurvature,
        int sigma,
        float t)
    {
        if (maxCurvature <= EPSILON || speed <= EPSILON)
        {
            Vector2 forward = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
            return p0 + forward * speed * t;
        }

        float radius = 1.0f / maxCurvature;
        float signedAngularSpeed = sigma * speed / radius;
        float futureHeading = heading + signedAngularSpeed * t;

        float x = p0.x + sigma * radius * (Mathf.Sin(futureHeading) - Mathf.Sin(heading));
        float y = p0.y + sigma * radius * (-Mathf.Cos(futureHeading) + Mathf.Cos(heading));
        return new Vector2(x, y);
    }

    private static float ResolveMaxCurvature(Resetter resetter, float speed)
    {
        float rotationSpeedDeg = Mathf.Abs(resetter.GetRotationSpeed());
        if (rotationSpeedDeg <= EPSILON || speed <= EPSILON)
            return 0.0f;

        float rotationSpeedRad = rotationSpeedDeg * Mathf.Deg2Rad;
        return rotationSpeedRad / speed;
    }

    private static float ResolveSafeDistance(Object2D userA, Object2D userB)
    {
        float radiusA = ResolveUserRadius(userA);
        float radiusB = ResolveUserRadius(userB);
        return radiusA + radiusB + DEFAULT_SAFE_BUFFER;
    }

    private static float ResolveUserRadius(Object2D user)
    {
        if (user is Circle2D)
            return ((Circle2D)user).GetRadius();

        return 0.5f;
    }

    private static long BuildPairKey(int unitAId, int unitBId)
    {
        int minId = Mathf.Min(unitAId, unitBId);
        int maxId = Mathf.Max(unitAId, unitBId);
        return ((long)(uint)minId << 32) | (uint)maxId;
    }

    private static PairTemporalState GetTemporalState(long pairKey)
    {
        if (temporalStates.TryGetValue(pairKey, out PairTemporalState state))
            return state;

        return new PairTemporalState();
    }

    private static float ResolveClosingSpeed(Vector2 offsetAB, Vector2 velocityA, Vector2 velocityB)
    {
        if (offsetAB.sqrMagnitude <= EPSILON)
            return 0.0f;

        Vector2 towardB = offsetAB.normalized;
        Vector2 relativeVelocity = velocityB - velocityA;
        return -Vector2.Dot(relativeVelocity, towardB);
    }

    private static Vector2 NormalizeOrZero(Vector2 vector)
    {
        if (vector.sqrMagnitude <= EPSILON)
            return Vector2.zero;

        return vector.normalized;
    }
}
