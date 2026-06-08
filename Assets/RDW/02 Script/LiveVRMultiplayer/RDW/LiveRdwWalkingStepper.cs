using System.Collections.Generic;
using UnityEngine;

public class LiveRdwWalkingStepper : ILiveRdwWalkingStepper
{
    public bool DriveVirtualUserFromHmdDelta { get; set; }

    public LiveRdwWalkingStepper()
    {
        DriveVirtualUserFromHmdDelta = true;
    }

    public LiveVRGainDebugSample StepLiveWalking(
        RedirectedUnit unit,
        LiveHmdPoseSample currentSample,
        LiveHmdPoseSample previousSample,
        IReadOnlyList<RedirectedUnit> allUnits)
    {
        if (unit == null)
            return default(LiveVRGainDebugSample);

        unit.ApplyExternalRealUserPose(currentSample.ExperimentPosition, currentSample.YawDegrees);

        if (DriveVirtualUserFromHmdDelta)
            return ApplyVirtualUserDelta(unit, currentSample, previousSample);

        return BuildDebugSample(unit, currentSample, previousSample, false, LiveRedirectionResult.Undefined, Vector2.zero, 0.0f, 0.0f, 0.0f, false);
    }

    private static LiveVRGainDebugSample ApplyVirtualUserDelta(
        RedirectedUnit unit,
        LiveHmdPoseSample currentSample,
        LiveHmdPoseSample previousSample)
    {
        Object2D virtualUser = unit.GetVirtualUser();
        if (virtualUser == null || virtualUser.transform2D == null)
            return default(LiveVRGainDebugSample);

        ResetPlan activeResetPlan;
        if (unit.TryGetExternalResetPlan(out activeResetPlan))
        {
            if (unit.controller != null)
                unit.controller.ResetCurrentState(virtualUser.transform2D);
            return BuildDebugSample(unit, currentSample, previousSample, false, LiveRedirectionResult.Undefined, Vector2.zero, 0.0f, 0.0f, 0.0f, true);
        }

        Vector2 physicalDelta = currentSample.ExperimentPosition - previousSample.ExperimentPosition;
        float physicalYawDelta = Mathf.DeltaAngle(previousSample.YawDegrees, currentSample.YawDegrees);
        float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        Vector2 physicalVelocity = physicalDelta / deltaTime;
        float physicalYawRate = physicalYawDelta / deltaTime;

        Transform2D virtualTransform = virtualUser.transform2D;
        Vector2 previousVirtualPosition = virtualTransform.localPosition;
        float previousVirtualYaw = virtualTransform.localRotation;
        float physicalToVirtualYaw = previousVirtualYaw - previousSample.YawDegrees;
        Vector2 virtualDelta = Utility.RotateVector2(physicalDelta, physicalToVirtualYaw);
        float virtualYawDelta = physicalYawDelta;
        float injectedYawDelta = 0.0f;

        LiveRedirectionResult redirectionResult;
        bool hasLiveRedirection = TryEvaluateLiveRedirectionAtCandidatePose(
            unit,
            virtualTransform,
            previousVirtualPosition + virtualDelta,
            NormalizeDegrees(previousVirtualYaw + physicalYawDelta),
            previousVirtualPosition,
            previousVirtualYaw,
            physicalVelocity,
            physicalYawRate,
            out redirectionResult);

        if (hasLiveRedirection)
        {
            if (redirectionResult.Type == GainType.Translation)
            {
                if (redirectionResult.UseTranslationScale)
                {
                    virtualDelta *= redirectionResult.TranslationScale;
                }
                else if (virtualDelta.sqrMagnitude > Mathf.Epsilon)
                {
                    virtualDelta = virtualDelta.normalized * Mathf.Max(0.0f, redirectionResult.PrimaryRate * deltaTime);
                }
            }
            else if (redirectionResult.Type == GainType.Rotation)
            {
                virtualYawDelta = redirectionResult.UseInverseRotationGain
                    ? physicalYawDelta / redirectionResult.RotationGainScale
                    : redirectionResult.PrimaryRate * deltaTime;
                injectedYawDelta = virtualYawDelta - physicalYawDelta;
                if (redirectionResult.UseTranslationScale)
                    virtualDelta *= redirectionResult.TranslationScale;
            }
            else if (redirectionResult.Type == GainType.Curvature)
            {
                if (redirectionResult.UseTranslationScale)
                    virtualDelta *= redirectionResult.TranslationScale;
                injectedYawDelta = redirectionResult.PrimaryRate * deltaTime;
                virtualYawDelta += injectedYawDelta;
            }
        }

        if (virtualDelta.sqrMagnitude > Mathf.Epsilon)
            virtualTransform.localPosition += virtualDelta;

        if (Mathf.Abs(virtualYawDelta) > Mathf.Epsilon)
            virtualTransform.localRotation = NormalizeDegrees(previousVirtualYaw + virtualYawDelta);

        if (unit.controller != null)
            unit.controller.ResetCurrentState(virtualTransform);

        return BuildDebugSample(
            unit,
            currentSample,
            previousSample,
            hasLiveRedirection,
            redirectionResult,
            virtualDelta,
            physicalYawDelta,
            virtualYawDelta,
            injectedYawDelta,
            false);
    }

    private static LiveVRGainDebugSample BuildDebugSample(
        RedirectedUnit unit,
        LiveHmdPoseSample currentSample,
        LiveHmdPoseSample previousSample,
        bool hasRedirection,
        LiveRedirectionResult redirectionResult,
        Vector2 virtualDelta,
        float physicalYawDelta,
        float virtualYawDelta,
        float injectedYawDelta,
        bool resetActive)
    {
        Object2D virtualUser = unit != null ? unit.GetVirtualUser() : null;
        Transform2D virtualTransform = virtualUser != null ? virtualUser.transform2D : null;
        Redirector redirector = unit != null ? unit.GetRedirector() : null;
        GainRedirector gainRedirector = redirector as GainRedirector;
        float newVirtualYaw = virtualTransform != null ? virtualTransform.localRotation : 0.0f;
        float previousVirtualYaw = NormalizeDegrees(newVirtualYaw - virtualYawDelta);
        float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);

        return new LiveVRGainDebugSample
        {
            IsValid = true,
            ResetActive = resetActive,
            HasRedirection = hasRedirection,
            GainType = hasRedirection ? redirectionResult.Type : GainType.Undefined,
            RedirectorName = redirector != null ? redirector.GetType().Name : "none",
            PhysicalDeltaMeters = Vector2.Distance(previousSample.ExperimentPosition, currentSample.ExperimentPosition),
            PhysicalYawDeltaDegrees = physicalYawDelta,
            PhysicalYawRateDegreesPerSecond = physicalYawDelta / deltaTime,
            VirtualDeltaMeters = virtualDelta.magnitude,
            VirtualYawDeltaDegrees = virtualYawDelta,
            InjectedYawDeltaDegrees = injectedYawDelta,
            PrimaryRateDegreesPerSecond = hasRedirection ? redirectionResult.PrimaryRate : 0.0f,
            TranslationScale = hasRedirection ? redirectionResult.TranslationScale : 1.0f,
            TranslationGain = gainRedirector != null ? gainRedirector.GetTranslationGain() : 0.0f,
            RotationGain = gainRedirector != null ? gainRedirector.GetRotationGain() : 0.0f,
            CurvatureGain = gainRedirector != null ? gainRedirector.GetCurvatureGain() : 0.0f,
            PreviousVirtualYawDegrees = previousVirtualYaw,
            NewVirtualYawDegrees = newVirtualYaw,
            CurrentPhysicalYawDegrees = currentSample.YawDegrees,
            VirtualPhysicalYawDiffDegrees = Mathf.DeltaAngle(currentSample.YawDegrees, newVirtualYaw)
        };
    }

    private static bool TryEvaluateLiveRedirectionAtCandidatePose(
        RedirectedUnit unit,
        Transform2D virtualTransform,
        Vector2 candidateVirtualPosition,
        float candidateVirtualYaw,
        Vector2 previousVirtualPosition,
        float previousVirtualYaw,
        Vector2 physicalVelocity,
        float physicalYawRate,
        out LiveRedirectionResult result)
    {
        virtualTransform.localPosition = candidateVirtualPosition;
        virtualTransform.localRotation = candidateVirtualYaw;

        try
        {
            return TryEvaluateLiveRedirection(unit, physicalVelocity, physicalYawRate, out result);
        }
        finally
        {
            virtualTransform.localPosition = previousVirtualPosition;
            virtualTransform.localRotation = previousVirtualYaw;
        }
    }

    private static bool TryEvaluateLiveRedirection(
        RedirectedUnit unit,
        Vector2 physicalVelocity,
        float physicalYawRate,
        out LiveRedirectionResult result)
    {
        result = LiveRedirectionResult.Undefined;

        Redirector redirector = unit.GetRedirector();
        if (redirector == null)
            return false;

        ARCRedirector arcRedirector = redirector as ARCRedirector;
        if (arcRedirector != null)
        {
            GainType type;
            List<float> degrees;
            (type, degrees) = arcRedirector.ApplyRedirection_ARC(unit, physicalVelocity, physicalYawRate);
            result = LiveRedirectionResult.FromArcResult(type, degrees, arcRedirector);
            return result.Type != GainType.Undefined;
        }

        ARCRedirector_OSP arcOspRedirector = redirector as ARCRedirector_OSP;
        if (arcOspRedirector != null)
        {
            GainType type;
            List<float> degrees;
            (type, degrees) = arcOspRedirector.ApplyRedirection_ARC_OSP(unit, physicalVelocity, physicalYawRate);
            result = LiveRedirectionResult.FromArcResult(type, degrees, arcOspRedirector);
            return result.Type != GainType.Undefined;
        }

        GainType singleType;
        float singleRate;
        (singleType, singleRate) = redirector.ApplyRedirection(unit, physicalVelocity, physicalYawRate);
        result = LiveRedirectionResult.FromSingleRate(singleType, singleRate, redirector as GainRedirector);
        return result.Type != GainType.Undefined;
    }

    private static float NormalizeDegrees(float degrees)
    {
        degrees %= 360.0f;
        if (degrees > 180.0f)
            degrees -= 360.0f;
        if (degrees < -180.0f)
            degrees += 360.0f;
        return degrees;
    }

    private struct LiveRedirectionResult
    {
        public static readonly LiveRedirectionResult Undefined = new LiveRedirectionResult
        {
            Type = GainType.Undefined,
            PrimaryRate = 0.0f,
            TranslationScale = 1.0f,
            RotationGainScale = 1.0f,
            UseTranslationScale = false,
            UseInverseRotationGain = false
        };

        public GainType Type;
        public float PrimaryRate;
        public float TranslationScale;
        public float RotationGainScale;
        public bool UseTranslationScale;
        public bool UseInverseRotationGain;

        public static LiveRedirectionResult FromSingleRate(GainType type, float primaryRate, GainRedirector gainRedirector)
        {
            return new LiveRedirectionResult
            {
                Type = type,
                PrimaryRate = primaryRate,
                TranslationScale = 1.0f,
                RotationGainScale = ResolveRotationGainScale(type, gainRedirector),
                UseTranslationScale = false,
                UseInverseRotationGain = type == GainType.Rotation && gainRedirector != null
            };
        }

        public static LiveRedirectionResult FromArcResult(GainType type, List<float> degrees, GainRedirector gainRedirector)
        {
            float primaryRate = degrees != null && degrees.Count > 0 ? degrees[0] : 0.0f;
            float translationScale = degrees != null && degrees.Count > 1 ? degrees[1] : 1.0f;

            return new LiveRedirectionResult
            {
                Type = type,
                PrimaryRate = primaryRate,
                TranslationScale = Mathf.Max(0.0f, translationScale),
                RotationGainScale = ResolveRotationGainScale(type, gainRedirector),
                UseTranslationScale = degrees != null && degrees.Count > 1,
                UseInverseRotationGain = type == GainType.Rotation && gainRedirector != null
            };
        }

        private static float ResolveRotationGainScale(GainType type, GainRedirector gainRedirector)
        {
            if (type != GainType.Rotation || gainRedirector == null)
                return 1.0f;

            float gain = Mathf.Abs(gainRedirector.GetRotationGain());
            return gain > 0.001f ? gain : 1.0f;
        }
    }
}
