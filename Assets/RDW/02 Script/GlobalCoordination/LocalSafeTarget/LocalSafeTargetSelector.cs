using System.Collections.Generic;
using UnityEngine;
using _GCM;

namespace RDW.Coordination.LocalSafeTarget
{
    /// <summary>
    /// Selects local safe target points within a Voronoi partition cell.
    /// Uses polar sampling and a multi-term cost-inspired score.
    /// </summary>
    public class LocalSafeTargetSelector
    {
        private const float Epsilon = 1e-5f;

        private LocalSafeTargetConfig config;
        private readonly List<Vector2> cachedCandidateSamples = new List<Vector2>();
        private readonly List<float> cachedSampleScores = new List<float>();

        public LocalSafeTargetSelector(LocalSafeTargetConfig cfg)
        {
            config = cfg;
        }

        public void UpdateConfig(LocalSafeTargetConfig newConfig)
        {
            config = newConfig;
        }

        /// <summary>
        /// Compatibility entry used by GlobalCoordinationManager.
        /// </summary>
        public LocalTargetResult SelectTargetForUser(
            int userId,
            Vector2 userPosition,
            Vector2 userHeading,
            List<Vector2> cellVertices,
            Vector2 cellCentroid,
            List<Vector2> allUserPositions,
            IReadOnlyCollection<int> neighborUserIds,
            List<PredictedOccupancyBand> occupancyBands,
            PartitionRiskFrame riskFrame)
        {
            Dictionary<int, PredictedOccupancyBand> occupancyByUserId = null;
            if (occupancyBands != null && occupancyBands.Count > 0)
            {
                occupancyByUserId = new Dictionary<int, PredictedOccupancyBand>();
                for (int i = 0; i < occupancyBands.Count; i++)
                {
                    PredictedOccupancyBand band = occupancyBands[i];
                    occupancyByUserId[band.UserId] = band;
                }
            }

            UserRiskMetrics userRiskMetrics = null;
            if (riskFrame?.PerUser != null)
            {
                for (int i = 0; i < riskFrame.PerUser.Count; i++)
                {
                    if (riskFrame.PerUser[i] != null && riskFrame.PerUser[i].UserId == userId)
                    {
                        userRiskMetrics = riskFrame.PerUser[i];
                        break;
                    }
                }
            }

            LocalSafeTargetResult selected = SelectTarget(
                userId,
                userPosition,
                userHeading,
                cellVertices,
                userRiskMetrics,
                null,
                occupancyByUserId,
                allUserPositions,
                neighborUserIds);

            return new LocalTargetResult
            {
                userId = userId,
                targetPoint = selected.targetPoint,
                hasSteeringDirection = selected.hasSteeringDirection,
                steeringDirection = selected.steeringDirection,
                totalScore = selected.totalScore,
                sampleCount = selected.sampleCount,
                fallbackUsed = selected.fallbackUsed,
                useBoundaryEscapeMaxCurvature = selected.useBoundaryEscapeMaxCurvature,
                boundaryEscapeDirection = selected.boundaryEscapeDirection,
                frameIndex = 0,
                timestamp = 0f
            };
        }

        /// <summary>
        /// Main entry point: select best safe target for a user within their Voronoi cell.
        /// </summary>
        public LocalSafeTargetResult SelectTarget(
            int userId,
            Vector2 userPosition,
            Vector2 userHeading,
            List<Vector2> cellVertices,
            UserRiskMetrics userRiskMetrics,
            PredictedOccupancyFrame occupancyFrame,
            Dictionary<int, PredictedOccupancyBand> allOccupancyByUserId,
            List<Vector2> allUserPositions,
            IReadOnlyCollection<int> neighborUserIds)
        {
            var result = new LocalSafeTargetResult
            {
                targetPoint = userPosition,
                hasSteeringDirection = false,
                steeringDirection = Vector2.zero,
                totalScore = float.MinValue,
                selfOpenComponent = 0f,
                frontMarginComponent = 0f,
                neighborImpactComponent = 0f,
                headingDevComponent = 0f,
                sampleCount = 0,
                fallbackUsed = false,
                useBoundaryEscapeMaxCurvature = false,
                boundaryEscapeDirection = Vector2.zero
            };

            if (cellVertices == null || cellVertices.Count < 3)
            {
                result.fallbackUsed = true;
                result.targetPoint = ComputeFallbackTarget(userPosition, userHeading);
                result.hasSteeringDirection = true;
                result.steeringDirection = userHeading.sqrMagnitude > Epsilon ? userHeading.normalized : Vector2.up;
                result.totalScore = 0f;
                return result;
            }

            GeneratePolarSamples(userPosition, userHeading, cellVertices);
            result.sampleCount = cachedCandidateSamples.Count;

            if (cachedCandidateSamples.Count == 0)
            {
                result.fallbackUsed = true;
                result.targetPoint = ComputeFallbackTargetByBoundaryGradient(
                    userId,
                    userPosition,
                    userHeading,
                    cellVertices,
                    allUserPositions,
                    allOccupancyByUserId,
                    out bool useBoundaryEscapeMaxCurvature,
                    out Vector2 boundaryEscapeDirection);
                result.useBoundaryEscapeMaxCurvature = useBoundaryEscapeMaxCurvature;
                result.boundaryEscapeDirection = boundaryEscapeDirection;
                result.hasSteeringDirection = boundaryEscapeDirection.sqrMagnitude > Epsilon;
                result.steeringDirection = result.hasSteeringDirection
                    ? boundaryEscapeDirection.normalized
                    : (userHeading.sqrMagnitude > Epsilon ? userHeading.normalized : Vector2.up);
                result.totalScore = 0f;
                return result;
            }

            cachedSampleScores.Clear();

            int bestIndex = -1;
            float bestScore = float.MinValue;
            float bestSelfOpen = 0f;
            float bestFrontMargin = 0f;
            float bestNeighborImpact = 0f;
            float bestHeadingDev = 0f;

            for (int i = 0; i < cachedCandidateSamples.Count; i++)
            {
                Vector2 sample = cachedCandidateSamples[i];
                float selfOpen;
                float frontMargin;
                float neighborImpact;
                float headingDev;

                float score = ScoreSample(
                    sample,
                    cellVertices,
                    userPosition,
                    userHeading,
                    userId,
                    allUserPositions,
                    neighborUserIds,
                    allOccupancyByUserId,
                    out selfOpen,
                    out frontMargin,
                    out neighborImpact,
                    out headingDev);

                cachedSampleScores.Add(score);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                    bestSelfOpen = selfOpen;
                    bestFrontMargin = frontMargin;
                    bestNeighborImpact = neighborImpact;
                    bestHeadingDev = headingDev;
                }
            }

            if (bestIndex >= 0)
            {
                result.targetPoint = cachedCandidateSamples[bestIndex];
                Vector2 bestDir = result.targetPoint - userPosition;
                if (bestDir.sqrMagnitude > Epsilon)
                {
                    result.hasSteeringDirection = true;
                    result.steeringDirection = bestDir.normalized;
                }
                else
                {
                    result.hasSteeringDirection = true;
                    result.steeringDirection = userHeading.sqrMagnitude > Epsilon ? userHeading.normalized : Vector2.up;
                }
                result.totalScore = bestScore;
                result.selfOpenComponent = bestSelfOpen;
                result.frontMarginComponent = bestFrontMargin;
                result.neighborImpactComponent = bestNeighborImpact;
                result.headingDevComponent = bestHeadingDev;
                result.useBoundaryEscapeMaxCurvature = false;
                result.boundaryEscapeDirection = Vector2.zero;
            }
            else
            {
                result.fallbackUsed = true;
                result.targetPoint = ComputeFallbackTargetByBoundaryGradient(
                    userId,
                    userPosition,
                    userHeading,
                    cellVertices,
                    allUserPositions,
                    allOccupancyByUserId,
                    out bool useBoundaryEscapeMaxCurvature,
                    out Vector2 boundaryEscapeDirection);
                result.useBoundaryEscapeMaxCurvature = useBoundaryEscapeMaxCurvature;
                result.boundaryEscapeDirection = boundaryEscapeDirection;
                result.hasSteeringDirection = boundaryEscapeDirection.sqrMagnitude > Epsilon;
                result.steeringDirection = result.hasSteeringDirection
                    ? boundaryEscapeDirection.normalized
                    : (userHeading.sqrMagnitude > Epsilon ? userHeading.normalized : Vector2.up);
                result.totalScore = 0f;
            }

            return result;
        }

        /// <summary>
        /// Polar sampling around user heading: K = angleCount * radiusCount (default 11 * 3 = 33).
        /// g(r,phi) = p_i + r * [cos(theta_i+phi), sin(theta_i+phi)]
        /// </summary>
        private void GeneratePolarSamples(
            Vector2 userPosition,
            Vector2 userHeading,
            List<Vector2> cellBoundary)
        {
            cachedCandidateSamples.Clear();

            int angleCount = Mathf.Max(1, config.AngleSampleCount);
            int radiusCount = Mathf.Max(1, config.RadiusSampleCount);

            Vector2 heading = userHeading.sqrMagnitude > Epsilon ? userHeading.normalized : Vector2.up;
            float theta = Mathf.Atan2(heading.y, heading.x);
            float halfFanRad = Mathf.Max(0f, config.FanHalfAngleDegrees) * Mathf.Deg2Rad;

            for (int rIndex = 0; rIndex < radiusCount; rIndex++)
            {
                float r = radiusCount == 1
                    ? config.SearchRadiusMax
                    : Mathf.Lerp(config.SearchRadiusMin, config.SearchRadiusMax, rIndex / (float)(radiusCount - 1));

                for (int aIndex = 0; aIndex < angleCount; aIndex++)
                {
                    float phi = angleCount == 1
                        ? 0f
                        : Mathf.Lerp(-halfFanRad, halfFanRad, aIndex / (float)(angleCount - 1));

                    float angle = theta + phi;
                    Vector2 sample = userPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;

                    if (IsValidCandidateSample(sample, cellBoundary))
                    {
                        cachedCandidateSamples.Add(sample);
                    }
                }
            }
        }

        private bool IsValidCandidateSample(Vector2 point, List<Vector2> cellBoundary)
        {
            if (!IsPointInPolygon(point, cellBoundary))
                return false;

            float boundaryClearance = ComputeDistanceToPolygonBoundary(point, cellBoundary);
            if (boundaryClearance < config.BoundaryBufferMin)
                return false;

            return true;
        }

        /// <summary>
        /// Score follows four terms proposed by design:
        /// 1) self-open      (prefer larger obstacle clearance)
        /// 2) front-margin   (prefer larger forward clearance)
        /// 3) neighbor-impact(prefer less compression on neighbors)
        /// 4) heading-dev    (prefer smaller heading deviation)
        ///
        /// We maximize utility form:
        /// score = w_open*d_obs + w_front*m_front - w_neighbor*impact - w_heading*|delta_phi|
        /// </summary>
        private float ScoreSample(
            Vector2 sample,
            List<Vector2> cellBoundary,
            Vector2 userPosition,
            Vector2 userHeading,
            int userId,
            List<Vector2> allUserPositions,
            IReadOnlyCollection<int> neighborUserIds,
            Dictionary<int, PredictedOccupancyBand> occupancyByUserId,
            out float selfOpen,
            out float frontMargin,
            out float neighborImpact,
            out float headingDev)
        {
            // Raw terms (with physical units)
            float rawSelfOpen = ComputeDistanceToObstacle(sample, cellBoundary, userId, occupancyByUserId);

            Vector2 direction = sample - userPosition;
            if (direction.sqrMagnitude > Epsilon)
            {
                direction.Normalize();
            }
            else
            {
                direction = userHeading.sqrMagnitude > Epsilon ? userHeading.normalized : Vector2.up;
            }

            float rawFrontMargin = ComputeForwardMargin(sample, direction, cellBoundary, userId, occupancyByUserId);

            float rawNeighborImpact = ComputeNeighborImpact(sample, userPosition, userId, allUserPositions, neighborUserIds);

            Vector2 heading = userHeading.sqrMagnitude > Epsilon ? userHeading.normalized : Vector2.up;
            float signedAngleDeg = Vector2.SignedAngle(heading, direction);
            float rawHeadingDev = Mathf.Abs(signedAngleDeg) * Mathf.Deg2Rad;

            // Dimensionless normalization
            float distanceScale = Mathf.Max(0.10f, config.SearchRadiusMax);
            int neighborCount = Mathf.Max(1, neighborUserIds != null ? neighborUserIds.Count : 0);
            float headingScale = Mathf.Max(5f * Mathf.Deg2Rad, config.FanHalfAngleDegrees * Mathf.Deg2Rad);

            selfOpen = NormalizePositive(rawSelfOpen, distanceScale);
            frontMargin = NormalizePositive(rawFrontMargin, distanceScale);
            neighborImpact = NormalizeNeighborImpact(rawNeighborImpact, distanceScale, neighborCount);
            headingDev = NormalizePositive(rawHeadingDev, headingScale);

            return
                config.WeightSelfOpen * selfOpen +
                config.WeightFrontMargin * frontMargin -
                config.WeightNeighborImpact * neighborImpact -
                config.WeightHeadingDeviation * headingDev;
        }

        private static float NormalizePositive(float value, float scale)
        {
            if (scale <= Epsilon)
                return 0f;

            return Mathf.Clamp01(Mathf.Max(0f, value) / scale);
        }

        private static float NormalizeNeighborImpact(float impact, float distanceScale, int neighborCount)
        {
            if (neighborCount <= 0 || distanceScale <= Epsilon)
                return 0f;

            // raw impact has unit 1/m; multiply by m to make it dimensionless,
            // and average by neighbor count to keep multi-user scale stable.
            float normalized = impact * distanceScale / neighborCount;
            return Mathf.Clamp01(Mathf.Max(0f, normalized));
        }

        private float ComputeDistanceToObstacle(
            Vector2 point,
            List<Vector2> cellBoundary,
            int userId,
            Dictionary<int, PredictedOccupancyBand> occupancyByUserId)
        {
            float minDistance = ComputeDistanceToPolygonBoundary(point, cellBoundary);

            if (occupancyByUserId != null)
            {
                foreach (var kvp in occupancyByUserId)
                {
                    if (kvp.Key == userId)
                        continue;

                    float distToOccupancy = ComputeDistanceToOccupancyBand(point, kvp.Value);
                    if (distToOccupancy < minDistance)
                        minDistance = distToOccupancy;
                }
            }

            return Mathf.Max(0f, minDistance);
        }

        private float ComputeForwardMargin(
            Vector2 origin,
            Vector2 direction,
            List<Vector2> cellBoundary,
            int userId,
            Dictionary<int, PredictedOccupancyBand> occupancyByUserId)
        {
            float margin = RaycastToPolygonBoundary(origin, direction, cellBoundary);

            // Optional dynamic obstacle handling: approximate nearest hit along ray to occupancy bands.
            if (occupancyByUserId != null)
            {
                foreach (var kvp in occupancyByUserId)
                {
                    if (kvp.Key == userId)
                        continue;

                    float occupancyHit = ApproximateRaycastToOccupancyBand(origin, direction, kvp.Value, margin);
                    if (occupancyHit < margin)
                        margin = occupancyHit;
                }
            }

            return Mathf.Max(0f, margin);
        }

        private float ComputeNeighborImpact(
            Vector2 sample,
            Vector2 selfPosition,
            int userId,
            List<Vector2> allUserPositions,
            IReadOnlyCollection<int> neighborUserIds)
        {
            if (allUserPositions == null || allUserPositions.Count == 0 || neighborUserIds == null || neighborUserIds.Count == 0)
                return 0f;

            Vector2 moveDir = sample - selfPosition;
            if (moveDir.sqrMagnitude <= Epsilon)
                return 0f;

            moveDir.Normalize();
            float impact = 0f;

            foreach (int j in neighborUserIds)
            {
                if (j == userId)
                    continue;
                if (j < 0 || j >= allUserPositions.Count)
                    continue;

                Vector2 toNeighbor = allUserPositions[j] - selfPosition;
                float distance = toNeighbor.magnitude;
                if (distance <= Epsilon)
                    continue;

                Vector2 eij = toNeighbor / distance;
                float cij = Mathf.Max(0f, Vector2.Dot(moveDir, eij));

                float denom = Mathf.Max(Epsilon, distance - config.NeighborSafetyBuffer);
                impact += cij / denom;
            }

            return impact;
        }

        private float ComputeDistanceToPolygonBoundary(Vector2 point, List<Vector2> polygon)
        {
            float minDistance = float.MaxValue;
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % n];
                float dist = ComputePointToSegmentDistance(point, a, b);
                minDistance = Mathf.Min(minDistance, dist);
            }

            return minDistance < float.MaxValue ? minDistance : 0f;
        }

        private float RaycastToPolygonBoundary(Vector2 origin, Vector2 dir, List<Vector2> polygon)
        {
            float minT = float.MaxValue;
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % n];

                if (TryRaySegmentIntersection(origin, dir, a, b, out float t) && t >= 0f)
                {
                    if (t < minT)
                        minT = t;
                }
            }

            if (minT == float.MaxValue)
                return 0f;

            return minT;
        }

        private bool TryRaySegmentIntersection(Vector2 p, Vector2 r, Vector2 a, Vector2 b, out float t)
        {
            t = 0f;
            Vector2 s = b - a;
            float rxs = Cross(r, s);
            if (Mathf.Abs(rxs) < Epsilon)
                return false;

            Vector2 ap = a - p;
            float tRay = Cross(ap, s) / rxs;
            float uSeg = Cross(ap, r) / rxs;

            if (tRay < 0f)
                return false;
            if (uSeg < 0f || uSeg > 1f)
                return false;

            t = tRay;
            return true;
        }

        private float ApproximateRaycastToOccupancyBand(
            Vector2 origin,
            Vector2 dir,
            PredictedOccupancyBand band,
            float maxDistance)
        {
            if (band == null || band.Segments == null || band.Segments.Count == 0 || maxDistance <= 0f)
                return float.MaxValue;

            const float step = 0.05f;
            float t = 0f;

            while (t <= maxDistance)
            {
                Vector2 probe = origin + dir * t;
                float dist = ComputeDistanceToOccupancyBand(probe, band);
                if (dist <= 0.01f)
                    return t;

                t += step;
            }

            return float.MaxValue;
        }

        private float ComputePointToSegmentDistance(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 pa = point - segmentStart;
            Vector2 ba = segmentEnd - segmentStart;
            float baSqr = Vector2.Dot(ba, ba);

            if (baSqr <= Epsilon)
                return Vector2.Distance(point, segmentStart);

            float t = Vector2.Dot(pa, ba) / baSqr;
            t = Mathf.Clamp01(t);

            Vector2 closest = segmentStart + ba * t;
            return Vector2.Distance(point, closest);
        }

        private float ComputeDistanceToOccupancyBand(Vector2 point, PredictedOccupancyBand band)
        {
            if (band == null || band.Segments == null || band.Segments.Count == 0)
                return float.MaxValue;

            float minDistance = float.MaxValue;

            foreach (var segment in band.Segments)
            {
                Vector2 segStart = new Vector2(segment.StartCenter.x, segment.StartCenter.z);
                Vector2 segEnd = new Vector2(segment.EndCenter.x, segment.EndCenter.z);

                float dist = ComputePointToSegmentDistance(point, segStart, segEnd);
                float avgRadius = (segment.StartRadius + segment.EndRadius) * 0.5f;
                dist = Mathf.Max(0f, dist - avgRadius);

                minDistance = Mathf.Min(minDistance, dist);
            }

            return minDistance < float.MaxValue ? minDistance : 0f;
        }

        private bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            int n = polygon.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 pi = polygon[i];
                Vector2 pj = polygon[j];

                if ((pi.y > point.y) != (pj.y > point.y) &&
                    point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        /// <summary>
        /// Fallback for constrained situations:
        /// output boundary-escape direction marker for redirector max-curvature mode.
        /// </summary>
        private Vector2 ComputeFallbackTargetByBoundaryGradient(
            int userId,
            Vector2 userPosition,
            Vector2 userHeading,
            List<Vector2> cellBoundary,
            List<Vector2> allUserPositions,
            Dictionary<int, PredictedOccupancyBand> occupancyByUserId,
            out bool useBoundaryEscapeMaxCurvature,
            out Vector2 boundaryEscapeDirection)
        {
            useBoundaryEscapeMaxCurvature = false;
            boundaryEscapeDirection = Vector2.zero;

            if (cellBoundary == null || cellBoundary.Count < 3)
                return ComputeFallbackTarget(userPosition, userHeading);

            if (!TryComputeNearestBoundaryGradient(userPosition, cellBoundary, out Vector2 boundaryGradient, out float boundaryClearance))
                return ComputeFallbackTarget(userPosition, userHeading);

            if (boundaryGradient.sqrMagnitude <= Epsilon)
                return ComputeFallbackTarget(userPosition, userHeading);

            boundaryGradient.Normalize();
            useBoundaryEscapeMaxCurvature = true;
            boundaryEscapeDirection = boundaryGradient;

            float step = Mathf.Clamp(boundaryClearance * 0.5f, 0.1f, 0.35f);
            Vector2 target = userPosition + boundaryGradient * step;
            if (!IsPointInPolygon(target, cellBoundary))
                target = userPosition + boundaryGradient * 0.1f;

            return target;
        }

        private bool TryComputeNearestBoundaryGradient(
            Vector2 point,
            List<Vector2> polygon,
            out Vector2 gradient,
            out float minDistance)
        {
            gradient = Vector2.zero;
            minDistance = float.MaxValue;

            if (polygon == null || polygon.Count < 2)
                return false;

            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % n];
                Vector2 closest = ClosestPointOnSegment(point, a, b);
                Vector2 away = point - closest;
                float dist = away.magnitude;
                if (dist < minDistance)
                {
                    minDistance = dist;
                    gradient = dist > Epsilon ? away / dist : away;
                }
            }

            if (minDistance == float.MaxValue)
            {
                gradient = Vector2.zero;
                minDistance = 0f;
                return false;
            }

            return true;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Vector2.Dot(ab, ab);
            if (denom <= Epsilon)
                return a;

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denom);
            return a + t * ab;
        }

        private Vector2 ComputeFallbackTarget(Vector2 userPosition, Vector2 userHeading)
        {
            Vector2 heading = userHeading.sqrMagnitude > Epsilon ? userHeading.normalized : Vector2.up;
            return userPosition + heading * config.SearchRadiusMin;
        }
    }
}
