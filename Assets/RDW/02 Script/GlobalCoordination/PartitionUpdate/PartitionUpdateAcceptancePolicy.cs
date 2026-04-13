using UnityEngine;

namespace _GCM.PartitionUpdate
{
    public class PartitionUpdateAcceptancePolicy
    {
        public struct Decision
        {
            public bool Accepted;
            public float WeightedImprovement;
            public float CellImprovement;
            public float NeighborImprovement;
            public PartitionUpdateRejectReason RejectReason;
        }

        private readonly float _acceptEpsilon;
        private readonly float _cellRiskWeight;
        private readonly float _neighborRiskWeight;
        private readonly float _maxAllowedCellRiskWorsen;
        private readonly float _maxAllowedNeighborRiskWorsen;

        public PartitionUpdateAcceptancePolicy(
            float acceptEpsilon,
            float cellRiskWeight,
            float neighborRiskWeight,
            float maxAllowedCellRiskWorsen,
            float maxAllowedNeighborRiskWorsen)
        {
            _acceptEpsilon = Mathf.Max(0f, acceptEpsilon);
            _cellRiskWeight = Mathf.Max(0f, cellRiskWeight);
            _neighborRiskWeight = Mathf.Max(0f, neighborRiskWeight);
            _maxAllowedCellRiskWorsen = Mathf.Max(0f, maxAllowedCellRiskWorsen);
            _maxAllowedNeighborRiskWorsen = Mathf.Max(0f, maxAllowedNeighborRiskWorsen);
        }

        public Decision Evaluate(float cellBefore, float cellAfter, float neighborBefore, float neighborAfter)
        {
            float cellImprovement = cellBefore - cellAfter;
            float neighborImprovement = neighborBefore - neighborAfter;
            float weightedImprovement = _cellRiskWeight * cellImprovement + _neighborRiskWeight * neighborImprovement;

            if (cellAfter > cellBefore + _maxAllowedCellRiskWorsen)
            {
                return new Decision
                {
                    Accepted = false,
                    WeightedImprovement = weightedImprovement,
                    CellImprovement = cellImprovement,
                    NeighborImprovement = neighborImprovement,
                    RejectReason = PartitionUpdateRejectReason.CellRiskWorsened
                };
            }

            if (neighborAfter > neighborBefore + _maxAllowedNeighborRiskWorsen)
            {
                return new Decision
                {
                    Accepted = false,
                    WeightedImprovement = weightedImprovement,
                    CellImprovement = cellImprovement,
                    NeighborImprovement = neighborImprovement,
                    RejectReason = PartitionUpdateRejectReason.NeighborRiskWorsened
                };
            }

            if (weightedImprovement <= _acceptEpsilon)
            {
                return new Decision
                {
                    Accepted = false,
                    WeightedImprovement = weightedImprovement,
                    CellImprovement = cellImprovement,
                    NeighborImprovement = neighborImprovement,
                    RejectReason = PartitionUpdateRejectReason.InsufficientImprovement
                };
            }

            return new Decision
            {
                Accepted = true,
                WeightedImprovement = weightedImprovement,
                CellImprovement = cellImprovement,
                NeighborImprovement = neighborImprovement,
                RejectReason = PartitionUpdateRejectReason.None
            };
        }
    }
}
