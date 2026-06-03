public static class RdwResetExecutionRegistry
{
    public static IRdwResetExecutionCoordinator Coordinator { get; private set; }

    public static void Register(IRdwResetExecutionCoordinator coordinator)
    {
        Coordinator = coordinator;
    }

    public static void Unregister(IRdwResetExecutionCoordinator coordinator)
    {
        if (Coordinator == coordinator)
            Coordinator = null;
    }

    public static bool TryBeginReset(RedirectedUnit unit, ResetPlan plan)
    {
        return Coordinator != null && Coordinator.TryBeginReset(unit, plan);
    }
}
