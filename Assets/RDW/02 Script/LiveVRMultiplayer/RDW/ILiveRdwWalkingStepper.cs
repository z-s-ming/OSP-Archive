using System.Collections.Generic;

public interface ILiveRdwWalkingStepper
{
    LiveVRGainDebugSample StepLiveWalking(
        RedirectedUnit unit,
        LiveHmdPoseSample currentSample,
        LiveHmdPoseSample previousSample,
        IReadOnlyList<RedirectedUnit> allUnits);
}
