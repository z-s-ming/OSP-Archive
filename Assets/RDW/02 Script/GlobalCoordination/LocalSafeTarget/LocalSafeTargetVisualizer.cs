using UnityEngine;
using System.Collections.Generic;
using _GCM;
using RDW.Coordination.LocalSafeTarget;

namespace RDW.Coordination.Visualization
{
    /// <summary>
    /// Visualizer for Phase 5: Local Safe Target Selection
    /// Renders selected targets and cell geometry
    /// </summary>
    public class LocalSafeTargetVisualizer : MonoBehaviour
    {
        [SerializeField] private bool enableVisualization = true;
        [SerializeField] private Color targetPointColor = new Color(1f, 0.84f, 0f, 1f); // Gold
        [SerializeField] private Color userPositionColor = Color.green;
        [SerializeField] private Color connectionColor = Color.cyan;
        [SerializeField] private Color cellOutlineColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        [SerializeField] private float gizmoHeight = 0.05f;
        [SerializeField] private float targetPointSize = 0.25f;
        [SerializeField] private float userPointSize = 0.15f;

        private Dictionary<int, LocalTargetResult> latestTargets = new Dictionary<int, LocalTargetResult>();
        private Dictionary<int, List<Vector2>> cachedCellVertices = new Dictionary<int, List<Vector2>>();
        private StateCollector stateCollector;

        public void Initialize(StateCollector collector)
        {
            stateCollector = collector;
        }

        public void UpdateTargets(Dictionary<int, LocalTargetResult> targets)
        {
            if (targets != null)
            {
                latestTargets = new Dictionary<int, LocalTargetResult>(targets);
            }
        }

        public void UpdateCellVertices(Dictionary<int, List<Vector2>> cellVertices)
        {
            if (cellVertices != null)
            {
                cachedCellVertices = cellVertices;
            }
        }

        public void Draw()
        {
            if (!enableVisualization || latestTargets == null || latestTargets.Count == 0)
                return;

            if (stateCollector?.PhysicalUsers == null)
                return;

            foreach (var kvp in latestTargets)
            {
                int userId = kvp.Key;
                LocalTargetResult targetResult = kvp.Value;

                if (userId >= stateCollector.PhysicalUsers.Count)
                    continue;

                if (!targetResult.HasValidTarget)
                    continue;

                var user = stateCollector.PhysicalUsers[userId];
                if (user == null)
                    continue;

                // Draw user position (green sphere)
                Vector3 userPos = user.transform.position + Vector3.up * gizmoHeight;
                Gizmos.color = userPositionColor;
                Gizmos.DrawWireSphere(userPos, userPointSize);

                // Draw target point (gold sphere)
                Vector3 targetPos = new Vector3(targetResult.TargetPosition.x, gizmoHeight, targetResult.TargetPosition.y);
                Gizmos.color = targetPointColor;
                Gizmos.DrawWireSphere(targetPos, targetPointSize);

                // Draw cross marker at target
                Gizmos.DrawLine(targetPos - Vector3.right * 0.2f, targetPos + Vector3.right * 0.2f);
                Gizmos.DrawLine(targetPos - Vector3.forward * 0.2f, targetPos + Vector3.forward * 0.2f);

                // Draw connection line and arrow
                Gizmos.color = connectionColor;
                Vector3 direction = (targetPos - userPos).normalized;
                float distance = Vector3.Distance(userPos, targetPos);
                if (distance > 0.1f)
                {
                    // Draw line
                    Gizmos.DrawLine(userPos, targetPos);

                    // Draw arrowhead
                    Vector3 arrowEnd = targetPos - direction * 0.3f;
                    Vector3 perpendicular = new Vector3(-direction.z, 0, direction.x) * 0.15f;
                    Gizmos.DrawLine(targetPos, arrowEnd + perpendicular);
                    Gizmos.DrawLine(targetPos, arrowEnd - perpendicular);
                }

                // Draw cell outline
                if (cachedCellVertices.TryGetValue(userId, out List<Vector2> cellVertices))
                {
                    DrawCellOutline(cellVertices);
                }

                // Display score info in editor
                #if UNITY_EDITOR
                string scoreText = $"Score: {targetResult.BestScore:F2}";
                UnityEditor.Handles.Label(targetPos + Vector3.up * 0.5f, scoreText);
                #endif
            }
        }

        private void DrawCellOutline(List<Vector2> vertices)
        {
            if (vertices == null || vertices.Count < 2)
                return;

            Gizmos.color = cellOutlineColor;

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 p1 = new Vector3(vertices[i].x, gizmoHeight, vertices[i].y);
                Vector3 p2 = new Vector3(vertices[(i + 1) % vertices.Count].x, gizmoHeight, vertices[(i + 1) % vertices.Count].y);
                Gizmos.DrawLine(p1, p2);
            }
        }

        private void OnDrawGizmos()
        {
            Draw();
        }
    }
}
