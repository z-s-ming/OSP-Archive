using System.Collections.Generic;
using UnityEngine;
using csDelaunay;
using _GCM;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace _GCM.PartitionUpdate
{
    public class PartitionUpdateVisualizer
    {
        public void Draw(IReadOnlyList<PartitionUpdateAttempt> attempts, PartitionResult partition, float gizmoHeight = 0.06f)
        {
            DrawCurrentSeeds(partition, gizmoHeight);
            DrawAttempts(attempts, gizmoHeight);
        }

        private void DrawCurrentSeeds(PartitionResult partition, float gizmoHeight)
        {
            if (partition == null || partition.SeedPoints == null)
                return;

            Gizmos.color = new Color(1f, 1f, 1f, 0.8f);
            for (int i = 0; i < partition.SeedPoints.Count; i++)
            {
                Vector2f seed = partition.SeedPoints[i];
                Gizmos.DrawSphere(new Vector3(seed.x, gizmoHeight, seed.y), 0.03f);
            }
        }

        private void DrawAttempts(IReadOnlyList<PartitionUpdateAttempt> attempts, float gizmoHeight)
        {
            if (attempts == null)
                return;

            for (int i = 0; i < attempts.Count; i++)
            {
                PartitionUpdateAttempt attempt = attempts[i];
                if (attempt == null)
                    continue;

                Vector3 oldPos = new Vector3(attempt.OldSeed.x, gizmoHeight, attempt.OldSeed.y);
                Vector3 newPos = new Vector3(attempt.NewSeed.x, gizmoHeight, attempt.NewSeed.y);

                string marker = string.Empty;
                if (attempt.Accepted)
                {
                    Gizmos.color = new Color(0.2f, 0.9f, 0.35f, 0.95f);
                    Gizmos.DrawLine(oldPos, newPos);
                    Gizmos.DrawSphere(newPos, 0.04f);
                }
                else if (attempt.RejectReason == PartitionUpdateRejectReason.CooldownBlocked)
                {
                    Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                    Gizmos.DrawSphere(oldPos, 0.035f);
                    marker = "CD";
                }
                else if (attempt.RejectReason == PartitionUpdateRejectReason.ProposalInvalid)
                {
                    Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.9f);
                    Gizmos.DrawSphere(oldPos, 0.035f);
                    float r = 0.05f;
                    Gizmos.DrawLine(oldPos + new Vector3(-r, 0f, -r), oldPos + new Vector3(r, 0f, r));
                    Gizmos.DrawLine(oldPos + new Vector3(-r, 0f, r), oldPos + new Vector3(r, 0f, -r));
                    marker = "X";
                }
                else
                {
                    Gizmos.color = new Color(0.95f, 0.35f, 0.2f, 0.95f);
                    Vector3 shortEnd = oldPos;
                    Vector3 delta = newPos - oldPos;
                    if (delta.sqrMagnitude > 0.000001f)
                        shortEnd = oldPos + delta.normalized * 0.06f;
                    Gizmos.DrawLine(oldPos, shortEnd);
                    Gizmos.DrawSphere(shortEnd, 0.03f);
                    marker = "R";
                }

#if UNITY_EDITOR
                Vector3 labelAnchor = attempt.Accepted ? newPos : oldPos;
                Vector3 labelPos = labelAnchor + Vector3.up * 0.08f;
                string status = attempt.Accepted ? "accepted" : $"rejected/{attempt.RejectReason}";
                if (!string.IsNullOrEmpty(marker))
                    status = $"{status} {marker}";
                Handles.Label(labelPos, $"U{attempt.UserId} {attempt.TriggerType} {status}");
#endif
            }
        }
    }
}