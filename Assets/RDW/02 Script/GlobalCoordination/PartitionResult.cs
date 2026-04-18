using System.Collections.Generic;
using UnityEngine;
using csDelaunay;

namespace _GCM
{
    public class PartitionResult
    {
        public List<Vector2f> SeedPoints { get; } = new List<Vector2f>();
        public List<Vector2> Centroids { get; } = new List<Vector2>();
        public List<float> Areas { get; } = new List<float>();
        public Dictionary<int, List<Vector2>> RegionVertices { get; } = new Dictionary<int, List<Vector2>>();
        public Dictionary<int, HashSet<int>> CellAdjacency { get; } = new Dictionary<int, HashSet<int>>();
        public List<Vector3> EdgeVertices { get; } = new List<Vector3>();
    }
}
