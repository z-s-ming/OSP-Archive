using UnityEngine;

public class LocalSafeCurvatureRedirector : SteerToTargetRedirector
{
    private const float MOVEMENT_THRESHOLD = 0.2f;
    private const float ROTATION_THRESHOLD = 1.5f;
    private const float MAXIMUM_LINEAR_MOVEMENT_ROTATION_RATE = 15f;
    private const float MAXIMUM_ANGULAR_ROTATION_RATE = 30f;
    private const float CURVATURE_SMOOTHING_FACTOR = 0.125f;
    private const float ROTATION_GAIN_SMOOTHING_FACTOR = 0.125f;
    private const float NEUTRAL_ROTATION_GAIN = 1f;
    private const float STABLE_TRANSLATION_GAIN = 0f;
    private const float LOCAL_COORDINATE_EPSILON = 1e-4f;
    private const float FALLBACK_TARGET_DISTANCE = 2f;

    private Vector2? externalSafeTarget = null;
    private Vector2? externalSteeringDirection = null;
    private bool boundaryEscapeMaxCurvatureEnabled = false;
    private Vector2 boundaryEscapeDirection = Vector2.zero;
    private float previousCurvatureGain = 0f;
    private float previousRotationGain = NEUTRAL_ROTATION_GAIN;

    public override void PickSteeringTarget()
    {
        if (externalSafeTarget.HasValue)
        {
            targetPosition = externalSafeTarget.Value;
            return;
        }

        Vector2 forward = userDirection.sqrMagnitude > LOCAL_COORDINATE_EPSILON ? userDirection.normalized : Vector2.up;
        targetPosition = userPosition + forward * FALLBACK_TARGET_DISTANCE;
    }

    public void SetExternalSafeTarget(Vector2 target)
    {
        externalSafeTarget = target;
    }

    public void ClearExternalSafeTarget()
    {
        externalSafeTarget = null;
    }

    public void SetExternalSteeringDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= LOCAL_COORDINATE_EPSILON)
        {
            externalSteeringDirection = null;
            return;
        }

        externalSteeringDirection = direction.normalized;
    }

    public void ClearExternalSteeringDirection()
    {
        externalSteeringDirection = null;
    }

    public void SetBoundaryEscapeMaxCurvature(Vector2 awayFromBoundaryDirection)
    {
        boundaryEscapeMaxCurvatureEnabled = awayFromBoundaryDirection.sqrMagnitude > LOCAL_COORDINATE_EPSILON;
        boundaryEscapeDirection = boundaryEscapeMaxCurvatureEnabled
            ? awayFromBoundaryDirection.normalized
            : Vector2.zero;
    }

    public void ClearBoundaryEscapeMaxCurvature()
    {
        boundaryEscapeMaxCurvatureEnabled = false;
        boundaryEscapeDirection = Vector2.zero;
    }

    public override (GainType, float) ApplyRedirection(RedirectedUnit unit, Vector2 deltaPosition, float deltaRotation)
    {
        if (deltaPosition == Vector2.zero && Mathf.Abs(deltaRotation) <= 0f)
            return (GainType.Undefined, 0f);

        Transform2D realUserTransform = unit.GetRealUser().transform2D;
        userPosition = realUserTransform.localPosition;
        userDirection = realUserTransform.forward;
        PickSteeringTarget();

        Vector2 userToTarget = targetPosition - userPosition;
        Vector2 steeringDirection = ResolveSteeringDirection(userDirection, userToTarget);
        float signedAngleToTarget = Vector2.SignedAngle(userDirection, steeringDirection);

        translationGain = STABLE_TRANSLATION_GAIN;
        if (boundaryEscapeMaxCurvatureEnabled)
            curvatureGain = ComputeBoundaryEscapeMaxCurvatureGain(userDirection, boundaryEscapeDirection);
        else if (externalSteeringDirection.HasValue)
            curvatureGain = ComputeCurvatureGainFromSteeringDirection(userDirection, externalSteeringDirection.Value);
        else
            curvatureGain = ComputeCurvatureGainFromLocalTarget(userToTarget, userDirection);
        rotationGain = ComputeRotationGainFromAngle(signedAngleToTarget, deltaRotation);

        float curvatureMagnitude = 0f;
        float rotationMagnitude = 0f;

        if (deltaPosition.magnitude > MOVEMENT_THRESHOLD)
        {
            curvatureMagnitude = Mathf.Clamp(
                Mathf.Rad2Deg * curvatureGain * deltaPosition.magnitude,
                -MAXIMUM_LINEAR_MOVEMENT_ROTATION_RATE,
                MAXIMUM_LINEAR_MOVEMENT_ROTATION_RATE);
        }

        if (Mathf.Abs(deltaRotation) >= ROTATION_THRESHOLD)
        {
            rotationMagnitude = Mathf.Clamp(
                rotationGain * deltaRotation,
                -MAXIMUM_ANGULAR_ROTATION_RATE,
                MAXIMUM_ANGULAR_ROTATION_RATE);
        }

        if (Mathf.Abs(curvatureMagnitude) >= Mathf.Abs(rotationMagnitude) && Mathf.Abs(curvatureMagnitude) > 0f)
        {
            // Keep the same sign convention used by existing steer-to-target redirectors.
            return (GainType.Curvature, -curvatureMagnitude);
        }

        if (Mathf.Abs(rotationMagnitude) > 0f)
            return (GainType.Rotation, rotationMagnitude);

        if (deltaPosition.magnitude > 0.01f)
            return (GainType.Translation, deltaPosition.magnitude);

        return (GainType.Undefined, 0f);
    }

    private Vector2 ResolveSteeringDirection(Vector2 forward, Vector2 userToTarget)
    {
        if (externalSteeringDirection.HasValue && externalSteeringDirection.Value.sqrMagnitude > LOCAL_COORDINATE_EPSILON)
            return externalSteeringDirection.Value.normalized;

        if (userToTarget.sqrMagnitude > LOCAL_COORDINATE_EPSILON)
            return userToTarget.normalized;

        return forward.sqrMagnitude > LOCAL_COORDINATE_EPSILON ? forward.normalized : Vector2.up;
    }

    private float ComputeCurvatureGainFromSteeringDirection(Vector2 forward, Vector2 steeringDir)
    {
        Vector2 normalizedForward = forward.sqrMagnitude > LOCAL_COORDINATE_EPSILON ? forward.normalized : Vector2.up;
        Vector2 normalizedSteering = steeringDir.sqrMagnitude > LOCAL_COORDINATE_EPSILON ? steeringDir.normalized : normalizedForward;
        Vector2 left = new Vector2(-normalizedForward.y, normalizedForward.x);

        float side = Vector2.Dot(normalizedSteering, left); // [-1,1], left positive
        float magnitude = Mathf.Abs(side) * HODGSON_MAX_CURVATURE_GAIN;
        float gain = -Mathf.Sign(side) * magnitude; // keep existing sign convention
        previousCurvatureGain = gain;
        return Mathf.Clamp(gain, HODGSON_MIN_CURVATURE_GAIN, HODGSON_MAX_CURVATURE_GAIN);
    }

    private float ComputeBoundaryEscapeMaxCurvatureGain(Vector2 forward, Vector2 awayDirection)
    {
        Vector2 normalizedForward = forward.sqrMagnitude > LOCAL_COORDINATE_EPSILON ? forward.normalized : Vector2.up;
        if (awayDirection.sqrMagnitude <= LOCAL_COORDINATE_EPSILON)
            return 0f;

        Vector2 normalizedAway = awayDirection.normalized;
        Vector2 left = new Vector2(-normalizedForward.y, normalizedForward.x);

        float side = Vector2.Dot(normalizedAway, left);
        if (Mathf.Abs(side) < 1e-3f)
        {
            side = Mathf.Sign(Vector2.SignedAngle(normalizedForward, normalizedAway));
            if (Mathf.Abs(side) < 1e-3f)
                side = 1f;
        }

        // Keep sign convention aligned with ComputeCurvatureGainFromLocalTarget:
        // target/escape direction on left => negative curvature gain.
        float forcedGain = -Mathf.Sign(side) * HODGSON_MAX_CURVATURE_GAIN;
        previousCurvatureGain = forcedGain;
        return Mathf.Clamp(forcedGain, HODGSON_MIN_CURVATURE_GAIN, HODGSON_MAX_CURVATURE_GAIN);
    }

    private float ComputeCurvatureGainFromLocalTarget(Vector2 userToTarget, Vector2 forward)
    {
        Vector2 normalizedForward = forward.sqrMagnitude > LOCAL_COORDINATE_EPSILON ? forward.normalized : Vector2.up;
        Vector2 left = new Vector2(-normalizedForward.y, normalizedForward.x);

        float localX = Vector2.Dot(userToTarget, normalizedForward);
        float localY = Vector2.Dot(userToTarget, left);
        float denominator = localX * localX + localY * localY;

        if (denominator < LOCAL_COORDINATE_EPSILON)
        {
            previousCurvatureGain = 0f;
            return 0f;
        }

        float requiredCurvature = 2f * localY / denominator;
        float desiredCurvatureGain = -requiredCurvature;
        float kappaMax = HODGSON_MAX_CURVATURE_GAIN;

        if (Mathf.Abs(desiredCurvatureGain) <= kappaMax)
        {
            previousCurvatureGain = Mathf.Lerp(
                previousCurvatureGain,
                desiredCurvatureGain,
                CURVATURE_SMOOTHING_FACTOR);
        }
        else
        {
            previousCurvatureGain = Mathf.Sign(desiredCurvatureGain) * kappaMax;
        }

        return Mathf.Clamp(previousCurvatureGain, HODGSON_MIN_CURVATURE_GAIN, HODGSON_MAX_CURVATURE_GAIN);
    }

    private float ComputeRotationGainFromAngle(float signedAngleToTarget, float deltaRotation)
    {
        if (Mathf.Abs(deltaRotation) < ROTATION_THRESHOLD)
        {
            previousRotationGain = Mathf.Lerp(
                previousRotationGain,
                NEUTRAL_ROTATION_GAIN,
                ROTATION_GAIN_SMOOTHING_FACTOR);
            return previousRotationGain;
        }

        float normalizedAngle = Mathf.Clamp01(Mathf.Abs(signedAngleToTarget) / 180f);
        bool rotatingTowardTarget = Mathf.Sign(signedAngleToTarget) == Mathf.Sign(deltaRotation);
        float desiredRotationGain = rotatingTowardTarget
            ? Mathf.Lerp(NEUTRAL_ROTATION_GAIN, MAX_ROTATION_GAIN, normalizedAngle)
            : Mathf.Lerp(NEUTRAL_ROTATION_GAIN, MIN_ROTATION_GAIN, normalizedAngle);

        previousRotationGain = Mathf.Lerp(
            previousRotationGain,
            desiredRotationGain,
            ROTATION_GAIN_SMOOTHING_FACTOR);
        return previousRotationGain;
    }
}
