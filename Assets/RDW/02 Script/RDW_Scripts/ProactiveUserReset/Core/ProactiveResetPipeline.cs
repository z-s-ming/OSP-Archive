using System.Collections.Generic;

public class ProactiveResetPipeline
{
    private const float CandidateCompareTolerance = 0.0001f;

    private readonly ProactiveResetPairFilter pairFilter;
    private readonly IProactiveResetCooldownPolicy cooldownPolicy;

    public ProactiveResetPipeline(IProactiveResetCooldownPolicy cooldownPolicy)
    {
        this.cooldownPolicy = cooldownPolicy;
        pairFilter = new ProactiveResetPairFilter();
    }

    public ProactiveResetFrameResult Evaluate(ProactiveResetFrameContext context)
    {
        ProactiveResetFrameResult result = new ProactiveResetFrameResult();
        if (!IsValidContext(context))
            return result;

        IProactiveResetTriggerDetector triggerDetector =
            ProactiveResetTriggerDetectorFactory.Create(context.Settings.judgeMode);
        IProactiveResetCandidateSelector candidateSelector =
            ProactiveResetCandidateSelectorFactory.Create(context.Settings.userSelectionMode);
        IProactiveResetSafetyPolicy safetyPolicy =
            ProactiveResetSafetyPolicyFactory.Create(context.Settings);

        Dictionary<int, ProactiveResetCandidate> selectedCandidateByUser = context.ProactiveEnabled
            ? new Dictionary<int, ProactiveResetCandidate>()
            : null;

        for (int userId = 0; userId < context.Units.Length; userId++)
        {
            if (!context.CellAdjacency.TryGetValue(userId, out HashSet<int> adjacentUsers) || adjacentUsers == null)
                continue;

            foreach (int adjacentUserId in adjacentUsers)
            {
                EvaluatePair(
                    context,
                    result,
                    selectedCandidateByUser,
                    triggerDetector,
                    candidateSelector,
                    safetyPolicy,
                    userId,
                    adjacentUserId);
            }
        }

        if (!context.ProactiveEnabled || selectedCandidateByUser == null)
            return result;

        foreach (KeyValuePair<int, ProactiveResetCandidate> kvp in selectedCandidateByUser)
        {
            ProactiveResetCandidate candidate = kvp.Value;
            result.Intents.Add(new ProactiveResetIntent
            {
                OriginTriggerId = candidate.OriginTriggerId,
                OriginCandidateId = candidate.CandidateId,
                DecisionId = candidate.DecisionId,
                SelectedUserId = candidate.SelectedUserId,
                OtherUserId = candidate.OtherUserId,
                ResetDirection = candidate.ResetDirection,
                IsBidirectionalUserResetEvent = true
            });
        }

        return result;
    }

    private void EvaluatePair(
        ProactiveResetFrameContext context,
        ProactiveResetFrameResult result,
        Dictionary<int, ProactiveResetCandidate> selectedCandidateByUser,
        IProactiveResetTriggerDetector triggerDetector,
        IProactiveResetCandidateSelector candidateSelector,
        IProactiveResetSafetyPolicy safetyPolicy,
        int userId,
        int adjacentUserId)
    {
        if (!pairFilter.TryBuildPairContext(context, userId, adjacentUserId, out ProactiveResetPairContext pairContext))
            return;

        EvaluateRecoverabilityForDebugIfNeeded(context, pairContext);

        if (!triggerDetector.TryCreateTrigger(context, pairContext, out ProactiveResetTriggerEvent triggerEvent))
            return;

        int triggerId = ProactiveResetEventIdTracker.NextTriggerId();
        triggerEvent.TriggerId = triggerId;

        if (!context.ProactiveEnabled)
            return;

        result.Triggers.Add(triggerEvent);

        int candidateId = ProactiveResetEventIdTracker.NextCandidateId();
        if (!candidateSelector.TrySelectCandidate(context, pairContext, out ProactiveResetCandidate candidate, out ProactiveResetRejection rejection))
        {
            rejection.DecisionId = ProactiveResetEventIdTracker.NextDecisionId();
            rejection.OriginTriggerId = triggerId;
            rejection.OriginCandidateId = candidateId;
            ProactiveResetEventIdTracker.RecordDecision(false);
            AddRejectionIfMeaningful(result, rejection);
            ProactiveCandidateFrameLogger.NotifyRejected(context, pairContext, rejection);
            return;
        }

        candidate.CandidateId = candidateId;
        candidate.OriginTriggerId = triggerId;

        if (cooldownPolicy != null && cooldownPolicy.IsPairCoolingDown(pairContext.UnitAId, pairContext.UnitBId, context.FrameIndex))
        {
            int decisionId = ProactiveResetEventIdTracker.NextDecisionId();
            ProactiveResetRejection cooldownRejection = new ProactiveResetRejection
            {
                DecisionId = decisionId,
                OriginTriggerId = triggerId,
                OriginCandidateId = candidateId,
                UserAId = pairContext.UnitAId,
                UserBId = pairContext.UnitBId,
                SelectedUserId = candidate.SelectedUserId,
                Reason = "PairCooldown"
            };
            candidate.DecisionId = decisionId;
            candidate.Accepted = false;
            candidate.Executed = false;
            candidate.RejectReason = cooldownRejection.Reason;
            ProactiveResetEventIdTracker.RecordDecision(false);
            result.Rejections.Add(cooldownRejection);
            ProactiveCandidateFrameLogger.NotifyRejected(
                context,
                pairContext,
                candidate,
                cooldownRejection);
            return;
        }

        if (cooldownPolicy != null && cooldownPolicy.IsCoolingDown(candidate.SelectedUserId, context.FrameIndex))
        {
            int decisionId = ProactiveResetEventIdTracker.NextDecisionId();
            ProactiveResetRejection cooldownRejection = new ProactiveResetRejection
            {
                DecisionId = decisionId,
                OriginTriggerId = triggerId,
                OriginCandidateId = candidateId,
                UserAId = pairContext.UnitAId,
                UserBId = pairContext.UnitBId,
                SelectedUserId = candidate.SelectedUserId,
                Reason = "Cooldown"
            };
            candidate.DecisionId = decisionId;
            candidate.Accepted = false;
            candidate.Executed = false;
            candidate.RejectReason = cooldownRejection.Reason;
            ProactiveResetEventIdTracker.RecordDecision(false);
            result.Rejections.Add(cooldownRejection);
            ProactiveCandidateFrameLogger.NotifyRejected(
                context,
                pairContext,
                candidate,
                cooldownRejection);
            return;
        }

        if (!safetyPolicy.IsCandidateSafe(context, pairContext, candidate, out ProactiveResetRejection safetyRejection))
        {
            int decisionId = ProactiveResetEventIdTracker.NextDecisionId();
            safetyRejection.DecisionId = decisionId;
            safetyRejection.OriginTriggerId = triggerId;
            safetyRejection.OriginCandidateId = candidateId;
            candidate.DecisionId = decisionId;
            candidate.Accepted = false;
            candidate.Executed = false;
            candidate.RejectReason = string.IsNullOrEmpty(safetyRejection.Reason) ? "UNKNOWN" : safetyRejection.Reason;
            ProactiveResetEventIdTracker.RecordDecision(false);
            AddRejectionIfMeaningful(result, safetyRejection);
            ProactiveCandidateFrameLogger.NotifyRejected(context, pairContext, candidate, safetyRejection);
            return;
        }

        candidate.DecisionId = ProactiveResetEventIdTracker.NextDecisionId();
        candidate.Accepted = true;
        candidate.Executed = false;
        candidate.RejectReason = "NONE";
        ProactiveResetEventIdTracker.RecordDecision(true);
        result.Candidates.Add(candidate);
        ProactiveCandidateFrameLogger.NotifyAccepted(context, pairContext, candidate);
        if (!selectedCandidateByUser.TryGetValue(candidate.SelectedUserId, out ProactiveResetCandidate existing) ||
            ShouldReplaceCandidate(existing, candidate))
        {
            selectedCandidateByUser[candidate.SelectedUserId] = candidate;
        }
    }

    private static bool IsValidContext(ProactiveResetFrameContext context)
    {
        return context != null &&
               context.ShouldRunPrecheck &&
               context.Settings != null &&
               context.Units != null &&
               context.Units.Length > 0 &&
               context.CellAdjacency != null;
    }

    private static void EvaluateRecoverabilityForDebugIfNeeded(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext)
    {
        if (!context.DebugVisualizationEnabled ||
            context.Settings.judgeMode == ProactiveUserResetJudgeMode.Recoverability)
        {
            return;
        }

        BidirectionalCollisionRecoverabilityEvaluator.Evaluate(
            pairContext.UnitA,
            pairContext.UnitB,
            pairContext.PredictionHorizonSeconds,
            pairContext.PredictionSampleCount,
            true);
    }

    private static void AddRejectionIfMeaningful(
        ProactiveResetFrameResult result,
        ProactiveResetRejection rejection)
    {
        if (string.IsNullOrEmpty(rejection.Reason))
            return;

        result.Rejections.Add(rejection);
    }

    private static bool ShouldReplaceCandidate(ProactiveResetCandidate current, ProactiveResetCandidate incoming)
    {
        if (incoming.KeepMargin < current.KeepMargin - CandidateCompareTolerance)
            return true;
        if (incoming.KeepMargin > current.KeepMargin + CandidateCompareTolerance)
            return false;

        if (incoming.SelectedM > current.SelectedM + CandidateCompareTolerance)
            return true;
        if (incoming.SelectedM < current.SelectedM - CandidateCompareTolerance)
            return false;

        if (incoming.SelectedCSelf < current.SelectedCSelf - CandidateCompareTolerance)
            return true;
        if (incoming.SelectedCSelf > current.SelectedCSelf + CandidateCompareTolerance)
            return false;

        if (incoming.OtherUserId < current.OtherUserId)
            return true;

        return false;
    }
}
