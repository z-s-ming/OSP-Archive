using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RDW.Coordination.LocalSafeTarget;

namespace RDW.Coordination.Logging
{
    /// <summary>
    /// Logs local safe target selection results to CSV for analysis
    /// </summary>
    public class LocalSafeTargetLogger
    {
        private string logFilePath;
        private StringBuilder csvBuilder = new StringBuilder();
        private bool headerWritten = false;

        public LocalSafeTargetLogger(string experimentFolder, string filename = "local_safe_targets.csv")
        {
            logFilePath = Path.Combine(experimentFolder, filename);
            InitializeHeader();
        }

        private void InitializeHeader()
        {
            csvBuilder.AppendLine("Frame,UserId,TargetX,TargetY,TotalScore,SampleCount,FallbackUsed,Timestamp");
            headerWritten = true;
        }

        public void LogFrame(int frameIndex, Dictionary<int, LocalTargetResult> targets, float timestamp)
        {
            foreach (var kvp in targets)
            {
                LocalTargetResult target = kvp.Value;
                string line = string.Format(
                    "{0},{1},{2},{3},{4},{5},{6},{7}",
                    frameIndex,
                    target.userId,
                    target.targetPoint.x.ToString("F4"),
                    target.targetPoint.y.ToString("F4"),
                    target.totalScore.ToString("F4"),
                    target.sampleCount,
                    target.fallbackUsed ? 1 : 0,
                    timestamp.ToString("F4"));

                csvBuilder.AppendLine(line);
            }
        }

        public void Flush()
        {
            if (!headerWritten || csvBuilder.Length == 0)
                return;

            try
            {
                File.WriteAllText(logFilePath, csvBuilder.ToString());
                Debug.Log($"[LocalSafeTargetLogger] Flushed {csvBuilder.Length} bytes to {logFilePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LocalSafeTargetLogger] Failed to write log file: {e.Message}");
            }
        }

        public void Clear()
        {
            csvBuilder.Clear();
            if (headerWritten)
            {
                csvBuilder.AppendLine("Frame,UserId,TargetX,TargetY,TotalScore,SampleCount,FallbackUsed,Timestamp");
            }
        }
    }
}
