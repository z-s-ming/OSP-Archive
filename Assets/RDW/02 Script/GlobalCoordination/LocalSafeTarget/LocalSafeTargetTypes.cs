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
        public float totalScore;
        public int sampleCount;
        public bool fallbackUsed;
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
        public float totalScore;
        
        // Score contribution breakdown
        public float boundaryDistanceComponent;
        public float occupancyDistanceComponent;
        public float userDistanceComponent;
        
        // Metadata
        public int sampleCount;
        public bool fallbackUsed;
    }

    /// <summary>
    /// Configuration for local safe target selection
    /// </summary>
    public struct LocalSafeTargetConfig
    {
        // Geometry parameters
        public float FanHalfAngleDegrees;
        public float SearchRadiusMin;
        public float SearchRadiusMax;
        public float BoundaryBufferMin;
        public float BoundaryBufferMax;
        public float GridResolutionMin;
        public float GridResolutionMax;

        // Scoring weights
        public float WeightBoundaryDist;
        public float WeightOccupancyDist;
        public float WeightDistancePenalty;

        // Adaptive sampling
        public float SampleDensityPerM2;      // ρ=55 pts/m²
        public int MinSamplesPerUser;         // n_min=24
        public int MaxSamplesPerUser;         // n_max=120

        public static LocalSafeTargetConfig GetDefaults()
        {
            return new LocalSafeTargetConfig
            {
                FanHalfAngleDegrees = 55f,
                SearchRadiusMin = 1.5f,
                SearchRadiusMax = 2.0f,
                BoundaryBufferMin = 0.3f,
                BoundaryBufferMax = 0.5f,
                GridResolutionMin = 0.15f,
                GridResolutionMax = 0.25f,
                WeightBoundaryDist = 1.0f,
                WeightOccupancyDist = 1.5f,
                WeightDistancePenalty = 0.4f,
                SampleDensityPerM2 = 55f,
                MinSamplesPerUser = 24,
                MaxSamplesPerUser = 120
            };
        }
    }
}
