using UnityEngine;

namespace _GCM.PartitionUpdate
{
    public enum PartitionUpdateTriggerType
    {
        None,
        Cell,
        Neighbor,
        Both
    }

    public class RiskDrivenSeedUpdateState
    {
        public Vector2 CommittedSeed;
        public float LastUpdateTime = float.NegativeInfinity;
        public int CellRiskPersistCount;
        public int NeighborRiskPersistCount;
        public bool IsCellRiskArmed;
        public bool IsNeighborRiskArmed;
        public float SpeedEma;
    }

    public enum PartitionUpdateRejectReason
    {
        None,
        InsufficientImprovement,
        CellRiskWorsened,
        NeighborRiskWorsened,
        ProposalInvalid,
        CooldownBlocked
    }

    public class PartitionUpdateAttempt
    {
        public int UserId;
        public PartitionUpdateTriggerType TriggerType;
        public Vector2 OldSeed;
        public Vector2 NewSeed;
        public float SeedShiftDist;
        public float CellRiskBefore;
        public float CellRiskAfter;
        public float NeighborRiskBefore;
        public float NeighborRiskAfter;
        public float WeightedImprovement;
        public float CellImprovement;
        public float NeighborImprovement;
        public float MinCellClearanceBefore;
        public float MinCellClearanceAfter;
        public float MinNeighborSeparationBefore;
        public float MinNeighborSeparationAfter;
        public float SpeedAtTrigger;
        public int CellPersistCount;
        public int NeighborPersistCount;
        public float CooldownRemaining;
        public PartitionUpdateRejectReason RejectReason;
        public bool Accepted;
    }
}