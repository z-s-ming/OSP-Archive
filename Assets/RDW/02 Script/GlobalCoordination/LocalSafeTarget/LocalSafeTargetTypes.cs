using UnityEngine;

namespace RDW.Coordination.LocalSafeTarget
{
    /// <summary>
    /// Storage result for local safe target (frame-level snapshot)
    /// </summary>
    public struct LocalTargetResult
    {
        public int userId;
        public Vector2 targetPoint;
        public bool hasSteeringDirection;
        public Vector2 steeringDirection;
        public float totalScore;
        public int sampleCount;
        public bool fallbackUsed;
        public bool useBoundaryEscapeMaxCurvature;
        public Vector2 boundaryEscapeDirection;
        public int frameIndex;
        public float timestamp;

        // Backward-compatible aliases used by existing manager/visualizer code.
        public bool HasValidTarget => sampleCount > 0 || fallbackUsed;
        public Vector2 TargetPosition => targetPoint;
        public float BestScore => totalScore;
    }

    /// <summary>
    /// Result of local safe target selection for a single user
    /// </summary>
    public struct LocalSafeTargetResult
    {
        public Vector2 targetPoint;
        public bool hasSteeringDirection;
        public Vector2 steeringDirection;
        public float totalScore;

        // Score contribution breakdown
        public float selfOpenComponent;
        public float frontMarginComponent;
        public float neighborImpactComponent;
        public float headingDevComponent;

        // Metadata
        public int sampleCount;
        public bool fallbackUsed;
        public bool useBoundaryEscapeMaxCurvature;
        public Vector2 boundaryEscapeDirection;
    }

    /// <summary>
    /// Configuration for local safe target selection
    /// </summary>
    public struct LocalSafeTargetConfig
    {
        // Sampling geometry
        public float FanHalfAngleDegrees;
        public float SearchRadiusMin;
        public float SearchRadiusMax;
        public float BoundaryBufferMin;
        public int AngleSampleCount;
        public int RadiusSampleCount;

        // Scoring weights
        public float WeightSelfOpen;
        public float WeightFrontMargin;
        public float WeightNeighborImpact;
        public float WeightHeadingDeviation;

        // Neighbor interaction
        public float NeighborSafetyBuffer;

        public static LocalSafeTargetConfig GetDefaults()
        {
            return new LocalSafeTargetConfig
            {
                FanHalfAngleDegrees = 55f,
                SearchRadiusMin = 1.5f,
                SearchRadiusMax = 2.0f,
                BoundaryBufferMin = 0.30f,
                AngleSampleCount = 11,
                RadiusSampleCount = 3,
                WeightSelfOpen = 1.0f,
                WeightFrontMargin = 0.8f,
                WeightNeighborImpact = 1.2f,
                WeightHeadingDeviation = 0.6f,
                NeighborSafetyBuffer = 0.35f
            };
        }
    }
}
