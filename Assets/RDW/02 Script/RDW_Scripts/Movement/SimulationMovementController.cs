public class SimulationMovementController : IMovementController
{
    public void Step(RDWSimulationManager simulationManager, RedirectedUnit[] units)
    {
        if (units == null)
            return;

        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] == null)
                continue;

            LiveVRNetworkManager liveVRNetworkManager = LiveVRNetworkManager.Instance;
            if (liveVRNetworkManager != null && liveVRNetworkManager.IsUserRunComplete(i))
                continue;

                units[i].Simulate(units);
        }
    }
}
