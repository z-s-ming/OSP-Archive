using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class ProactiveCandidateFrameLogger
{
    private class CandidateSession
    {
        public float TriggerTime;
        public float HorizonSeconds;
    }

    private const string HEADER = "Date,Timestamp,episodeObjectId,frame,simTime,originTriggerId,candidateId,decisionId,executionId,userId,otherUserId,pairMinUserId,pairMaxUserId,judgeMode,selectionMode,candidateStatus,accepted,executed,resetDirectionX,resetDirectionY,keepMargin,selectedM,selectedCSelf,selectedScore,rejectReason";
    private static string logFilePath = string.Empty;
    private static readonly Dictionary<long, CandidateSession> activeSessionsByPair = new Dictionary<long, CandidateSession>();

    public static void ResetSession()
    {
        logFilePath = string.Empty;
        activeSessionsByPair.Clear();
    }

    public static void Tick()
    {
        if (!ShouldLoggingEnabled() || activeSessionsByPair.Count == 0)
            return;

        List<long> keys = new List<long>(activeSessionsByPair.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            long key = keys[i];
            CandidateSession session = activeSessionsByPair[key];
            if (Time.time - session.TriggerTime >= session.HorizonSeconds)
            {
                activeSessionsByPair.Remove(key);
            }
        }
    }

    public static void NotifyAccepted(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        ProactiveResetCandidate candidate)
    {
        AppendCandidateRowIfNewSession(context, pairContext, "ACCEPTED", candidate, "NONE");
    }

    public static void NotifyRejected(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        ProactiveResetRejection rejection)
    {
        ProactiveResetCandidate emptyCandidate = new ProactiveResetCandidate
        {
            CandidateId = rejection.OriginCandidateId,
            OriginTriggerId = rejection.OriginTriggerId,
            DecisionId = rejection.DecisionId,
            SelectedUserId = rejection.SelectedUserId,
            OtherUserId = -1,
            ResetDirection = Vector2.zero,
            KeepMargin = 0.0f,
            SelectedM = 0.0f,
            SelectedCSelf = 0.0f,
            SelectedScore = 0.0f,
            Accepted = false,
            Executed = false,
            RejectReason = rejection.Reason
        };
        string status = string.Equals(rejection.Reason, "SelectionDisabled", StringComparison.Ordinal)
            ? "SELECTION_DISABLED"
            : "REJECTED";
        AppendCandidateRowIfNewSession(context, pairContext, status, emptyCandidate, rejection.Reason);
    }

    public static void NotifyRejected(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        ProactiveResetCandidate candidate,
        ProactiveResetRejection rejection)
    {
        string reason = string.IsNullOrEmpty(rejection.Reason) ? "UNKNOWN" : rejection.Reason;
        AppendCandidateRowIfNewSession(context, pairContext, "REJECTED", candidate, reason);
    }

    private static void AppendCandidateRowIfNewSession(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        string candidateStatus,
        ProactiveResetCandidate candidate,
        string rejectReason)
    {
        if (!ShouldLoggingEnabled())
            return;
        if (context == null || pairContext.UnitA == null || pairContext.UnitB == null)
            return;
        if (pairContext.UnitA.GetRealUser() == null || pairContext.UnitB.GetRealUser() == null)
            return;

        int userId = pairContext.UnitAId;
        int otherUserId = pairContext.UnitBId;
        int pairMinUserId = Mathf.Min(userId, otherUserId);
        int pairMaxUserId = Mathf.Max(userId, otherUserId);

        AppendCandidateRow(
            context,
            pairContext,
            userId,
            otherUserId,
            pairMinUserId,
            pairMaxUserId,
            candidateStatus,
            candidate,
            rejectReason);
    }

    private static void AppendCandidateRow(
        ProactiveResetFrameContext context,
        ProactiveResetPairContext pairContext,
        int userId,
        int otherUserId,
        int pairMinUserId,
        int pairMaxUserId,
        string candidateStatus,
        ProactiveResetCandidate candidate,
        string rejectReason)
    {
        EnsureLogFile();

        int episodeObjectId = pairContext.UnitA.controller != null ? pairContext.UnitA.controller.GetEpisodeID() : -1;
        StringBuilder sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyyMMddHHmmss.fff", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(ToInvariant(Time.time)).Append(',');
        sb.Append(episodeObjectId).Append(',');
        sb.Append(Time.frameCount).Append(',');
        sb.Append(ToInvariant(Time.time)).Append(',');
        sb.Append(candidate.OriginTriggerId).Append(',');
        sb.Append(candidate.CandidateId).Append(',');
        sb.Append(candidate.DecisionId).Append(',');
        sb.Append(-1).Append(',');
        sb.Append(userId).Append(',');
        sb.Append(otherUserId).Append(',');
        sb.Append(pairMinUserId).Append(',');
        sb.Append(pairMaxUserId).Append(',');
        sb.Append(SanitizeCsv(context.Settings.judgeMode.ToString())).Append(',');
        sb.Append(SanitizeCsv(context.Settings.userSelectionMode.ToString())).Append(',');

        sb.Append(SanitizeCsv(candidateStatus)).Append(',');
        sb.Append(candidate.Accepted ? 1 : 0).Append(',');
        sb.Append(candidate.Executed ? 1 : 0).Append(',');
        sb.Append(ToInvariant(candidate.ResetDirection.x)).Append(',');
        sb.Append(ToInvariant(candidate.ResetDirection.y)).Append(',');
        sb.Append(ToInvariant(candidate.KeepMargin)).Append(',');
        sb.Append(ToInvariant(candidate.SelectedM)).Append(',');
        sb.Append(ToInvariant(candidate.SelectedCSelf)).Append(',');
        sb.Append(ToInvariant(candidate.SelectedScore)).Append(',');
        sb.Append(SanitizeCsv(string.IsNullOrEmpty(rejectReason) ? "NONE" : rejectReason));
        sb.AppendLine();

        AppendLineWithHeader(logFilePath, sb.ToString());
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

        logFilePath = _GCM.GM_DataRecord.instance != null
            ? _GCM.GM_DataRecord.instance.GetRunRawLogPath("proactive_candidate_frame.csv")
            : string.Empty;
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
