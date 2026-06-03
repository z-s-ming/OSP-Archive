using System.Collections.Generic;

public interface ILiveRdwWalkingStepper
{
    void StepLiveWalking(
        RedirectedUnit unit,
        LiveHmdPoseSample currentSample,
        LiveHmdPoseSample previousSample,
        IReadOnlyList<RedirectedUnit> allUnits);
}
