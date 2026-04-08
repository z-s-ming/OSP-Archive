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
        private readonly List<int> _usersTotalResetPerEpisode = new List<int>();
        private readonly List<int> _userbetResetPerEpisode = new List<int>();
        private readonly List<int> _usersShutterResetPerEpisode = new List<int>();
        private readonly List<float> _mdbrPerEpisode = new List<float>();
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

        public void BeginEpisode(StateCollector stateCollector)
        {
            stateCollector?.ResetEpisodeDistance();
        }

        public void Tick(StateCollector stateCollector)
        {
            if (stateCollector == null)
                return;

            stateCollector.AccumulateDistanceStep(_totalUserCount, 2.0f);
        }

        public bool ShouldEndEpisode(StateCollector stateCollector)
        {
            if (stateCollector == null)
                return false;

            float targetTotalDistance = _totalUserCount * TargetDistancePerUser;
            return stateCollector.CurrentEpisodeTotalDistance >= targetTotalDistance;
        }

        public void FinalizeEpisode(StateCollector stateCollector)
        {
            if (stateCollector == null)
                return;

            var units = RDWSimulationManager.instance.GetRedirectedUnits;

            List<int> userWallReset = new List<int>();
            for (int i = 0; i < _totalUserCount; i++)
            {
                if (units != null && i < units.Length && units[i] != null)
                    userWallReset.Add((int)(units[i].resultData.getWallReset()));
            }
            int wallResetSum = userWallReset.Sum();

            List<int> userShutterReset = new List<int>();
            for (int i = 0; i < _totalUserCount; i++)
            {
                userShutterReset.Add((int)(RDWSimulationManager.instance.GetRedirectedUnits[i].resultData.getShutterReset()));
            }
            int shutterResetSum = userShutterReset.Sum();

            int userbet = RDWSimulationManager.instance.Calc_UserResetFilter();
            int totalResets = wallResetSum + userbet + shutterResetSum;

            _usersTotalResetPerEpisode.Add(totalResets);
            _usersShutterResetPerEpisode.Add(shutterResetSum);
            _userbetResetPerEpisode.Add(userbet);

            float mdbrAvg;
            if (totalResets > 0)
            {
                mdbrAvg = stateCollector.UsersCumulativeDist.Sum() / totalResets;
            }
            else
            {
                mdbrAvg = stateCollector.UsersCumulativeDist.Sum();
            }

            Debug.Log(string.Format("walllreset {0} / userreset {1} /shutterreset {2} / MDbR AVg. {3}", wallResetSum, userbet, shutterResetSum, mdbrAvg));
            Debug.LogWarning(string.Format("walllreset {0} / userreset {1} /shutterreset {2} / MDbR AVg. {3}", wallResetSum, userbet, shutterResetSum, mdbrAvg));

            _mdbrPerEpisode.Add(mdbrAvg);

            StringBuilder sb = new StringBuilder();
            sb.Append(',');
            sb.Append(',');
            sb.Append(wallResetSum).Append(',');
            sb.Append(wallResetSum + shutterResetSum + userbet).Append(',');
            sb.Append(userbet).Append(',');
            sb.Append(shutterResetSum).Append(',');
            sb.Append(mdbrAvg).Append(',');

            if (sb.Length > 0 && sb[sb.Length - 1] == ',')
            {
                sb.Remove(sb.Length - 1, 1);
            }

            if (GM_DataRecord.instance != null)
            {
                GM_DataRecord.instance.Enequeue_Data(sb.ToString());
            }

            CurrentSimulationCount++;
            if (_textCurrentEpisode != null)
            {
                _textCurrentEpisode.text = "Current Episode : " + (CurrentSimulationCount + 1);
            }

            if (CurrentSimulationCount == SimulationCountMax)
            {
                CurrentSimulationCount = 0;
                FlushBatchToRecord();
                _usersTotalResetPerEpisode.Clear();
                _userbetResetPerEpisode.Clear();
                _usersShutterResetPerEpisode.Clear();
                _mdbrPerEpisode.Clear();
            }
        }

        private void FlushBatchToRecord()
        {
            if (GM_DataRecord.instance == null)
                return;

            GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.TotalResetMean, _usersTotalResetPerEpisode.Average().ToString("F3"));
            GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.TotalResetMean, GetStandardDeviation(_usersTotalResetPerEpisode).ToString("F3"));
            GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UserbetResetMean, _userbetResetPerEpisode.Average().ToString("F3"));
            GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UserbetResetMean, GetStandardDeviation(_userbetResetPerEpisode).ToString("F3"));
            GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UsershutterResetMean, _usersShutterResetPerEpisode.Average().ToString("F3"));
            GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UsershutterResetMean, GetStandardDeviation(_usersShutterResetPerEpisode).ToString("F3"));
            GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.MeanDistBetResets, _mdbrPerEpisode.Average().ToString("F3"));
            GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.MeanDistBetResets, GetStandardDeviation(_mdbrPerEpisode).ToString("F3"));
            GM_DataRecord.instance.Save_SteamingData_Batch();
        }

        private float GetStandardDeviation(List<float> values)
        {
            float average = values.Average();
            float sumOfDerivation = 0;
            foreach (float value in values)
            {
                sumOfDerivation += value * value;
            }
            float sumOfDerivationAverage = sumOfDerivation / values.Count;
            return Mathf.Sqrt(sumOfDerivationAverage - (average * average));
        }

        private double GetStandardDeviation(List<int> values)
        {
            double average = values.Average();
            int sumOfDerivation = 0;
            foreach (int value in values)
            {
                sumOfDerivation += value * value;
            }
            int sumOfDerivationAverage = sumOfDerivation / values.Count;
            return Mathf.Sqrt((float)(sumOfDerivationAverage - (average * average)));
        }
    }
}
