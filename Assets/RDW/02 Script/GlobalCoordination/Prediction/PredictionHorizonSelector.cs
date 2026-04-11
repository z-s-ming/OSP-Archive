using System.Collections.Generic;

namespace _GCM
{
    public class PredictionHorizonSelector
    {
        public float MeanErrorThreshold { get; set; } = 0.60f;
        public float StdErrorThreshold { get; set; } = 0.45f;
        public int MaxCensoredCount { get; set; } = 0;

        public HorizonSelectionResult SelectTrustedHorizons(IReadOnlyList<EpisodePredictionSummary> summaries, string mode)
        {
            HorizonSelectionResult result = new HorizonSelectionResult();
            if (summaries == null)
                return result;

            for (int i = 0; i < summaries.Count; i++)
            {
                EpisodePredictionSummary summary = summaries[i];
                if (summary.TrajectoryMode != mode)
                    continue;

                bool trusted =
                    summary.SampleCount > 0 &&
                    summary.MeanError <= MeanErrorThreshold &&
                    summary.StdError <= StdErrorThreshold &&
                    summary.CensoredCount <= MaxCensoredCount;

                if (trusted)
                    result.TrustedHorizons.Add(summary.Horizon);
                else
                    result.UntrustedHorizons.Add(summary.Horizon);
            }

            return result;
        }
    }

    public class HorizonSelectionResult
    {
        public readonly List<float> TrustedHorizons = new List<float>();
        public readonly List<float> UntrustedHorizons = new List<float>();
    }
}
