using UnityEngine;

public static class ProactiveUserResetSafetyValidator
{
    private const float EPSILON = 0.0001f;
    private const float DEFAULT_USER_RADIUS = 0.5f;
    private const float DEFAULT_SAFE_BUFFER = 0.1f;
    private const float DEFAULT_RESET_ROTATION_SPEED_DEG = 60.0f;
    private const float CLOSING_SPEED_THRESHOLD = 0.05f;

    public static bool IsSafeInPlaceReset(
        RedirectedUnit[] units,
        int selectedUnitIndex,
        Vector2 resetDirection,
        float extraBufferSeconds,
        int sampleCount,
        out int blockingUserIndex)
    {
        blockingUserIndex = -1;

        if (units == null || selectedUnitIndex < 0 || selectedUnitIndex >= units.Length)
            return false;

        RedirectedUnit selectedUnit = units[selectedUnitIndex];
        if (selectedUnit == null || selectedUnit.GetRealUser() == null)
            return false;

        Vector2 selectedPosition = selectedUnit.GetRealUser().transform2D.localPosition;
        float resetDuration = EstimateResetDuration(selectedUnit, resetDirection);
        float horizonSeconds = Mathf.Max(resetDuration + Mathf.Max(0.0f, extraBufferSeconds), Time.fixedDeltaTime);
        int safeSampleCount = Mathf.Max(sampleCount, 2);

        for (int i = 0; i < units.Length; i++)
        {
            if (i == selectedUnitIndex)
                continue;

            RedirectedUnit movingUnit = units[i];
            if (movingUnit == null || movingUnit.GetRealUser() == null)
                continue;

            if (!IsMovingUserBlockedByStationaryResetter(movingUnit, selectedPosition, selectedUnit.GetRealUser(), horizonSeconds, safeSampleCount))
                continue;

            blockingUserIndex = i;
            return false;
        }

        return true;
    }

    private static bool IsMovingUserBlockedByStationaryResetter(
        RedirectedUnit movingUnit,
        Vector2 stationaryPosition,
        Object2D stationaryUser,
        float horizonSeconds,
        int sampleCount)
    {
        Object2D movingUser = movingUnit.GetRealUser();
        Vector2 movingPosition = movingUser.transform2D.localPosition;
        float safeDistance = ResolveSafeDistance(movingUser, stationaryUser);
        Vector2 offsetToStationary = stationaryPosition - movingPosition;

        if (offsetToStationary.magnitude < safeDistance)
            return true;

        Vector2 movement = NormalizeOrZero(movingUnit.GetLastMovementDirection());
        float speed = Mathf.Max(0.0f, movingUnit.GetLastInstantaneousSpeed());
        if (movement.sqrMagnitude <= EPSILON || speed <= EPSILON)
            return false;

        float closingSpeed = Vector2.Dot(movement * speed, offsetToStationary.normalized);
        if (closingSpeed <= CLOSING_SPEED_THRESHOLD)
            return false;

        float heading = Mathf.Atan2(movement.y, movement.x);
        float maxCurvature = ResolveMaxCurvature(movingUnit.GetResetter(), speed);
        float leftMargin = EvaluateMovingMargin(movingPosition, heading, speed, maxCurvature, +1, stationaryPosition, safeDistance, horizonSeconds, sampleCount);
        float rightMargin = EvaluateMovingMargin(movingPosition, heading, speed, maxCurvature, -1, stationaryPosition, safeDistance, horizonSeconds, sampleCount);
        float bestMargin = Mathf.Max(leftMargin, rightMargin);

        return bestMargin < 0.0f;
    }

    private static float EvaluateMovingMargin(
        Vector2 p0,
        float heading,
        float speed,
        float maxCurvature,
        int sigma,
        Vector2 stationaryPosition,
        float safeDistance,
        float horizonSeconds,
        int sampleCount)
    {
        float minMargin = float.PositiveInfinity;
        for (int i = 0; i <= sampleCount; i++)
        {
            float t = horizonSeconds * i / sampleCount;
            Vector2 predicted = PredictPosition(p0, heading, speed, maxCurvature, sigma, t);
            float margin = Vector2.Distance(predicted, stationaryPosition) - safeDistance;
            if (margin < minMargin)
                minMargin = margin;
        }

        return minMargin;
    }

    private static float EstimateResetDuration(RedirectedUnit selectedUnit, Vector2 resetDirection)
    {
        Object2D user = selectedUnit.GetRealUser();
        Vector2 safeResetDirection = resetDirection.sqrMagnitude > EPSILON
            ? resetDirection.normalized
            : selectedUnit.GetLastMovementDirection();

        float angle = Mathf.Abs(Vector2.SignedAngle(user.transform2D.forward, safeResetDirection));
        float rotationSpeed = selectedUnit.GetResetter() != null ? Mathf.Abs(selectedUnit.GetResetter().GetRotationSpeed()) : 0.0f;
        if (rotationSpeed <= EPSILON)
            rotationSpeed = DEFAULT_RESET_ROTATION_SPEED_DEG;

        return angle / rotationSpeed;
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
        float rotationSpeedDeg = resetter != null ? Mathf.Abs(resetter.GetRotationSpeed()) : 0.0f;
        if (rotationSpeedDeg <= EPSILON || speed <= EPSILON)
            return 0.0f;

        float rotationSpeedRad = rotationSpeedDeg * Mathf.Deg2Rad;
        return rotationSpeedRad / speed;
    }

    private static float ResolveSafeDistance(Object2D userA, Object2D userB)
    {
        return ResolveUserRadius(userA) + ResolveUserRadius(userB) + DEFAULT_SAFE_BUFFER;
    }

    private static float ResolveUserRadius(Object2D user)
    {
        if (user is Circle2D circle)
            return circle.GetRadius();

        return DEFAULT_USER_RADIUS;
    }

    private static Vector2 NormalizeOrZero(Vector2 vector)
    {
        if (vector.sqrMagnitude <= EPSILON)
            return Vector2.zero;

        return vector.normalized;
    }
}
