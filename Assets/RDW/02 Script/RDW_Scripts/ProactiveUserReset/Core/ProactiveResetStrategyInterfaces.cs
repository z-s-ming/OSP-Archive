public interface IProactiveResetTriggerDetector
{
    bool TryCreateTrigger(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        out ProactiveResetTriggerEvent triggerEvent);
}

public interface IProactiveResetCandidateSelector
{
    bool TrySelectCandidate(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        out ProactiveResetCandidate candidate,
        out ProactiveResetRejection rejection);
}

public interface IProactiveResetSafetyPolicy
{
    bool IsCandidateSafe(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        ProactiveResetCandidate candidate,
        out ProactiveResetRejection rejection);
}

public interface IProactiveResetCooldownPolicy
{
    void Clear();
    bool IsCoolingDown(int userId, int currentFrame);
    bool IsPairCoolingDown(int userAId, int userBId, int currentFrame);
    void RegisterExecution(int userId, int currentFrame, float fixedDeltaTime, float cooldownSeconds);
    void RegisterPairExecution(int userAId, int userBId, int currentFrame, float fixedDeltaTime, float cooldownSeconds);
}
