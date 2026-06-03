public class SimulationMovementController : IMovementController
{
    public void Step(RDWSimulationManager simulationManager, RedirectedUnit[] units)
    {
        if (units == null)
            return;

        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] != null)
                units[i].Simulate(units);
        }
    }
}
