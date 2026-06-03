using UnityEngine;

public class NoOpMovementController : IMovementController
{
    private bool hasWarned;

    public void Step(RDWSimulationManager simulationManager, RedirectedUnit[] units)
    {
        if (hasWarned)
            return;

        hasWarned = true;
        Debug.LogWarning("[RDW] LiveUser profile is active, but no IMovementController component is assigned. AutoPilot simulation movement is disabled for this frame path.");
    }
}
