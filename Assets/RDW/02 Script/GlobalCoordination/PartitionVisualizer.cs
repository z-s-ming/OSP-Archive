using System.Collections.Generic;
using UnityEngine;
using csDelaunay;

namespace _GCM
{
    public class PartitionVisualizer
    {
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
                    _centerPointers[i].transform.position = new Vector3(centroid.x, 0.0f, centroid.y);
                    _centerPointers[i].name = "centroid for user" + i;
                }
            }
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
