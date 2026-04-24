using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public enum ProactiveUserResetJudgeMode
{
    Recoverability = 0,
    Simple = 1
}

[System.Serializable]
public enum ProactiveUserResetUserSelectionMode
{
    Arbitration = 0
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

    [HideInInspector] public bool enableProactiveUserResetArbitration = true; // legacy
    [HideInInspector] public bool useSimpleProactiveTrigger; // legacy

    public bool enableBiRecoverabilityLogging;
    public int biRecoverabilityLogEveryNFrames = 1;
    public bool showTarget;
    public bool showResetLocator;
    public bool showRealWall;
}
