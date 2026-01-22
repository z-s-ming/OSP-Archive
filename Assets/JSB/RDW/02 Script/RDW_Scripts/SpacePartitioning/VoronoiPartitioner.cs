using System.Collections.Generic;
using UnityEngine;
using csDelaunay;

namespace JSB.RDW.SpacePartitioning
{
    /// <summary>
    /// Voronoi 空间分区算法实现
    /// 封装原有的 Voronoi 图生成逻辑，符合新的空间分区框架
    /// </summary>
    public class VoronoiPartitioner : SpacePartitioner
    {
        #region Voronoi 特有参数

        [Header("Voronoi 算法参数")]
        [Tooltip("Lloyd 松弛迭代次数（用于优化 Voronoi 图的均匀性）")]
        [Range(0, 10)]
        public int lloydRelaxationIterations = 0;

        [Tooltip("是否在计算时考虑用户权重（加权 Voronoi）")]
        public bool useWeightedVoronoi = false;

        [Tooltip("边界缓冲距离（避免多边形过于贴近边界）")]
        [Range(0f, 1f)]
        public float boundaryBuffer = 0.1f;

        [Header("Voronoi 可视化/障碍 Prefab")]
        [SerializeField]
        private GameObject prefabVoronoiVertex;

        [SerializeField]
        private GameObject prefabVoronoiSeedPoint;

        #endregion

        #region 内部变量

        private csDelaunay.Voronoi voronoi;
        private Dictionary<Vector2f, Site> sitesDict;
        private List<Edge> voronoiEdges;

        private readonly List<GameObject> voronoiVertexMarkers = new List<GameObject>();
        private readonly List<GameObject> seedPointVisuals = new List<GameObject>();
        private float cachedShutterWidth = 0.5f;
        private const float Eps = 0.001f;

        #endregion

        #region 核心方法实现

        /// <summary>
        /// 实现 Voronoi 空间分区计算
        /// </summary>
        public override List<PartitionResult> CalculatePartition(List<UserInfo> users, SpaceBounds bounds)
        {
            List<PartitionResult> results = new List<PartitionResult>();

            if (users == null || users.Count == 0)
            {
                Debug.LogWarning("[VoronoiPartitioner] 用户列表为空，无法计算分区");
                return results;
            }

            // 1. 准备种子点数据
            List<Vector2f> seedPoints = new List<Vector2f>();
            foreach (var user in users)
            {
                seedPoints.Add(new Vector2f(user.position.x, user.position.y));
            }

            // 2. 定义 Voronoi 图的边界
            Rectf plotBounds = new Rectf(
                bounds.min.x - boundaryBuffer,
                bounds.min.y - boundaryBuffer,
                bounds.size.x + boundaryBuffer * 2,
                bounds.size.y + boundaryBuffer * 2
            );

            // 3. 生成 Voronoi 图
            try
            {
                if (lloydRelaxationIterations > 0)
                {
                    voronoi = new csDelaunay.Voronoi(seedPoints, plotBounds, lloydRelaxationIterations);
                }
                else
                {
                    voronoi = new csDelaunay.Voronoi(seedPoints, plotBounds);
                }

                sitesDict = voronoi.SitesIndexedByLocation;
                voronoiEdges = voronoi.Edges;

                // 4. 为每个用户提取其 Voronoi 区域
                for (int i = 0; i < users.Count; i++)
                {
                    UserInfo user = users[i];
                    PartitionResult result = new PartitionResult(user.userId);

                    // 获取该用户的 Voronoi 区域顶点
                    Vector2f seedPoint = new Vector2f(user.position.x, user.position.y);
                    List<Vector2f> regionVertices = voronoi.Region(seedPoint);

                    if (regionVertices != null && regionVertices.Count > 0)
                    {
                        // 转换为 Vector2 并裁剪到实际边界内
                        List<Vector2> polygon = new List<Vector2>();
                        foreach (var vertex in regionVertices)
                        {
                            polygon.Add(new Vector2(vertex.x, vertex.y));
                        }

                        // 裁剪到实际边界内（去除缓冲区）
                        polygon = ClipPolygonToBounds(polygon, bounds);

                        // 确保多边形有效
                        if (polygon.Count >= 3)
                        {
                            result.safeSpacePolygon = polygon;
                            result.centroid = CalculateCentroid(polygon);
                            result.area = CalculateArea(polygon);

                            // 添加调试信息
                            result.debugInfo["vertexCount"] = polygon.Count;
                            result.debugInfo["originalPosition"] = user.position;
                        }
                        else
                        {
                            Debug.LogWarning($"[VoronoiPartitioner] 用户 {user.userId} 的分区多边形无效（顶点数 < 3）");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[VoronoiPartitioner] 无法为用户 {user.userId} 生成 Voronoi 区域");
                    }

                    results.Add(result);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VoronoiPartitioner] Voronoi 图生成失败: {e.Message}");
            }

            return results;
        }

        /// <summary>
        /// 初始化可视化与障碍对象池
        /// </summary>
        public override void InitializeSpatialObjects(int totalUserCount, int edgeMaxCount, float shutterWidth)
        {
            cachedShutterWidth = shutterWidth;

            int vertexMarkerCount = Mathf.Max(100, edgeMaxCount * 2);
            if (prefabVoronoiVertex != null && voronoiVertexMarkers.Count < vertexMarkerCount)
            {
                int addCount = vertexMarkerCount - voronoiVertexMarkers.Count;
                for (int i = 0; i < addCount; i++)
                {
                    GameObject marker = Instantiate(prefabVoronoiVertex, new Vector3(-50.0f, 0.0f, 0.0f), Quaternion.identity);
                    marker.SetActive(false);
                    marker.name = "VoronoiVertexMarker " + (voronoiVertexMarkers.Count + 1);
                    voronoiVertexMarkers.Add(marker);
                }
            }

            int seedVisualCount = Mathf.Max(10, totalUserCount);
            if (prefabVoronoiSeedPoint != null && seedPointVisuals.Count < seedVisualCount)
            {
                int addCount = seedVisualCount - seedPointVisuals.Count;
                for (int i = 0; i < addCount; i++)
                {
                    GameObject seedView = Instantiate(prefabVoronoiSeedPoint, new Vector3(-50.0f, 0.0f, 0.0f), Quaternion.identity);
                    seedView.name = "seedpointView " + (seedPointVisuals.Count + 1);
                    seedPointVisuals.Add(seedView);
                }
            }

        }

        public override void UpdateSeedVisuals(IReadOnlyList<Vector2> seedPoints)
        {
            if (seedPoints == null)
                return;

            int count = Mathf.Min(seedPoints.Count, seedPointVisuals.Count);
            for (int i = 0; i < count; i++)
            {
                Vector2 p = seedPoints[i];
                seedPointVisuals[i].transform.position = new Vector3(p.x, 0.0f, p.y);
            }
        }

        public override PartitionFrameResult UpdatePartitioning(
            IReadOnlyList<Vector2> seedPoints,
            IReadOnlyList<GameObject> physicalUsers,
            SpaceBounds bounds,
            int totalUserCount,
            bool useVecObservation,
            IReadOnlyList<GameObject> s2cCenterPointers,
            Dictionary<int, List<Vector2>> areaSegments,
            float shutterWidth)
        {
            PartitionFrameResult frameResult = new PartitionFrameResult();

            if (seedPoints == null || seedPoints.Count == 0)
                return frameResult;

            cachedShutterWidth = shutterWidth;

            Rectf plotBounds = new Rectf(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
            List<Vector2f> seeds = new List<Vector2f>(seedPoints.Count);
            for (int i = 0; i < seedPoints.Count; i++)
            {
                seeds.Add(new Vector2f(seedPoints[i].x, seedPoints[i].y));
            }

            voronoi = new csDelaunay.Voronoi(seeds, plotBounds);
            sitesDict = voronoi.SitesIndexedByLocation;
            voronoiEdges = voronoi.Edges;

            foreach (var item in voronoiVertexMarkers)
            {
                MeshRenderer renderer = item.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            int tempCount = 0;
            int voronoiEdgeCount = 0;

            foreach (Edge edge in voronoiEdges)
            {
                if (edge.ClippedEnds == null)
                    continue;

                if (tempCount + 1 >= voronoiVertexMarkers.Count)
                    break;

                voronoiVertexMarkers[tempCount].transform.position = new Vector3(edge.ClippedEnds[LR.LEFT].x, 0.0f, edge.ClippedEnds[LR.LEFT].y);
                voronoiVertexMarkers[tempCount].GetComponent<MeshRenderer>().enabled = true;
                tempCount++;

                voronoiVertexMarkers[tempCount].transform.position = new Vector3(edge.ClippedEnds[LR.RIGHT].x, 0.0f, edge.ClippedEnds[LR.RIGHT].y);
                voronoiVertexMarkers[tempCount].GetComponent<MeshRenderer>().enabled = true;
                tempCount++;

                voronoiEdgeCount++;
            }

            if (totalUserCount > 2)
            {
                List<Vector2f> centroids = new List<Vector2f>();
                List<float> areas = new List<float>();
                SiteList sitesForCentroid = new SiteList();
                centroids = voronoi.GetCentroid_site(totalUserCount, ref areas, ref sitesForCentroid);

                for (int i = 0; i < totalUserCount; i++)
                {
                    frameResult.centroids.Add(new Vector2(centroids[i].x, centroids[i].y));
                }
                frameResult.areas.AddRange(areas);

                if (frameResult.centroids.Count == totalUserCount && !float.IsNaN(frameResult.centroids[0].x) && !float.IsNaN(frameResult.centroids[0].y))
                {
                    for (int i = 0; i < totalUserCount; i++)
                    {
                        int targetIndex = 0;
                        for (int j = 0; j < totalUserCount; j++)
                        {
                            if (Mathf.Abs(seedPoints[i].x - sitesForCentroid.GetSite_byIndex(j).x) < Eps
                                && Mathf.Abs(seedPoints[i].y - sitesForCentroid.GetSite_byIndex(j).y) < Eps)
                            {
                                targetIndex = j;
                            }
                        }

                        if (useVecObservation && i < s2cCenterPointers.Count)
                        {
                            s2cCenterPointers[i].transform.position = new Vector3(frameResult.centroids[targetIndex].x, 0.0f, frameResult.centroids[targetIndex].y);
                        }

                        if (i < s2cCenterPointers.Count)
                        {
                            s2cCenterPointers[i].name = "centroid for user" + targetIndex;
                        }

                        if (!areaSegments.TryGetValue(i, out List<Vector2> listAreaSegments))
                        {
                            listAreaSegments = new List<Vector2>();
                            areaSegments[i] = listAreaSegments;
                        }

                        listAreaSegments.Clear();
                        List<Vector2f> region = sitesForCentroid.GetSite_byIndex(targetIndex).Region(plotBounds);
                        for (int k = region.Count - 1; k >= 0; k--)
                        {
                            listAreaSegments.Add(new Vector2(region[k].x, region[k].y));
                        }

                        if (i < s2cCenterPointers.Count && RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector() is S2CRedirector)
                        {
                            ((S2CRedirector)RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector()).SetCenterPoint(s2cCenterPointers[i].transform.position);
                            s2cCenterPointers[i].gameObject.SetActive(true);
                        }
                    }
                }
            }
            else if (totalUserCount == 2)
            {
                if (voronoiVertexMarkers.Count < 2 || physicalUsers == null || physicalUsers.Count == 0)
                {
                    return frameResult;
                }

                List<Vector2f> centroids = new List<Vector2f>();
                List<float> areas = new List<float>();
                float areaValue = 0.0f;
                (Vector2f, List<Vector2f>) item = voronoi.GetCentroidTwin_region(0, true, ref areaValue);
                (Vector2f, List<Vector2f>) item2 = voronoi.GetCentroidTwin_region(1, false, ref areaValue);
                centroids.Add(item.Item1);
                areas.Add(areaValue);
                centroids.Add(item2.Item1);
                areas.Add(areaValue);

                Vector3 vec1 = voronoiVertexMarkers[1].transform.position - voronoiVertexMarkers[0].transform.position;
                Vector3 cntr1 = new Vector3(centroids[0].x, 0.0f, centroids[0].y) - voronoiVertexMarkers[0].transform.position;
                Vector3 vec2 = physicalUsers[0].transform.position - voronoiVertexMarkers[0].transform.position;

                if (!areaSegments.TryGetValue(0, out List<Vector2> listAreaSegments0))
                {
                    listAreaSegments0 = new List<Vector2>();
                    areaSegments[0] = listAreaSegments0;
                }

                if (!areaSegments.TryGetValue(1, out List<Vector2> listAreaSegments1))
                {
                    listAreaSegments1 = new List<Vector2>();
                    areaSegments[1] = listAreaSegments1;
                }

                listAreaSegments0.Clear();
                listAreaSegments1.Clear();

                if (Mathf.Sign(Vector3.Cross(vec1, cntr1).y) == Mathf.Sign(Vector3.Cross(vec1, vec2).y))
                {
                    frameResult.centroids.Add(new Vector2(centroids[0].x, centroids[0].y));
                    frameResult.areas.Add(areas[0]);
                    for (int j = item.Item2.Count - 1; j >= 0; j--)
                    {
                        listAreaSegments0.Add(new Vector2(item.Item2[j].x, item.Item2[j].y));
                    }

                    frameResult.centroids.Add(new Vector2(centroids[1].x, centroids[1].y));
                    frameResult.areas.Add(areas[1]);
                    for (int j = item2.Item2.Count - 1; j >= 0; j--)
                    {
                        listAreaSegments1.Add(new Vector2(item2.Item2[j].x, item2.Item2[j].y));
                    }
                }
                else
                {
                    frameResult.areas.Add(areas[1]);
                    frameResult.centroids.Add(new Vector2(centroids[1].x, centroids[1].y));
                    for (int j = item2.Item2.Count - 1; j >= 0; j--)
                    {
                        listAreaSegments0.Add(new Vector2(item2.Item2[j].x, item2.Item2[j].y));
                    }

                    frameResult.areas.Add(areas[0]);
                    frameResult.centroids.Add(new Vector2(centroids[0].x, centroids[0].y));
                    for (int j = item.Item2.Count - 1; j >= 0; j--)
                    {
                        listAreaSegments1.Add(new Vector2(item.Item2[j].x, item.Item2[j].y));
                    }
                }

                if (frameResult.centroids.Count == totalUserCount && !float.IsNaN(frameResult.centroids[0].x) && !float.IsNaN(frameResult.centroids[0].y))
                {
                    for (int i = 0; i < totalUserCount; i++)
                    {
                        if (useVecObservation && i < s2cCenterPointers.Count)
                        {
                            s2cCenterPointers[i].transform.position = new Vector3(frameResult.centroids[i].x, 0.0f, frameResult.centroids[i].y);
                        }

                        if (i < s2cCenterPointers.Count && RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector() is S2CRedirector)
                        {
                            ((S2CRedirector)RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector()).SetCenterPoint(s2cCenterPointers[i].transform.position);
                            s2cCenterPointers[i].gameObject.SetActive(true);
                        }
                    }
                }
            }

            return frameResult;
        }

        public override void InitObstacleInfo()
        {
        }

        public override List<Vector2> GenerateUniformSeedPoints(int totalUserCount, SpaceBounds bounds, float epsilon)
        {
            List<Vector2> results = new List<Vector2>();
            if (totalUserCount <= 0)
                return results;

            List<Vector2f> seeds = new List<Vector2f>(totalUserCount);
            for (int i = 0; i < totalUserCount; i++)
            {
                float x = Random.Range(bounds.min.x, bounds.max.x);
                float y = Random.Range(bounds.min.y, bounds.max.y);
                seeds.Add(new Vector2f(x, y));
            }

            Rectf plotBounds = new Rectf(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
            csDelaunay.Voronoi tempVoronoi = new csDelaunay.Voronoi(seeds, plotBounds, 5000);

            foreach (var kv in tempVoronoi.SitesIndexedByLocation)
            {
                results.Add(new Vector2(kv.Key.x, kv.Key.y));
            }

            tempVoronoi.Dispose();
            return results;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取用户的邻居（共享 Voronoi 边的用户）
        /// </summary>
        public List<int> GetNeighborUsers(UserInfo user, List<UserInfo> allUsers)
        {
            List<int> neighbors = new List<int>();

            if (voronoi == null)
                return neighbors;

            Vector2f seedPoint = new Vector2f(user.position.x, user.position.y);
            List<Vector2f> neighborSites = voronoi.NeighborSitesForSite(seedPoint);

            foreach (var neighborSite in neighborSites)
            {
                // 找到对应的用户
                foreach (var otherUser in allUsers)
                {
                    if (Mathf.Approximately(otherUser.position.x, neighborSite.x) &&
                        Mathf.Approximately(otherUser.position.y, neighborSite.y))
                    {
                        neighbors.Add(otherUser.userId);
                        break;
                    }
                }
            }

            return neighbors;
        }

        /// <summary>
        /// 获取 Voronoi 图的所有边（用于可视化）
        /// </summary>
        public List<LineSegment> GetVoronoiDiagram()
        {
            if (voronoi == null)
                return new List<LineSegment>();

            return voronoi.VoronoiDiagram();
        }

        #endregion

        #region 可视化

        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();

            if (!Application.isPlaying || voronoi == null)
                return;

            if (showPartitionBoundaries)
            {
                DrawVoronoiEdges();
            }
        }

        /// <summary>
        /// 绘制 Voronoi 图的边
        /// </summary>
        private void DrawVoronoiEdges()
        {
            List<LineSegment> diagram = GetVoronoiDiagram();
            if (diagram == null)
                return;

            Gizmos.color = boundaryColor;

            foreach (var segment in diagram)
            {
                if (segment != null)
                {
                    Vector3 p0 = new Vector3(segment.p0.x, 0.05f, segment.p0.y);
                    Vector3 p1 = new Vector3(segment.p1.x, 0.05f, segment.p1.y);
                    Gizmos.DrawLine(p0, p1);
                }
            }
        }

        #endregion

        #region 清理

        private void OnDestroy()
        {
            CleanupVoronoi();
        }

        /// <summary>
        /// 清理 Voronoi 数据
        /// </summary>
        private void CleanupVoronoi()
        {
            if (voronoi != null)
            {
                voronoi.Dispose();
                voronoi = null;
            }

            sitesDict = null;
            voronoiEdges = null;
        }

        #endregion
    }
}
