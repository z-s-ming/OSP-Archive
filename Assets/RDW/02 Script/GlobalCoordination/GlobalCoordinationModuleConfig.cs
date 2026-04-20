using System;
using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    [Serializable]
    public class GlobalCoordinationModuleConfig
    {
        public VelocityModuleConfig Velocity = new VelocityModuleConfig();
        public PredictionModuleConfig Prediction = new PredictionModuleConfig();
        public PredictiveOccupancyModuleConfig PredictiveOccupancy = new PredictiveOccupancyModuleConfig();
        public PartitionRiskModuleConfig PartitionRisk = new PartitionRiskModuleConfig();
        public PartitionUpdateModuleConfig PartitionUpdate = new PartitionUpdateModuleConfig();
        public LocalSafeTargetModuleConfig LocalSafeTarget = new LocalSafeTargetModuleConfig();
    }

    [Serializable]
    public class VelocityModuleConfig
    {
        public bool UseVelocityOffset = false;
        public float AlphaMax = 0.15f;
        public float VMax = 0.6f;
        public float VelocityToOffsetFactor = 0.3f;
        public float MaxVelocityOffsetDist = 0.5f;
        public float StopThreshold = 0.05f;
    }

    [Serializable]
    public class PredictionModuleConfig
    {
        public bool EnableEvaluation = false;
        public bool ExportResolvedSamples = false;
        public int SampleEveryNFrames = 10;
        public string TrajectoryModeLabel = "DefaultMode";
        public List<float> EvaluationHorizons = new List<float> { 0.3f, 0.5f, 1.0f, 1.5f };
    }

    [Serializable]
    public class PredictiveOccupancyModuleConfig
    {
        public bool Enable = false;
        public List<float> Horizons = new List<float> { 0.3f, 0.5f, 1.0f, 1.5f };
        public bool EnableGizmos = false;
        public float GizmoHeight = 0.03f;

        public float TrustedHorizonMeanErrorThreshold = 0.60f;
        public float TrustedHorizonStdErrorThreshold = 0.45f;
        public int TrustedHorizonMaxCensoredCount = 0;
    }

    [Serializable]
    public class PartitionRiskModuleConfig
    {
        public bool EnableEvaluation = false;
        public bool EnableLogging = false;
        public bool EnableVisualization = false;
        public int OutputEveryNFrames = 10;

        public float CellBoundarySafeClearance = 0.35f;
        public float PhysicalBoundarySafeClearance = 0.50f;
        public float PairSafeSeparation = 0.40f;
        public float SeedDeltaReference = 0.25f;

        public float WeightCellBoundary = 0.40f;
        public float WeightUser = 0.40f;
        public float WeightSmooth = 0.20f;

        public float AdjacencyThreshold = 0.35f;
        public float DominantNoneThreshold = 0.10f;
        public float DominantMixedGap = 0.08f;
    }

    [Serializable]
    public class PartitionUpdateModuleConfig
    {
        public bool EnableRiskDrivenUpdate = false;
        public float TauCellRiskEnter = 0.60f;
        public float TauCellRiskExit = 0.50f;
        public float TauNeighborRiskEnter = 0.60f;
        public float TauNeighborRiskExit = 0.50f;
        public int PersistFramesCell = 3;
        public int PersistFramesNeighbor = 3;
        public float SeedUpdateCooldown = 0.75f;
        public float SeedTriggerMoveThreshold = 0.12f;
        public float SeedTrendWeight = 0.55f;
        public float SeedNeighborWeight = 0.35f;
        public float SeedAnchorWeight = 0.10f;
        public float SeedStepLow = 0.05f;
        public float SeedStepMedium = 0.10f;
        public float SeedStepHigh = 0.15f;
        public float MaxSeedShiftPerUpdate = 0.20f;
        public float MaxSeedOffsetFromUser = 0.60f;
        public float PartitionAcceptEpsilon = 0.03f;
        public float PartitionCellRiskWeight = 0.5f;
        public float PartitionNeighborRiskWeight = 0.5f;
        public float PartitionMaxAllowedCellRiskWorsen = 0.02f;
        public float PartitionMaxAllowedNeighborRiskWorsen = 0.02f;
        public bool EnableVisualization = false;
    }

    [Serializable]
    public class LocalSafeTargetModuleConfig
    {
        public bool EnableSelection = false;
        public bool EnableVisualization = false;
        public bool EnableLogging = false;

        public float FanHalfAngleDeg = 55f;
        public float SearchRadiusMin = 1.5f;
        public float SearchRadiusMax = 2.0f;
        public float BoundaryBufferMin = 0.3f;
        public float BoundaryBufferMax = 0.5f;
        public float GridResolutionMin = 0.15f;
        public float GridResolutionMax = 0.25f;

        public float WeightBoundaryDist = 1.0f;
        public float WeightOccupancyDist = 1.5f;
        public float WeightDistancePenalty = 0.4f;
        public float SampleDensityPerM2 = 55f;
        public int MinSamplesPerUser = 24;
        public int MaxSamplesPerUser = 120;

        // Polar sampling: K = angleCount * radiusCount (default 11 * 3 = 33).
        public int AngleSamples = 11;
        public int RadiusSamples = 3;

        // Four-term score weights.
        public float WeightSelfOpen = 1.0f;
        public float WeightFrontMargin = 0.8f;
        public float WeightNeighborImpact = 1.2f;
        public float WeightHeadingDeviation = 0.6f;

        // Neighbor-impact denominator guard: ||p_j - p_i|| - rho_s.
        public float NeighborSafetyBuffer = 0.35f;
    }
}

