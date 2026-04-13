using System;
using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    public enum DominantRiskType
    {
        None,
        Wall,
        User,
        Smooth,
        Mixed
    }

    [Serializable]
    public struct RiskWeights
    {
        public float CellBoundaryWeight;
        public float UserWeight;
        public float SmoothWeight;

        public static RiskWeights Default => new RiskWeights
        {
            CellBoundaryWeight = 0.40f,
            UserWeight = 0.40f,
            SmoothWeight = 0.20f
        };
    }

    [Serializable]
    public class PartitionRiskConfig
    {
        public int OutputEveryNFrames = 10;
        public float CellBoundarySafeClearance = 0.35f;
        public float PhysicalBoundarySafeClearance = 0.50f;
        public float PairSafeSeparation = 0.40f;
        public float SeedDeltaReference = 0.25f;
        public float AdjacencyRiskThreshold = 0.35f;
        public float DominantRiskNoneThreshold = 0.10f;
        public float DominantRiskMixedGap = 0.08f;
        public RiskWeights Weights = RiskWeights.Default;
    }

    [Serializable]
    public class UserRiskMetrics
    {
        public int UserId;
        public float CellBoundaryRisk;
        public float PhysicalBoundaryRisk;
        public float UserRisk;
        public float SeedMotionPenalty;
        public float CellShapePenaltyReserved;
        public float SmoothRisk;
        public float WeightedTotalRisk;
        public DominantRiskType DominantRisk;

        // Raw geometry metrics for analysis and plotting.
        public float MinCellClearance;
        public float MinPhysicalClearance;
        public float MinPairSeparation;
        public float MinOccupancySeparation;
        public float SeedDelta;
    }

    public class PartitionRiskFrame
    {
        public int FrameIndex;
        public float SimulationTime;
        public float[,] PairwiseRiskMatrix = new float[0, 0];
        public readonly List<UserRiskMetrics> PerUser = new List<UserRiskMetrics>();

        // Vector form (mean over users) should be the primary external signal.
        public float MeanCellBoundaryRisk;
        public float MeanPhysicalBoundaryRisk;
        public float MeanUserRisk;
        public float MeanSmoothRisk;

        // Secondary scalar for ranking/sorting/logging.
        public float WeightedTotalRisk;
        public float AdjacencyRiskThreshold;
    }

    public class PartitionRiskEvaluator
    {
        public class PartitionRiskTemporalSnapshot
        {
            public bool HasPreviousSeeds;
            public List<Vector2> PreviousSeeds = new List<Vector2>();
        }

        private readonly PartitionRiskConfig _config;
        private readonly List<Vector2> _previousSeeds = new List<Vector2>();
        private bool _hasPreviousSeeds;

        public PartitionRiskEvaluator(PartitionRiskConfig config)
        {
            _config = config ?? new PartitionRiskConfig();
        }

        public void ResetTemporalState()
        {
            _hasPreviousSeeds = false;
            _previousSeeds.Clear();
        }

        public PartitionRiskTemporalSnapshot CaptureTemporalState()
        {
            return new PartitionRiskTemporalSnapshot
            {
                HasPreviousSeeds = _hasPreviousSeeds,
                PreviousSeeds = new List<Vector2>(_previousSeeds)
            };
        }

        public void RestoreTemporalState(PartitionRiskTemporalSnapshot snapshot)
        {
            _previousSeeds.Clear();
            if (snapshot != null && snapshot.PreviousSeeds != null)
            {
                for (int i = 0; i < snapshot.PreviousSeeds.Count; i++)
                {
                    _previousSeeds.Add(snapshot.PreviousSeeds[i]);
                }
            }

            _hasPreviousSeeds = snapshot != null && snapshot.HasPreviousSeeds;
        }

        public PartitionRiskFrame Evaluate(
            int frameIndex,
            float simulationTime,
            PartitionResult partition,
            PredictedOccupancyFrame occupancyFrame,
            IReadOnlyList<GameObject> physicalUsers,
            float roomHalfWidth,
            float roomHalfHeight)
        {
            PartitionRiskFrame frame = new PartitionRiskFrame
            {
                FrameIndex = frameIndex,
                SimulationTime = simulationTime,
                AdjacencyRiskThreshold = _config.AdjacencyRiskThreshold
            };

            if (partition == null)
                return frame;

            PredictedOccupancyFrame effectiveOccupancy = occupancyFrame ?? BuildFallbackOccupancyFrame(physicalUsers, frameIndex, simulationTime);
            int userCount = ResolveUserCount(partition, effectiveOccupancy, physicalUsers);
            if (userCount <= 0)
                return frame;

            float[,] pairwiseRisk = new float[userCount, userCount];
            float[,] pairwiseMinSep = new float[userCount, userCount];
            InitializePairwiseMatrix(pairwiseMinSep, float.PositiveInfinity);

            for (int i = 0; i < userCount; i++)
            {
                for (int j = i + 1; j < userCount; j++)
                {
                    PredictedOccupancyBand bandA = GetBandByUserId(effectiveOccupancy, i);
                    PredictedOccupancyBand bandB = GetBandByUserId(effectiveOccupancy, j);

                    float minOccupancySeparation = ComputeMinOccupancySeparation(bandA, bandB);
                    float risk = RiskFromSeparation(minOccupancySeparation, _config.PairSafeSeparation);

                    pairwiseRisk[i, j] = risk;
                    pairwiseRisk[j, i] = risk;
                    pairwiseMinSep[i, j] = minOccupancySeparation;
                    pairwiseMinSep[j, i] = minOccupancySeparation;
                }
            }

            frame.PairwiseRiskMatrix = pairwiseRisk;

            float sumCellRisk = 0f;
            float sumPhysicalRisk = 0f;
            float sumUserRisk = 0f;
            float sumSmoothRisk = 0f;
            float sumWeighted = 0f;

            EnsurePreviousSeedCacheSize(userCount);

            for (int userId = 0; userId < userCount; userId++)
            {
                UserRiskMetrics metrics = new UserRiskMetrics
                {
                    UserId = userId
                };

                List<Vector2> cellPolygon = GetRegionVertices(partition, userId);
                PredictedOccupancyBand band = GetBandByUserId(effectiveOccupancy, userId);

                metrics.MinCellClearance = ComputeMinCellClearance(cellPolygon, band, physicalUsers, userId);
                metrics.CellBoundaryRisk = RiskFromClearance(metrics.MinCellClearance, _config.CellBoundarySafeClearance);

                metrics.MinPhysicalClearance = ComputeMinPhysicalClearance(roomHalfWidth, roomHalfHeight, band, physicalUsers, userId);
                metrics.PhysicalBoundaryRisk = RiskFromClearance(metrics.MinPhysicalClearance, _config.PhysicalBoundarySafeClearance);

                metrics.MinPairSeparation = ResolveUserMinPairSeparation(userId, pairwiseMinSep);
                metrics.MinOccupancySeparation = metrics.MinPairSeparation;
                metrics.UserRisk = ResolveUserRisk(userId, pairwiseRisk);

                Vector2 currentSeed = ResolveCurrentSeed(partition, userId);
                metrics.SeedDelta = _hasPreviousSeeds ? Vector2.Distance(_previousSeeds[userId], currentSeed) : 0f;
                metrics.SeedMotionPenalty = Mathf.Clamp01(metrics.SeedDelta / Mathf.Max(0.0001f, _config.SeedDeltaReference));
                metrics.CellShapePenaltyReserved = 0f;
                metrics.SmoothRisk = Mathf.Clamp01(metrics.SeedMotionPenalty + metrics.CellShapePenaltyReserved);

                metrics.WeightedTotalRisk =
                    _config.Weights.CellBoundaryWeight * metrics.CellBoundaryRisk +
                    _config.Weights.UserWeight * metrics.UserRisk +
                    _config.Weights.SmoothWeight * metrics.SmoothRisk;

                metrics.DominantRisk = ResolveDominantRiskType(metrics.CellBoundaryRisk, metrics.UserRisk, metrics.SmoothRisk);

                frame.PerUser.Add(metrics);

                sumCellRisk += metrics.CellBoundaryRisk;
                sumPhysicalRisk += metrics.PhysicalBoundaryRisk;
                sumUserRisk += metrics.UserRisk;
                sumSmoothRisk += metrics.SmoothRisk;
                sumWeighted += metrics.WeightedTotalRisk;

                _previousSeeds[userId] = currentSeed;
            }

            _hasPreviousSeeds = true;

            float inv = 1f / userCount;
            frame.MeanCellBoundaryRisk = sumCellRisk * inv;
            frame.MeanPhysicalBoundaryRisk = sumPhysicalRisk * inv;
            frame.MeanUserRisk = sumUserRisk * inv;
            frame.MeanSmoothRisk = sumSmoothRisk * inv;
            frame.WeightedTotalRisk = sumWeighted * inv;

            return frame;
        }

        private static int ResolveUserCount(PartitionResult partition, PredictedOccupancyFrame occupancyFrame, IReadOnlyList<GameObject> users)
        {
            int count = 0;
            if (partition != null)
            {
                count = Mathf.Max(count, partition.SeedPoints.Count);
                count = Mathf.Max(count, partition.Centroids.Count);
                count = Mathf.Max(count, partition.RegionVertices.Count);
            }

            if (occupancyFrame != null)
            {
                count = Mathf.Max(count, occupancyFrame.Bands.Count);
            }

            if (users != null)
            {
                count = Mathf.Max(count, users.Count);
            }

            return count;
        }

        private static void InitializePairwiseMatrix(float[,] matrix, float value)
        {
            int rows = matrix.GetLength(0);
            int cols = matrix.GetLength(1);
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    matrix[i, j] = value;
                }
            }
        }

        private void EnsurePreviousSeedCacheSize(int userCount)
        {
            while (_previousSeeds.Count < userCount)
            {
                _previousSeeds.Add(Vector2.zero);
            }
        }

        private static PredictedOccupancyFrame BuildFallbackOccupancyFrame(
            IReadOnlyList<GameObject> users,
            int frameIndex,
            float simulationTime)
        {
            PredictedOccupancyFrame frame = new PredictedOccupancyFrame
            {
                FrameIndex = frameIndex,
                SimulationTime = simulationTime
            };

            if (users == null)
                return frame;

            for (int i = 0; i < users.Count; i++)
            {
                GameObject user = users[i];
                if (user == null)
                    continue;

                Vector3 position = user.transform.position;
                PredictedOccupancyBand band = new PredictedOccupancyBand
                {
                    UserId = i,
                    CurrentPosition = position
                };

                band.Samples.Add(new PredictedOccupancySample
                {
                    UserId = i,
                    Horizon = 0f,
                    PredictedPosition = position,
                    UncertaintyRadius = 0f,
                    Disc = new PredictedOccupancyDisc
                    {
                        Center = position,
                        Radius = 0f
                    }
                });

                frame.Bands.Add(band);
            }

            return frame;
        }

        private static PredictedOccupancyBand GetBandByUserId(PredictedOccupancyFrame frame, int userId)
        {
            if (frame == null)
                return null;

            for (int i = 0; i < frame.Bands.Count; i++)
            {
                if (frame.Bands[i].UserId == userId)
                    return frame.Bands[i];
            }

            return null;
        }

        private static List<Vector2> GetRegionVertices(PartitionResult partition, int userId)
        {
            if (partition != null && partition.RegionVertices.TryGetValue(userId, out List<Vector2> vertices))
                return vertices;

            return null;
        }

        private static Vector2 ResolveCurrentSeed(PartitionResult partition, int userId)
        {
            if (partition != null && userId >= 0 && userId < partition.SeedPoints.Count)
            {
                Vector2f seed = partition.SeedPoints[userId];
                return new Vector2(seed.x, seed.y);
            }

            return Vector2.zero;
        }

        private static float ComputeMinCellClearance(
            IReadOnlyList<Vector2> polygon,
            PredictedOccupancyBand band,
            IReadOnlyList<GameObject> physicalUsers,
            int userId)
        {
            if (polygon == null || polygon.Count < 2)
                return float.PositiveInfinity;

            float minClearance = float.PositiveInfinity;
            bool hasSample = false;

            if (band != null && band.Samples != null && band.Samples.Count > 0)
            {
                for (int i = 0; i < band.Samples.Count; i++)
                {
                    PredictedOccupancySample sample = band.Samples[i];
                    Vector2 p = new Vector2(sample.PredictedPosition.x, sample.PredictedPosition.z);
                    float dist = DistancePointToPolygonBoundary(p, polygon) - sample.UncertaintyRadius;
                    minClearance = Mathf.Min(minClearance, dist);
                    hasSample = true;
                }
            }

            if (!hasSample && physicalUsers != null && userId >= 0 && userId < physicalUsers.Count && physicalUsers[userId] != null)
            {
                Vector3 pos3 = physicalUsers[userId].transform.position;
                Vector2 p = new Vector2(pos3.x, pos3.z);
                minClearance = DistancePointToPolygonBoundary(p, polygon);
            }

            return minClearance;
        }

        private static float ComputeMinPhysicalClearance(
            float roomHalfWidth,
            float roomHalfHeight,
            PredictedOccupancyBand band,
            IReadOnlyList<GameObject> physicalUsers,
            int userId)
        {
            float minClearance = float.PositiveInfinity;
            bool hasSample = false;

            if (band != null && band.Samples != null && band.Samples.Count > 0)
            {
                for (int i = 0; i < band.Samples.Count; i++)
                {
                    PredictedOccupancySample sample = band.Samples[i];
                    float c = ComputePhysicalBoundaryClearance(
                        sample.PredictedPosition,
                        sample.UncertaintyRadius,
                        roomHalfWidth,
                        roomHalfHeight);

                    minClearance = Mathf.Min(minClearance, c);
                    hasSample = true;
                }
            }

            if (!hasSample && physicalUsers != null && userId >= 0 && userId < physicalUsers.Count && physicalUsers[userId] != null)
            {
                Vector3 pos = physicalUsers[userId].transform.position;
                minClearance = ComputePhysicalBoundaryClearance(pos, 0f, roomHalfWidth, roomHalfHeight);
            }

            return minClearance;
        }

        private static float ComputePhysicalBoundaryClearance(Vector3 position, float radius, float roomHalfWidth, float roomHalfHeight)
        {
            float dx = roomHalfWidth - Mathf.Abs(position.x) - radius;
            float dz = roomHalfHeight - Mathf.Abs(position.z) - radius;
            return Mathf.Min(dx, dz);
        }

        private static float ResolveUserRisk(int userId, float[,] pairwiseRisk)
        {
            int n = pairwiseRisk.GetLength(0);
            if (userId < 0 || userId >= n || n <= 1)
                return 0f;

            float sum = 0f;
            int count = 0;
            for (int j = 0; j < n; j++)
            {
                if (j == userId)
                    continue;

                sum += pairwiseRisk[userId, j];
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        private static float ResolveUserMinPairSeparation(int userId, float[,] pairwiseMinSep)
        {
            int n = pairwiseMinSep.GetLength(0);
            if (userId < 0 || userId >= n || n <= 1)
                return float.PositiveInfinity;

            float minValue = float.PositiveInfinity;
            for (int j = 0; j < n; j++)
            {
                if (j == userId)
                    continue;

                minValue = Mathf.Min(minValue, pairwiseMinSep[userId, j]);
            }

            return minValue;
        }

        private float ComputeMinOccupancySeparation(PredictedOccupancyBand a, PredictedOccupancyBand b)
        {
            if (a == null || b == null || a.Samples == null || b.Samples == null || a.Samples.Count == 0 || b.Samples.Count == 0)
                return float.PositiveInfinity;

            float minSeparation = float.PositiveInfinity;

            // Interface is intentionally generic (min occupancy separation).
            // Current implementation uses same-horizon approximation for cost control.
            const float horizonTolerance = 0.0001f;
            bool hasSameHorizonPairs = false;
            for (int i = 0; i < a.Samples.Count; i++)
            {
                PredictedOccupancySample sampleA = a.Samples[i];
                for (int j = 0; j < b.Samples.Count; j++)
                {
                    PredictedOccupancySample sampleB = b.Samples[j];
                    if (Mathf.Abs(sampleA.Horizon - sampleB.Horizon) > horizonTolerance)
                        continue;

                    float sep = ComputeSeparation(sampleA, sampleB);
                    minSeparation = Mathf.Min(minSeparation, sep);
                    hasSameHorizonPairs = true;
                }
            }

            // Robust fallback in case horizon lists are not aligned.
            if (!hasSameHorizonPairs)
            {
                for (int i = 0; i < a.Samples.Count; i++)
                {
                    for (int j = 0; j < b.Samples.Count; j++)
                    {
                        float sep = ComputeSeparation(a.Samples[i], b.Samples[j]);
                        minSeparation = Mathf.Min(minSeparation, sep);
                    }
                }
            }

            return minSeparation;
        }

        private static float ComputeSeparation(PredictedOccupancySample a, PredictedOccupancySample b)
        {
            Vector2 pa = new Vector2(a.PredictedPosition.x, a.PredictedPosition.z);
            Vector2 pb = new Vector2(b.PredictedPosition.x, b.PredictedPosition.z);
            return Vector2.Distance(pa, pb) - (a.UncertaintyRadius + b.UncertaintyRadius);
        }

        private DominantRiskType ResolveDominantRiskType(float wallRisk, float userRisk, float smoothRisk)
        {
            float maxValue = wallRisk;
            DominantRiskType dominant = DominantRiskType.Wall;

            if (userRisk > maxValue)
            {
                maxValue = userRisk;
                dominant = DominantRiskType.User;
            }

            if (smoothRisk > maxValue)
            {
                maxValue = smoothRisk;
                dominant = DominantRiskType.Smooth;
            }

            if (maxValue < _config.DominantRiskNoneThreshold)
                return DominantRiskType.None;

            float second = float.MinValue;
            if (dominant != DominantRiskType.Wall)
                second = Mathf.Max(second, wallRisk);
            if (dominant != DominantRiskType.User)
                second = Mathf.Max(second, userRisk);
            if (dominant != DominantRiskType.Smooth)
                second = Mathf.Max(second, smoothRisk);

            if (maxValue - second <= _config.DominantRiskMixedGap)
                return DominantRiskType.Mixed;

            return dominant;
        }

        private static float RiskFromClearance(float clearance, float safeClearance)
        {
            if (float.IsPositiveInfinity(clearance))
                return 0f;

            float denom = Mathf.Max(0.0001f, safeClearance);
            return Mathf.Clamp01((safeClearance - clearance) / denom);
        }

        private static float RiskFromSeparation(float separation, float safeSeparation)
        {
            if (float.IsPositiveInfinity(separation))
                return 0f;

            float denom = Mathf.Max(0.0001f, safeSeparation);
            return Mathf.Clamp01((safeSeparation - separation) / denom);
        }

        private static float DistancePointToPolygonBoundary(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            int count = polygon.Count;
            if (count < 2)
                return float.PositiveInfinity;

            float minDist = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % count];
                float dist = DistancePointToSegment(point, a, b);
                minDist = Mathf.Min(minDist, dist);
            }

            return minDist;
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq <= 0.000001f)
                return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 closest = a + t * ab;
            return Vector2.Distance(p, closest);
        }
    }
}
