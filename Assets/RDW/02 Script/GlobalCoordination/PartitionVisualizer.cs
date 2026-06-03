using System.Collections.Generic;
using UnityEngine;
using csDelaunay;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace _GCM
{
    public class PartitionVisualizer
    {
        private const float GizmoHeight = 0.02f;
        private const float OutlineHeightOffset = 0.005f;
        private const float FillAlpha = 0.24f;

        private readonly int _totalUserCount;
        private readonly GameObject _voronoiVertexPrefab;
        private readonly GameObject _seedPointPrefab;
        private readonly GameObject _centerPointerPrefab;

        private readonly List<GameObject> _voronoiVertexMarkers = new List<GameObject>();
        private readonly List<GameObject> _seedPointVisuals = new List<GameObject>();
        private readonly List<GameObject> _centerPointers = new List<GameObject>();

        public PartitionVisualizer(int totalUserCount, GameObject voronoiVertexPrefab, GameObject seedPointPrefab, GameObject centerPointerPrefab)
        {
            _totalUserCount = totalUserCount;
            _voronoiVertexPrefab = voronoiVertexPrefab;
            _seedPointPrefab = seedPointPrefab;
            _centerPointerPrefab = centerPointerPrefab;
        }

        public void InitializePools()
        {
            for (int i = 0; i < 100; i++)
            {
                GameObject marker = Object.Instantiate(_voronoiVertexPrefab, new Vector3(-50.0f, 0.0f, 0.0f), Quaternion.identity);
                marker.SetActive(false);
                marker.name = "VoronoiVertexMarker " + i;
                marker.hideFlags = HideFlags.HideInHierarchy;
                _voronoiVertexMarkers.Add(marker);
            }

            for (int i = 0; i < _totalUserCount; i++)
            {
                GameObject seedPoint = Object.Instantiate(_seedPointPrefab, new Vector3(-50.0f, 0.0f, 0.0f), Quaternion.identity);
                seedPoint.name = "seedpointView " + i;
                _seedPointVisuals.Add(seedPoint);

                GameObject centerPointer = Object.Instantiate(_centerPointerPrefab);
                centerPointer.name = "centroid for user" + i;
                centerPointer.SetActive(false);
                _centerPointers.Add(centerPointer);
            }
        }

        public void UpdatePartitionVisuals(PartitionResult partitionResult)
        {
            if (partitionResult == null)
                return;

            UpdateSeedPointVisuals(partitionResult.SeedPoints);
            UpdateVoronoiVertexVisuals(partitionResult.EdgeVertices);
            UpdateCenterPointerVisuals(partitionResult.Centroids);
        }

        public Vector3 GetCenterPointerPosition(int index)
        {
            if (index < 0 || index >= _centerPointers.Count || _centerPointers[index] == null)
                return Vector3.zero;

            return _centerPointers[index].transform.position;
        }

        public void SetCenterPointerActive(int index, bool active)
        {
            if (index < 0 || index >= _centerPointers.Count || _centerPointers[index] == null)
                return;

            _centerPointers[index].SetActive(active);
        }

        public void Dispose()
        {
            DestroyAll(_centerPointers);
            DestroyAll(_seedPointVisuals);
            DestroyAll(_voronoiVertexMarkers);
        }

        public static void DrawPartitionAreaGizmos(Dictionary<int, List<Vector2>> areaSegmentsVertex, List<Material> partitionedSpaceMaterials)
        {
            if (areaSegmentsVertex == null || areaSegmentsVertex.Count == 0)
                return;

            foreach (var kv in areaSegmentsVertex)
            {
                int userIndex = kv.Key;
                List<Vector2> vertices2D = kv.Value;
                if (vertices2D == null || vertices2D.Count < 3)
                    continue;

                Color baseColor = ResolvePartitionColor(userIndex, partitionedSpaceMaterials);
                Vector3[] points = new Vector3[vertices2D.Count];

                for (int i = 0; i < vertices2D.Count; i++)
                {
                    points[i] = new Vector3(vertices2D[i].x, GizmoHeight, vertices2D[i].y);
                }

#if UNITY_EDITOR
                Color fillColor = baseColor;
                fillColor.a = FillAlpha;
                Handles.color = fillColor;
                Handles.DrawAAConvexPolygon(points);
#endif

                Color outlineColor = baseColor;
                outlineColor.a = 0.95f;
                Gizmos.color = outlineColor;

                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 a = points[i] + Vector3.up * OutlineHeightOffset;
                    Vector3 b = points[(i + 1) % points.Length] + Vector3.up * OutlineHeightOffset;
                    Gizmos.DrawLine(a, b);
                }
            }
        }

        public static Color ResolvePartitionColor(int userIndex, List<Material> partitionedSpaceMaterials)
        {
            if (partitionedSpaceMaterials != null && userIndex >= 0 && userIndex < partitionedSpaceMaterials.Count)
            {
                Material mat = partitionedSpaceMaterials[userIndex];
                if (mat != null)
                    return mat.color;
            }

            return Color.HSVToRGB(Mathf.Repeat(userIndex * 0.173f, 1f), 0.75f, 1f);
        }

        private void UpdateSeedPointVisuals(IReadOnlyList<Vector2f> seedPoints)
        {
            if (seedPoints == null)
                return;

            int count = Mathf.Min(_totalUserCount, seedPoints.Count, _seedPointVisuals.Count);
            for (int i = 0; i < count; i++)
            {
                _seedPointVisuals[i].transform.position = new Vector3(seedPoints[i].x, 0.0f, seedPoints[i].y);
            }
        }

        private void UpdateVoronoiVertexVisuals(IReadOnlyList<Vector3> edgeVertices)
        {
            for (int i = 0; i < _voronoiVertexMarkers.Count; i++)
            {
                _voronoiVertexMarkers[i].GetComponent<MeshRenderer>().enabled = false;
            }

            if (edgeVertices == null)
                return;

            int count = Mathf.Min(edgeVertices.Count, _voronoiVertexMarkers.Count);
            for (int i = 0; i < count; i++)
            {
                _voronoiVertexMarkers[i].transform.position = edgeVertices[i];
                _voronoiVertexMarkers[i].GetComponent<MeshRenderer>().enabled = true;
            }
        }

        private void UpdateCenterPointerVisuals(IReadOnlyList<Vector2> centroids)
        {
            for (int i = 0; i < _centerPointers.Count; i++)
            {
                if (_centerPointers[i] == null)
                    continue;

                if (centroids != null && i < centroids.Count)
                {
                    Vector2 centroid = centroids[i];
                    if (!IsFinite(centroid))
                    {
                        _centerPointers[i].SetActive(false);
                        continue;
                    }

                    _centerPointers[i].SetActive(true);
                    _centerPointers[i].transform.position = new Vector3(centroid.x, 0.0f, centroid.y);
                    _centerPointers[i].name = "centroid for user" + i;
                }
            }
        }

        private static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsInfinity(value.y);
        }

        private static void DestroyAll(List<GameObject> objects)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null)
                {
                    Object.Destroy(objects[i]);
                }
            }
            objects.Clear();
        }
    }
}
