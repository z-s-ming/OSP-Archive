using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;
using csDelaunay;

namespace _GCM
{
    public class VoronoiPartitioner
    {
        private readonly int _totalUserCount;
        private readonly float _userRadius;
        private readonly float _shutterWidth;
        private readonly float _eps;

        private Voronoi _voronoi;
        private readonly List<Vector2f> _seedPoints = new List<Vector2f>();
        private List<float> _voronoiArea = new List<float>();

        public VoronoiPartitioner(int totalUserCount, float userRadius, float shutterWidth, float eps)
        {
            _totalUserCount = totalUserCount;
            _userRadius = userRadius;
            _shutterWidth = shutterWidth;
            _eps = eps;

            for (int i = 0; i < _totalUserCount; i++)
            {
                _seedPoints.Add(new Vector2f(0f, 0f));
            }
        }

        public void ResetSeedsFromUsers(IReadOnlyList<GameObject> physicalUsers)
        {
            for (int i = 0; i < _totalUserCount; i++)
            {
                if (physicalUsers != null && i < physicalUsers.Count && physicalUsers[i] != null)
                {
                    Vector3 p = physicalUsers[i].transform.position;
                    _seedPoints[i] = new Vector2f(p.x, p.z);
                }
                else
                {
                    _seedPoints[i] = new Vector2f(0f, 0f);
                }
            }
        }

        public List<Vector2> GetSeedPointsCopy()
        {
            List<Vector2> seedPoints = new List<Vector2>(_seedPoints.Count);
            for (int i = 0; i < _seedPoints.Count; i++)
            {
                seedPoints.Add(new Vector2(_seedPoints[i].x, _seedPoints[i].y));
            }

            return seedPoints;
        }

        public Vector2 GetSeedPoint(int userId)
        {
            if (userId < 0 || userId >= _seedPoints.Count)
                return Vector2.zero;

            return new Vector2(_seedPoints[userId].x, _seedPoints[userId].y);
        }

        public void SetSeedPoint(int userId, Vector2 seedPoint)
        {
            if (userId < 0 || userId >= _seedPoints.Count)
                return;

            _seedPoints[userId] = new Vector2f(seedPoint.x, seedPoint.y);
        }

        public void SetSeedPoints(IReadOnlyList<Vector2> seedPoints)
        {
            if (seedPoints == null)
                return;

            int count = Mathf.Min(_totalUserCount, seedPoints.Count);
            for (int i = 0; i < count; i++)
            {
                _seedPoints[i] = new Vector2f(seedPoints[i].x, seedPoints[i].y);
            }
        }

        public List<Vector2> BuildInitialUniformSeeds(float roomHalfWidth, float roomHalfHeight)
        {
            List<Vector2f> seedPointsFixed = new List<Vector2f>();
            for (int i = 0; i < _totalUserCount; i++)
            {
                seedPointsFixed.Add(new Vector2f(Random.Range(-roomHalfWidth, roomHalfWidth), Random.Range(-roomHalfHeight, roomHalfHeight)));
            }

            Rectf bounds = new Rectf(-roomHalfWidth, -roomHalfHeight, roomHalfWidth * 2, roomHalfHeight * 2);
            Voronoi tempVoronoi = null;

            try
            {
                tempVoronoi = new Voronoi(seedPointsFixed, bounds, 5000);
                Dictionary<Vector2f, Site> sites = tempVoronoi.SitesIndexedByLocation;

                int count = 0;
                foreach (KeyValuePair<Vector2f, Site> kv in sites)
                {
                    seedPointsFixed[count++] = new Vector2f(kv.Key.x, kv.Key.y);
                }

                List<float> tempAreas = new List<float>();
                SiteList sitesForCentroid = new SiteList();
                tempVoronoi.GetCentroid_site(_totalUserCount, ref tempAreas, ref sitesForCentroid);

                List<Vector2f> orderedSeeds = new List<Vector2f>();
                for (int i = 0; i < _totalUserCount; i++)
                {
                    int targetIndex = i;
                    for (int j = 0; j < _totalUserCount; j++)
                    {
                        if (Mathf.Abs(seedPointsFixed[i].x - sitesForCentroid.GetSite_byIndex(j).x) < _eps
                            && Mathf.Abs(seedPointsFixed[i].y - sitesForCentroid.GetSite_byIndex(j).y) < _eps)
                        {
                            targetIndex = j;
                            break;
                        }
                    }

                    orderedSeeds.Add(seedPointsFixed[targetIndex]);
                }

                return orderedSeeds.Select(p => new Vector2(p.x, p.y)).ToList();
            }
            finally
            {
                if (tempVoronoi != null)
                {
                    tempVoronoi.Dispose();
                }
            }
        }

        public PartitionResult Build(
            FrameState frameState,
            IReadOnlyList<Vector3> offsets,
            float dt,
            bool useVelocityOffset,
            float roomHalfWidth,
            float roomHalfHeight)
        {
            PartitionResult result = new PartitionResult();
            EnsureRegionKeys(result.RegionVertices);

            if (frameState == null || frameState.PhysicalUsers == null || frameState.PhysicalUsers.Count < _totalUserCount)
                return result;

            float maxSafeRadius = 0f;
            if (useVelocityOffset)
            {
                maxSafeRadius = Mathf.Max(0f, CalcStableAreaRadius(frameState.PhysicalUsers));
            }

            for (int i = 0; i < _totalUserCount; i++)
            {
                Vector3 userPos = frameState.PhysicalUsers[i].transform.position;

                Vector3 offsetVector = Vector3.zero;
                if (useVelocityOffset && offsets != null && i < offsets.Count)
                {
                    offsetVector = offsets[i];
                    if (offsetVector.magnitude > maxSafeRadius)
                    {
                        offsetVector = offsetVector.normalized * maxSafeRadius;
                    }
                }

                Vector3 virtualPos = userPos + offsetVector;

                float safeHalfW = roomHalfWidth - 0.1f;
                float safeHalfH = roomHalfHeight - 0.1f;
                float clampedX = Mathf.Clamp(virtualPos.x, -safeHalfW, safeHalfW);
                float clampedZ = Mathf.Clamp(virtualPos.z, -safeHalfH, safeHalfH);

                Vector2 targetSeed = new Vector2(clampedX, clampedZ);
                Vector2 currentSeed = new Vector2(_seedPoints[i].x, _seedPoints[i].y);
                Vector2 smoothedSeed = Vector2.Lerp(currentSeed, targetSeed, dt * 10f);

                _seedPoints[i] = new Vector2f(smoothedSeed.x, smoothedSeed.y);
            }

            Rectf bounds = new Rectf(-roomHalfWidth, -roomHalfHeight, roomHalfWidth * 2, roomHalfHeight * 2);

            if (_voronoi != null)
            {
                _voronoi.Dispose();
            }

            _voronoi = new Voronoi(_seedPoints, bounds);
            List<Edge> edges = _voronoi.Edges;

            for (int i = 0; i < _seedPoints.Count; i++)
            {
                result.SeedPoints.Add(_seedPoints[i]);
            }

            foreach (Edge edge in edges)
            {
                if (edge.ClippedEnds == null)
                    continue;

                result.EdgeVertices.Add(new Vector3(edge.ClippedEnds[LR.LEFT].x, 0f, edge.ClippedEnds[LR.LEFT].y));
                result.EdgeVertices.Add(new Vector3(edge.ClippedEnds[LR.RIGHT].x, 0f, edge.ClippedEnds[LR.RIGHT].y));
            }

            if (_totalUserCount > 2)
            {
                BuildMultiUserResult(bounds, result);
            }
            else if (_totalUserCount == 2)
            {
                BuildTwinUserResult(bounds, result, frameState.PhysicalUsers);
            }

            return result;
        }

        public void Dispose()
        {
            if (_voronoi != null)
            {
                _voronoi.Dispose();
                _voronoi = null;
            }
        }

        private void BuildMultiUserResult(Rectf bounds, PartitionResult result)
        {
            _voronoiArea.Clear();
            SiteList sitesForCentroid = new SiteList();
            List<Vector2f> centroids = _voronoi.GetCentroid_site(_totalUserCount, ref _voronoiArea, ref sitesForCentroid);

            for (int i = 0; i < _totalUserCount; i++)
            {
                int targetIndex = 0;
                for (int j = 0; j < _totalUserCount; j++)
                {
                    if (Mathf.Abs(_seedPoints[i].x - sitesForCentroid.GetSite_byIndex(j).x) < _eps
                        && Mathf.Abs(_seedPoints[i].y - sitesForCentroid.GetSite_byIndex(j).y) < _eps)
                    {
                        targetIndex = j;
                        break;
                    }
                }

                result.Centroids.Add(new Vector2(centroids[targetIndex].x, centroids[targetIndex].y));
                result.Areas.Add(targetIndex < _voronoiArea.Count ? _voronoiArea[targetIndex] : 0f);

                List<Vector2f> region = sitesForCentroid.GetSite_byIndex(targetIndex).Region(bounds);
                List<Vector2> regionVertices = result.RegionVertices[i];
                regionVertices.Clear();
                for (int k = region.Count - 1; k >= 0; k--)
                {
                    regionVertices.Add(new Vector2(region[k].x, region[k].y));
                }
            }
        }

        private void BuildTwinUserResult(Rectf bounds, PartitionResult result, IReadOnlyList<GameObject> physicalUsers)
        {
            _voronoiArea.Clear();
            List<Vector2f> centroids = new List<Vector2f>();
            List<float> areas = new List<float>();

            float areaValue = 0.0f;
            (Vector2f, List<Vector2f>) item = _voronoi.GetCentroidTwin_region(0, true, ref areaValue);
            (Vector2f, List<Vector2f>) item2 = _voronoi.GetCentroidTwin_region(1, false, ref areaValue);
            centroids.Add(item.Item1);
            areas.Add(areaValue);
            centroids.Add(item2.Item1);
            areas.Add(areaValue);

            if (result.EdgeVertices.Count < 2 || physicalUsers == null || physicalUsers.Count == 0 || physicalUsers[0] == null)
                return;

            Vector3 vec1 = result.EdgeVertices[1] - result.EdgeVertices[0];
            Vector3 cntr1 = new Vector3(centroids[0].x, 0.0f, centroids[0].y) - result.EdgeVertices[0];
            Vector3 vec2 = physicalUsers[0].transform.position - result.EdgeVertices[0];

            List<Vector2> region0 = result.RegionVertices[0];
            List<Vector2> region1 = result.RegionVertices[1];
            region0.Clear();
            region1.Clear();

            if (Mathf.Sign(Vector3.Cross(vec1, cntr1).y) == Mathf.Sign(Vector3.Cross(vec1, vec2).y))
            {
                result.Centroids.Add(new Vector2(centroids[0].x, centroids[0].y));
                result.Areas.Add(areas[0]);
                for (int j = item.Item2.Count - 1; j >= 0; j--)
                {
                    region0.Add(new Vector2(item.Item2[j].x, item.Item2[j].y));
                }

                result.Centroids.Add(new Vector2(centroids[1].x, centroids[1].y));
                result.Areas.Add(areas[1]);
                for (int j = item2.Item2.Count - 1; j >= 0; j--)
                {
                    region1.Add(new Vector2(item2.Item2[j].x, item2.Item2[j].y));
                }
            }
            else
            {
                result.Centroids.Add(new Vector2(centroids[1].x, centroids[1].y));
                result.Areas.Add(areas[1]);
                for (int j = item2.Item2.Count - 1; j >= 0; j--)
                {
                    region0.Add(new Vector2(item2.Item2[j].x, item2.Item2[j].y));
                }

                result.Centroids.Add(new Vector2(centroids[0].x, centroids[0].y));
                result.Areas.Add(areas[0]);
                for (int j = item.Item2.Count - 1; j >= 0; j--)
                {
                    region1.Add(new Vector2(item.Item2[j].x, item.Item2[j].y));
                }
            }
        }

        private float CalcStableAreaRadius(IReadOnlyList<GameObject> physicalUsers)
        {
            Dictionary<string, float> distDic = new Dictionary<string, float>();

            for (int i = 0; i < _totalUserCount; i++)
            {
                if (i >= physicalUsers.Count || physicalUsers[i] == null)
                    continue;

                Vector3 me = physicalUsers[i].transform.position;

                for (int j = i; j < _totalUserCount; j++)
                {
                    if (i == j)
                        continue;

                    if (j >= physicalUsers.Count || physicalUsers[j] == null)
                        continue;

                    Vector3 target = physicalUsers[j].transform.position;
                    Vector3 distVec = new Vector3(me.x, 0f, me.z) - new Vector3(target.x, 0f, target.z);
                    distDic.Add(i.ToString() + j.ToString(), distVec.magnitude);
                }
            }

            if (distDic.Count == 0)
                return 0f;

            float minDistValue = distDic.OrderBy(kvp => kvp.Value).First().Value;
            return ((minDistValue - _shutterWidth) / 2f) - _userRadius;
        }

        private void EnsureRegionKeys(Dictionary<int, List<Vector2>> regionVertices)
        {
            for (int i = 0; i < _totalUserCount; i++)
            {
                if (!regionVertices.ContainsKey(i))
                {
                    regionVertices.Add(i, new List<Vector2>());
                }
            }
        }
    }
}
