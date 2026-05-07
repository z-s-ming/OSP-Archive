using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class ProactiveTriggerWindowLogger
{
    private class TriggerSession
    {
        public float TriggerTime;
        public float HorizonSeconds;
    }

    private const string HEADER = "triggerIdInLog,episodeObjectId,triggerFrame,triggerTime,runtimePairMinId,runtimePairMaxId,runtimeUnitAId,runtimeUnitBId,judgeMode,horizonSeconds,triggerDistance,triggerClosingSpeed,unitMinStatus,unitMaxStatus,predictedPairType";
    private static string logFilePath = string.Empty;
    private static string legacyLogFilePath = string.Empty;
    private static int nextTriggerId = 1;
    private static readonly Dictionary<long, TriggerSession> activeSessionsByPair = new Dictionary<long, TriggerSession>();

    public static void ResetSession()
    {
        logFilePath = string.Empty;
        legacyLogFilePath = string.Empty;
        nextTriggerId = 1;
        activeSessionsByPair.Clear();
    }

    public static void NotifyTriggerCandidate(
        RedirectedUnit unitA,
        RedirectedUnit unitB,
        string judgeMode,
        float horizonSeconds,
        float triggerDistance,
        float closingSpeed)
    {
        if (!ShouldLoggingEnabled())
            return;
        if (unitA == null || unitB == null || unitA.GetRealUser() == null || unitB.GetRealUser() == null)
            return;

        int runtimeUnitAId = unitA.GetID();
        int runtimeUnitBId = unitB.GetID();
        int runtimePairMinId = Mathf.Min(runtimeUnitAId, runtimeUnitBId);
        int runtimePairMaxId = Mathf.Max(runtimeUnitAId, runtimeUnitBId);
        long pairKey = BuildPairKey(runtimePairMinId, runtimePairMaxId);

        if (activeSessionsByPair.ContainsKey(pairKey))
            return;

        RedirectedUnit unitMin = runtimeUnitAId == runtimePairMinId ? unitA : unitB;
        RedirectedUnit unitMax = runtimeUnitAId == runtimePairMaxId ? unitA : unitB;
        float safeHorizonSeconds = Mathf.Max(0.1f, horizonSeconds);

        activeSessionsByPair[pairKey] = new TriggerSession
        {
            TriggerTime = Time.time,
            HorizonSeconds = safeHorizonSeconds
        };

        AppendTriggerRow(
            unitMin,
            unitMax,
            runtimeUnitAId,
            runtimeUnitBId,
            string.IsNullOrEmpty(judgeMode) ? "UNKNOWN" : judgeMode,
            safeHorizonSeconds,
            triggerDistance,
            closingSpeed);
    }

    public static void Tick()
    {
        if (!ShouldLoggingEnabled() || activeSessionsByPair.Count == 0)
            return;

        List<long> keys = new List<long>(activeSessionsByPair.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            long key = keys[i];
            TriggerSession session = activeSessionsByPair[key];
            if (Time.time - session.TriggerTime >= session.HorizonSeconds)
            {
                activeSessionsByPair.Remove(key);
            }
        }
    }

    private static void AppendTriggerRow(
        RedirectedUnit unitMin,
        RedirectedUnit unitMax,
        int runtimeUnitAId,
        int runtimeUnitBId,
        string judgeMode,
        float horizonSeconds,
        float triggerDistance,
        float closingSpeed)
    {
        EnsureLogFile();

        StringBuilder sb = new StringBuilder();

        int episodeObjectId = unitMin.controller != null ? unitMin.controller.GetEpisodeID() : -1;
        string minStatus = unitMin.GetStatus() ?? string.Empty;
        string maxStatus = unitMax.GetStatus() ?? string.Empty;

        sb.Append(nextTriggerId++).Append(',');
        sb.Append(episodeObjectId).Append(',');
        sb.Append(Time.frameCount).Append(',');
        sb.Append(ToInvariant(Time.time)).Append(',');
        sb.Append(unitMin.GetID()).Append(',');
        sb.Append(unitMax.GetID()).Append(',');
        sb.Append(runtimeUnitAId).Append(',');
        sb.Append(runtimeUnitBId).Append(',');
        sb.Append(SanitizeCsv(judgeMode)).Append(',');
        sb.Append(ToInvariant(horizonSeconds)).Append(',');
        sb.Append(ToInvariant(triggerDistance)).Append(',');
        sb.Append(ToInvariant(closingSpeed)).Append(',');
        sb.Append(SanitizeCsv(minStatus)).Append(',');
        sb.Append(SanitizeCsv(maxStatus)).Append(',');
        sb.Append(SanitizeCsv(ResolvePredictedPairType(unitMin, unitMax, minStatus, maxStatus)));
        sb.AppendLine();

        AppendLineWithHeader(logFilePath, sb.ToString());
        if (!string.IsNullOrEmpty(legacyLogFilePath) &&
            !string.Equals(logFilePath, legacyLogFilePath, StringComparison.OrdinalIgnoreCase))
        {
            AppendLineWithHeader(legacyLogFilePath, sb.ToString());
        }
    }

    private static string ResolvePredictedPairType(RedirectedUnit unitMin, RedirectedUnit unitMax, string minStatus, string maxStatus)
    {
        string resetType = ResolveResetType(minStatus, maxStatus);
        if (!string.IsNullOrEmpty(resetType))
            return resetType;

        Vector2 minForward = unitMin.GetRealUser().transform2D.forward;
        Vector2 maxForward = unitMax.GetRealUser().transform2D.forward;
        float forwardDot = 1.0f;
        if (minForward.sqrMagnitude > Mathf.Epsilon && maxForward.sqrMagnitude > Mathf.Epsilon)
        {
            forwardDot = Vector2.Dot(minForward.normalized, maxForward.normalized);
        }

        if (IsClosingTowardEachOther(unitMin, unitMax) && forwardDot < 0.0f)
            return "BIDIRECTIONAL_USER_PAIR";

        return "USER_PAIR";
    }

    private static bool IsClosingTowardEachOther(RedirectedUnit unitMin, RedirectedUnit unitMax)
    {
        Vector2 offset = unitMax.GetRealUser().transform2D.localPosition - unitMin.GetRealUser().transform2D.localPosition;
        if (offset.sqrMagnitude <= Mathf.Epsilon)
            return false;

        Vector2 velocityMin = unitMin.GetLastMovementDirection() * Mathf.Max(0.0f, unitMin.GetLastInstantaneousSpeed());
        Vector2 velocityMax = unitMax.GetLastMovementDirection() * Mathf.Max(0.0f, unitMax.GetLastInstantaneousSpeed());
        return Vector2.Dot(velocityMin - velocityMax, offset.normalized) > 0.0f;
    }

    private static string ResolveResetType(string minStatus, string maxStatus)
    {
        bool minUser = string.Equals(minStatus, "USER_RESET", StringComparison.Ordinal);
        bool maxUser = string.Equals(maxStatus, "USER_RESET", StringComparison.Ordinal);
        if (minUser && maxUser)
            return "USER_RESET_BOTH";
        if (minUser || maxUser)
            return "USER_RESET";

        return string.Empty;
    }

    private static bool ShouldLoggingEnabled()
    {
        RDWSimulationManager manager = RDWSimulationManager.instance;
        return manager != null &&
               manager.simulationSetting != null &&
               manager.simulationSetting.enableProactiveResetPairDistanceLogging;
    }

    private static void EnsureLogFile()
    {
        if (!string.IsNullOrEmpty(logFilePath))
            return;

        string legacyRoot = Path.Combine(Directory.GetCurrentDirectory(), "CGnA_DataLog", "proactiveResetPairDistance");
        Directory.CreateDirectory(legacyRoot);
        string legacyName = $"proactive_trigger_frame_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        legacyLogFilePath = Path.Combine(legacyRoot, legacyName);

        logFilePath = _GCM.GM_DataRecord.instance != null
            ? _GCM.GM_DataRecord.instance.GetRunRawLogPath("proactive_trigger_frame.csv")
            : legacyLogFilePath;
    }

    private static void AppendLineWithHeader(string filePath, string line)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(line))
            return;

        string directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        bool shouldWriteHeader = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;
        StringBuilder payload = new StringBuilder();
        if (shouldWriteHeader)
        {
            payload.AppendLine(HEADER);
        }
        payload.Append(line);
        File.AppendAllText(filePath, payload.ToString());
    }

    private static string ToInvariant(float value)
    {
        if (float.IsNaN(value))
            return "NaN";
        if (float.IsPositiveInfinity(value))
            return "Inf";
        if (float.IsNegativeInfinity(value))
            return "-Inf";

        return value.ToString("F6", CultureInfo.InvariantCulture);
    }

    private static string SanitizeCsv(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Replace(",", "_");
    }

    private static long BuildPairKey(int minId, int maxId)
    {
        return ((long)(uint)minId << 32) | (uint)maxId;
    }
}
