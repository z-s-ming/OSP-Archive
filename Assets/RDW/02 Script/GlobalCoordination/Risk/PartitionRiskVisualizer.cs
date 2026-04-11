using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace _GCM
{
    public class PartitionRiskVisualizer
    {
        private GUIStyle _labelStyle;
        private GUIStyle _labelOutlineStyle;

        public void Draw(PartitionRiskFrame frame, IReadOnlyList<GameObject> users)
        {
            if (frame == null || users == null || frame.PerUser == null)
                return;

            DrawRiskAdjacency(frame, users);
            DrawUserRiskLabels(frame, users);
        }

        private void DrawRiskAdjacency(PartitionRiskFrame frame, IReadOnlyList<GameObject> users)
        {
            if (frame.PairwiseRiskMatrix == null)
                return;

            int n = frame.PairwiseRiskMatrix.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                if (i >= users.Count || users[i] == null)
                    continue;

                for (int j = i + 1; j < n; j++)
                {
                    if (j >= users.Count || users[j] == null)
                        continue;

                    float risk = frame.PairwiseRiskMatrix[i, j];
                    if (risk < frame.AdjacencyRiskThreshold)
                        continue;

                    Vector3 a = users[i].transform.position + Vector3.up * 0.05f;
                    Vector3 b = users[j].transform.position + Vector3.up * 0.05f;
                    Color color = Color.black;
                    color.a = Mathf.Lerp(0.35f, 0.95f, Mathf.Clamp01(risk));

                    Gizmos.color = color;
                    Gizmos.DrawLine(a, b);
                }
            }
        }

        private void DrawUserRiskLabels(PartitionRiskFrame frame, IReadOnlyList<GameObject> users)
        {
#if UNITY_EDITOR
            for (int i = 0; i < frame.PerUser.Count; i++)
            {
                UserRiskMetrics m = frame.PerUser[i];
                if (m.UserId < 0 || m.UserId >= users.Count || users[m.UserId] == null)
                    continue;

                Vector3 p = users[m.UserId].transform.position + Vector3.up * 1.15f;
                DrawReadableLabel(p, BuildLabelText(m));
            }
#endif
        }

#if UNITY_EDITOR
        private void DrawReadableLabel(Vector3 worldPos, string text)
        {
            EnsureLabelStyles();

            // Draw four-pass outline first, then foreground text for high contrast.
            float offset = 0.015f;
            Handles.Label(worldPos + new Vector3(offset, 0f, 0f), text, _labelOutlineStyle);
            Handles.Label(worldPos + new Vector3(-offset, 0f, 0f), text, _labelOutlineStyle);
            Handles.Label(worldPos + new Vector3(0f, 0f, offset), text, _labelOutlineStyle);
            Handles.Label(worldPos + new Vector3(0f, 0f, -offset), text, _labelOutlineStyle);
            Handles.Label(worldPos, text, _labelStyle);
        }

        private void EnsureLabelStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
            }

            if (_labelOutlineStyle == null)
            {
                _labelOutlineStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.black }
                };
            }
        }
#endif

        private static string BuildLabelText(UserRiskMetrics m)
        {
            return string.Format(
                "U{0} dom:{1}\ncell:{2:F2} phy:{3:F2}\nuser:{4:F2} smooth:{5:F2}\nvec:[{2:F2},{4:F2},{5:F2}] tot:{6:F2}",
                m.UserId,
                m.DominantRisk,
                m.CellBoundaryRisk,
                m.PhysicalBoundaryRisk,
                m.UserRisk,
                m.SmoothRisk,
                m.WeightedTotalRisk);
        }

    }
}
