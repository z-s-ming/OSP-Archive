using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace _GCM
{
    [Serializable]
    public struct PredictionSummaryStats
    {
        public int Count;
        public float MeanError;
        public float StdError;
        public float MaxError;

        public override string ToString()
        {
            return $"Count={Count}, Mean={MeanError:F4}, Std={StdError:F4}, Max={MaxError:F4}";
        }
    }

    internal struct PendingPredictionSample
    {
        public int EpisodeId;
        public int UserId;
        public int FrameIndex;
        public float SampleTime;
        public float Horizon;
        public string TrajectoryMode;
        public float UncertaintyRadius;

        public Vector3 PredictedPosition;
    }

    public struct ResolvedPredictionSample
    {
        public int EpisodeId;
        public int UserId;
        public int FrameIndex;
        public float SampleTime;
        public float Horizon;
        public string TrajectoryMode;

        public Vector3 PredictedPosition;
        public Vector3 RealizedPosition;
        public float Error;
        public float UncertaintyRadius;
    }

    public struct EpisodePredictionSummary
    {
        public int EpisodeId;
        public string TrajectoryMode;
        public float Horizon;
        public int SampleCount;
        public float MeanError;
        public float StdError;
        public float MaxError;
        public int CensoredCount;
    }

    internal struct RunningStats
    {
        public int Count;
        public float Sum;
        public float SumSq;
        public float Max;

        public void Add(float value)
        {
            Count++;
            Sum += value;
            SumSq += value * value;
            if (value > Max) Max = value;
        }

        public PredictionSummaryStats ToSummary()
        {
            PredictionSummaryStats stats = new PredictionSummaryStats
            {
                Count = Count,
                MeanError = Count > 0 ? Sum / Count : 0f,
                MaxError = Count > 0 ? Max : 0f
            };

            if (Count > 1)
            {
                float mean = Sum / Count;
                float variance = Mathf.Max(0f, SumSq / Count - mean * mean);
                stats.StdError = Mathf.Sqrt(variance);
            }
            else
            {
                stats.StdError = 0f;
            }

            return stats;
        }
    }

    internal struct OverallSummaryAccumulator
    {
        public int SampleCount;
        public float Sum;
        public float SumSq;
        public float Max;
        public int CensoredCount;

        public void Add(EpisodePredictionSummary summary)
        {
            if (summary.SampleCount > 0)
            {
                SampleCount += summary.SampleCount;
                Sum += summary.MeanError * summary.SampleCount;

                float variance = summary.StdError * summary.StdError;
                float secondMoment = variance + summary.MeanError * summary.MeanError;
                SumSq += secondMoment * summary.SampleCount;

                if (summary.MaxError > Max)
                    Max = summary.MaxError;
            }

            CensoredCount += summary.CensoredCount;
        }

        public EpisodePredictionSummary ToOverallSummary(string mode, float horizon)
        {
            EpisodePredictionSummary summary = new EpisodePredictionSummary
            {
                EpisodeId = 0,
                TrajectoryMode = mode,
                Horizon = horizon,
                SampleCount = SampleCount,
                MeanError = SampleCount > 0 ? Sum / SampleCount : 0f,
                MaxError = SampleCount > 0 ? Max : 0f,
                CensoredCount = CensoredCount
            };

            if (SampleCount > 1)
            {
                float mean = summary.MeanError;
                float variance = Mathf.Max(0f, SumSq / SampleCount - mean * mean);
                summary.StdError = Mathf.Sqrt(variance);
            }
            else
            {
                summary.StdError = 0f;
            }

            return summary;
        }
    }

    public class PredictionEvaluator
    {
        private readonly Dictionary<string, RunningStats> _episodeStatsByKey = new Dictionary<string, RunningStats>();
        private readonly Dictionary<string, OverallSummaryAccumulator> _overallSummaryByKey = new Dictionary<string, OverallSummaryAccumulator>();
        private readonly Dictionary<string, int> _episodeCensoredCountByKey = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _keyToMode = new Dictionary<string, string>();
        private readonly Dictionary<string, float> _keyToHorizon = new Dictionary<string, float>();

        private readonly List<PendingPredictionSample> _pendingSamples = new List<PendingPredictionSample>();
        private readonly List<ResolvedPredictionSample> _episodeResolvedSamples = new List<ResolvedPredictionSample>();

        private int _currentEpisodeId = 0;

        public bool EnableEvaluation { get; set; } = false;
        public bool ExportResolvedSamples { get; set; } = false;

        /// <summary>
        /// 每隔多少帧采样一次。建议 5 / 10 / 20，不要每帧都采。
        /// </summary>
        public int SampleEveryNFrames { get; set; } = 10;

        /// <summary>
        /// 需要评估的 horizon 列表（秒）
        /// </summary>
        public List<float> Horizons { get; } = new List<float>();

        public void ClearAll()
        {
            _episodeStatsByKey.Clear();
            _overallSummaryByKey.Clear();
            _episodeCensoredCountByKey.Clear();
            _keyToMode.Clear();
            _keyToHorizon.Clear();
            _pendingSamples.Clear();
            _episodeResolvedSamples.Clear();
            _currentEpisodeId = 0;
        }

        public void BeginEpisode(int episodeId)
        {
            _currentEpisodeId = episodeId;
            ClearEpisodeData();
        }

        public void ClearEpisodeData()
        {
            _pendingSamples.Clear();
            _episodeResolvedSamples.Clear();
            _episodeStatsByKey.Clear();
            _episodeCensoredCountByKey.Clear();
        }

        public List<EpisodePredictionSummary> EndEpisode(bool countUnresolvedAsCensored = true)
        {
            if (countUnresolvedAsCensored)
            {
                for (int i = 0; i < _pendingSamples.Count; i++)
                {
                    PendingPredictionSample sample = _pendingSamples[i];
                    string key = BuildStatsKey(sample.TrajectoryMode, sample.Horizon);
                    RegisterKeyMeta(key, sample.TrajectoryMode, sample.Horizon);

                    if (!_episodeCensoredCountByKey.TryGetValue(key, out int count))
                        count = 0;

                    _episodeCensoredCountByKey[key] = count + 1;
                }
            }

            _pendingSamples.Clear();

            HashSet<string> allKeys = new HashSet<string>(_episodeStatsByKey.Keys);
            foreach (var kv in _episodeCensoredCountByKey)
            {
                allKeys.Add(kv.Key);
            }

            List<EpisodePredictionSummary> episodeSummaries = new List<EpisodePredictionSummary>();
            foreach (string key in allKeys)
            {
                PredictionSummaryStats stats = default;
                if (_episodeStatsByKey.TryGetValue(key, out RunningStats runningStats))
                {
                    stats = runningStats.ToSummary();
                }

                int censoredCount = 0;
                _episodeCensoredCountByKey.TryGetValue(key, out censoredCount);

                EpisodePredictionSummary summary = new EpisodePredictionSummary
                {
                    EpisodeId = _currentEpisodeId,
                    TrajectoryMode = _keyToMode.TryGetValue(key, out string mode) ? mode : "Unknown",
                    Horizon = _keyToHorizon.TryGetValue(key, out float horizon) ? horizon : 0f,
                    SampleCount = stats.Count,
                    MeanError = stats.MeanError,
                    StdError = stats.StdError,
                    MaxError = stats.MaxError,
                    CensoredCount = censoredCount
                };

                episodeSummaries.Add(summary);

                if (!_overallSummaryByKey.TryGetValue(key, out OverallSummaryAccumulator overall))
                {
                    overall = new OverallSummaryAccumulator();
                }

                overall.Add(summary);
                _overallSummaryByKey[key] = overall;
            }

            return episodeSummaries;
        }

        public bool ShouldSample(int frameIndex)
        {
            if (!EnableEvaluation) return false;
            if (SampleEveryNFrames <= 0) return false;
            return frameIndex % SampleEveryNFrames == 0;
        }

        /// <summary>
        /// 注册当前时刻的预测样本。这里只记录预测结果，不立即算误差。
        /// </summary>
        public void RegisterPredictions(
            int episodeId,
            int frameIndex,
            float currentTime,
            IReadOnlyList<GameObject> physicalUsers,
            VelocityPredictor predictor,
            string trajectoryMode)
        {
            if (!EnableEvaluation || physicalUsers == null || predictor == null)
                return;

            if (Horizons.Count == 0)
                return;

            for (int userId = 0; userId < physicalUsers.Count; userId++)
            {
                GameObject go = physicalUsers[userId];
                if (go == null)
                    continue;

                Vector3 currentPos = go.transform.position;

                IReadOnlyList<PredictionSequenceStep> predictionSequence = predictor.GetPredictionSequence(
                    userId,
                    currentPos,
                    Horizons,
                    null);

                for (int h = 0; h < predictionSequence.Count; h++)
                {
                    PredictionSequenceStep step = predictionSequence[h];

                    PendingPredictionSample sample = new PendingPredictionSample
                    {
                        EpisodeId = _currentEpisodeId != 0 ? _currentEpisodeId : episodeId,
                        UserId = userId,
                        FrameIndex = frameIndex,
                        SampleTime = currentTime,
                        Horizon = step.Horizon,
                        TrajectoryMode = trajectoryMode ?? "Unknown",
                        PredictedPosition = step.PredictedPosition,
                        UncertaintyRadius = step.UncertaintyRadius
                    };

                    _pendingSamples.Add(sample);
                }
            }
        }

        /// <summary>
        /// 检查哪些样本已经“到期”，到期就取真实位置并计算误差。
        /// </summary>
        public void ResolveDuePredictions(float currentTime, IReadOnlyList<GameObject> physicalUsers)
        {
            if (!EnableEvaluation || physicalUsers == null || _pendingSamples.Count == 0)
                return;

            for (int i = _pendingSamples.Count - 1; i >= 0; i--)
            {
                PendingPredictionSample sample = _pendingSamples[i];
                if (currentTime < sample.SampleTime + sample.Horizon)
                    continue;

                if (sample.UserId < 0 || sample.UserId >= physicalUsers.Count || physicalUsers[sample.UserId] == null)
                {
                    _pendingSamples.RemoveAt(i);
                    continue;
                }

                Vector3 realPos = physicalUsers[sample.UserId].transform.position;
                realPos.y = 0f;

                Vector3 predPos = sample.PredictedPosition;
                predPos.y = 0f;

                float error = Vector3.Distance(predPos, realPos);

                string key = BuildStatsKey(sample.TrajectoryMode, sample.Horizon);
                RegisterKeyMeta(key, sample.TrajectoryMode, sample.Horizon);

                if (!_episodeStatsByKey.TryGetValue(key, out RunningStats stats))
                {
                    stats = new RunningStats();
                }

                stats.Add(error);
                _episodeStatsByKey[key] = stats;

                if (ExportResolvedSamples)
                {
                    ResolvedPredictionSample resolved = new ResolvedPredictionSample
                    {
                        EpisodeId = sample.EpisodeId,
                        UserId = sample.UserId,
                        FrameIndex = sample.FrameIndex,
                        SampleTime = sample.SampleTime,
                        Horizon = sample.Horizon,
                        TrajectoryMode = sample.TrajectoryMode,
                        PredictedPosition = predPos,
                        RealizedPosition = realPos,
                        Error = error,
                        UncertaintyRadius = sample.UncertaintyRadius
                    };
                    _episodeResolvedSamples.Add(resolved);
                }

                _pendingSamples.RemoveAt(i);
            }
        }

        public PredictionSummaryStats GetStats(string trajectoryMode, float horizon)
        {
            string key = BuildStatsKey(trajectoryMode, horizon);
            if (_episodeStatsByKey.TryGetValue(key, out RunningStats stats))
                return stats.ToSummary();

            return default;
        }

        public Dictionary<string, PredictionSummaryStats> GetCurrentEpisodeStats()
        {
            Dictionary<string, PredictionSummaryStats> result = new Dictionary<string, PredictionSummaryStats>();
            foreach (var kv in _episodeStatsByKey)
            {
                result[kv.Key] = kv.Value.ToSummary();
            }
            return result;
        }

        public string BuildReadableSummary(IReadOnlyList<EpisodePredictionSummary> summaries)
        {
            StringBuilder sb = new StringBuilder();
            if (summaries == null)
                return string.Empty;

            for (int i = 0; i < summaries.Count; i++)
            {
                EpisodePredictionSummary summary = summaries[i];
                sb.AppendLine(
                    $"E{summary.EpisodeId:000} {summary.TrajectoryMode}_H{summary.Horizon:F2} -> " +
                    $"Count={summary.SampleCount}, Mean={summary.MeanError:F4}, Std={summary.StdError:F4}, " +
                    $"Max={summary.MaxError:F4}, Censored={summary.CensoredCount}");
            }
            return sb.ToString();
        }

        public void ExportEpisodeSamplesCsv(string filePath)
        {
            if (!ExportResolvedSamples)
            {
                Debug.LogWarning("PredictionEvaluator.ExportEpisodeSamplesCsv called, but ExportResolvedSamples is false.");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("EpisodeId,UserId,FrameIndex,SampleTime,Horizon,TrajectoryMode,PredX,PredY,PredZ,RealX,RealY,RealZ,Error,UncertaintyRadius");

            for (int i = 0; i < _episodeResolvedSamples.Count; i++)
            {
                ResolvedPredictionSample s = _episodeResolvedSamples[i];

                sb.Append(s.EpisodeId).Append(",");
                sb.Append(s.UserId).Append(",");
                sb.Append(s.FrameIndex).Append(",");
                sb.Append(s.SampleTime.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(s.Horizon.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(s.TrajectoryMode).Append(",");
                sb.Append(s.PredictedPosition.x.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(s.PredictedPosition.y.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(s.PredictedPosition.z.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(s.RealizedPosition.x.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(s.RealizedPosition.y.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(s.RealizedPosition.z.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(s.Error.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(s.UncertaintyRadius.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"PredictionEvaluator episode samples CSV exported: {filePath}");
        }

        public void ExportEpisodeSummaryCsv(string filePath, IReadOnlyList<EpisodePredictionSummary> summaries)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("EpisodeId,TrajectoryMode,Horizon,SampleCount,MeanError,StdError,MaxError,CensoredCount");

            if (summaries != null)
            {
                for (int i = 0; i < summaries.Count; i++)
                {
                    EpisodePredictionSummary s = summaries[i];

                    sb.Append(s.EpisodeId).Append(",");
                    sb.Append(s.TrajectoryMode).Append(",");
                    sb.Append(s.Horizon.ToString(CultureInfo.InvariantCulture)).Append(",");
                    sb.Append(s.SampleCount).Append(",");
                    sb.Append(s.MeanError.ToString(CultureInfo.InvariantCulture)).Append(",");
                    sb.Append(s.StdError.ToString(CultureInfo.InvariantCulture)).Append(",");
                    sb.Append(s.MaxError.ToString(CultureInfo.InvariantCulture)).Append(",");
                    sb.Append(s.CensoredCount);
                    sb.AppendLine();
                }
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"PredictionEvaluator episode summary CSV exported: {filePath}");
        }

        public void ExportOverallSummaryCsv(string filePath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TrajectoryMode,Horizon,SampleCount,MeanError,StdError,MaxError,CensoredCount");

            foreach (var kv in _overallSummaryByKey)
            {
                string key = kv.Key;
                OverallSummaryAccumulator acc = kv.Value;

                string mode = _keyToMode.TryGetValue(key, out string keyMode) ? keyMode : "Unknown";
                float horizon = _keyToHorizon.TryGetValue(key, out float keyHorizon) ? keyHorizon : 0f;

                EpisodePredictionSummary summary = acc.ToOverallSummary(mode, horizon);

                sb.Append(summary.TrajectoryMode).Append(",");
                sb.Append(summary.Horizon.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(summary.SampleCount).Append(",");
                sb.Append(summary.MeanError.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(summary.StdError.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(summary.MaxError.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(summary.CensoredCount);
                sb.AppendLine();
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"PredictionEvaluator overall summary CSV exported: {filePath}");
        }

        private string BuildStatsKey(string mode, float horizon)
        {
            return $"{mode}_H{horizon:F2}";
        }

        private void RegisterKeyMeta(string key, string mode, float horizon)
        {
            if (!_keyToMode.ContainsKey(key))
                _keyToMode[key] = string.IsNullOrEmpty(mode) ? "Unknown" : mode;

            if (!_keyToHorizon.ContainsKey(key))
                _keyToHorizon[key] = horizon;
        }
    }
}