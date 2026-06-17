using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace _GCM
{
    public class EpisodeService
    {
        private readonly int _totalUserCount;
        private readonly Text _textCurrentEpisode;

        public EpisodeService(int totalUserCount, int simulationCountMax, float targetDistancePerUser, Text textCurrentEpisode)
        {
            _totalUserCount = totalUserCount;
            SimulationCountMax = simulationCountMax;
            TargetDistancePerUser = targetDistancePerUser;
            _textCurrentEpisode = textCurrentEpisode;
        }

        public int SimulationCountMax { get; set; }
        public float TargetDistancePerUser { get; set; }
        public int CurrentSimulationCount { get; private set; }
        public bool IsExperimentCompleted { get; private set; }

        public void BeginEpisode(StateCollector stateCollector)
        {
            stateCollector?.ResetEpisodeDistance();
            RDWSimulationManager.instance?.ResetEpisodeUserResetEventCounts();
            GM_DataRecord.instance?.ResetInterResetDistanceTracking();
        }

        public void Tick(StateCollector stateCollector)
        {
            if (stateCollector == null)
                return;

            stateCollector.AccumulateVirtualDistanceStep(_totalUserCount, 2.0f);
        }

        public bool ShouldEndEpisode(StateCollector stateCollector)
        {
            if (stateCollector == null)
                return false;

            if (stateCollector.UsersCumulativeDist.Count < _totalUserCount)
                return false;

            for (int i = 0; i < _totalUserCount; i++)
            {
                if (stateCollector.UsersCumulativeDist[i] < TargetDistancePerUser)
                    return false;
            }

            return true;
        }

        public void FinalizeEpisode(StateCollector stateCollector)
        {
            if (stateCollector == null || IsExperimentCompleted)
                return;

            var units = RDWSimulationManager.instance.GetRedirectedUnits;

            StringBuilder sb = new StringBuilder();
            int totalResetCountFromUsers = 0;
            int proactiveActionCount = RDWSimulationManager.instance.GetEpisodeProactiveUserResetEventCount();
            int doubleActionCount = RDWSimulationManager.instance.GetEpisodeDoubleUserResetEventCount();
            int singleActionCount = RDWSimulationManager.instance.GetEpisodeSingleUserResetEventCount();
            int boundaryCollisionCount = 0;
            List<int> userResetCounts = new List<int>();
            int episodeSeed = GlobalCoordinationManager.instance != null
                ? GlobalCoordinationManager.instance.CurrentEpisodeSeed
                : int.MinValue;

            for (int i = 0; i < _totalUserCount; i++)
            {
                if (units != null && i < units.Length && units[i] != null)
                {
                    int resetCount = (int)units[i].resultData.getTotalReset();
                    int wallResetCount = (int)units[i].resultData.getWallReset();
                    int shutterResetCount = (int)units[i].resultData.getShutterReset();
                    totalResetCountFromUsers += resetCount;
                    boundaryCollisionCount += wallResetCount + shutterResetCount;
                    userResetCounts.Add(resetCount);
                }
                else
                {
                    userResetCounts.Add(0);
                }
            }

            float userResetVariance = CalculateVariance(userResetCounts);

            sb.Append(episodeSeed == int.MinValue ? "NA" : episodeSeed.ToString()).Append(',');
            sb.Append(totalResetCountFromUsers).Append(',');
            sb.Append(userResetVariance.ToString("F4")).Append(',');
            sb.Append(boundaryCollisionCount).Append(',');
            sb.Append(proactiveActionCount).Append(',');
            sb.Append(singleActionCount).Append(',');
            sb.Append(doubleActionCount);

            for (int i = 0; i < _totalUserCount; i++)
            {
                sb.Append(',').Append(userResetCounts[i]);
            }

            Debug.Log("Per-user reset counts: " + sb);

            if (GM_DataRecord.instance != null)
            {
                GM_DataRecord.instance.Enequeue_Data(sb.ToString());
            }

            CurrentSimulationCount++;
            if (_textCurrentEpisode != null)
            {
                int nextEpisodeDisplay = Mathf.Min(CurrentSimulationCount + 1, SimulationCountMax);
                _textCurrentEpisode.text = "Current Episode : " + nextEpisodeDisplay;
            }

            if (CurrentSimulationCount >= SimulationCountMax)
            {
                IsExperimentCompleted = true;
                GM_DataRecord.instance?.Save_SteamingData_Batch();
                GM_DataRecord.instance?.Save_InterResetDistance_Batch();
            }
        }

        public void CompleteInitialStateReplayEpisode()
        {
            if (IsExperimentCompleted)
                return;

            CurrentSimulationCount++;
            if (_textCurrentEpisode != null)
            {
                int nextEpisodeDisplay = Mathf.Min(CurrentSimulationCount + 1, SimulationCountMax);
                _textCurrentEpisode.text = "Current Episode : " + nextEpisodeDisplay;
            }

            if (CurrentSimulationCount >= SimulationCountMax)
            {
                IsExperimentCompleted = true;
                GM_DataRecord.instance?.Save_InterResetDistance_Batch();
            }
        }

        private float CalculateVariance(List<int> values)
        {
            if (values == null || values.Count == 0)
                return 0f;

            float mean = (float)values.Average();
            float squaredDeviationSum = 0f;

            for (int i = 0; i < values.Count; i++)
            {
                float deviation = values[i] - mean;
                squaredDeviationSum += deviation * deviation;
            }

            return squaredDeviationSum / values.Count;
        }
    }
}
