using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public enum ProactiveUserResetJudgeMode
{
    Recoverability = 0,
    Simple = 1,
    TTC = 2,
    VoronoiBoundary = 3
}

[System.Serializable]
public enum ProactiveUserResetUserSelectionMode
{
    Arbitration = 0,
    None = 1
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

    [HideInInspector]
    [Tooltip("Prediction horizon (seconds) used by proactive precheck and trigger evaluation.")]
    [Min(0.1f)] public float predictionHorizonSeconds = 1.5f;

    [HideInInspector]
    [Tooltip("Sampling count used by proactive precheck and trigger evaluation.")]
    [Range(2, 300)] public int predictionSampleCount = 60;

    [HideInInspector]
    [Tooltip("Simple trigger distance threshold in meters.")]
    [Min(0.1f)] public float simpleTriggerDistanceMeters = 1.5f;

    [HideInInspector]
    [Tooltip("Simple trigger closing-speed threshold (m/s).")]
    [Min(0.0f)] public float simpleClosingSpeedThreshold = 0.05f;

    [HideInInspector]
    [Tooltip("Distance threshold (meters) used to detect TTC collision band entry.")]
    [Min(0.1f)] public float ttcCollisionDistanceMeters = 1.0f;

    [HideInInspector]
    [Tooltip("Minimum lead time (seconds) required for TTC trigger.")]
    [Min(0.0f)] public float ttcMinTimeToHitSeconds = 0.5f;

    [HideInInspector]
    [Tooltip("Tie-break epsilon for candidate worst walking distance in arbitration.")]
    [Min(0.0f)] public float arbitrationMEpsilon = 0.02f;

    [HideInInspector]
    [Tooltip("Tie-break epsilon for C_self score in arbitration.")]
    [Min(0.0f)] public float arbitrationCEpsilon = 0.05f;

    [Tooltip("Reject proactive reset if the selected user rotating in place would cause another user to unavoidably collide with it.")]
    public bool enableInPlaceSafetyCheck = true;

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

    [Header("Proactive User Reset")]
    public ProactiveUserResetSettings proactiveUserReset = new ProactiveUserResetSettings();

    [HideInInspector]
    [Tooltip("Legacy code-level switch for proactive trigger/candidate log-only runs. Proactive strategy runs write these logs automatically.")]
    public bool enableProactiveResetPairDistanceLogging;
    public bool showTarget;
    public bool showResetLocator;
    public bool showRealWall;
}
