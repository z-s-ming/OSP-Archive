using UnityEngine;

public class ProactiveResetPairFilter
{
    private const float DirectionEpsilon = 0.0001f;
    private const float ClosingSpeedThreshold = 0.05f;
    private const float ResetStatusFallbackSpeedMetersPerSecond = 1.0f;

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

        Vector2 offsetAB = unitB.GetRealUser().transform2D.localPosition - unitA.GetRealUser().transform2D.localPosition;
        bool unitAIsResetting = IsResetStatus(unitA.GetStatus());
        bool unitBIsResetting = IsResetStatus(unitB.GetStatus());

        Vector2 movementA = ResolveMovementDirection(unitA, offsetAB, unitAIsResetting, true);
        Vector2 movementB = ResolveMovementDirection(unitB, offsetAB, unitBIsResetting, false);
        if (movementA.sqrMagnitude <= DirectionEpsilon ||
            movementB.sqrMagnitude <= DirectionEpsilon)
        {
            return false;
        }

        float speedA = ResolveSpeed(unitA, unitAIsResetting);
        float speedB = ResolveSpeed(unitB, unitBIsResetting);
        Vector2 velocityA = NormalizeOrZero(movementA) * speedA;
        Vector2 velocityB = NormalizeOrZero(movementB) * speedB;

        float closingSpeed = ResolveClosingSpeedFromKinematics(offsetAB, velocityA, velocityB);

        bool resetInvolved = unitAIsResetting || unitBIsResetting;
        if (!resetInvolved && Vector2.Dot(velocityA, velocityB) >= 0.0f)
            return false;

        if (resetInvolved)
            closingSpeed = Mathf.Max(closingSpeed, ResetStatusFallbackSpeedMetersPerSecond);

        if (!resetInvolved && closingSpeed <= ClosingSpeedThreshold)
            return false;

        if (!resetInvolved && IsPotentialSingleSideCollision(offsetAB, velocityA, velocityB))
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

    private static bool IsResetStatus(string status)
    {
        return string.Equals(status, "WALL_RESET", System.StringComparison.Ordinal) ||
               string.Equals(status, "USER_RESET", System.StringComparison.Ordinal) ||
               string.Equals(status, "SHUTTER_RESET", System.StringComparison.Ordinal) ||
               string.Equals(status, "PROACTIVE_USER_RESET", System.StringComparison.Ordinal);
    }

    private static Vector2 ResolveMovementDirection(
        RedirectedUnit unit,
        Vector2 offsetAB,
        bool isResetting,
        bool isUnitA)
    {
        if (isResetting && offsetAB.sqrMagnitude > DirectionEpsilon)
            return isUnitA ? offsetAB.normalized : -offsetAB.normalized;

        return unit.GetLastMovementDirection();
    }

    private static float ResolveSpeed(RedirectedUnit unit, bool isResetting)
    {
        float speed = Mathf.Max(0.0f, unit.GetLastInstantaneousSpeed());
        if (isResetting && speed <= ClosingSpeedThreshold)
            return ResetStatusFallbackSpeedMetersPerSecond;

        return speed;
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
