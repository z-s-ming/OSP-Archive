using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public enum ProactiveUserResetJudgeMode
{
    Recoverability = 0,
    Simple = 1,
    TTC = 2,
    VoronoiBoundary = 3,
    RecoveryMarginTrend = 4,
    CoverageSpread = 5,
    OpposingFlow = 6
}

[System.Serializable]
public enum ProactiveUserResetUserSelectionMode
{
    Arbitration = 0,
    None = 1,
    ScoredArbitration = 2
}

[System.Serializable]
public enum ProactiveResetDirectionMode
{
    DefaultDirection = 0,
    AwayFromOther = 1,
    LocalAPF = 2,
    MaxPhysicalRemainingDistance = 3
}

[System.Serializable]
public enum ExperimentProfile
{
    Simulation = 0,
    LiveUser = 1
}

[System.Serializable]
public class ProactiveUserResetSettings
{
    [Tooltip("Master switch for proactive user reset strategy.")]
    public bool enableStrategy = true;

    [Tooltip("Judge used to trigger proactive user reset.")]
    public ProactiveUserResetJudgeMode judgeMode = ProactiveUserResetJudgeMode.Recoverability;

    [Tooltip("Strategy used to select which user performs proactive reset.")]
    public ProactiveUserResetUserSelectionMode userSelectionMode = ProactiveUserResetUserSelectionMode.Arbitration;

    [Tooltip("Direction policy used after proactive user reset arbitration selects a user.")]
    public ProactiveResetDirectionMode resetDirectionMode = ProactiveResetDirectionMode.DefaultDirection;

    [Tooltip("Prediction horizon (seconds) used by proactive precheck and trigger evaluation.")]
    [Min(0.1f)] public float predictionHorizonSeconds = 1.5f;

    [Tooltip("Sampling count used by proactive precheck and trigger evaluation.")]
    [Range(2, 300)] public int predictionSampleCount = 60;

    [Tooltip("Simple trigger distance threshold in meters.")]
    [Min(0.1f)] public float simpleTriggerDistanceMeters = 1.5f;

    [Tooltip("Simple trigger closing-speed threshold (m/s).")]
    [Min(0.0f)] public float simpleClosingSpeedThreshold = 0.05f;

    [Tooltip("Distance threshold (meters) used to detect TTC collision band entry.")]
    [Min(0.1f)] public float ttcCollisionDistanceMeters = 1.0f;

    [Tooltip("Minimum lead time (seconds) required for TTC trigger.")]
    [Min(0.0f)] public float ttcMinTimeToHitSeconds = 0.5f;

    [HideInInspector]
    [Tooltip("Tie-break epsilon for candidate worst walking distance in arbitration.")]
    [Min(0.0f)] public float arbitrationMEpsilon = 0.02f;

    [HideInInspector]
    [Tooltip("Tie-break epsilon for C_self score in arbitration.")]
    [Min(0.0f)] public float arbitrationCEpsilon = 0.05f;

    [Tooltip("ScoredArbitration only: alpha weight for normalized total recovery distance.")]
    [Min(0.0f)] public float arbitrationScoreAlpha = 1.0f;

    [Tooltip("ScoredArbitration only: beta weight for normalized minimum recovery distance.")]
    [Min(0.0f)] public float arbitrationScoreBeta = 1.0f;

    [Tooltip("ScoredArbitration only: gamma weight for short-distance penalty.")]
    [Min(0.0f)] public float arbitrationScoreGamma = 1.5f;

    [Tooltip("ScoredArbitration only: d0 reference distance in meters. D_min below this is penalized.")]
    [Min(0.001f)] public float arbitrationScoreD0Meters = 2.0f;

    [Tooltip("ScoredArbitration only: epsilon in meters used to stabilize the short-distance penalty denominator.")]
    [Min(0.001f)] public float arbitrationScoreEpsilonMeters = 0.1f;

    [Tooltip("ScoredArbitration only: tie-break epsilon for proactive reset arbitration score.")]
    [Min(0.0f)] public float arbitrationScoreTieEpsilon = 0.001f;

    [Tooltip("Deprecated: in-place proactive reset safety rejection is disabled.")]
    public bool enableInPlaceSafetyCheck = false;

    [HideInInspector]
    [Tooltip("Extra prediction time after the estimated in-place reset duration.")]
    [Min(0.0f)] public float inPlaceSafetyBufferSeconds = 0.2f;

    [HideInInspector]
    [Tooltip("Cooldown applied to a user after a proactive reset is executed.")]
    [Min(0.0f)] public float executionCooldownSeconds = 1.5f;

    [HideInInspector]
    [Tooltip("Cooldown applied to the same user pair after a proactive reset is executed.")]
    [Min(0.0f)] public float pairExecutionCooldownSeconds = 3.0f;

    [HideInInspector]
    [Tooltip("Voronoi boundary early trigger threshold for the minimum user-to-conflict-boundary distance.")]
    [Min(0.0f)] public float voronoiBoundaryDistanceThreshold = 1.0f;

    [HideInInspector]
    [Tooltip("Trigger when at least one user's reverse movement direction has less real-space wall distance than this threshold.")]
    [Min(0.0f)] public float voronoiBoundaryReverseWallDistanceThreshold = 2.0f;

    [HideInInspector]
    [Tooltip("Frame window used to test whether the conflict-boundary distance is decreasing.")]
    [Range(2, 30)] public int voronoiBoundaryTrendWindowFrames = 5;

    [HideInInspector]
    [Tooltip("Required decreasing samples inside the Voronoi boundary trend window.")]
    [Range(1, 30)] public int voronoiBoundaryTrendRequiredFrames = 4;

    [HideInInspector]
    [Tooltip("Lead time used by recovery-margin trend proactive trigger.")]
    [Min(0.0f)] public float recoveryMarginLeadTimeSeconds = 0.30f;

    [HideInInspector]
    [Tooltip("Uncertainty buffer used by recovery-margin trend proactive trigger.")]
    [Min(0.0f)] public float recoveryMarginBufferMeters = 0.05f;

    [HideInInspector]
    [Tooltip("Minimum proactive margin threshold for recovery-margin trend trigger.")]
    [Min(0.0f)] public float recoveryMarginMinThresholdMeters = 0.15f;

    [HideInInspector]
    [Tooltip("Maximum proactive margin threshold for recovery-margin trend trigger.")]
    [Min(0.0f)] public float recoveryMarginMaxThresholdMeters = 0.50f;

    [HideInInspector]
    [Tooltip("Frame window used to test whether recovery margin is decreasing.")]
    [Range(2, 30)] public int recoveryMarginTrendWindowFrames = 5;

    [HideInInspector]
    [Tooltip("Required decreasing samples inside the recovery-margin trend window.")]
    [Range(1, 30)] public int recoveryMarginTrendRequiredFrames = 3;

    [Tooltip("Minimum expected walking-distance improvement required after proactive arbitration.")]
    [Min(0.0f)] public float proactiveMinExpectedImprovementMeters = 0.20f;

    [Tooltip("CoverageSpread trigger threshold for local user-to-cell-center spread error in square meters.")]
    [Min(0.0f)] public float coverageSpreadThreshold = 0.50f;

    [Tooltip("CoverageSpread trigger minimum meaningful spread change in square meters.")]
    [Min(0.0f)] public float coverageSpreadMinImprovement = 0.05f;

    [Tooltip("CoverageSpread trigger frame window used to test whether spread is increasing.")]
    [Range(2, 30)] public int coverageSpreadTrendWindowFrames = 5;

    [Tooltip("CoverageSpread trigger required increasing samples inside the trend window.")]
    [Range(1, 30)] public int coverageSpreadTrendRequiredFrames = 3;

    [Tooltip("OpposingFlow trigger distance scale for the Gaussian density kernel in meters.")]
    [Min(0.01f)] public float opposingFlowKernelSigmaMeters = 2.0f;

    [Tooltip("OpposingFlow trigger minimum risk score.")]
    [Min(0.0f)] public float opposingFlowRiskThreshold = 0.15f;

    [Tooltip("OpposingFlow trigger minimum user speed considered a valid flow direction.")]
    [Min(0.0f)] public float opposingFlowMinSpeedMetersPerSecond = 0.05f;

    [Tooltip("OpposingFlow trigger maximum pair distance considered for local dynamic-obstacle risk.")]
    [Min(0.1f)] public float opposingFlowMaxDistanceMeters = 4.0f;
}

[System.Serializable]
public class SimulationSetting
{
    //public ObjectSetting_v2 objectSetting;
    public bool useVisualization;
    public bool useDebugMode;
    public bool useContinousSimulation;
    public PrefabSetting prefabSetting;
    public SpaceSetting realSpaceSetting;
    public SpaceSetting virtualSpaceSetting; // 기존 세팅과 동일하고자 할 때 적용.
    public UnitSetting[] unitSettings;
    public bool bAllowUserReset;

    [Header("Experiment Profile")]
    [Tooltip("Simulation keeps the original AutoPilot -> RDW -> Simulation Logger chain. LiveUser expects an external movement controller, such as HMD pose input, around the RDW core.")]
    public ExperimentProfile experimentProfile = ExperimentProfile.Simulation;
    [Tooltip("Legacy compatibility flag. Prefer experimentProfile = LiveUser for real-user headset experiments.")]
    public bool useLiveVRPhysicalUserInput;

    [Header("Proactive User Reset")]
    public ProactiveUserResetSettings proactiveUserReset = new ProactiveUserResetSettings();

    [HideInInspector]
    [Tooltip("Legacy code-level switch for proactive trigger/candidate log-only runs. Proactive strategy runs write these logs automatically.")]
    public bool enableProactiveResetPairDistanceLogging;
    public bool showTarget;
    public bool showResetLocator;
    public bool showRealWall;
}
