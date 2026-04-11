using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    /// <summary>
    /// Data structures for predictive occupancy model (未来占用)
    /// </summary>
    public struct PredictedOccupancyDisc
    {
        public Vector3 Center;
        public float Radius;
    }

    public struct PredictedOccupancyCapsuleSegment
    {
        public Vector3 StartCenter;
        public Vector3 EndCenter;
        public float StartRadius;
        public float EndRadius;
    }

    public struct PredictedOccupancySample
    {
        public int UserId;
        public float Horizon;
        public Vector3 PredictedPosition;
        public float UncertaintyRadius;
        public PredictedOccupancyDisc Disc;
    }

    public class PredictedOccupancyBand
    {
        public int UserId;
        public Vector3 CurrentPosition;
        public readonly List<PredictedOccupancySample> Samples = new List<PredictedOccupancySample>();
        public readonly List<PredictedOccupancyCapsuleSegment> Segments = new List<PredictedOccupancyCapsuleSegment>();
    }

    public class PredictedOccupancyFrame
    {
        public int FrameIndex;
        public float SimulationTime;
        public readonly List<PredictedOccupancyBand> Bands = new List<PredictedOccupancyBand>();
    }

    /// <summary>
    /// Builder for converting prediction sequences into occupancy bands (prediction → occupancy)
    /// </summary>
    public class PredictiveOccupancyBuilder
    {
        public PredictedOccupancyFrame BuildFrame(
            IReadOnlyList<GameObject> physicalUsers,
            VelocityPredictor predictor,
            IReadOnlyList<float> horizons,
            PredictionUncertaintyModel uncertaintyModel,
            int frameIndex,
            float simulationTime)
        {
            PredictedOccupancyFrame frame = new PredictedOccupancyFrame
            {
                FrameIndex = frameIndex,
                SimulationTime = simulationTime
            };

            if (physicalUsers == null || predictor == null || horizons == null)
                return frame;

            PredictionUncertaintyModel model = uncertaintyModel ?? new PredictionUncertaintyModel();

            for (int userId = 0; userId < physicalUsers.Count; userId++)
            {
                GameObject user = physicalUsers[userId];
                if (user == null)
                    continue;

                Vector3 currentPosition = user.transform.position;
                PredictedOccupancyBand band = new PredictedOccupancyBand
                {
                    UserId = userId,
                    CurrentPosition = currentPosition
                };

                IReadOnlyList<PredictionSequenceStep> sequence = predictor.GetPredictionSequence(
                    userId,
                    currentPosition,
                    horizons,
                    model);

                for (int i = 0; i < sequence.Count; i++)
                {
                    PredictionSequenceStep step = sequence[i];
                    PredictedOccupancySample sample = new PredictedOccupancySample
                    {
                        UserId = userId,
                        Horizon = step.Horizon,
                        PredictedPosition = step.PredictedPosition,
                        UncertaintyRadius = step.UncertaintyRadius,
                        Disc = new PredictedOccupancyDisc
                        {
                            Center = step.PredictedPosition,
                            Radius = step.UncertaintyRadius
                        }
                    };

                    band.Samples.Add(sample);
                }

                BuildCapsuleSegments(band);
                frame.Bands.Add(band);
            }

            return frame;
        }

        private void BuildCapsuleSegments(PredictedOccupancyBand band)
        {
            if (band == null || band.Samples.Count < 2)
                return;

            for (int i = 0; i < band.Samples.Count - 1; i++)
            {
                PredictedOccupancySample a = band.Samples[i];
                PredictedOccupancySample b = band.Samples[i + 1];

                band.Segments.Add(new PredictedOccupancyCapsuleSegment
                {
                    StartCenter = a.Disc.Center,
                    EndCenter = b.Disc.Center,
                    StartRadius = a.Disc.Radius,
                    EndRadius = b.Disc.Radius
                });
            }
        }
    }
}
