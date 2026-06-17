using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class RoadConflictTuningLogger : MonoBehaviour
{
    public static RoadConflictTuningLogger Instance { get; private set; }

    [Header("Scenario")]
    [SerializeField] private string scenarioLabel = "road_head_on";

    [Header("Distance Thresholds")]
    [SerializeField] private float collisionDistanceMeters = 1.0f;
    [SerializeField] private float severeCloseDistanceMeters = 1.2f;
    [SerializeField] private float closeDistanceMeters = 1.5f;
    [SerializeField] private float releaseHysteresisMeters = 0.2f;

    [Header("Walking-Distance Bins")]
    [SerializeField] private float walkingDistanceBinMeters = 10.0f;
    [SerializeField] private bool useMeanUserDistance = true;

    [Header("Output")]
    [SerializeField] private bool logFrameSamples = false;
    [SerializeField] private int frameSampleStride = 10;
    [SerializeField] private bool writeLiveStatus = true;
    [SerializeField] private float flushIntervalSeconds = 2.0f;
    [SerializeField] private string outputSubfolder = "roadConflictTuning";

    [Header("Unresolved Close Diagnostics")]
    [SerializeField] private float diagnosticDistanceMeters = 1.2f;
    [SerializeField] private int continuousCloseMinFrames = 5;
    [SerializeField] private bool writeOnlyProblemEvents = true;

    private readonly Dictionary<long, PairState> pairStates = new Dictionary<long, PairState>();
    private readonly List<string> binRows = new List<string>();
    private readonly List<string> frameRows = new List<string>();
    private readonly List<string> unresolvedCloseRows = new List<string>();
    private readonly List<string> episodeInitialPositionRows = new List<string>();

    private Vector2[] previousPositions;
    private bool[] hasPreviousPositions;
    private float[] traveledDistances;
    private Object2D[] trackedRealUsers;

    private float currentBinStartDistance;
    private float currentBinMinDistance = float.MaxValue;
    private float currentBinDistanceSum;
    private int currentBinSampleCount;
    private int currentBinCollisionEvents;
    private int currentBinSevereCloseEvents;
    private int currentBinCloseEvents;
    private int totalCollisionEvents;
    private int totalSevereCloseEvents;
    private int totalCloseEvents;
    private int currentBinIdleUserFrames;
    private int currentBinWallResetUserFrames;
    private int currentBinUserResetUserFrames;
    private int currentBinOtherStatusUserFrames;
    private float lastObservedWalkedDistance;
    private float lastObservedMinPairDistance;
    private float lastFlushRealtime;

    private string runFolder;
    private bool initialized;
    private int nextUnresolvedCloseEventId = 1;
    private int lastLoggedEpisodeObjectId = int.MinValue;

    private class PairState
    {
        public bool InCollision;
        public bool InSevereClose;
        public bool InClose;
        public bool DiagnosticActive;
        public int DiagnosticEventId;
        public int DiagnosticStartFrame;
        public float DiagnosticStartTime;
        public float DiagnosticStartWalkedDistance;
        public string DiagnosticStartMinStatus;
        public string DiagnosticStartMaxStatus;
        public int DiagnosticFrameCount;
        public float DiagnosticMinDistance;
        public bool HadProactiveTrigger;
        public int TriggerId = -1;
        public string JudgeMode = "NONE";
        public bool HadCandidate;
        public string CandidateStatus = "NONE";
        public string RejectReason = "NONE";
        public bool HadAcceptedCandidate;
        public bool HadProactiveExecution;
        public int ExecutionId = -1;
        public int LastSignalFrame = -1;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void FixedUpdate()
    {
        RDWSimulationManager manager = RDWSimulationManager.instance;
        if (manager == null || manager.GetRedirectedUnits == null || manager.GetRedirectedUnits.Length < 2)
            return;

        RedirectedUnit[] units = manager.GetRedirectedUnits;
        EnsureInitialized(units);
        RecordEpisodeInitialPositionsIfNeeded(units);

        float walkedDistance = UpdateWalkingDistances(units);
        float minPairDistance = SamplePairDistances(units, walkedDistance);
        lastObservedWalkedDistance = walkedDistance;
        lastObservedMinPairDistance = minPairDistance;

        currentBinMinDistance = Mathf.Min(currentBinMinDistance, minPairDistance);
        currentBinDistanceSum += minPairDistance;
        currentBinSampleCount++;
        CountCurrentStatuses(units);

        if (logFrameSamples && Time.frameCount % Mathf.Max(1, frameSampleStride) == 0)
        {
            frameRows.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1:F4},{2:F4},{3},{4},{5},{6:F4}",
                Time.frameCount,
                Time.time,
                walkedDistance,
                currentBinCollisionEvents,
                currentBinSevereCloseEvents,
                currentBinCloseEvents,
                minPairDistance));
        }

        if (walkedDistance - currentBinStartDistance >= walkingDistanceBinMeters)
        {
            FlushCurrentBin(walkedDistance);
            currentBinStartDistance = walkedDistance;
        }

        if (ShouldFlushLiveFiles())
        {
            WriteLogs();
            lastFlushRealtime = Time.realtimeSinceStartup;
        }
    }

    private void OnDisable()
    {
        FinishOpenDiagnosticEvents(GetCurrentWalkingDistance());
        FlushCurrentBin(GetCurrentWalkingDistance());
        WriteLogs();
    }

    private void OnApplicationQuit()
    {
        FinishOpenDiagnosticEvents(GetCurrentWalkingDistance());
        FlushCurrentBin(GetCurrentWalkingDistance());
        WriteLogs();
    }

    private void EnsureInitialized(RedirectedUnit[] units)
    {
        if (initialized && previousPositions != null && previousPositions.Length == units.Length && IsTrackingSameUsers(units))
            return;

        initialized = true;
        pairStates.Clear();
        binRows.Clear();
        frameRows.Clear();
        unresolvedCloseRows.Clear();
        episodeInitialPositionRows.Clear();
        nextUnresolvedCloseEventId = 1;
        lastLoggedEpisodeObjectId = int.MinValue;

        previousPositions = new Vector2[units.Length];
        hasPreviousPositions = new bool[units.Length];
        traveledDistances = new float[units.Length];
        trackedRealUsers = new Object2D[units.Length];
        for (int i = 0; i < units.Length; i++)
        {
            trackedRealUsers[i] = units[i] != null ? units[i].GetRealUser() : null;
        }
        currentBinStartDistance = 0.0f;
        totalCollisionEvents = 0;
        totalSevereCloseEvents = 0;
        totalCloseEvents = 0;
        ResetCurrentBin();

        string safeScenarioLabel = MakeSafePathPart(string.IsNullOrEmpty(scenarioLabel) ? "unnamed_scenario" : scenarioLabel);
        string root = Path.Combine(Directory.GetCurrentDirectory(), "CGnA_DataLog", outputSubfolder, safeScenarioLabel);
        runFolder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(runFolder);

        binRows.Add("BinStartDistance,BinEndDistance,Frame,Time,MinPairDistance,MeanPairDistance,CollisionEvents,SevereCloseEvents,CloseEvents,IdleUserFrames,WallResetUserFrames,UserResetUserFrames,OtherStatusUserFrames,TotalCollisionEvents,TotalSevereCloseEvents,TotalCloseEvents");
        if (logFrameSamples)
        {
            frameRows.Add("Frame,Time,WalkedDistance,BinCollisionEvents,BinSevereCloseEvents,BinCloseEvents,MinPairDistance");
        }
        unresolvedCloseRows.Add("EventId,EpisodeObjectId,PairMinUserId,PairMaxUserId,StartFrame,EndFrame,StartTime,EndTime,DurationFrames,DurationSeconds,MinDistance,EnterThreshold,ReleaseThreshold,StartWalkedDistance,EndWalkedDistance,UnitMinStartStatus,UnitMaxStartStatus,UnitMinEndStatus,UnitMaxEndStatus,HadProactiveTrigger,TriggerId,JudgeMode,HadCandidate,CandidateStatus,RejectReason,HadAcceptedCandidate,HadProactiveExecution,ExecutionId,Classification");
        episodeInitialPositionRows.Add("EpisodeObjectId,Frame,Time,UnitId,Status,RealX,RealY,VirtualX,VirtualY");

        WriteConfig(units);
        WriteLogs();
        lastFlushRealtime = Time.realtimeSinceStartup;
    }

    private bool IsTrackingSameUsers(RedirectedUnit[] units)
    {
        if (trackedRealUsers == null || trackedRealUsers.Length != units.Length)
            return false;

        for (int i = 0; i < units.Length; i++)
        {
            Object2D currentRealUser = units[i] != null ? units[i].GetRealUser() : null;
            if (trackedRealUsers[i] != currentRealUser)
                return false;
        }

        return true;
    }

    private void RecordEpisodeInitialPositionsIfNeeded(RedirectedUnit[] units)
    {
        int episodeObjectId = ResolveEpisodeObjectId(units);
        if (episodeObjectId == lastLoggedEpisodeObjectId)
            return;

        lastLoggedEpisodeObjectId = episodeObjectId;
        if (units == null)
            return;

        for (int i = 0; i < units.Length; i++)
        {
            RedirectedUnit unit = units[i];
            if (unit == null || unit.GetRealUser() == null)
                continue;

            Vector2 realPosition = unit.GetRealUser().transform2D.localPosition;
            Vector2 virtualPosition = unit.GetVirtualUser() != null
                ? unit.GetVirtualUser().transform2D.localPosition
                : Vector2.zero;
            episodeInitialPositionRows.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2:F4},{3},{4},{5:F6},{6:F6},{7:F6},{8:F6}",
                episodeObjectId,
                Time.frameCount,
                Time.time,
                unit.GetID(),
                SanitizeCsv(unit.GetStatus()),
                realPosition.x,
                realPosition.y,
                virtualPosition.x,
                virtualPosition.y));
        }
    }

    private float UpdateWalkingDistances(RedirectedUnit[] units)
    {
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] == null || units[i].GetRealUser() == null)
                continue;

            Vector2 position = units[i].GetRealUser().transform2D.localPosition;
            if (hasPreviousPositions[i])
            {
                traveledDistances[i] += Vector2.Distance(previousPositions[i], position);
            }

            previousPositions[i] = position;
            hasPreviousPositions[i] = true;
        }

        return GetCurrentWalkingDistance();
    }

    private float GetCurrentWalkingDistance()
    {
        if (traveledDistances == null || traveledDistances.Length == 0)
            return 0.0f;

        float sum = 0.0f;
        float max = 0.0f;
        for (int i = 0; i < traveledDistances.Length; i++)
        {
            sum += traveledDistances[i];
            max = Mathf.Max(max, traveledDistances[i]);
        }

        return useMeanUserDistance ? sum / traveledDistances.Length : max;
    }

    private float SamplePairDistances(RedirectedUnit[] units, float walkedDistance)
    {
        float minDistance = float.MaxValue;
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] == null || units[i].GetRealUser() == null)
                continue;

            for (int j = i + 1; j < units.Length; j++)
            {
                if (units[j] == null || units[j].GetRealUser() == null)
                    continue;

                float distance = Vector2.Distance(
                    units[i].GetRealUser().transform2D.localPosition,
                    units[j].GetRealUser().transform2D.localPosition);
                minDistance = Mathf.Min(minDistance, distance);
                UpdatePairEvents(BuildPairKey(units[i].GetID(), units[j].GetID()), units[i], units[j], distance, walkedDistance);
            }
        }

        return minDistance < float.MaxValue ? minDistance : 0.0f;
    }

    private void UpdatePairEvents(long pairKey, RedirectedUnit unitA, RedirectedUnit unitB, float distance, float walkedDistance)
    {
        PairState state;
        if (!pairStates.TryGetValue(pairKey, out state))
        {
            state = new PairState();
            pairStates.Add(pairKey, state);
        }

        UpdateBand(distance, collisionDistanceMeters, ref state.InCollision, ref currentBinCollisionEvents);
        UpdateBand(distance, severeCloseDistanceMeters, ref state.InSevereClose, ref currentBinSevereCloseEvents);
        UpdateBand(distance, closeDistanceMeters, ref state.InClose, ref currentBinCloseEvents);
        UpdateDiagnosticEvent(pairKey, state, unitA, unitB, distance, walkedDistance);
    }

    private void UpdateBand(float distance, float threshold, ref bool active, ref int counter)
    {
        if (!active && distance <= threshold)
        {
            active = true;
            counter++;
        }
        else if (active && distance >= threshold + releaseHysteresisMeters)
        {
            active = false;
        }
    }

    private void UpdateDiagnosticEvent(
        long pairKey,
        PairState state,
        RedirectedUnit unitA,
        RedirectedUnit unitB,
        float distance,
        float walkedDistance)
    {
        float enterThreshold = Mathf.Max(0.0f, diagnosticDistanceMeters);
        float releaseThreshold = enterThreshold + Mathf.Max(0.0f, releaseHysteresisMeters);
        if (distance <= enterThreshold)
        {
            if (!state.DiagnosticActive)
            {
                StartDiagnosticEvent(pairKey, state, unitA, unitB, distance, walkedDistance);
            }
            else
            {
                state.DiagnosticFrameCount++;
                state.DiagnosticMinDistance = Mathf.Min(state.DiagnosticMinDistance, distance);
            }
            return;
        }

        if (state.DiagnosticActive && distance >= releaseThreshold)
        {
            FinishDiagnosticEvent(pairKey, state, unitA, unitB, walkedDistance);
            ResetDiagnosticState(state);
        }
    }

    private void StartDiagnosticEvent(
        long pairKey,
        PairState state,
        RedirectedUnit unitA,
        RedirectedUnit unitB,
        float distance,
        float walkedDistance)
    {
        DecodePairKey(pairKey, out int pairMinUserId, out int pairMaxUserId);
        state.DiagnosticActive = true;
        state.DiagnosticEventId = nextUnresolvedCloseEventId++;
        state.DiagnosticStartFrame = Time.frameCount;
        state.DiagnosticStartTime = Time.time;
        state.DiagnosticStartWalkedDistance = walkedDistance;
        state.DiagnosticStartMinStatus = ResolveStatusForPairId(unitA, unitB, pairMinUserId);
        state.DiagnosticStartMaxStatus = ResolveStatusForPairId(unitA, unitB, pairMaxUserId);
        state.DiagnosticFrameCount = 1;
        state.DiagnosticMinDistance = distance;
        if (state.LastSignalFrame < Time.frameCount - 1)
        {
            ClearDiagnosticSignals(state);
        }
    }

    private void FinishOpenDiagnosticEvents(float walkedDistance)
    {
        if (pairStates.Count == 0)
            return;

        RDWSimulationManager manager = RDWSimulationManager.instance;
        RedirectedUnit[] units = manager != null ? manager.GetRedirectedUnits : null;
        foreach (KeyValuePair<long, PairState> kvp in pairStates)
        {
            PairState state = kvp.Value;
            if (!state.DiagnosticActive)
                continue;

            DecodePairKey(kvp.Key, out int pairMinUserId, out int pairMaxUserId);
            RedirectedUnit unitA = ResolveUnit(units, pairMinUserId);
            RedirectedUnit unitB = ResolveUnit(units, pairMaxUserId);
            FinishDiagnosticEvent(kvp.Key, state, unitA, unitB, walkedDistance);
            ResetDiagnosticState(state);
        }
    }

    private void FinishDiagnosticEvent(
        long pairKey,
        PairState state,
        RedirectedUnit unitA,
        RedirectedUnit unitB,
        float walkedDistance)
    {
        if (state.DiagnosticFrameCount < Mathf.Max(1, continuousCloseMinFrames))
            return;

        string classification = ClassifyDiagnosticEvent(state, unitA, unitB);
        if (writeOnlyProblemEvents && !IsProblemClassification(classification))
            return;

        DecodePairKey(pairKey, out int pairMinUserId, out int pairMaxUserId);
        int episodeObjectId = ResolveEpisodeObjectId(unitA, unitB);
        string endMinStatus = ResolveStatusForPairId(unitA, unitB, pairMinUserId);
        string endMaxStatus = ResolveStatusForPairId(unitA, unitB, pairMaxUserId);
        unresolvedCloseRows.Add(string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2},{3},{4},{5},{6:F4},{7:F4},{8},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14:F4},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27},{28}",
            state.DiagnosticEventId,
            episodeObjectId,
            pairMinUserId,
            pairMaxUserId,
            state.DiagnosticStartFrame,
            Time.frameCount,
            state.DiagnosticStartTime,
            Time.time,
            state.DiagnosticFrameCount,
            Time.time - state.DiagnosticStartTime,
            state.DiagnosticMinDistance,
            Mathf.Max(0.0f, diagnosticDistanceMeters),
            Mathf.Max(0.0f, diagnosticDistanceMeters) + Mathf.Max(0.0f, releaseHysteresisMeters),
            state.DiagnosticStartWalkedDistance,
            walkedDistance,
            SanitizeCsv(state.DiagnosticStartMinStatus),
            SanitizeCsv(state.DiagnosticStartMaxStatus),
            SanitizeCsv(endMinStatus),
            SanitizeCsv(endMaxStatus),
            state.HadProactiveTrigger ? 1 : 0,
            state.TriggerId,
            SanitizeCsv(state.JudgeMode),
            state.HadCandidate ? 1 : 0,
            SanitizeCsv(state.CandidateStatus),
            SanitizeCsv(state.RejectReason),
            state.HadAcceptedCandidate ? 1 : 0,
            state.HadProactiveExecution ? 1 : 0,
            state.ExecutionId,
            SanitizeCsv(classification)));
    }

    private static void ResetDiagnosticState(PairState state)
    {
        state.DiagnosticActive = false;
        state.DiagnosticFrameCount = 0;
        ClearDiagnosticSignals(state);
    }

    private static void ClearDiagnosticSignals(PairState state)
    {
        state.HadProactiveTrigger = false;
        state.TriggerId = -1;
        state.JudgeMode = "NONE";
        state.HadCandidate = false;
        state.CandidateStatus = "NONE";
        state.RejectReason = "NONE";
        state.HadAcceptedCandidate = false;
        state.HadProactiveExecution = false;
        state.ExecutionId = -1;
        state.LastSignalFrame = -1;
    }

    private static string ClassifyDiagnosticEvent(PairState state, RedirectedUnit unitA, RedirectedUnit unitB)
    {
        if (state.HadProactiveExecution)
            return "EXECUTED_PROACTIVE_RESET";
        if (state.HadAcceptedCandidate)
            return "TRIGGER_ACCEPTED_NOT_EXECUTED";
        if (state.HadCandidate && !state.HadAcceptedCandidate)
            return "TRIGGER_REJECTED";
        if (state.HadProactiveTrigger)
            return "TRIGGER_NO_CANDIDATE";

        string statusA = unitA != null ? unitA.GetStatus() : string.Empty;
        string statusB = unitB != null ? unitB.GetStatus() : string.Empty;
        if (string.Equals(statusA, "USER_RESET", StringComparison.Ordinal) ||
            string.Equals(statusB, "USER_RESET", StringComparison.Ordinal))
        {
            return "ORDINARY_USER_RESET";
        }
        if (string.Equals(statusA, "WALL_RESET", StringComparison.Ordinal) ||
            string.Equals(statusB, "WALL_RESET", StringComparison.Ordinal))
        {
            return "WALL_OR_OTHER_RESET";
        }

        return "UNTRIGGERED_CONTINUOUS_CLOSE";
    }

    private static bool IsProblemClassification(string classification)
    {
        return string.Equals(classification, "UNTRIGGERED_CONTINUOUS_CLOSE", StringComparison.Ordinal) ||
               string.Equals(classification, "TRIGGER_REJECTED", StringComparison.Ordinal) ||
               string.Equals(classification, "TRIGGER_ACCEPTED_NOT_EXECUTED", StringComparison.Ordinal) ||
               string.Equals(classification, "TRIGGER_NO_CANDIDATE", StringComparison.Ordinal);
    }

    private void FlushCurrentBin(float walkedDistance)
    {
        if (!initialized || currentBinSampleCount <= 0)
            return;

        totalCollisionEvents += currentBinCollisionEvents;
        totalSevereCloseEvents += currentBinSevereCloseEvents;
        totalCloseEvents += currentBinCloseEvents;

        float meanPairDistance = currentBinDistanceSum / currentBinSampleCount;
        binRows.Add(string.Format(
            CultureInfo.InvariantCulture,
            "{0:F4},{1:F4},{2},{3:F4},{4:F4},{5:F4},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15}",
            currentBinStartDistance,
            walkedDistance,
            Time.frameCount,
            Time.time,
            currentBinMinDistance,
            meanPairDistance,
            currentBinCollisionEvents,
            currentBinSevereCloseEvents,
            currentBinCloseEvents,
            currentBinIdleUserFrames,
            currentBinWallResetUserFrames,
            currentBinUserResetUserFrames,
            currentBinOtherStatusUserFrames,
            totalCollisionEvents,
            totalSevereCloseEvents,
            totalCloseEvents));

        ResetCurrentBin();
    }

    private void ResetCurrentBin()
    {
        currentBinMinDistance = float.MaxValue;
        currentBinDistanceSum = 0.0f;
        currentBinSampleCount = 0;
        currentBinCollisionEvents = 0;
        currentBinSevereCloseEvents = 0;
        currentBinCloseEvents = 0;
        currentBinIdleUserFrames = 0;
        currentBinWallResetUserFrames = 0;
        currentBinUserResetUserFrames = 0;
        currentBinOtherStatusUserFrames = 0;
    }

    private void CountCurrentStatuses(RedirectedUnit[] units)
    {
        if (units == null)
            return;

        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] == null)
                continue;

            string status = units[i].GetStatus();
            if (string.Equals(status, "IDLE", StringComparison.Ordinal))
                currentBinIdleUserFrames++;
            else if (string.Equals(status, "WALL_RESET", StringComparison.Ordinal))
                currentBinWallResetUserFrames++;
            else if (string.Equals(status, "USER_RESET", StringComparison.Ordinal))
                currentBinUserResetUserFrames++;
            else
                currentBinOtherStatusUserFrames++;
        }
    }

    public static void NotifyProactiveTrigger(
        ProactiveResetPairContext pairContext,
        ProactiveResetTriggerEvent triggerEvent)
    {
        RoadConflictTuningLogger logger = Instance;
        if (logger == null || !logger.isActiveAndEnabled)
            return;
        if (!logger.EnsureInitializedForNotification())
            return;

        PairState state = logger.GetOrCreatePairState(pairContext.UnitAId, pairContext.UnitBId);
        state.HadProactiveTrigger = true;
        state.TriggerId = triggerEvent.TriggerId;
        state.JudgeMode = string.IsNullOrEmpty(triggerEvent.JudgeMode) ? "UNKNOWN" : triggerEvent.JudgeMode;
        state.LastSignalFrame = Time.frameCount;
    }

    public static void NotifyProactiveCandidate(
        ProactiveResetPairContext pairContext,
        ProactiveResetCandidate candidate,
        string candidateStatus,
        string rejectReason)
    {
        RoadConflictTuningLogger logger = Instance;
        if (logger == null || !logger.isActiveAndEnabled)
            return;
        if (!logger.EnsureInitializedForNotification())
            return;

        PairState state = logger.GetOrCreatePairState(pairContext.UnitAId, pairContext.UnitBId);
        state.HadCandidate = true;
        state.CandidateStatus = string.IsNullOrEmpty(candidateStatus) ? "UNKNOWN" : candidateStatus;
        state.RejectReason = string.IsNullOrEmpty(rejectReason) ? "NONE" : rejectReason;
        state.HadAcceptedCandidate = state.HadAcceptedCandidate || candidate.Accepted;
        state.LastSignalFrame = Time.frameCount;
    }

    public static void NotifyProactiveExecution(int userId, int otherUserId, int executionId)
    {
        RoadConflictTuningLogger logger = Instance;
        if (logger == null || !logger.isActiveAndEnabled || userId < 0 || otherUserId < 0)
            return;
        if (!logger.EnsureInitializedForNotification())
            return;

        PairState state = logger.GetOrCreatePairState(userId, otherUserId);
        state.HadProactiveExecution = true;
        state.ExecutionId = executionId;
        state.LastSignalFrame = Time.frameCount;
    }

    private bool EnsureInitializedForNotification()
    {
        RDWSimulationManager manager = RDWSimulationManager.instance;
        if (manager == null || manager.GetRedirectedUnits == null || manager.GetRedirectedUnits.Length < 2)
            return false;

        EnsureInitialized(manager.GetRedirectedUnits);
        return initialized;
    }

    private PairState GetOrCreatePairState(int unitAId, int unitBId)
    {
        long pairKey = BuildPairKey(unitAId, unitBId);
        PairState state;
        if (!pairStates.TryGetValue(pairKey, out state))
        {
            state = new PairState();
            pairStates.Add(pairKey, state);
        }

        return state;
    }

    private void WriteConfig(RedirectedUnit[] units)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Key,Value");
        builder.AppendLine("ScenarioLabel," + scenarioLabel);
        builder.AppendLine("CollisionDistanceMeters," + collisionDistanceMeters.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("SevereCloseDistanceMeters," + severeCloseDistanceMeters.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("CloseDistanceMeters," + closeDistanceMeters.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("ReleaseHysteresisMeters," + releaseHysteresisMeters.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("WalkingDistanceBinMeters," + walkingDistanceBinMeters.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("UseMeanUserDistance," + useMeanUserDistance);
        builder.AppendLine("UnresolvedCloseDiagnostics,EnabledByRoadConflictTuningLogger");
        builder.AppendLine("DiagnosticDistanceMeters," + diagnosticDistanceMeters.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("ContinuousCloseMinFrames," + continuousCloseMinFrames);
        builder.AppendLine("WriteOnlyProblemEvents," + writeOnlyProblemEvents);
        builder.AppendLine("UnitCount," + units.Length);

        RDWSimulationManager manager = RDWSimulationManager.instance;
        if (manager != null && manager.simulationSetting != null)
        {
            builder.AppendLine("UseVisualization," + manager.simulationSetting.useVisualization);
            builder.AppendLine("AllowUserReset," + manager.simulationSetting.bAllowUserReset);
        }

        File.WriteAllText(Path.Combine(runFolder, "config.csv"), builder.ToString());
    }

    private void WriteLogs()
    {
        if (!initialized || string.IsNullOrEmpty(runFolder))
            return;

        File.WriteAllLines(Path.Combine(runFolder, "distance_bins.csv"), binRows.ToArray());
        if (logFrameSamples)
        {
            File.WriteAllLines(Path.Combine(runFolder, "frame_samples.csv"), frameRows.ToArray());
        }
        File.WriteAllLines(Path.Combine(runFolder, "unresolved_close_events.csv"), unresolvedCloseRows.ToArray());
        File.WriteAllLines(Path.Combine(runFolder, "episode_initial_positions.csv"), episodeInitialPositionRows.ToArray());

        if (writeLiveStatus)
        {
            WriteLiveStatus();
        }
    }

    private bool ShouldFlushLiveFiles()
    {
        if (!writeLiveStatus && !logFrameSamples)
            return false;

        return Time.realtimeSinceStartup - lastFlushRealtime >= Mathf.Max(0.1f, flushIntervalSeconds);
    }

    private void WriteLiveStatus()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Key,Value");
        builder.AppendLine("Frame," + Time.frameCount);
        builder.AppendLine("Time," + Time.time.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("WalkedDistance," + lastObservedWalkedDistance.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("CurrentBinStartDistance," + currentBinStartDistance.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("CurrentBinSampleCount," + currentBinSampleCount);
        builder.AppendLine("CurrentBinMinPairDistance," + (currentBinSampleCount > 0 ? currentBinMinDistance : 0.0f).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("CurrentBinMeanPairDistance," + (currentBinSampleCount > 0 ? currentBinDistanceSum / currentBinSampleCount : 0.0f).ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("LastMinPairDistance," + lastObservedMinPairDistance.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("CurrentBinCollisionEvents," + currentBinCollisionEvents);
        builder.AppendLine("CurrentBinSevereCloseEvents," + currentBinSevereCloseEvents);
        builder.AppendLine("CurrentBinCloseEvents," + currentBinCloseEvents);
        builder.AppendLine("CurrentBinIdleUserFrames," + currentBinIdleUserFrames);
        builder.AppendLine("CurrentBinWallResetUserFrames," + currentBinWallResetUserFrames);
        builder.AppendLine("CurrentBinUserResetUserFrames," + currentBinUserResetUserFrames);
        builder.AppendLine("CurrentBinOtherStatusUserFrames," + currentBinOtherStatusUserFrames);

        RDWSimulationManager manager = RDWSimulationManager.instance;
        if (manager != null && manager.GetRedirectedUnits != null)
        {
            RedirectedUnit[] units = manager.GetRedirectedUnits;
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] == null || units[i].GetRealUser() == null)
                    continue;

                Vector2 realPosition = units[i].GetRealUser().transform2D.localPosition;
                Vector2 virtualPosition = units[i].GetVirtualUser() != null
                    ? units[i].GetVirtualUser().transform2D.localPosition
                    : Vector2.zero;
                builder.AppendLine("Unit" + i + "Status," + units[i].GetStatus());
                builder.AppendLine("Unit" + i + "RealX," + realPosition.x.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Unit" + i + "RealY," + realPosition.y.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Unit" + i + "VirtualX," + virtualPosition.x.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("Unit" + i + "VirtualY," + virtualPosition.y.ToString(CultureInfo.InvariantCulture));
                if (traveledDistances != null && i < traveledDistances.Length)
                {
                    builder.AppendLine("Unit" + i + "TraveledDistance," + traveledDistances[i].ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        File.WriteAllText(Path.Combine(runFolder, "live_status.csv"), builder.ToString());
    }

    private static long BuildPairKey(int a, int b)
    {
        int min = Mathf.Min(a, b);
        int max = Mathf.Max(a, b);
        return ((long)(uint)min << 32) | (uint)max;
    }

    private static void DecodePairKey(long pairKey, out int minId, out int maxId)
    {
        minId = (int)(pairKey >> 32);
        maxId = (int)(pairKey & 0xffffffffL);
    }

    private static RedirectedUnit ResolveUnit(RedirectedUnit[] units, int unitId)
    {
        if (units == null || unitId < 0)
            return null;

        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] != null && units[i].GetID() == unitId)
                return units[i];
        }

        if (unitId < units.Length)
            return units[unitId];

        return null;
    }

    private static string ResolveStatusForPairId(RedirectedUnit unitA, RedirectedUnit unitB, int pairUserId)
    {
        if (unitA != null && unitA.GetID() == pairUserId)
            return unitA.GetStatus();
        if (unitB != null && unitB.GetID() == pairUserId)
            return unitB.GetStatus();

        return string.Empty;
    }

    private static int ResolveEpisodeObjectId(RedirectedUnit unitA, RedirectedUnit unitB)
    {
        if (unitA != null && unitA.controller != null)
            return unitA.controller.GetEpisodeID();
        if (unitB != null && unitB.controller != null)
            return unitB.controller.GetEpisodeID();

        return -1;
    }

    private static int ResolveEpisodeObjectId(RedirectedUnit[] units)
    {
        if (units == null)
            return -1;

        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] != null && units[i].controller != null)
                return units[i].controller.GetEpisodeID();
        }

        return -1;
    }

    private static string SanitizeCsv(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Replace(",", "_");
    }

    private static string MakeSafePathPart(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            builder.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }
}
