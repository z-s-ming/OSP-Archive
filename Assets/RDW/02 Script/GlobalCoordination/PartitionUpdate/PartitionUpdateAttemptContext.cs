using System.Collections.Generic;
using UnityEngine;
using _GCM;

namespace _GCM.PartitionUpdate
{
    public class PartitionUpdateAttemptContext
    {
        public int UserId;
        public PartitionUpdateTriggerType TriggerType;
        public Vector2 OldSeed;
        public Vector2 ProposedSeed;
        public float OldCellRisk;
        public float OldNeighborRisk;
        public PartitionUpdateAttempt Attempt;
    }

    public class PartitionUpdateTransaction
    {
        public List<Vector2> OriginalSeeds = new List<Vector2>();
        public PartitionRiskEvaluator.PartitionRiskTemporalSnapshot RiskSnapshot;
        public PartitionResult PreviousPartitionResult;
        public PartitionRiskFrame PreviousRiskFrame;
    }
}
