using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BatchExperimentRunner
{
    [MenuItem("RDW/Run CVT Flow Direction Trigger Comparison 4 Users")]
    public static void RunCvtFlowDirectionTriggerComparison4Users()
    {
        Scene activeScene = EditorSceneManager.GetActiveScene();
        if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.path))
            throw new InvalidOperationException("No saved active scene is open. Open and save the RDW Square 100 4-user scene first.");

        if (activeScene.isDirty)
            throw new InvalidOperationException("The active scene has unsaved changes. Save the current 4-user Unity scene before running batch experiments.");

        _GCM.GlobalCoordinationManager gcm = UnityEngine.Object.FindObjectOfType<_GCM.GlobalCoordinationManager>();
        if (gcm == null)
            throw new InvalidOperationException("GlobalCoordinationManager was not found in the active scene.");

        if (gcm.totalUserCount != 4)
            throw new InvalidOperationException("Expected GlobalCoordinationManager.totalUserCount == 4, but got " + gcm.totalUserCount + ".");

        GameObject controllerObject = new GameObject("__CVTFlowBatchExperimentController");
        CvtFlowBatchExperimentController controller =
            controllerObject.AddComponent<CvtFlowBatchExperimentController>();
        controller.ScenePath = activeScene.path;
        controller.ReportPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "artifacts",
            "reports",
            "cvt_flow_direction_trigger_comparison_4users.md");
        controller.Groups = BuildGroups();

        Debug.Log("[BatchExperimentRunner] Starting CVT/Flow direction-trigger comparison from scene: " + activeScene.path);
        EditorApplication.isPlaying = true;
    }

    private static CvtFlowBatchExperimentController.BatchGroup[] BuildGroups()
    {
        return new[]
        {
            new CvtFlowBatchExperimentController.BatchGroup
            {
                Label = "baseline_recovery_margin_default_direction",
                Sweep = "Direction",
                JudgeMode = ProactiveUserResetJudgeMode.RecoveryMarginTrend,
                DirectionMode = ProactiveResetDirectionMode.DefaultDirection
            },
            new CvtFlowBatchExperimentController.BatchGroup
            {
                Label = "direction_away_from_other",
                Sweep = "Direction",
                JudgeMode = ProactiveUserResetJudgeMode.RecoveryMarginTrend,
                DirectionMode = ProactiveResetDirectionMode.AwayFromOther
            },
            new CvtFlowBatchExperimentController.BatchGroup
            {
                Label = "direction_local_apf",
                Sweep = "Direction",
                JudgeMode = ProactiveUserResetJudgeMode.RecoveryMarginTrend,
                DirectionMode = ProactiveResetDirectionMode.LocalAPF
            },
            new CvtFlowBatchExperimentController.BatchGroup
            {
                Label = "direction_max_physical_remaining_distance",
                Sweep = "Direction",
                JudgeMode = ProactiveUserResetJudgeMode.RecoveryMarginTrend,
                DirectionMode = ProactiveResetDirectionMode.MaxPhysicalRemainingDistance
            },
            new CvtFlowBatchExperimentController.BatchGroup
            {
                Label = "trigger_coverage_spread",
                Sweep = "Trigger",
                JudgeMode = ProactiveUserResetJudgeMode.CoverageSpread,
                DirectionMode = ProactiveResetDirectionMode.DefaultDirection
            },
            new CvtFlowBatchExperimentController.BatchGroup
            {
                Label = "trigger_opposing_flow",
                Sweep = "Trigger",
                JudgeMode = ProactiveUserResetJudgeMode.OpposingFlow,
                DirectionMode = ProactiveResetDirectionMode.DefaultDirection
            }
        };
    }
}
