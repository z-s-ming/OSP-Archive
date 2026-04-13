using UnityEngine;
using System.Collections.Generic;
using _GCM;

namespace RDW.Coordination.LocalSafeTarget
{
    /// <summary>
    /// Selects local safe target points within a Voronoi partition cell
    /// Uses adaptive grid sampling and multi-term scoring formula
    /// </summary>
    public class LocalSafeTargetSelector
    {
        private LocalSafeTargetConfig config;
        private List<Vector2> cachedCandidateSamples = new List<Vector2>();
        private List<float> cachedSampleScores = new List<float>();

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
                occupancyByUserId);

            return new LocalTargetResult
            {
                userId = userId,
                targetPoint = selected.targetPoint,
                totalScore = selected.totalScore,
                sampleCount = selected.sampleCount,
                fallbackUsed = selected.fallbackUsed,
                frameIndex = 0,
                timestamp = 0f
            };
        }

        /// <summary>
        /// Main entry point: select best safe target for a user within their Voronoi cell
        /// </summary>
        public LocalSafeTargetResult SelectTarget(
            int userId,
            Vector2 userPosition,
            Vector2 userHeading,
            List<Vector2> cellVertices,
            UserRiskMetrics userRiskMetrics,
            PredictedOccupancyFrame occupancyFrame,
            Dictionary<int, PredictedOccupancyBand> allOccupancyByUserId)
        {
            var result = new LocalSafeTargetResult
            {
                targetPoint = userPosition,
                totalScore = float.MinValue,
                boundaryDistanceComponent = 0f,
                occupancyDistanceComponent = 0f,
                userDistanceComponent = 0f,
                sampleCount = 0,
                fallbackUsed = false
            };

            // Validate cell geometry
            if (cellVertices == null || cellVertices.Count < 3)
            {
                result.fallbackUsed = true;
                result.targetPoint = ComputeFallbackTarget(userPosition, userHeading);
                return result;
            }

            // Step 1: Compute candidate region (cell ∩ forwardFan ∩ buffer inset)
            var candidateRegion = ComputeCandidateRegion(cellVertices, userPosition, userHeading);
            if (candidateRegion == null || candidateRegion.Count < 3)
            {
                result.fallbackUsed = true;
                result.targetPoint = ComputeFallbackTarget(userPosition, userHeading);
                return result;
            }

            // Step 2: Generate adaptive grid samples
            float regionArea = ComputePolygonArea(candidateRegion);
            GenerateAdaptiveSamples(candidateRegion, regionArea, userPosition, userHeading, cellVertices);
            result.sampleCount = cachedCandidateSamples.Count;

            if (cachedCandidateSamples.Count == 0)
            {
                result.fallbackUsed = true;
                result.targetPoint = ComputeFallbackTarget(userPosition, userHeading);
                return result;
            }

            // Step 3: Score all samples and select best
            cachedSampleScores.Clear();
            for (int i = 0; i < cachedCandidateSamples.Count; i++)
            {
                Vector2 sample = cachedCandidateSamples[i];
                float score = ScoreSample(
                    sample,
                    cellVertices,
                    userPosition,
                    userId,
                    allOccupancyByUserId);
                cachedSampleScores.Add(score);
            }

            // Step 4: Find best sample
            int bestIndex = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < cachedSampleScores.Count; i++)
            {
                if (cachedSampleScores[i] > bestScore)
                {
                    bestScore = cachedSampleScores[i];
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                result.targetPoint = cachedCandidateSamples[bestIndex];
                result.totalScore = bestScore;
                // TODO: Store component breakdowns for analysis
            }
            else
            {
                result.fallbackUsed = true;
                result.targetPoint = ComputeFallbackTarget(userPosition, userHeading);
            }

            return result;
        }

        /// <summary>
        /// Compute candidate region: cell ∩ forwardFan ∩ buffer inset
        /// </summary>
        private List<Vector2> ComputeCandidateRegion(List<Vector2> cellVertices, Vector2 userPos, Vector2 userHeading)
        {
            // Keep the candidate polygon identical to the Voronoi cell; geometric constraints
            // are enforced per-sample to avoid malformed inset geometry pushing points outside.
            if (cellVertices == null || cellVertices.Count < 3)
                return null;

            return cellVertices;
        }

        /// <summary>
        /// Inset polygon boundary by a given distance (move inward)
        /// Simple version: offset all edges inward
        /// </summary>
        private List<Vector2> InsetPolygon(List<Vector2> vertices, float insetDistance)
        {
            if (vertices == null || vertices.Count < 3)
                return null;

            var insetVertices = new List<Vector2>();
            int n = vertices.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 prev = vertices[(i - 1 + n) % n];
                Vector2 curr = vertices[i];
                Vector2 next = vertices[(i + 1) % n];

                // Compute inward normal at current vertex
                Vector2 edge1 = (curr - prev).normalized;
                Vector2 edge2 = (next - curr).normalized;

                // Rotate 90° counterclockwise to get inward normal
                Vector2 normal1 = new Vector2(-edge1.y, edge1.x);
                Vector2 normal2 = new Vector2(-edge2.y, edge2.x);

                // Average normals and normalize
                Vector2 avgNormal = (normal1 + normal2).normalized;

                // Offset vertex inward
                insetVertices.Add(curr + avgNormal * insetDistance);
            }

            return insetVertices;
        }

        /// <summary>
        /// Create forward fan geometry (cone in direction of userHeading)
        /// Returns list of vertices representing the fan
        /// </summary>
        private List<Vector2> CreateForwardFan(Vector2 center, Vector2 heading, float angleDegrees, float radius)
        {
            var fan = new List<Vector2> { center };

            float halfAngleDeg = angleDegrees / 2f;
            float halfAngleRad = halfAngleDeg * Mathf.Deg2Rad;
            float headingAngleRad = Mathf.Atan2(heading.y, heading.x);

            // Left arc
            float leftAngleRad = headingAngleRad + halfAngleRad;
            for (int i = 0; i <= 16; i++)  // 16 points on arc
            {
                float angle = leftAngleRad - (halfAngleRad * 2f * i / 16f);
                fan.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }

            // Right arc
            float rightAngleRad = headingAngleRad - halfAngleRad;
            fan.Add(center + new Vector2(Mathf.Cos(rightAngleRad), Mathf.Sin(rightAngleRad)) * radius);

            return fan;
        }

        /// <summary>
        /// Check if point is within forward fan cone
        /// </summary>
        private bool IsPointInForwardFan(Vector2 point, Vector2 center, Vector2 heading, float angleDegrees, float radius)
        {
            // Distance check
            float distToCenter = Vector2.Distance(point, center);
            if (distToCenter > radius)
                return false;

            // Angle check
            float halfAngleDeg = angleDegrees / 2f;
            Vector2 toPoint = (point - center).normalized;
            float angleToPoint = Vector2.Angle(heading, toPoint);

            return angleToPoint <= halfAngleDeg;
        }

        /// <summary>
        /// Generate adaptive grid of candidate points
        /// Density: n = clamp(ρ * A_eff, n_min, n_max)
        /// </summary>
        private void GenerateAdaptiveSamples(
            List<Vector2> candidateRegion,
            float regionArea,
            Vector2 userPosition,
            Vector2 userHeading,
            List<Vector2> cellBoundary)
        {
            cachedCandidateSamples.Clear();

            // Compute adaptive grid density
            float pointCount = config.SampleDensityPerM2 * regionArea;
            int n = Mathf.Clamp((int)pointCount, config.MinSamplesPerUser, config.MaxSamplesPerUser);

            // For now, use simple regular grid within bounding box
            // TODO: More sophisticated grid that respects polygon boundary
            if (candidateRegion.Count < 3)
                return;

            // Compute bounding box
            Vector2 minCorner = candidateRegion[0];
            Vector2 maxCorner = candidateRegion[0];
            foreach (var v in candidateRegion)
            {
                minCorner = Vector2.Min(minCorner, v);
                maxCorner = Vector2.Max(maxCorner, v);
            }

            float rawGridSpacing = Mathf.Sqrt(Mathf.Max(1e-4f, regionArea) / Mathf.Max(1, n));
            float gridSpacing = Mathf.Clamp(rawGridSpacing, config.GridResolutionMin, config.GridResolutionMax);

            for (float x = minCorner.x; x <= maxCorner.x; x += gridSpacing)
            {
                for (float y = minCorner.y; y <= maxCorner.y; y += gridSpacing)
                {
                    Vector2 point = new Vector2(x, y);

                    // A sample is valid only if it respects both geometry and safety constraints.
                    if (IsValidCandidateSample(point, candidateRegion, cellBoundary, userPosition, userHeading))
                    {
                        cachedCandidateSamples.Add(point);
                    }
                }
            }
        }

        private bool IsValidCandidateSample(
            Vector2 point,
            List<Vector2> candidateRegion,
            List<Vector2> cellBoundary,
            Vector2 userPosition,
            Vector2 userHeading)
        {
            if (!IsPointInPolygon(point, candidateRegion))
                return false;

            Vector2 heading = userHeading.sqrMagnitude > 1e-6f ? userHeading.normalized : Vector2.up;

            float distToUser = Vector2.Distance(point, userPosition);
            if (distToUser < config.SearchRadiusMin || distToUser > config.SearchRadiusMax)
                return false;

            if (!IsPointInForwardFan(point, userPosition, heading, config.FanHalfAngleDegrees * 2f, config.SearchRadiusMax))
                return false;

            float boundaryClearance = ComputeDistanceToPolygonBoundary(point, cellBoundary);
            if (boundaryClearance < config.BoundaryBufferMin)
                return false;

            return true;
        }

        /// <summary>
        /// Score a candidate point using multi-term formula:
        /// LocalScore(q) = η₁·dist(q,∂Cᵢ) + η₂·min_j≠ᵢ dist(q,Oⱼ) - η₃·‖q-pᵢ‖
        /// </summary>
        private float ScoreSample(
            Vector2 sample,
            List<Vector2> cellBoundary,
            Vector2 userPosition,
            int userId,
            Dictionary<int, PredictedOccupancyBand> occupancyByUserId)
        {
            // Term 1: Distance to nearest cell boundary (inward distance)
            float boundaryDistance = ComputeDistanceToPolygonBoundary(sample, cellBoundary);

            // Term 2: Distance to nearest other user's occupancy band
            float occupancyDistance = 0f;
            if (occupancyByUserId != null && occupancyByUserId.Count > 0)
            {
                occupancyDistance = float.MaxValue;
                foreach (var kvp in occupancyByUserId)
                {
                    if (kvp.Key == userId)
                        continue;

                    float distToOccupancy = ComputeDistanceToOccupancyBand(sample, kvp.Value);
                    occupancyDistance = Mathf.Min(occupancyDistance, distToOccupancy);
                }

                if (occupancyDistance == float.MaxValue)
                    occupancyDistance = 0f;
            }

            // Term 3: Distance to user position (negative weight: prefer points far from user)
            float userDistance = Vector2.Distance(sample, userPosition);

            // Combine with weights
            float score = 
                config.WeightBoundaryDist * boundaryDistance +
                config.WeightOccupancyDist * occupancyDistance -
                config.WeightDistancePenalty * userDistance;

            return score;
        }

        /// <summary>
        /// Compute distance from point to nearest polygon boundary
        /// </summary>
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

        /// <summary>
        /// Compute distance from point to line segment
        /// </summary>
        private float ComputePointToSegmentDistance(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 pa = point - segmentStart;
            Vector2 ba = segmentEnd - segmentStart;
            float baSqr = Vector2.Dot(ba, ba);

            if (baSqr == 0f)
                return Vector2.Distance(point, segmentStart);

            float t = Vector2.Dot(pa, ba) / baSqr;
            t = Mathf.Clamp01(t);

            Vector2 closest = segmentStart + ba * t;
            return Vector2.Distance(point, closest);
        }

        /// <summary>
        /// Compute distance from point to occupancy band
        /// Band consists of line segments with uncertainty radius
        /// </summary>
        private float ComputeDistanceToOccupancyBand(Vector2 point, PredictedOccupancyBand band)
        {
            if (band == null || band.Segments == null || band.Segments.Count == 0)
                return float.MaxValue;

            float minDistance = float.MaxValue;

            foreach (var segment in band.Segments)
            {
                // Convert 3D segment to 2D
                Vector2 segStart = new Vector2(segment.StartCenter.x, segment.StartCenter.z);
                Vector2 segEnd = new Vector2(segment.EndCenter.x, segment.EndCenter.z);
                
                // Compute distance to segment (line)
                float dist = ComputePointToSegmentDistance(point, segStart, segEnd);
                
                // Subtract average radius to account for capsule volume
                float avgRadius = (segment.StartRadius + segment.EndRadius) * 0.5f;
                dist = Mathf.Max(0f, dist - avgRadius);
                
                minDistance = Mathf.Min(minDistance, dist);
            }

            return minDistance < float.MaxValue ? minDistance : 0f;
        }

        /// <summary>
        /// Check if point is inside polygon using ray casting
        /// </summary>
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

        /// <summary>
        /// Compute area of polygon using shoelace formula
        /// </summary>
        private float ComputePolygonArea(List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return 0f;

            float area = 0f;
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % n];
                area += current.x * next.y - next.x * current.y;
            }

            return Mathf.Abs(area) * 0.5f;
        }

        /// <summary>
        /// Fallback target: project user position forward along heading
        /// </summary>
        private Vector2 ComputeFallbackTarget(Vector2 userPosition, Vector2 userHeading)
        {
            return userPosition;
        }
    }
}
