public interface IRdwResetExecutionCoordinator
{
    bool TryBeginReset(RedirectedUnit unit, ResetPlan plan);
}
