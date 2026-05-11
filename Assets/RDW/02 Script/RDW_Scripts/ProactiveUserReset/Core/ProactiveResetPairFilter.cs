using UnityEngine;

public class ProactiveResetPairFilter
{
    private const float DirectionEpsilon = 0.0001f;
    private const float ClosingSpeedThreshold = 0.05f;

    public bool TryBuildPairContext(
        ProactiveResetFrameContext context,
        int unitAId,
        int unitBId,
        out ProactiveResetPairContext pairContext)
    {
        pairContext = default;
        if (context == null || context.Units == null)
            return false;
        if (unitBId <= unitAId || unitBId < 0 || unitBId >= context.Units.Length)
            return false;

        RedirectedUnit unitA = context.Units[unitAId];
        RedirectedUnit unitB = context.Units[unitBId];
        if (unitA == null || unitB == null || unitA.GetRealUser() == null || unitB.GetRealUser() == null)
            return false;

        Vector2 movementA = unitA.GetLastMovementDirection();
        Vector2 movementB = unitB.GetLastMovementDirection();
        if (movementA.sqrMagnitude <= DirectionEpsilon ||
            movementB.sqrMagnitude <= DirectionEpsilon)
        {
            return false;
        }

        float speedA = Mathf.Max(0.0f, unitA.GetLastInstantaneousSpeed());
        float speedB = Mathf.Max(0.0f, unitB.GetLastInstantaneousSpeed());
        Vector2 velocityA = NormalizeOrZero(movementA) * speedA;
        Vector2 velocityB = NormalizeOrZero(movementB) * speedB;

        Vector2 offsetAB = unitB.GetRealUser().transform2D.localPosition - unitA.GetRealUser().transform2D.localPosition;
        float closingSpeed = ResolveClosingSpeedFromKinematics(offsetAB, velocityA, velocityB);

        if (Vector2.Dot(velocityA, velocityB) >= 0.0f)
            return false;

        if (closingSpeed <= ClosingSpeedThreshold)
            return false;

        if (IsPotentialSingleSideCollision(offsetAB, velocityA, velocityB))
            return false;

        ProactiveUserResetSettings settings = context.Settings;
        pairContext = new ProactiveResetPairContext
        {
            UnitA = unitA,
            UnitB = unitB,
            UnitAId = unitAId,
            UnitBId = unitBId,
            OffsetAB = offsetAB,
            VelocityA = velocityA,
            VelocityB = velocityB,
            ClosingSpeed = closingSpeed,
            PredictionHorizonSeconds = Mathf.Max(settings.predictionHorizonSeconds, 0.1f),
            PredictionSampleCount = Mathf.Max(settings.predictionSampleCount, 2)
        };
        return true;
    }

    private static Vector2 NormalizeOrZero(Vector2 vector)
    {
        if (vector.sqrMagnitude <= DirectionEpsilon)
            return Vector2.zero;

        return vector.normalized;
    }

    private static float ResolveClosingSpeedFromKinematics(Vector2 offsetAB, Vector2 velocityA, Vector2 velocityB)
    {
        if (offsetAB.sqrMagnitude <= DirectionEpsilon)
            return 0.0f;

        Vector2 towardB = offsetAB.normalized;
        Vector2 relativeVelocity = velocityB - velocityA;
        return -Vector2.Dot(relativeVelocity, towardB);
    }

    private static bool IsPotentialSingleSideCollision(Vector2 offsetAB, Vector2 velocityA, Vector2 velocityB)
    {
        if (offsetAB.sqrMagnitude <= DirectionEpsilon)
            return false;

        Vector2 towardB = offsetAB.normalized;
        Vector2 towardA = -towardB;
        bool aMovingTowardB = Vector2.Dot(velocityA, towardB) > ClosingSpeedThreshold;
        bool bMovingTowardA = Vector2.Dot(velocityB, towardA) > ClosingSpeedThreshold;
        return aMovingTowardB ^ bMovingTowardA;
    }
}
