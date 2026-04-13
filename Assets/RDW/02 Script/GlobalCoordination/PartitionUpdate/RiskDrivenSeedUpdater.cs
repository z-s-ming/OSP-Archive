using System.Collections.Generic;
using UnityEngine;
using _GCM;

namespace _GCM.PartitionUpdate
{
    public class RiskDrivenSeedUpdater
    {
        private readonly float _seedTrendWeight;
        private readonly float _seedNeighborWeight;
        private readonly float _seedAnchorWeight;
        private readonly float _seedStepLow;
        private readonly float _seedStepMedium;
        private readonly float _seedStepHigh;
        private readonly float _maxSeedShiftPerUpdate;
        private readonly float _maxSeedOffsetFromUser;

        public RiskDrivenSeedUpdater(
            float seedTrendWeight,
            float seedNeighborWeight,
            float seedAnchorWeight,
            float seedStepLow,
            float seedStepMedium,
            float seedStepHigh,
            float maxSeedShiftPerUpdate,
            float maxSeedOffsetFromUser)
        {
            _seedTrendWeight = Mathf.Max(0f, seedTrendWeight);
            _seedNeighborWeight = Mathf.Max(0f, seedNeighborWeight);
            _seedAnchorWeight = Mathf.Max(0f, seedAnchorWeight);
            _seedStepLow = Mathf.Max(0f, seedStepLow);
            _seedStepMedium = Mathf.Max(_seedStepLow, seedStepMedium);
            _seedStepHigh = Mathf.Max(_seedStepMedium, seedStepHigh);
            _maxSeedShiftPerUpdate = Mathf.Max(0f, maxSeedShiftPerUpdate);
            _maxSeedOffsetFromUser = Mathf.Max(0f, maxSeedOffsetFromUser);
        }

        public Vector2 ProposeSeed(
            int userId,
            Vector2 currentSeed,
            Vector2 currentUserPosition,
            Vector2 predictedCenter,
            UserRiskMetrics metrics,
            PartitionRiskFrame riskFrame,
            IReadOnlyList<GameObject> physicalUsers,
            float roomHalfWidth,
            float roomHalfHeight)
        {
            Vector2 trendDirection = NormalizeSafe(predictedCenter - currentUserPosition);
            Vector2 neighborDirection = ResolveNeighborRepulsion(userId, currentSeed, physicalUsers, riskFrame);
            Vector2 anchorDirection = NormalizeSafe(currentUserPosition - currentSeed);

            Vector2 weightedDirection = Vector2.zero;
            float weightSum = 0f;
            AccumulateDirection(ref weightedDirection, ref weightSum, trendDirection, _seedTrendWeight);
            AccumulateDirection(ref weightedDirection, ref weightSum, neighborDirection, _seedNeighborWeight);
            AccumulateDirection(ref weightedDirection, ref weightSum, anchorDirection, _seedAnchorWeight);

            if (weightSum <= 0.000001f || weightedDirection.sqrMagnitude <= 0.000001f)
                return currentSeed;

            Vector2 updateDirection = weightedDirection.normalized;
            float severity = Mathf.Max(metrics != null ? metrics.CellBoundaryRisk : 0f, metrics != null ? metrics.UserRisk : 0f);
            float step = ResolveStep(severity);

            Vector2 proposedSeed = currentSeed + updateDirection * step;
            proposedSeed = ClampToRoomBounds(proposedSeed, roomHalfWidth, roomHalfHeight);
            proposedSeed = ClampOffsetFromUser(currentUserPosition, proposedSeed);

            float shift = Vector2.Distance(currentSeed, proposedSeed);
            if (shift > _maxSeedShiftPerUpdate && shift > 0.000001f)
            {
                proposedSeed = currentSeed + (proposedSeed - currentSeed).normalized * _maxSeedShiftPerUpdate;
            }

            proposedSeed = ClampToRoomBounds(proposedSeed, roomHalfWidth, roomHalfHeight);
            proposedSeed = ClampOffsetFromUser(currentUserPosition, proposedSeed);

            return proposedSeed;
        }

        private float ResolveStep(float severity)
        {
            if (severity >= 0.85f)
                return _seedStepHigh;

            if (severity >= 0.70f)
                return _seedStepMedium;

            return _seedStepLow;
        }

        private Vector2 ResolveNeighborRepulsion(
            int userId,
            Vector2 currentSeed,
            IReadOnlyList<GameObject> physicalUsers,
            PartitionRiskFrame riskFrame)
        {
            if (riskFrame == null || riskFrame.PairwiseRiskMatrix == null || physicalUsers == null)
                return Vector2.zero;

            int userCount = riskFrame.PairwiseRiskMatrix.GetLength(0);
            if (userId < 0 || userId >= userCount)
                return Vector2.zero;

            Vector2 sum = Vector2.zero;
            float weightSum = 0f;

            for (int otherId = 0; otherId < userCount; otherId++)
            {
                if (otherId == userId)
                    continue;

                if (otherId >= physicalUsers.Count || physicalUsers[otherId] == null)
                    continue;

                float pairRisk = riskFrame.PairwiseRiskMatrix[userId, otherId];
                if (pairRisk <= 0.0001f)
                    continue;

                Vector3 otherPosition3 = physicalUsers[otherId].transform.position;
                Vector2 otherPosition = new Vector2(otherPosition3.x, otherPosition3.z);
                Vector2 repel = currentSeed - otherPosition;
                if (repel.sqrMagnitude <= 0.000001f)
                {
                    Vector3 selfPosition3 = physicalUsers[userId].transform.position;
                    Vector2 selfPosition = new Vector2(selfPosition3.x, selfPosition3.z);
                    repel = selfPosition - otherPosition;
                }

                if (repel.sqrMagnitude <= 0.000001f)
                    continue;

                float weight = Mathf.Clamp01(pairRisk);
                sum += repel.normalized * weight;
                weightSum += weight;
            }

            if (weightSum <= 0.000001f)
                return Vector2.zero;

            return sum / weightSum;
        }

        private Vector2 ClampToRoomBounds(Vector2 seed, float roomHalfWidth, float roomHalfHeight)
        {
            float safeHalfW = Mathf.Max(0f, roomHalfWidth - 0.1f);
            float safeHalfH = Mathf.Max(0f, roomHalfHeight - 0.1f);
            seed.x = Mathf.Clamp(seed.x, -safeHalfW, safeHalfW);
            seed.y = Mathf.Clamp(seed.y, -safeHalfH, safeHalfH);
            return seed;
        }

        private Vector2 ClampOffsetFromUser(Vector2 currentUserPosition, Vector2 proposedSeed)
        {
            Vector2 offset = proposedSeed - currentUserPosition;
            float distance = offset.magnitude;
            if (distance <= _maxSeedOffsetFromUser || distance <= 0.000001f)
                return proposedSeed;

            return currentUserPosition + offset.normalized * _maxSeedOffsetFromUser;
        }

        private static Vector2 NormalizeSafe(Vector2 vector)
        {
            if (vector.sqrMagnitude <= 0.000001f)
                return Vector2.zero;

            return vector.normalized;
        }

        private static void AccumulateDirection(ref Vector2 accumulator, ref float weightSum, Vector2 direction, float weight)
        {
            if (weight <= 0.000001f || direction.sqrMagnitude <= 0.000001f)
                return;

            accumulator += direction * weight;
            weightSum += weight;
        }
    }
}