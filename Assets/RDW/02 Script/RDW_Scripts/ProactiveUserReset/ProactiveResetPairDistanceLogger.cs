using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class ProactiveResetPairDistanceLogger
{
    private const float EPSILON = 0.0001f;

    private struct ActivePairSession
    {
        public int UnitMinId;
        public int UnitMaxId;
        public int TriggerFrame;
        public float TriggerTime;
        public float DistanceMin;
        public float DistanceMax;
    }

    private static string logFilePath = string.Empty;
    private static bool headerWritten;
    private static readonly Dictionary<int, Vector2> lastKnownPositionsByUnitId = new Dictionary<int, Vector2>();
    private static readonly Dictionary<long, ActivePairSession> activeSessionsByPair = new Dictionary<long, ActivePairSession>();

    public static void ResetSession()
    {
        logFilePath = string.Empty;
        headerWritten = false;
        lastKnownPositionsByUnitId.Clear();
        activeSessionsByPair.Clear();
    }

    public static void NotifyProactiveTrigger(RedirectedUnit unitA, RedirectedUnit unitB)
    {
        if (!ShouldLoggingEnabled())
            return;

        if (unitA == null || unitB == null || unitA.GetRealUser() == null || unitB.GetRealUser() == null)
            return;

        int idA = unitA.GetID();
        int idB = unitB.GetID();
        int minId = Mathf.Min(idA, idB);
        int maxId = Mathf.Max(idA, idB);
        long pairKey = BuildPairKey(minId, maxId);

        if (activeSessionsByPair.ContainsKey(pairKey))
            return;

        activeSessionsByPair[pairKey] = new ActivePairSession
        {
            UnitMinId = minId,
            UnitMaxId = maxId,
            TriggerFrame = Time.frameCount,
            TriggerTime = Time.time,
            DistanceMin = 0.0f,
            DistanceMax = 0.0f
        };

        // Anchor baseline positions at trigger time so post-trigger distance starts from zero.
        lastKnownPositionsByUnitId[minId] = unitA.GetID() == minId
            ? unitA.GetRealUser().transform2D.localPosition
            : unitB.GetRealUser().transform2D.localPosition;
        lastKnownPositionsByUnitId[maxId] = unitA.GetID() == maxId
            ? unitA.GetRealUser().transform2D.localPosition
            : unitB.GetRealUser().transform2D.localPosition;
    }

    public static void Tick(RedirectedUnit[] units)
    {
        if (!ShouldLoggingEnabled())
            return;
        if (units == null || units.Length == 0)
            return;
        if (activeSessionsByPair.Count == 0)
            return;

        for (int i = 0; i < units.Length; i++)
        {
            RedirectedUnit unit = units[i];
            if (unit == null || unit.GetRealUser() == null)
                continue;

            int unitId = unit.GetID();
            Vector2 currentPosition = unit.GetRealUser().transform2D.localPosition;

            if (lastKnownPositionsByUnitId.TryGetValue(unitId, out Vector2 lastPosition))
            {
                float moved = Vector2.Distance(lastPosition, currentPosition);
                if (moved > EPSILON)
                {
                    AccumulateDistance(unitId, moved);
                }
            }

            lastKnownPositionsByUnitId[unitId] = currentPosition;
        }

        TryFinalizeActiveSessions(units);
    }

    private static bool ShouldLoggingEnabled()
    {
        RDWSimulationManager manager = RDWSimulationManager.instance;
        if (manager == null || manager.simulationSetting == null)
            return false;

        return manager.simulationSetting.enableProactiveResetPairDistanceLogging;
    }

    private static void EnsureLogFile()
    {
        if (!string.IsNullOrEmpty(logFilePath))
            return;

        string root = Path.Combine(Directory.GetCurrentDirectory(), "CGnA_DataLog", "proactiveResetPairDistance");
        Directory.CreateDirectory(root);

        string name = $"proactive_reset_pair_distance_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        logFilePath = Path.Combine(root, name);
    }

    private static void AccumulateDistance(int unitId, float distance)
    {
        if (distance <= EPSILON || activeSessionsByPair.Count == 0)
            return;

        List<long> keys = new List<long>(activeSessionsByPair.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            long key = keys[i];
            ActivePairSession session = activeSessionsByPair[key];
            if (session.UnitMinId == unitId)
            {
                session.DistanceMin += distance;
                activeSessionsByPair[key] = session;
            }
            else if (session.UnitMaxId == unitId)
            {
                session.DistanceMax += distance;
                activeSessionsByPair[key] = session;
            }
        }
    }

    private static void TryFinalizeActiveSessions(RedirectedUnit[] units)
    {
        EnsureLogFile();
        if (string.IsNullOrEmpty(logFilePath))
            return;

        Dictionary<int, RedirectedUnit> unitById = BuildUnitMap(units);
        List<long> keys = new List<long>(activeSessionsByPair.Keys);

        StringBuilder sb = new StringBuilder();
        if (!headerWritten)
        {
            sb.AppendLine("triggerFrame,triggerTime,endFrame,endTime,pairMinId,pairMaxId,resetByUnitId,unitMinStatus,unitMaxStatus,unitMinWalkDistance,unitMaxWalkDistance,totalWalkDistance");
            headerWritten = true;
        }

        for (int i = 0; i < keys.Count; i++)
        {
            long key = keys[i];
            ActivePairSession session = activeSessionsByPair[key];
            string statusMin = ResolveStatus(unitById, session.UnitMinId);
            string statusMax = ResolveStatus(unitById, session.UnitMaxId);
            bool minReset = IsResetStatus(statusMin);
            bool maxReset = IsResetStatus(statusMax);
            if (!minReset && !maxReset)
                continue;

            int resetByUnitId = -1;
            if (minReset && !maxReset) resetByUnitId = session.UnitMinId;
            else if (!minReset && maxReset) resetByUnitId = session.UnitMaxId;

            float totalDistance = session.DistanceMin + session.DistanceMax;
            sb.Append(session.TriggerFrame).Append(',');
            sb.Append(ToInvariant(session.TriggerTime)).Append(',');
            sb.Append(Time.frameCount).Append(',');
            sb.Append(ToInvariant(Time.time)).Append(',');
            sb.Append(session.UnitMinId).Append(',');
            sb.Append(session.UnitMaxId).Append(',');
            sb.Append(resetByUnitId).Append(',');
            sb.Append(SanitizeCsv(statusMin)).Append(',');
            sb.Append(SanitizeCsv(statusMax)).Append(',');
            sb.Append(ToInvariant(session.DistanceMin)).Append(',');
            sb.Append(ToInvariant(session.DistanceMax)).Append(',');
            sb.Append(ToInvariant(totalDistance));
            sb.AppendLine();

            activeSessionsByPair.Remove(key);
        }

        if (sb.Length > 0)
        {
            File.AppendAllText(logFilePath, sb.ToString());
        }
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
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace(",", "_");
    }

    private static Dictionary<int, RedirectedUnit> BuildUnitMap(RedirectedUnit[] units)
    {
        Dictionary<int, RedirectedUnit> map = new Dictionary<int, RedirectedUnit>();
        if (units == null)
            return map;

        for (int i = 0; i < units.Length; i++)
        {
            RedirectedUnit unit = units[i];
            if (unit == null)
                continue;
            map[unit.GetID()] = unit;
        }

        return map;
    }

    private static string ResolveStatus(Dictionary<int, RedirectedUnit> unitById, int unitId)
    {
        if (!unitById.TryGetValue(unitId, out RedirectedUnit unit) || unit == null)
            return "MISSING";

        return unit.GetStatus() ?? string.Empty;
    }

    private static bool IsResetStatus(string status)
    {
        return string.Equals(status, "WALL_RESET", StringComparison.Ordinal) ||
               string.Equals(status, "USER_RESET", StringComparison.Ordinal);
    }

    private static long BuildPairKey(int minId, int maxId)
    {
        return ((long)(uint)minId << 32) | (uint)maxId;
    }
}
