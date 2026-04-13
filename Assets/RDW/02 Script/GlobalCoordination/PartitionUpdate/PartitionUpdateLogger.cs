using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace _GCM.PartitionUpdate
{
    public class PartitionUpdateLogger
    {
        private string _logFilePath = string.Empty;
        private bool _headerWritten;

        public void ResetSession()
        {
            _logFilePath = string.Empty;
            _headerWritten = false;
        }

        public void TryLogAttempt(int frameIndex, float time, PartitionUpdateAttempt attempt)
        {
            if (attempt == null)
                return;

            EnsureLogFile();
            if (string.IsNullOrEmpty(_logFilePath))
                return;

            StringBuilder sb = new StringBuilder();
            if (!_headerWritten)
            {
                sb.AppendLine("frame,time,userId,triggerType,oldSeedX,oldSeedY,newSeedX,newSeedY,seedShiftDist,weightedImprovement,cellImprovement,neighborImprovement,cellRiskBefore,cellRiskAfter,neighborRiskBefore,neighborRiskAfter,minCellClearanceBefore,minCellClearanceAfter,minNeighborSeparationBefore,minNeighborSeparationAfter,speedAtTrigger,cellPersistCount,neighborPersistCount,cooldownRemaining,accepted,rejectReason");
                _headerWritten = true;
            }

            sb.Append(frameIndex).Append(',');
            sb.Append(ToInvariant(time)).Append(',');
            sb.Append(attempt.UserId).Append(',');
            sb.Append(attempt.TriggerType).Append(',');
            sb.Append(ToInvariant(attempt.OldSeed.x)).Append(',');
            sb.Append(ToInvariant(attempt.OldSeed.y)).Append(',');
            sb.Append(ToInvariant(attempt.NewSeed.x)).Append(',');
            sb.Append(ToInvariant(attempt.NewSeed.y)).Append(',');
            sb.Append(ToInvariant(attempt.SeedShiftDist)).Append(',');
            sb.Append(ToInvariant(attempt.WeightedImprovement)).Append(',');
            sb.Append(ToInvariant(attempt.CellImprovement)).Append(',');
            sb.Append(ToInvariant(attempt.NeighborImprovement)).Append(',');
            sb.Append(ToInvariant(attempt.CellRiskBefore)).Append(',');
            sb.Append(ToInvariant(attempt.CellRiskAfter)).Append(',');
            sb.Append(ToInvariant(attempt.NeighborRiskBefore)).Append(',');
            sb.Append(ToInvariant(attempt.NeighborRiskAfter)).Append(',');
            sb.Append(ToInvariant(attempt.MinCellClearanceBefore)).Append(',');
            sb.Append(ToInvariant(attempt.MinCellClearanceAfter)).Append(',');
            sb.Append(ToInvariant(attempt.MinNeighborSeparationBefore)).Append(',');
            sb.Append(ToInvariant(attempt.MinNeighborSeparationAfter)).Append(',');
            sb.Append(ToInvariant(attempt.SpeedAtTrigger)).Append(',');
            sb.Append(attempt.CellPersistCount).Append(',');
            sb.Append(attempt.NeighborPersistCount).Append(',');
            sb.Append(ToInvariant(attempt.CooldownRemaining)).Append(',');
            sb.Append(attempt.Accepted ? 1 : 0).Append(',');
            sb.Append(attempt.RejectReason);
            sb.AppendLine();

            File.AppendAllText(_logFilePath, sb.ToString());
        }

        private void EnsureLogFile()
        {
            if (!string.IsNullOrEmpty(_logFilePath))
                return;

            string root = Path.Combine(Directory.GetCurrentDirectory(), "CGnA_DataLog", "partitionUpdate");
            Directory.CreateDirectory(root);

            string name = $"partition_update_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
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