using System.Collections.Generic;

public class ProactiveResetPipeline
{
    private const float CandidateCompareTolerance = 0.0001f;

    private readonly ProactiveResetPairFilter pairFilter;
    private readonly IProactiveResetCooldownPolicy cooldownPolicy;
    private readonly Dictionary<long, float> activeTriggerUntilTimeByPair = new Dictionary<long, float>();

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

        ReleaseExpiredTriggerSessions(context.TimeSeconds);

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

        if (IsTriggerSessionActive(pairContext, context.TimeSeconds))
            return;

        RegisterTriggerSession(pairContext, context.TimeSeconds);

        int triggerId = ProactiveResetEventIdTracker.NextTriggerId();
        triggerEvent.TriggerId = triggerId;
        RoadConflictTuningLogger.NotifyProactiveTrigger(pairContext, triggerEvent);

        if (!context.ProactiveEnabled)
            return;

        result.Triggers.Add(triggerEvent);

        int candidateId = ProactiveResetEventIdTracker.NextCandidateId();
        if (!candidateSelector.TrySelectCandidate(context, pairContext, out ProactiveResetCandidate candidate, out ProactiveResetRejection rejection))
        {
            rejection.DecisionId = ProactiveResetEventIdTracker.NextDecisionId();
            rejection.OriginTriggerId = triggerId;
            rejection.OriginCandidateId = candidateId;
            candidate.DecisionId = rejection.DecisionId;
            candidate.OriginTriggerId = triggerId;
            candidate.CandidateId = candidateId;
            ProactiveResetEventIdTracker.RecordDecision(false);
            AddRejectionIfMeaningful(result, rejection);
            if (HasRejectedCandidateDetails(candidate))
            {
                ProactiveCandidateFrameLogger.NotifyRejected(context, pairContext, candidate, rejection);
                RoadConflictTuningLogger.NotifyProactiveCandidate(pairContext, candidate, "REJECTED", rejection.Reason);
            }
            else
            {
                ProactiveCandidateFrameLogger.NotifyRejected(context, pairContext, rejection);
                RoadConflictTuningLogger.NotifyProactiveCandidate(pairContext, candidate, "REJECTED", rejection.Reason);
            }
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
            RoadConflictTuningLogger.NotifyProactiveCandidate(pairContext, candidate, "REJECTED", cooldownRejection.Reason);
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
            RoadConflictTuningLogger.NotifyProactiveCandidate(pairContext, candidate, "REJECTED", cooldownRejection.Reason);
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
            RoadConflictTuningLogger.NotifyProactiveCandidate(pairContext, candidate, "REJECTED", safetyRejection.Reason);
            return;
        }

        candidate.DecisionId = ProactiveResetEventIdTracker.NextDecisionId();
        candidate.Accepted = true;
        candidate.Executed = false;
        candidate.RejectReason = "NONE";
        ProactiveResetEventIdTracker.RecordDecision(true);
        result.Candidates.Add(candidate);
        ProactiveCandidateFrameLogger.NotifyAccepted(context, pairContext, candidate);
        RoadConflictTuningLogger.NotifyProactiveCandidate(pairContext, candidate, "ACCEPTED", "NONE");
        if (!selectedCandidateByUser.TryGetValue(candidate.SelectedUserId, out ProactiveResetCandidate existing) ||
            ShouldReplaceCandidate(existing, candidate))
        {
            selectedCandidateByUser[candidate.SelectedUserId] = candidate;
        }
    }

    public void ClearActiveTriggerSessions()
    {
        activeTriggerUntilTimeByPair.Clear();
    }

    private void RegisterTriggerSession(ProactiveResetPairContext pairContext, float currentTimeSeconds)
    {
        long pairKey = BuildPairKey(pairContext.UnitAId, pairContext.UnitBId);
        float horizonSeconds = System.Math.Max(pairContext.PredictionHorizonSeconds, 0.1f);
        activeTriggerUntilTimeByPair[pairKey] = currentTimeSeconds + horizonSeconds;
    }

    private bool IsTriggerSessionActive(ProactiveResetPairContext pairContext, float currentTimeSeconds)
    {
        long pairKey = BuildPairKey(pairContext.UnitAId, pairContext.UnitBId);
        return activeTriggerUntilTimeByPair.TryGetValue(pairKey, out float activeUntilTime) &&
               activeUntilTime > currentTimeSeconds;
    }

    private void ReleaseExpiredTriggerSessions(float currentTimeSeconds)
    {
        if (activeTriggerUntilTimeByPair.Count == 0)
            return;

        List<long> expiredKeys = null;
        foreach (KeyValuePair<long, float> kvp in activeTriggerUntilTimeByPair)
        {
            if (kvp.Value <= currentTimeSeconds)
            {
                if (expiredKeys == null)
                    expiredKeys = new List<long>();

                expiredKeys.Add(kvp.Key);
            }
        }

        if (expiredKeys == null)
            return;

        for (int i = 0; i < expiredKeys.Count; i++)
        {
            activeTriggerUntilTimeByPair.Remove(expiredKeys[i]);
        }
    }

    private static long BuildPairKey(int unitAId, int unitBId)
    {
        int minId = unitAId < unitBId ? unitAId : unitBId;
        int maxId = unitAId < unitBId ? unitBId : unitAId;
        return ((long)(uint)minId << 32) | (uint)maxId;
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

        if (incoming.SelectedScore > current.SelectedScore + CandidateCompareTolerance)
            return true;
        if (incoming.SelectedScore < current.SelectedScore - CandidateCompareTolerance)
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

    private static bool HasRejectedCandidateDetails(ProactiveResetCandidate candidate)
    {
        return candidate.SelectedUserId >= 0 ||
               candidate.OtherUserId >= 0 ||
               candidate.ResetDirection.sqrMagnitude > 0.000001f ||
               candidate.KeepMargin != 0.0f ||
               candidate.SelectedM != 0.0f ||
               candidate.SelectedCSelf != 0.0f ||
               candidate.SelectedScore != 0.0f;
    }
}
