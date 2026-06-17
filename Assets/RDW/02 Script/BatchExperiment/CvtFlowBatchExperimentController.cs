using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CvtFlowBatchExperimentController : MonoBehaviour
{
    [Serializable]
    public class BatchGroup
    {
        public string Label;
        public string Sweep;
        public ProactiveUserResetJudgeMode JudgeMode;
        public ProactiveResetDirectionMode DirectionMode;
    }

    [Serializable]
    private class BatchGroupResult
    {
        public BatchGroup Group;
        public string RunFolderPath;
        public GroupStats Stats;
    }

    private class GroupStats
    {
        public int EpisodeCount;
        public float MeanTotalReset;
        public float StdTotalReset;
        public int InterResetCount;
        public float InterResetUnder1Ratio;
        public float InterResetUnder2Ratio;
        public float InterResetUnder3Ratio;
        public float InterResetBottomQuartileMean;
    }

    public string ScenePath;
    public string ReportPath;
    public BatchGroup[] Groups;

    private readonly List<BatchGroupResult> results = new List<BatchGroupResult>();

    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);

        if (Groups == null || Groups.Length == 0)
        {
            Fail("No batch experiment groups were configured.");
            yield break;
        }

        for (int i = 0; i < Groups.Length; i++)
        {
            yield return RunGroup(Groups[i]);
        }

        WriteReport();
        Debug.Log("[CVTFlowBatch] All groups completed. Report: " + ReportPath);
        Application.Quit(0);
    }

    private IEnumerator RunGroup(BatchGroup group)
    {
        Debug.Log("[CVTFlowBatch] Starting group: " + group.Label);
        SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
        yield return null;
        yield return null;

        CleanupDuplicateDataRecordObjects();

        _GCM.GlobalCoordinationManager gcm = FindObjectOfType<_GCM.GlobalCoordinationManager>();
        RDWSimulationManager simulationManager = RDWSimulationManager.instance;
        _GCM.GM_DataRecord dataRecord = _GCM.GM_DataRecord.instance;
        if (gcm == null || simulationManager == null || dataRecord == null)
        {
            Fail("Required scene managers were not found after loading scene " + ScenePath);
            yield break;
        }

        if (gcm.totalUserCount != 4)
        {
            Fail("Expected GlobalCoordinationManager.totalUserCount == 4, but got " + gcm.totalUserCount);
            yield break;
        }

        if (simulationManager.simulationSetting == null ||
            simulationManager.simulationSetting.proactiveUserReset == null)
        {
            Fail("SimulationSetting.proactiveUserReset is missing.");
            yield break;
        }

        ProactiveUserResetSettings settings = simulationManager.simulationSetting.proactiveUserReset;
        settings.enableStrategy = true;
        settings.judgeMode = group.JudgeMode;
        settings.userSelectionMode = ProactiveUserResetUserSelectionMode.Arbitration;
        settings.resetDirectionMode = group.DirectionMode;
        ApplyGroupDefaultThresholds(settings, group);

        string runId = dataRecord.BeginNewRunSession(group.Label);
        Debug.Log("[CVTFlowBatch] Run session " + runId + " for " + group.Label);
        gcm.ResetEpisode();

        while (!gcm.IsExperimentCompleted)
        {
            yield return null;
        }

        dataRecord.Save_SteamingData_Batch();
        dataRecord.Save_InterResetDistance_Batch();

        string runFolder = dataRecord.GetRunFolderPath();
        results.Add(new BatchGroupResult
        {
            Group = group,
            RunFolderPath = runFolder,
            Stats = ComputeStats(runFolder)
        });
        Debug.Log("[CVTFlowBatch] Completed group: " + group.Label + " runFolder=" + runFolder);
    }

    private static void ApplyGroupDefaultThresholds(
        ProactiveUserResetSettings settings,
        BatchGroup group)
    {
        if (settings == null || group == null)
            return;

        if (group.JudgeMode == ProactiveUserResetJudgeMode.CoverageSpread)
        {
            settings.coverageSpreadThreshold = 0.50f;
            settings.coverageSpreadMinImprovement = 0.05f;
            settings.coverageSpreadTrendWindowFrames = 5;
            settings.coverageSpreadTrendRequiredFrames = 3;
        }
    }

    private static void CleanupDuplicateDataRecordObjects()
    {
        _GCM.GM_DataRecord[] dataRecords = FindObjectsOfType<_GCM.GM_DataRecord>();
        for (int i = 0; i < dataRecords.Length; i++)
        {
            if (dataRecords[i] != null && dataRecords[i] != _GCM.GM_DataRecord.instance)
                Destroy(dataRecords[i].gameObject);
        }
    }

    private static GroupStats ComputeStats(string runFolder)
    {
        GroupStats stats = new GroupStats();
        string rawFolder = Path.Combine(runFolder, "raw");
        string episodePath = Path.Combine(rawFolder, "episode_summary.csv");
        string interResetPath = Path.Combine(rawFolder, "inter_reset_distance.csv");

        List<float> totalResets = ReadFloatColumn(episodePath, "totalResetCountPerEpisode");
        stats.EpisodeCount = totalResets.Count;
        stats.MeanTotalReset = Mean(totalResets);
        stats.StdTotalReset = Std(totalResets, stats.MeanTotalReset);

        List<float> interResetDistances = ReadFloatColumn(interResetPath, "interResetDistance");
        stats.InterResetCount = interResetDistances.Count;
        stats.InterResetUnder1Ratio = RatioUnder(interResetDistances, 1.0f);
        stats.InterResetUnder2Ratio = RatioUnder(interResetDistances, 2.0f);
        stats.InterResetUnder3Ratio = RatioUnder(interResetDistances, 3.0f);
        stats.InterResetBottomQuartileMean = BottomQuartileMean(interResetDistances);
        return stats;
    }

    private static List<float> ReadFloatColumn(string path, string columnName)
    {
        List<float> values = new List<float>();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return values;

        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 2)
            return values;

        string[] headers = SplitCsvLine(lines[0]);
        int columnIndex = Array.IndexOf(headers, columnName);
        if (columnIndex < 0)
            return values;

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            string[] cells = SplitCsvLine(lines[i]);
            if (columnIndex >= cells.Length)
                continue;

            if (float.TryParse(cells[columnIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                values.Add(value);
        }

        return values;
    }

    private static string[] SplitCsvLine(string line)
    {
        return line.Split(',');
    }

    private void WriteReport()
    {
        string directory = Path.GetDirectoryName(ReportPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# CVT / Opposing Flow Direction and Trigger Comparison (4 Users)");
        sb.AppendLine();
        sb.AppendLine("- Scene: `" + ScenePath.Replace("\\", "/") + "`");
        sb.AppendLine("- Baseline: `RecoveryMarginTrend + DefaultDirection + Arbitration`");
        sb.AppendLine("- User count requirement: `GlobalCoordinationManager.totalUserCount == 4`");
        sb.AppendLine();
        sb.AppendLine("## Direction Sweep");
        sb.AppendLine();
        sb.AppendLine("| Group | Trigger | Direction | Episodes | Mean resets/episode | Std | Inter-reset <1m | <2m | <3m | Bottom 25% inter-reset mean | Run folder |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        AppendRows(sb, "Direction");
        sb.AppendLine();
        sb.AppendLine("## Trigger Sweep");
        sb.AppendLine();
        sb.AppendLine("| Group | Trigger | Direction | Episodes | Mean resets/episode | Std | Inter-reset <1m | <2m | <3m | Bottom 25% inter-reset mean | Run folder |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        AppendRows(sb, "Trigger");
        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("- Direction sweep fixes trigger to `RecoveryMarginTrend` and user selection to `Arbitration`.");
        sb.AppendLine("- Trigger sweep fixes direction to `DefaultDirection` and user selection to `Arbitration`.");
        sb.AppendLine("- The `<1m/<2m/<3m` ratios use `inter_reset_distance.csv` as the walking-distance interval source.");

        File.WriteAllText(ReportPath, sb.ToString(), Encoding.UTF8);
    }

    private void AppendRows(StringBuilder sb, string sweep)
    {
        for (int i = 0; i < results.Count; i++)
        {
            BatchGroupResult result = results[i];
            if (result.Group.Sweep != sweep && result.Group.Label != "baseline_recovery_margin_default_direction")
                continue;

            GroupStats stats = result.Stats;
            sb.Append("| ")
                .Append(result.Group.Label).Append(" | ")
                .Append(result.Group.JudgeMode).Append(" | ")
                .Append(result.Group.DirectionMode).Append(" | ")
                .Append(stats.EpisodeCount).Append(" | ")
                .Append(FormatFloat(stats.MeanTotalReset)).Append(" | ")
                .Append(FormatFloat(stats.StdTotalReset)).Append(" | ")
                .Append(FormatPercent(stats.InterResetUnder1Ratio)).Append(" | ")
                .Append(FormatPercent(stats.InterResetUnder2Ratio)).Append(" | ")
                .Append(FormatPercent(stats.InterResetUnder3Ratio)).Append(" | ")
                .Append(FormatFloat(stats.InterResetBottomQuartileMean)).Append(" | `")
                .Append(result.RunFolderPath.Replace("\\", "/")).AppendLine("` |");
        }
    }

    private static float Mean(List<float> values)
    {
        if (values == null || values.Count == 0)
            return 0.0f;

        float sum = 0.0f;
        for (int i = 0; i < values.Count; i++)
            sum += values[i];

        return sum / values.Count;
    }

    private static float Std(List<float> values, float mean)
    {
        if (values == null || values.Count <= 1)
            return 0.0f;

        float sum = 0.0f;
        for (int i = 0; i < values.Count; i++)
        {
            float diff = values[i] - mean;
            sum += diff * diff;
        }

        return Mathf.Sqrt(sum / values.Count);
    }

    private static float RatioUnder(List<float> values, float threshold)
    {
        if (values == null || values.Count == 0)
            return 0.0f;

        int count = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] < threshold)
                count++;
        }

        return (float)count / values.Count;
    }

    private static float BottomQuartileMean(List<float> values)
    {
        if (values == null || values.Count == 0)
            return 0.0f;

        values.Sort();
        int count = Mathf.Max(1, Mathf.CeilToInt(values.Count * 0.25f));
        float sum = 0.0f;
        for (int i = 0; i < count; i++)
            sum += values[i];

        return sum / count;
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(float value)
    {
        return (value * 100.0f).ToString("F2", CultureInfo.InvariantCulture) + "%";
    }

    private static void Fail(string message)
    {
        Debug.LogError("[CVTFlowBatch] " + message);
        Application.Quit(1);
    }
}
