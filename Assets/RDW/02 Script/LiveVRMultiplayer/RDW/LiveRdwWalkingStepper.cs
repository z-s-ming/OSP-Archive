using System.Collections.Generic;
using UnityEngine;

public class LiveRdwWalkingStepper : ILiveRdwWalkingStepper
{
    public bool DriveVirtualUserFromHmdDelta { get; set; }

    public LiveRdwWalkingStepper()
    {
        DriveVirtualUserFromHmdDelta = true;
    }

    public void StepLiveWalking(
        RedirectedUnit unit,
        LiveHmdPoseSample currentSample,
        LiveHmdPoseSample previousSample,
        IReadOnlyList<RedirectedUnit> allUnits)
    {
        if (unit == null)
            return;

        if (DriveVirtualUserFromHmdDelta)
            ApplyVirtualUserDelta(unit, currentSample, previousSample);

        unit.ApplyExternalRealUserPose(currentSample.ExperimentPosition, currentSample.YawDegrees);
    }

    private static void ApplyVirtualUserDelta(
        RedirectedUnit unit,
        LiveHmdPoseSample currentSample,
        LiveHmdPoseSample previousSample)
    {
        Object2D virtualUser = unit.GetVirtualUser();
        if (virtualUser == null || virtualUser.transform2D == null)
            return;

        ResetPlan activeResetPlan;
        if (unit.TryGetExternalResetPlan(out activeResetPlan))
        {
            if (unit.controller != null)
                unit.controller.ResetCurrentState(virtualUser.transform2D);
            return;
        }

        Vector2 physicalDelta = currentSample.ExperimentPosition - previousSample.ExperimentPosition;
        float physicalYawDelta = Mathf.DeltaAngle(previousSample.YawDegrees, currentSample.YawDegrees);

        Transform2D virtualTransform = virtualUser.transform2D;
        float previousVirtualYaw = virtualTransform.localRotation;
        float physicalToVirtualYaw = previousVirtualYaw - previousSample.YawDegrees;
        Vector2 virtualDelta = Utility.RotateVector2(physicalDelta, physicalToVirtualYaw);

        if (virtualDelta.sqrMagnitude > Mathf.Epsilon)
            virtualTransform.localPosition += virtualDelta;

        if (Mathf.Abs(physicalYawDelta) > Mathf.Epsilon)
            virtualTransform.localRotation = NormalizeDegrees(previousVirtualYaw + physicalYawDelta);

        if (unit.controller != null)
            unit.controller.ResetCurrentState(virtualTransform);
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
}
