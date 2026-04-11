using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace _GCM
{
    public class PartitionRiskLogger
    {
        private readonly int _outputEveryNFrames;
        private string _logFilePath = string.Empty;
        private bool _headerWritten;

        public PartitionRiskLogger(int outputEveryNFrames)
        {
            _outputEveryNFrames = Mathf.Max(1, outputEveryNFrames);
        }

        public void ResetSession()
        {
            _logFilePath = string.Empty;
            _headerWritten = false;
        }

        public void TryLogFrame(PartitionRiskFrame frame)
        {
            if (frame == null || frame.PerUser == null || frame.PerUser.Count == 0)
                return;

            if (frame.FrameIndex % _outputEveryNFrames != 0)
                return;

            EnsureLogFile();
            if (string.IsNullOrEmpty(_logFilePath))
                return;

            StringBuilder sb = new StringBuilder();
            if (!_headerWritten)
            {
                sb.AppendLine("frame,time,userId,cellBoundaryRisk,physicalBoundaryRisk,userRisk,seedMotionPenalty,cellShapePenaltyReserved,smoothRisk,weightedTotalRisk,dominantRisk,minCellClearance,minPhysicalClearance,minPairSeparation,minOccupancySeparation,seedDelta,frameMeanCellRisk,frameMeanPhysicalRisk,frameMeanUserRisk,frameMeanSmoothRisk,frameWeightedTotalRisk");
                _headerWritten = true;
            }

            for (int i = 0; i < frame.PerUser.Count; i++)
            {
                UserRiskMetrics m = frame.PerUser[i];
                sb.Append(frame.FrameIndex).Append(',');
                sb.Append(ToInvariant(frame.SimulationTime)).Append(',');
                sb.Append(m.UserId).Append(',');
                sb.Append(ToInvariant(m.CellBoundaryRisk)).Append(',');
                sb.Append(ToInvariant(m.PhysicalBoundaryRisk)).Append(',');
                sb.Append(ToInvariant(m.UserRisk)).Append(',');
                sb.Append(ToInvariant(m.SeedMotionPenalty)).Append(',');
                sb.Append(ToInvariant(m.CellShapePenaltyReserved)).Append(',');
                sb.Append(ToInvariant(m.SmoothRisk)).Append(',');
                sb.Append(ToInvariant(m.WeightedTotalRisk)).Append(',');
                sb.Append(m.DominantRisk).Append(',');
                sb.Append(ToInvariant(m.MinCellClearance)).Append(',');
                sb.Append(ToInvariant(m.MinPhysicalClearance)).Append(',');
                sb.Append(ToInvariant(m.MinPairSeparation)).Append(',');
                sb.Append(ToInvariant(m.MinOccupancySeparation)).Append(',');
                sb.Append(ToInvariant(m.SeedDelta)).Append(',');
                sb.Append(ToInvariant(frame.MeanCellBoundaryRisk)).Append(',');
                sb.Append(ToInvariant(frame.MeanPhysicalBoundaryRisk)).Append(',');
                sb.Append(ToInvariant(frame.MeanUserRisk)).Append(',');
                sb.Append(ToInvariant(frame.MeanSmoothRisk)).Append(',');
                sb.Append(ToInvariant(frame.WeightedTotalRisk));
                sb.AppendLine();
            }

            File.AppendAllText(_logFilePath, sb.ToString());

            Debug.Log($"[RiskFrame] f={frame.FrameIndex} total={frame.WeightedTotalRisk:F3} vec=[{frame.MeanCellBoundaryRisk:F3},{frame.MeanUserRisk:F3},{frame.MeanSmoothRisk:F3}] physical={frame.MeanPhysicalBoundaryRisk:F3}");
        }

        private void EnsureLogFile()
        {
            if (!string.IsNullOrEmpty(_logFilePath))
                return;

            string root = Path.Combine(Directory.GetCurrentDirectory(), "CGnA_DataLog", "riskEvaluate");
            Directory.CreateDirectory(root);

            string name = $"risk_frame_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            _logFilePath = Path.Combine(root, name);
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
    }
}
