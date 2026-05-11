using System.Collections.Generic;
using UnityEngine;

public class ProactiveResetFrameContext
{
    public RedirectedUnit[] Units;
    public _GCM.PartitionResult PartitionResult;
    public Dictionary<int, HashSet<int>> CellAdjacency;
    public ProactiveUserResetSettings Settings;
    public int FrameIndex;
    public float TimeSeconds;
    public float FixedDeltaTime;
    public bool ProactiveEnabled;
    public bool ShouldRunPrecheck;
    public bool DebugVisualizationEnabled;
}

public struct ProactiveResetTriggerEvent
{
    public int TriggerId;
    public RedirectedUnit UnitA;
    public RedirectedUnit UnitB;
    public int UnitAId;
    public int UnitBId;
    public string JudgeMode;
    public float HorizonSeconds;
    public float TriggerDistance;
    public float ClosingSpeed;
    public float ConflictBoundaryDistanceA;
    public float ConflictBoundaryDistanceB;
    public float ConflictBoundaryDistancePair;
    public float ReverseWallDistanceA;
    public float ReverseWallDistanceB;
    public int ConflictBoundaryTrendHitCount;
    public int ConflictBoundaryTrendWindowFrames;
}

public struct ProactiveResetPairContext
{
    public RedirectedUnit UnitA;
    public RedirectedUnit UnitB;
    public int UnitAId;
    public int UnitBId;
    public Vector2 OffsetAB;
    public Vector2 VelocityA;
    public Vector2 VelocityB;
    public float ClosingSpeed;
    public float PredictionHorizonSeconds;
    public int PredictionSampleCount;
}

public struct ProactiveResetCandidate
{
    public int CandidateId;
    public int OriginTriggerId;
    public int DecisionId;
    public int SelectedUserId;
    public int OtherUserId;
    public Vector2 ResetDirection;
    public float KeepMargin;
    public float SelectedM;
    public float SelectedCSelf;
    public bool Accepted;
    public bool Executed;
    public string RejectReason;
}

public struct ProactiveResetIntent
{
    public int OriginTriggerId;
    public int OriginCandidateId;
    public int DecisionId;
    public int SelectedUserId;
    public int OtherUserId;
    public Vector2 ResetDirection;
    public bool IsBidirectionalUserResetEvent;
}

public struct ProactiveResetRejection
{
    public int DecisionId;
    public int OriginTriggerId;
    public int OriginCandidateId;
    public int UserAId;
    public int UserBId;
    public int SelectedUserId;
    public string Reason;
}

public class ProactiveResetFrameResult
{
    public readonly List<ProactiveResetTriggerEvent> Triggers = new List<ProactiveResetTriggerEvent>();
    public readonly List<ProactiveResetCandidate> Candidates = new List<ProactiveResetCandidate>();
    public readonly List<ProactiveResetIntent> Intents = new List<ProactiveResetIntent>();
    public readonly List<ProactiveResetRejection> Rejections = new List<ProactiveResetRejection>();
}
