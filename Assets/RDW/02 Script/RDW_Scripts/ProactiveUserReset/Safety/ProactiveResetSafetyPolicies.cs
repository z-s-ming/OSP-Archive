using UnityEngine;

public static class ProactiveResetSafetyPolicyFactory
{
    public static IProactiveResetSafetyPolicy Create(ProactiveUserResetSettings settings)
    {
        return new NoProactiveResetSafetyPolicy();
    }
}

public class NoProactiveResetSafetyPolicy : IProactiveResetSafetyPolicy
{
    public bool IsCandidateSafe(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        ProactiveResetCandidate candidate,
        out ProactiveResetRejection rejection)
    {
        rejection = default;
        return true;
    }
}

public class InPlaceProactiveResetSafetyPolicy : IProactiveResetSafetyPolicy
{
    public bool IsCandidateSafe(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        ProactiveResetCandidate candidate,
        out ProactiveResetRejection rejection)
    {
        rejection = default;
        ProactiveUserResetSettings settings = context.Settings;
        if (ProactiveUserResetSafetyValidator.IsSafeInPlaceReset(
            context.Units,
            candidate.SelectedUserId,
            candidate.ResetDirection,
            settings.inPlaceSafetyBufferSeconds,
            pairContext.PredictionSampleCount,
            out int blockingUserIndex))
        {
            return true;
        }

        rejection = new ProactiveResetRejection
        {
            UserAId = pairContext.UnitAId,
            UserBId = pairContext.UnitBId,
            SelectedUserId = candidate.SelectedUserId,
            Reason = "InPlaceSafetyCheck"
        };
        Debug.Log(
            $"[Proactive Reset Cancelled] triggerId={candidate.OriginTriggerId}, candidateId={candidate.CandidateId}, selectedUserId={candidate.SelectedUserId}, pair=({pairContext.UnitAId},{pairContext.UnitBId}), " +
            $"blockedByUserId={blockingUserIndex}, reason=InPlaceSafetyCheck");
        return false;
    }
}
