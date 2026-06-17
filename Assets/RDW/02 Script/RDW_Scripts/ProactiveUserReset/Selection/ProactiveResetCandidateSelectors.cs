using UnityEngine;

public static class ProactiveResetCandidateSelectorFactory
{
    public static IProactiveResetCandidateSelector Create(ProactiveUserResetUserSelectionMode selectionMode)
    {
        if (selectionMode == ProactiveUserResetUserSelectionMode.None)
            return new NoProactiveResetCandidateSelector();

        bool useScoredArbitration = selectionMode == ProactiveUserResetUserSelectionMode.ScoredArbitration;
        return new ArbitrationProactiveResetCandidateSelector(useScoredArbitration);
    }
}

public class NoProactiveResetCandidateSelector : IProactiveResetCandidateSelector
{
    public bool TrySelectCandidate(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        out ProactiveResetCandidate candidate,
        out ProactiveResetRejection rejection)
    {
        candidate = default;
        rejection = new ProactiveResetRejection
        {
            UserAId = pairContext.UnitAId,
            UserBId = pairContext.UnitBId,
            SelectedUserId = -1,
            Reason = "SelectionDisabled"
        };
        return false;
    }
}

public class ArbitrationProactiveResetCandidateSelector : IProactiveResetCandidateSelector
{
    private readonly bool useScoredArbitration;

    public ArbitrationProactiveResetCandidateSelector(bool useScoredArbitration)
    {
        this.useScoredArbitration = useScoredArbitration;
    }

    public bool TrySelectCandidate(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        out ProactiveResetCandidate candidate,
        out ProactiveResetRejection rejection)
    {
        candidate = default;
        rejection = default;
        ProactiveUserResetSettings settings = context.Settings;
        bool arbitrationSucceeded = ProactiveUserResetArbitrationService.TryArbitratePair(
            pairContext.UnitA,
            pairContext.UnitAId,
            pairContext.UnitB,
            pairContext.UnitBId,
            Mathf.Max(0.0f, settings.arbitrationMEpsilon),
            Mathf.Max(0.0f, settings.arbitrationCEpsilon),
            useScoredArbitration,
            Mathf.Max(0.0f, settings.arbitrationScoreAlpha),
            Mathf.Max(0.0f, settings.arbitrationScoreBeta),
            Mathf.Max(0.0f, settings.arbitrationScoreGamma),
            Mathf.Max(0.001f, settings.arbitrationScoreD0Meters),
            Mathf.Max(0.001f, settings.arbitrationScoreEpsilonMeters),
            Mathf.Max(0.0f, settings.arbitrationScoreTieEpsilon),
            settings.resetDirectionMode,
            out ProactiveUserResetArbitrationService.ArbitrationResult arbitrationResult);

        if (!arbitrationSucceeded)
        {
            rejection = new ProactiveResetRejection
            {
                UserAId = pairContext.UnitAId,
                UserBId = pairContext.UnitBId,
                SelectedUserId = -1,
                Reason = "ArbitrationFailed"
            };
            return false;
        }

        float minExpectedImprovement = context.Settings.judgeMode == ProactiveUserResetJudgeMode.RecoveryMarginTrend
            ? Mathf.Max(0.0f, settings.proactiveMinExpectedImprovementMeters)
            : 0.0f;
        if (minExpectedImprovement > 0.0f &&
            arbitrationResult.SelectedM < arbitrationResult.KeepMargin + minExpectedImprovement)
        {
            candidate = new ProactiveResetCandidate
            {
                SelectedUserId = arbitrationResult.SelectedUnitIndex,
                OtherUserId = arbitrationResult.OtherUnitIndex,
                ResetDirection = arbitrationResult.SelectedResetDirection,
                KeepMargin = arbitrationResult.KeepMargin,
                SelectedM = arbitrationResult.SelectedM,
                SelectedCSelf = arbitrationResult.SelectedCSelf,
                SelectedScore = arbitrationResult.SelectedScore,
                Accepted = false,
                Executed = false,
                RejectReason = "InsufficientExpectedImprovement"
            };
            rejection = new ProactiveResetRejection
            {
                UserAId = pairContext.UnitAId,
                UserBId = pairContext.UnitBId,
                SelectedUserId = arbitrationResult.SelectedUnitIndex,
                Reason = "InsufficientExpectedImprovement"
            };
            return false;
        }

        candidate = new ProactiveResetCandidate
        {
            SelectedUserId = arbitrationResult.SelectedUnitIndex,
            OtherUserId = arbitrationResult.OtherUnitIndex,
            ResetDirection = arbitrationResult.SelectedResetDirection,
            KeepMargin = arbitrationResult.KeepMargin,
            SelectedM = arbitrationResult.SelectedM,
            SelectedCSelf = arbitrationResult.SelectedCSelf,
            SelectedScore = arbitrationResult.SelectedScore
        };
        return true;
    }
}
