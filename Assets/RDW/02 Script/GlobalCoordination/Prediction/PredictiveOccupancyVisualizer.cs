using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    /// <summary>
    /// Visualizer for predictive occupancy bands using Gizmos.
    /// Renders occupancy predictions in editor with customizable height and color callback.
    /// </summary>
    public class PredictiveOccupancyVisualizer
    {
        /// <summary>
        /// Callback to resolve color for a given userId.
        /// If null, uses default color interpolation.
        /// </summary>
        public System.Func<int, Color> ColorResolver { get; set; }

        private const float DefaultHorizonHeight = 0.03f;

        /// <summary>
        /// Draw all occupancy bands from the latest frame.
        /// Call this from OnDrawGizmos().
        /// </summary>
        public void Draw(PredictedOccupancyFrame frame, float gizmoHeight = DefaultHorizonHeight)
        {
            if (frame == null || frame.Bands.Count == 0)
                return;

            for (int i = 0; i < frame.Bands.Count; i++)
            {
                DrawBand(frame.Bands[i], gizmoHeight);
            }
        }

        private void DrawBand(PredictedOccupancyBand band, float gizmoHeight)
        {
            if (band == null)
                return;

            Color baseColor = ColorResolver != null
                ? ColorResolver(band.UserId)
                : ResolveDefaultColor(band.UserId);

            // Current position (large filled sphere)
            Vector3 currentPos = band.CurrentPosition;
            currentPos.y = gizmoHeight;
            Gizmos.color = baseColor;
            Gizmos.DrawSphere(currentPos, 0.045f);

            // Predicted samples (points + uncertainty discs)
            for (int s = 0; s < band.Samples.Count; s++)
            {
                DrawSample(band.Samples[s], baseColor, gizmoHeight);
            }

            // Capsule segments (center line + side rails)
            for (int seg = 0; seg < band.Segments.Count; seg++)
            {
                DrawSegment(band.Segments[seg], baseColor, gizmoHeight);
            }
        }

        private void DrawSample(PredictedOccupancySample sample, Color baseColor, float gizmoHeight)
        {
            Vector3 pos = sample.PredictedPosition;
            pos.y = gizmoHeight;

            // Small filled sphere for predicted point
            Color pointColor = baseColor;
            pointColor.a = 0.9f;
            Gizmos.color = pointColor;
            Gizmos.DrawSphere(pos, 0.03f);

            // Wire sphere for uncertainty disc
            Color discColor = baseColor;
            discColor.a = 0.55f;
            Gizmos.color = discColor;
            Gizmos.DrawWireSphere(pos, sample.UncertaintyRadius);
        }

        private void DrawSegment(PredictedOccupancyCapsuleSegment segment, Color baseColor, float gizmoHeight)
        {
            Vector3 a = segment.StartCenter;
            a.y = gizmoHeight;
            Vector3 b = segment.EndCenter;
            b.y = gizmoHeight;

            // Center line
            Color lineColor = baseColor;
            lineColor.a = 0.8f;
            Gizmos.color = lineColor;
            Gizmos.DrawLine(a, b);

            // Side rails showing radius transition
            Vector3 dir = (b - a).normalized;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                Vector3 aL = a + side * segment.StartRadius;
                Vector3 aR = a - side * segment.StartRadius;
                Vector3 bL = b + side * segment.EndRadius;
                Vector3 bR = b - side * segment.EndRadius;
                Gizmos.DrawLine(aL, bL);
                Gizmos.DrawLine(aR, bR);
            }
        }

        /// <summary>
        /// Default color resolution using HSV rotation by userId
        /// </summary>
        private Color ResolveDefaultColor(int userId)
        {
            float hue = (userId * 0.37f) % 1f;
            return Color.HSVToRGB(hue, 0.7f, 0.85f);
        }
    }
}
