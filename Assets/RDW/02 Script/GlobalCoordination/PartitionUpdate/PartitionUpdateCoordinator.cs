using System;
using System.Collections.Generic;
using UnityEngine;
using _GCM;

namespace _GCM.PartitionUpdate
{
    public class PartitionUpdateCoordinator
    {
        private readonly int _totalUserCount;
        private readonly VoronoiPartitioner _voronoiPartitioner;
        private readonly PartitionRiskEvaluator _partitionRiskEvaluator;
        private readonly PartitionUpdateTriggerEvaluator _partitionUpdateTriggerEvaluator;
        private readonly RiskDrivenSeedUpdater _riskDrivenSeedUpdater;
        private readonly PartitionUpdateAcceptancePolicy _partitionUpdateAcceptancePolicy;
        private readonly PartitionUpdateLogger _partitionUpdateLogger;

        public PartitionUpdateCoordinator(
            int totalUserCount,
            VoronoiPartitioner voronoiPartitioner,
            PartitionRiskEvaluator partitionRiskEvaluator,
            PartitionUpdateTriggerEvaluator partitionUpdateTriggerEvaluator,
            RiskDrivenSeedUpdater riskDrivenSeedUpdater,
            PartitionUpdateAcceptancePolicy partitionUpdateAcceptancePolicy,
            PartitionUpdateLogger partitionUpdateLogger)
        {
            _totalUserCount = Mathf.Max(0, totalUserCount);
            _voronoiPartitioner = voronoiPartitioner;
            _partitionRiskEvaluator = partitionRiskEvaluator;
            _partitionUpdateTriggerEvaluator = partitionUpdateTriggerEvaluator;
            _riskDrivenSeedUpdater = riskDrivenSeedUpdater;
            _partitionUpdateAcceptancePolicy = partitionUpdateAcceptancePolicy;
            _partitionUpdateLogger = partitionUpdateLogger;
        }

        public void Execute(
            bool enableRiskDrivenPartitionUpdate,
            FrameState frameState,
            IReadOnlyList<Vector3> offsetResult,
            VelocityPredictor velocityPredictor,
            PredictedOccupancyFrame latestPredictedOccupancyFrame,
            RiskDrivenSeedUpdateState[] partitionUpdateStates,
            List<PartitionUpdateAttempt> latestPartitionUpdateAttempts,
            int simulationFrameIndex,
            float simulationElapsedTime,
            float deltaTime,
            bool useVelocityOffset,
            float physicalRoomWidthHalf,
            float physicalRoomHeightHalf,
            ref PartitionResult latestPartitionResult,
            ref PartitionRiskFrame latestPartitionRiskFrame,
            Action<PartitionResult> applyPartitionResult)
        {
            if (!enableRiskDrivenPartitionUpdate)
                return;

            if (frameState == null || frameState.PhysicalUsers == null || latestPartitionResult == null || latestPartitionRiskFrame == null)
                return;

            if (_partitionUpdateTriggerEvaluator == null || _riskDrivenSeedUpdater == null || _partitionRiskEvaluator == null || _voronoiPartitioner == null)
                return;

            if (partitionUpdateStates == null || partitionUpdateStates.Length == 0 || latestPartitionUpdateAttempts == null)
                return;

            latestPartitionUpdateAttempts.Clear();
            PartitionUpdateTransaction transaction = BeginAttempt(latestPartitionResult, latestPartitionRiskFrame);
            List<PartitionUpdateAttemptContext> attemptContexts = BeginAttemptContexts(
                frameState,
                velocityPredictor,
                latestPredictedOccupancyFrame,
                partitionUpdateStates,
                latestPartitionRiskFrame,
                latestPartitionUpdateAttempts,
                simulationFrameIndex,
                simulationElapsedTime,
                physicalRoomWidthHalf,
                physicalRoomHeightHalf);

            if (attemptContexts.Count == 0)
            {
                LogPartitionUpdateAttempts(latestPartitionUpdateAttempts, simulationFrameIndex, simulationElapsedTime);
                return;
            }

            bool candidateApplied = ApplyCandidate(
                frameState,
                offsetResult,
                latestPredictedOccupancyFrame,
                attemptContexts,
                transaction,
                simulationFrameIndex,
                simulationElapsedTime,
                deltaTime,
                useVelocityOffset,
                physicalRoomWidthHalf,
                physicalRoomHeightHalf,
                out PartitionResult candidatePartition,
                out PartitionRiskFrame candidateRiskFrame);

            if (!candidateApplied)
            {
                RollbackAttempt(
                    transaction,
                    attemptContexts,
                    partitionUpdateStates,
                    simulationElapsedTime,
                    PartitionUpdateRejectReason.ProposalInvalid,
                    ref latestPartitionResult,
                    ref latestPartitionRiskFrame,
                    applyPartitionResult);
                LogPartitionUpdateAttempts(latestPartitionUpdateAttempts, simulationFrameIndex, simulationElapsedTime);
                return;
            }

            bool acceptCandidate = EvaluateCandidate(
                attemptContexts,
                latestPartitionRiskFrame,
                candidatePartition,
                candidateRiskFrame,
                out PartitionUpdateRejectReason rollbackReason);

            if (acceptCandidate)
            {
                CommitAttempt(
                    attemptContexts,
                    partitionUpdateStates,
                    simulationElapsedTime,
                    candidatePartition,
                    candidateRiskFrame,
                    ref latestPartitionResult,
                    ref latestPartitionRiskFrame,
                    applyPartitionResult);
            }
            else
            {
                RollbackAttempt(
                    transaction,
                    attemptContexts,
                    partitionUpdateStates,
                    simulationElapsedTime,
                    rollbackReason,
                    ref latestPartitionResult,
                    ref latestPartitionRiskFrame,
                    applyPartitionResult);
            }

            LogPartitionUpdateAttempts(latestPartitionUpdateAttempts, simulationFrameIndex, simulationElapsedTime);
        }

        private PartitionUpdateTransaction BeginAttempt(PartitionResult latestPartitionResult, PartitionRiskFrame latestPartitionRiskFrame)
        {
            return new PartitionUpdateTransaction
            {
                OriginalSeeds = _voronoiPartitioner.GetSeedPointsCopy(),
                RiskSnapshot = _partitionRiskEvaluator.CaptureTemporalState(),
                PreviousPartitionResult = latestPartitionResult,
                PreviousRiskFrame = latestPartitionRiskFrame
            };
        }

        private List<PartitionUpdateAttemptContext> BeginAttemptContexts(
            FrameState frameState,
            VelocityPredictor velocityPredictor,
            PredictedOccupancyFrame latestPredictedOccupancyFrame,
            RiskDrivenSeedUpdateState[] partitionUpdateStates,
            PartitionRiskFrame latestPartitionRiskFrame,
            List<PartitionUpdateAttempt> latestPartitionUpdateAttempts,
            int simulationFrameIndex,
            float simulationElapsedTime,
            float physicalRoomWidthHalf,
            float physicalRoomHeightHalf)
        {
            List<PartitionUpdateAttemptContext> contexts = new List<PartitionUpdateAttemptContext>();
            List<Vector2> originalSeeds = _voronoiPartitioner.GetSeedPointsCopy();

            for (int userId = 0; userId < _totalUserCount; userId++)
            {
                if (userId >= frameState.PhysicalUsers.Count || frameState.PhysicalUsers[userId] == null)
                    continue;

                if (userId >= partitionUpdateStates.Length || partitionUpdateStates[userId] == null)
                    continue;

                UserRiskMetrics metrics = GetRiskMetricsByUserId(latestPartitionRiskFrame, userId);
                if (metrics == null)
                    continue;

                float speed = velocityPredictor != null ? velocityPredictor.GetCurrentSpeed(userId) : 0f;
                PartitionUpdateTriggerResult triggerResult = _partitionUpdateTriggerEvaluator.UpdateAndEvaluate(
                    metrics.CellBoundaryRisk,
                    metrics.UserRisk,
                    speed,
                    simulationElapsedTime,
                    partitionUpdateStates[userId]);

                if (triggerResult.TriggerType == PartitionUpdateTriggerType.None && !triggerResult.CooldownBlocked)
                    continue;

                Vector2 currentSeed = userId < originalSeeds.Count ? originalSeeds[userId] : _voronoiPartitioner.GetSeedPoint(userId);
                PartitionUpdateAttempt attempt = new PartitionUpdateAttempt
                {
                    UserId = userId,
                    TriggerType = triggerResult.TriggerType,
                    OldSeed = currentSeed,
                    NewSeed = currentSeed,
                    SeedShiftDist = 0f,
                    CellRiskBefore = metrics.CellBoundaryRisk,
                    CellRiskAfter = metrics.CellBoundaryRisk,
                    NeighborRiskBefore = metrics.UserRisk,
                    NeighborRiskAfter = metrics.UserRisk,
                    WeightedImprovement = 0f,
                    CellImprovement = 0f,
                    NeighborImprovement = 0f,
                    MinCellClearanceBefore = metrics.MinCellClearance,
                    MinCellClearanceAfter = metrics.MinCellClearance,
                    MinNeighborSeparationBefore = metrics.MinPairSeparation,
                    MinNeighborSeparationAfter = metrics.MinPairSeparation,
                    SpeedAtTrigger = triggerResult.SpeedForDecision,
                    CellPersistCount = triggerResult.CellPersistCount,
                    NeighborPersistCount = triggerResult.NeighborPersistCount,
                    CooldownRemaining = triggerResult.CooldownRemaining,
                    RejectReason = PartitionUpdateRejectReason.None,
                    Accepted = false
                };

                if (triggerResult.CooldownBlocked)
                {
                    attempt.RejectReason = PartitionUpdateRejectReason.CooldownBlocked;
                    latestPartitionUpdateAttempts.Add(attempt);
                    continue;
                }

                if (triggerResult.TriggerType == PartitionUpdateTriggerType.None)
                    continue;

                Vector3 userPosition3 = frameState.PhysicalUsers[userId].transform.position;
                Vector2 userPosition = new Vector2(userPosition3.x, userPosition3.z);
                Vector2 predictedCenter = ResolvePredictedBandCenter(userId, frameState, latestPredictedOccupancyFrame);

                Vector2 proposedSeed = _riskDrivenSeedUpdater.ProposeSeed(
                    userId,
                    currentSeed,
                    userPosition,
                    predictedCenter,
                    metrics,
                    latestPartitionRiskFrame,
                    frameState.PhysicalUsers,
                    physicalRoomWidthHalf,
                    physicalRoomHeightHalf);

                if (!IsFinite(proposedSeed))
                {
                    attempt.RejectReason = PartitionUpdateRejectReason.ProposalInvalid;
                    latestPartitionUpdateAttempts.Add(attempt);
                    continue;
                }

                attempt.NewSeed = proposedSeed;
                attempt.SeedShiftDist = Vector2.Distance(currentSeed, proposedSeed);
                latestPartitionUpdateAttempts.Add(attempt);

                contexts.Add(new PartitionUpdateAttemptContext
                {
                    UserId = userId,
                    TriggerType = triggerResult.TriggerType,
                    OldSeed = currentSeed,
                    ProposedSeed = proposedSeed,
                    OldCellRisk = metrics.CellBoundaryRisk,
                    OldNeighborRisk = metrics.UserRisk,
                    Attempt = attempt
                });
            }

            return contexts;
        }

        private bool ApplyCandidate(
            FrameState frameState,
            IReadOnlyList<Vector3> offsetResult,
            PredictedOccupancyFrame latestPredictedOccupancyFrame,
            List<PartitionUpdateAttemptContext> contexts,
            PartitionUpdateTransaction transaction,
            int simulationFrameIndex,
            float simulationElapsedTime,
            float deltaTime,
            bool useVelocityOffset,
            float physicalRoomWidthHalf,
            float physicalRoomHeightHalf,
            out PartitionResult candidatePartition,
            out PartitionRiskFrame candidateRiskFrame)
        {
            candidatePartition = null;
            candidateRiskFrame = null;

            if (contexts == null || contexts.Count == 0 || transaction == null)
                return false;

            _voronoiPartitioner.SetSeedPoints(transaction.OriginalSeeds);
            for (int i = 0; i < contexts.Count; i++)
            {
                PartitionUpdateAttemptContext context = contexts[i];
                _voronoiPartitioner.SetSeedPoint(context.UserId, context.ProposedSeed);
            }

            candidatePartition = _voronoiPartitioner.Build(
                frameState,
                offsetResult,
                deltaTime,
                useVelocityOffset,
                physicalRoomWidthHalf,
                physicalRoomHeightHalf);

            if (candidatePartition == null)
                return false;

            candidateRiskFrame = _partitionRiskEvaluator.Evaluate(
                simulationFrameIndex,
                simulationElapsedTime,
                candidatePartition,
                latestPredictedOccupancyFrame,
                frameState.PhysicalUsers,
                physicalRoomWidthHalf,
                physicalRoomHeightHalf);

            return candidateRiskFrame != null;
        }

        private bool EvaluateCandidate(
            List<PartitionUpdateAttemptContext> contexts,
            PartitionRiskFrame latestPartitionRiskFrame,
            PartitionResult candidatePartition,
            PartitionRiskFrame candidateRiskFrame,
            out PartitionUpdateRejectReason rollbackReason)
        {
            rollbackReason = PartitionUpdateRejectReason.InsufficientImprovement;
            bool allAccepted = true;

            for (int i = 0; i < contexts.Count; i++)
            {
                PartitionUpdateAttemptContext context = contexts[i];
                PartitionUpdateAttempt attempt = context.Attempt;

                UserRiskMetrics beforeMetrics = GetRiskMetricsByUserId(latestPartitionRiskFrame, context.UserId);
                UserRiskMetrics afterMetrics = GetRiskMetricsByUserId(candidateRiskFrame, context.UserId);
                if (beforeMetrics == null || afterMetrics == null || attempt == null)
                {
                    if (attempt != null)
                        attempt.RejectReason = PartitionUpdateRejectReason.ProposalInvalid;
                    rollbackReason = PartitionUpdateRejectReason.ProposalInvalid;
                    allAccepted = false;
                    continue;
                }

                if (context.UserId >= 0 && context.UserId < candidatePartition.SeedPoints.Count)
                {
                    attempt.NewSeed = new Vector2(candidatePartition.SeedPoints[context.UserId].x, candidatePartition.SeedPoints[context.UserId].y);
                    attempt.SeedShiftDist = Vector2.Distance(attempt.OldSeed, attempt.NewSeed);
                }

                attempt.CellRiskAfter = afterMetrics.CellBoundaryRisk;
                attempt.NeighborRiskAfter = afterMetrics.UserRisk;
                attempt.MinCellClearanceAfter = afterMetrics.MinCellClearance;
                attempt.MinNeighborSeparationAfter = afterMetrics.MinPairSeparation;

                PartitionUpdateAcceptancePolicy.Decision decision = _partitionUpdateAcceptancePolicy != null
                    ? _partitionUpdateAcceptancePolicy.Evaluate(
                        beforeMetrics.CellBoundaryRisk,
                        afterMetrics.CellBoundaryRisk,
                        beforeMetrics.UserRisk,
                        afterMetrics.UserRisk)
                    : new PartitionUpdateAcceptancePolicy.Decision
                    {
                        Accepted = true,
                        WeightedImprovement = 0f,
                        CellImprovement = 0f,
                        NeighborImprovement = 0f,
                        RejectReason = PartitionUpdateRejectReason.None
                    };

                attempt.WeightedImprovement = decision.WeightedImprovement;
                attempt.CellImprovement = decision.CellImprovement;
                attempt.NeighborImprovement = decision.NeighborImprovement;
                attempt.RejectReason = decision.RejectReason;

                if (!decision.Accepted)
                {
                    if (rollbackReason == PartitionUpdateRejectReason.InsufficientImprovement)
                        rollbackReason = decision.RejectReason;
                    allAccepted = false;
                }
            }

            return allAccepted;
        }

        private void CommitAttempt(
            List<PartitionUpdateAttemptContext> contexts,
            RiskDrivenSeedUpdateState[] partitionUpdateStates,
            float simulationElapsedTime,
            PartitionResult candidatePartition,
            PartitionRiskFrame candidateRiskFrame,
            ref PartitionResult latestPartitionResult,
            ref PartitionRiskFrame latestPartitionRiskFrame,
            Action<PartitionResult> applyPartitionResult)
        {
            latestPartitionResult = candidatePartition;
            latestPartitionRiskFrame = candidateRiskFrame;
            applyPartitionResult?.Invoke(latestPartitionResult);

            for (int i = 0; i < contexts.Count; i++)
            {
                PartitionUpdateAttemptContext context = contexts[i];
                PartitionUpdateAttempt attempt = context.Attempt;
                if (attempt != null)
                {
                    attempt.Accepted = true;
                    attempt.RejectReason = PartitionUpdateRejectReason.None;
                }

                if (context.UserId >= 0 && context.UserId < partitionUpdateStates.Length)
                {
                    Vector2 committedSeed = attempt != null ? attempt.NewSeed : context.ProposedSeed;
                    _partitionUpdateTriggerEvaluator.Commit(committedSeed, simulationElapsedTime, partitionUpdateStates[context.UserId]);
                }
            }
        }

        private void RollbackAttempt(
            PartitionUpdateTransaction transaction,
            List<PartitionUpdateAttemptContext> contexts,
            RiskDrivenSeedUpdateState[] partitionUpdateStates,
            float simulationElapsedTime,
            PartitionUpdateRejectReason rollbackReason,
            ref PartitionResult latestPartitionResult,
            ref PartitionRiskFrame latestPartitionRiskFrame,
            Action<PartitionResult> applyPartitionResult)
        {
            if (transaction != null)
            {
                _partitionRiskEvaluator.RestoreTemporalState(transaction.RiskSnapshot);
                _voronoiPartitioner.SetSeedPoints(transaction.OriginalSeeds);
                latestPartitionResult = transaction.PreviousPartitionResult;
                latestPartitionRiskFrame = transaction.PreviousRiskFrame;
                applyPartitionResult?.Invoke(latestPartitionResult);
            }

            if (contexts == null)
                return;

            for (int i = 0; i < contexts.Count; i++)
            {
                PartitionUpdateAttemptContext context = contexts[i];
                PartitionUpdateAttempt attempt = context.Attempt;
                if (attempt != null)
                {
                    attempt.Accepted = false;
                    if (attempt.RejectReason == PartitionUpdateRejectReason.None)
                        attempt.RejectReason = rollbackReason;
                }

                if (context.UserId >= 0 && context.UserId < partitionUpdateStates.Length)
                {
                    _partitionUpdateTriggerEvaluator.Rollback(partitionUpdateStates[context.UserId], simulationElapsedTime);
                }
            }
        }

        private void LogPartitionUpdateAttempts(List<PartitionUpdateAttempt> latestPartitionUpdateAttempts, int simulationFrameIndex, float simulationElapsedTime)
        {
            if (_partitionUpdateLogger == null)
                return;

            for (int i = 0; i < latestPartitionUpdateAttempts.Count; i++)
            {
                _partitionUpdateLogger.TryLogAttempt(simulationFrameIndex, simulationElapsedTime, latestPartitionUpdateAttempts[i]);
            }
        }

        private static UserRiskMetrics GetRiskMetricsByUserId(PartitionRiskFrame frame, int userId)
        {
            if (frame == null || frame.PerUser == null)
                return null;

            for (int i = 0; i < frame.PerUser.Count; i++)
            {
                if (frame.PerUser[i].UserId == userId)
                    return frame.PerUser[i];
            }

            return null;
        }

        private static Vector2 ResolvePredictedBandCenter(int userId, FrameState frameState, PredictedOccupancyFrame latestPredictedOccupancyFrame)
        {
            if (frameState == null || frameState.PhysicalUsers == null || userId < 0 || userId >= frameState.PhysicalUsers.Count || frameState.PhysicalUsers[userId] == null)
                return Vector2.zero;

            if (latestPredictedOccupancyFrame == null)
            {
                Vector3 fallback = frameState.PhysicalUsers[userId].transform.position;
                return new Vector2(fallback.x, fallback.z);
            }

            for (int i = 0; i < latestPredictedOccupancyFrame.Bands.Count; i++)
            {
                PredictedOccupancyBand band = latestPredictedOccupancyFrame.Bands[i];
                if (band == null || band.UserId != userId)
                    continue;

                if (band.Samples.Count == 0)
                {
                    Vector3 current = band.CurrentPosition;
                    return new Vector2(current.x, current.z);
                }

                Vector2 weightedSum = Vector2.zero;
                float weightSum = 0f;
                for (int j = 0; j < band.Samples.Count; j++)
                {
                    PredictedOccupancySample sample = band.Samples[j];
                    float weight = 1f / (1f + Mathf.Max(0f, sample.Horizon));
                    weightedSum += new Vector2(sample.PredictedPosition.x, sample.PredictedPosition.z) * weight;
                    weightSum += weight;
                }

                if (weightSum > 0.0001f)
                    return weightedSum / weightSum;

                Vector3 currentPosition = band.CurrentPosition;
                return new Vector2(currentPosition.x, currentPosition.z);
            }

            Vector3 fallbackPosition = frameState.PhysicalUsers[userId].transform.position;
            return new Vector2(fallbackPosition.x, fallbackPosition.z);
        }

        private static bool IsFinite(Vector2 value)
        {
            return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.x) || float.IsInfinity(value.y));
        }
    }
}
