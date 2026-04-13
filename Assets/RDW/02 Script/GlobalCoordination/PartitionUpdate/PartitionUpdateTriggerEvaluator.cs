using System.Collections.Generic;
using UnityEngine;

namespace _GCM.PartitionUpdate
{
    public class PartitionUpdateTriggerResult
    {
        public PartitionUpdateTriggerType TriggerType;
        public bool CooldownBlocked;
        public float CooldownRemaining;
        public float SpeedForDecision;
        public int CellPersistCount;
        public int NeighborPersistCount;
    }

    public class PartitionUpdateTriggerEvaluator
    {
        private readonly float _cellRiskEnterThreshold;
        private readonly float _cellRiskExitThreshold;
        private readonly float _neighborRiskEnterThreshold;
        private readonly float _neighborRiskExitThreshold;
        private readonly int _persistFramesCell;
        private readonly int _persistFramesNeighbor;
        private readonly float _seedUpdateCooldown;
        private readonly float _seedTriggerMoveThreshold;
        private readonly float _speedEmaAlpha;

        public PartitionUpdateTriggerEvaluator(
            float cellRiskEnterThreshold,
            float cellRiskExitThreshold,
            float neighborRiskEnterThreshold,
            float neighborRiskExitThreshold,
            int persistFramesCell,
            int persistFramesNeighbor,
            float seedUpdateCooldown,
            float seedTriggerMoveThreshold,
            float speedEmaAlpha = 0.5f)
        {
            _cellRiskEnterThreshold = Mathf.Clamp01(cellRiskEnterThreshold);
            _cellRiskExitThreshold = Mathf.Clamp01(Mathf.Min(cellRiskExitThreshold, _cellRiskEnterThreshold));
            _neighborRiskEnterThreshold = Mathf.Clamp01(neighborRiskEnterThreshold);
            _neighborRiskExitThreshold = Mathf.Clamp01(Mathf.Min(neighborRiskExitThreshold, _neighborRiskEnterThreshold));
            _persistFramesCell = Mathf.Max(1, persistFramesCell);
            _persistFramesNeighbor = Mathf.Max(1, persistFramesNeighbor);
            _seedUpdateCooldown = Mathf.Max(0f, seedUpdateCooldown);
            _seedTriggerMoveThreshold = Mathf.Max(0f, seedTriggerMoveThreshold);
            _speedEmaAlpha = Mathf.Clamp01(speedEmaAlpha);
        }

        public PartitionUpdateTriggerResult UpdateAndEvaluate(
            float cellRisk,
            float neighborRisk,
            float instantSpeed,
            float currentTime,
            RiskDrivenSeedUpdateState state)
        {
            PartitionUpdateTriggerResult result = new PartitionUpdateTriggerResult
            {
                TriggerType = PartitionUpdateTriggerType.None,
                CooldownBlocked = false,
                CooldownRemaining = 0f,
                SpeedForDecision = 0f,
                CellPersistCount = 0,
                NeighborPersistCount = 0
            };

            if (state == null)
                return result;

            if (state.SpeedEma <= 0.0001f)
                state.SpeedEma = Mathf.Max(0f, instantSpeed);
            else
                state.SpeedEma = Mathf.Lerp(state.SpeedEma, Mathf.Max(0f, instantSpeed), _speedEmaAlpha);

            result.SpeedForDecision = state.SpeedEma;

            UpdateArmedStates(cellRisk, neighborRisk, state);

            bool moving = state.SpeedEma > _seedTriggerMoveThreshold;
            if (!moving)
            {
                state.CellRiskPersistCount = 0;
                state.NeighborRiskPersistCount = 0;
                result.CellPersistCount = 0;
                result.NeighborPersistCount = 0;
                return result;
            }

            if (state.IsCellRiskArmed)
                state.CellRiskPersistCount++;
            else
                state.CellRiskPersistCount = 0;

            if (state.IsNeighborRiskArmed)
                state.NeighborRiskPersistCount++;
            else
                state.NeighborRiskPersistCount = 0;

            result.CellPersistCount = state.CellRiskPersistCount;
            result.NeighborPersistCount = state.NeighborRiskPersistCount;

            bool cellTriggered = state.CellRiskPersistCount >= _persistFramesCell;
            bool neighborTriggered = state.NeighborRiskPersistCount >= _persistFramesNeighbor;

            if (!cellTriggered && !neighborTriggered)
                return result;

            if (cellTriggered && neighborTriggered)
                result.TriggerType = PartitionUpdateTriggerType.Both;
            else
                result.TriggerType = cellTriggered ? PartitionUpdateTriggerType.Cell : PartitionUpdateTriggerType.Neighbor;

            float cooldownRemaining = GetCooldownRemaining(currentTime, state);
            result.CooldownRemaining = cooldownRemaining;
            if (cooldownRemaining > 0f)
            {
                result.CooldownBlocked = true;
            }

            return result;
        }

        private void UpdateArmedStates(float cellRisk, float neighborRisk, RiskDrivenSeedUpdateState state)
        {
            if (!state.IsCellRiskArmed)
            {
                if (cellRisk > _cellRiskEnterThreshold)
                    state.IsCellRiskArmed = true;
            }
            else if (cellRisk < _cellRiskExitThreshold)
            {
                state.IsCellRiskArmed = false;
            }

            if (!state.IsNeighborRiskArmed)
            {
                if (neighborRisk > _neighborRiskEnterThreshold)
                    state.IsNeighborRiskArmed = true;
            }
            else if (neighborRisk < _neighborRiskExitThreshold)
            {
                state.IsNeighborRiskArmed = false;
            }
        }

        public float GetCooldownRemaining(float currentTime, RiskDrivenSeedUpdateState state)
        {
            if (state == null)
                return 0f;

            float elapsed = currentTime - state.LastUpdateTime;
            if (elapsed >= _seedUpdateCooldown)
                return 0f;

            return _seedUpdateCooldown - Mathf.Max(0f, elapsed);
        }

        public void Commit(Vector2 committedSeed, float currentTime, RiskDrivenSeedUpdateState state)
        {
            if (state == null)
                return;

            state.CommittedSeed = committedSeed;
            state.LastUpdateTime = currentTime;
            state.CellRiskPersistCount = 0;
            state.NeighborRiskPersistCount = 0;
        }

        public void Rollback(RiskDrivenSeedUpdateState state, float currentTime)
        {
            if (state == null)
                return;

            state.LastUpdateTime = currentTime;
            state.CellRiskPersistCount = 0;
            state.NeighborRiskPersistCount = 0;
        }

        public void ResetAll(IReadOnlyList<RiskDrivenSeedUpdateState> states, IReadOnlyList<Vector2> seeds)
        {
            if (states == null)
                return;

            for (int i = 0; i < states.Count; i++)
            {
                RiskDrivenSeedUpdateState state = states[i];
                if (state == null)
                    continue;

                Vector2 seed = Vector2.zero;
                if (seeds != null && i < seeds.Count)
                    seed = seeds[i];

                state.CommittedSeed = seed;
                state.LastUpdateTime = float.NegativeInfinity;
                state.CellRiskPersistCount = 0;
                state.NeighborRiskPersistCount = 0;
                state.IsCellRiskArmed = false;
                state.IsNeighborRiskArmed = false;
                state.SpeedEma = 0f;
            }
        }
    }
}