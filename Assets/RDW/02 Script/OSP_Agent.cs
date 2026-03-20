using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityEngine.UI;
using System.Text;
using System.Linq;
using csDelaunay;

namespace _OSP
{
    public class OSP_Agent : MonoBehaviour
    {
        #region singleton pattern
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static OSP_Agent instance = null;
        #endregion

        #region Simulation Configuration
        [Header("=== Simulation Configuration ===")]
        /// <summary>
        /// Total number of users in the simulation
        /// </summary>
        public int totalUserCount = 2;

        /// <summary>
        /// Radius of the user (agent)
        /// </summary>
        [SerializeField]
        private float userRadius = 0.5f;

        /// <summary>
        /// Width of the shutter (obstacle/boundary)
        /// </summary>
        [SerializeField]
        private float shutterWidth = 0.5f;

        /// <summary>
        /// Max number of episodes to run
        /// </summary>
        public int SimulationCount_max = 100;

        /// <summary>
        /// Current episode count
        /// </summary>
        private int currentSimulationCount = 0;

        /// <summary>
        /// Target walking distance per user to finish an episode
        /// </summary>
        public float TargetDistancePerUser = 200.0f;

        /// <summary>
        /// Current accumulated total distance for the episode
        /// </summary>
        private float currentEpisodeTotalDistance = 0.0f;

        /// <summary>
        /// Enable mixed exploration logic (if applicable)
        /// </summary>
        public bool bMixedExploration = true;
        #endregion

        #region Room Dimensions
        [Header("=== Room Settings ===")]
        /// <summary>
        /// Half-width of the physical room
        /// </summary>
        private float physicalRoom_width_half = 0;
        /// <summary>
        /// Half-height of the physical room
        /// </summary>
        private float physicalRoom_height_half = 0;
        /// <summary>
        /// Half-width of the virtual room
        /// </summary>
        private float virtualRoom_width_half = 0;
        /// <summary>
        /// Half-height of the virtual room
        /// </summary>
        private float virtualRoom_height_Half = 0;

        // Legacy / Unused room stats
        private float actual_halfRoomsize_default = 36;
        private float actual_roomhypotenuse = 0.0f;
        #endregion

        #region User Management
        [Header("=== User Management ===")]
        /// <summary>
        /// List of physical user GameObjects
        /// </summary>        
        private List<GameObject> list_physical_simulatedUsers = new List<GameObject>();

        /// <summary>
        /// List of virtual user GameObjects
        /// </summary>        
        private List<GameObject> list_virtual_simulatedUsers = new List<GameObject>();

        /// <summary>
        /// Users' previous physical positions (for distance calc)
        /// </summary>
        private List<Vector2> list_UsersPrePhysicalPos = new List<Vector2>();

        /// <summary>
        /// Users' current physical positions
        /// </summary>
        private List<Vector2> list_UsersCurrentPhysicalPos = new List<Vector2>();

        /// <summary>
        /// Users' accumulated distance
        /// </summary>
        private List<float> list_UsersCumulativeDist = new List<float>();
        #endregion

        #region Statistics & Data Recording
        [Header("=== Statistics Recording ===")]
        /// <summary>
        /// Total resets count per episode
        /// </summary>
        List<int> list_usersTotalReset_perEpisode = new List<int>();

        /// <summary>
        /// User-between resets count per episode
        /// </summary>
        List<int> list_userbetReset_perEpisode = new List<int>();

        /// <summary>
        /// Shutter (obstacle) resets count per episode
        /// </summary>
        List<int> list_usersShutterReset_perEpisode = new List<int>();

        /// <summary>
        /// Mean Distance Between Resets (MDbR) per episode
        /// </summary>
        List<float> list_MDbR_perEpisode = new List<float>();

        #endregion

        #region Voronoi & Spatial Partitioning
        [Header("=== OSP / Voronoi Data ===")]
        /// <summary>
        /// Core Voronoi object
        /// </summary>
        private Voronoi voronoi;
        private Dictionary<Vector2f, Site> sites;
        private List<Edge> edges;

        /// <summary>
        /// Seed points for Voronoi generation (Dynamic)
        /// </summary>
        List<Vector2f> list_VoronoiSeedPoint = new List<Vector2f>();
        
        /// <summary>
        /// Fixed seed points (if used)
        /// </summary>
        List<Vector2f> list_VoronoiSeedPoint_Fixed = new List<Vector2f>();

        /// <summary>
        /// Calculated centroids of Voronoi cells
        /// </summary>
        List<Vector2> list_voronoiCentroid = new List<Vector2>();

        /// <summary>
        /// Areas of Voronoi cells
        /// </summary>
        List<float> list_voronoiArea = new List<float>();

        /// <summary>
        /// Vertices for area segments
        /// </summary>
        public Dictionary<int, List<Vector2>> dic_AreaSegmentsVertex = new Dictionary<int, List<Vector2>>();

        /// <summary>
        /// Use Lloyd Relaxation for initial uniform distribution
        /// </summary>
        public bool bEnable_InitPhyUserPosUni = false;


        
        private int edgeMaxcount = 45;
        private float eps = 0.001f;
        private RaycastHit rayCastHit;
        #endregion

        #region Visualization & Prefabs
        [Header("=== Visualization & Prefabs ===")]
        [SerializeField] private GameObject prefab_VoronoiVertex;
        [SerializeField] private GameObject prefab_VoronoiSeedPoint;
        [SerializeField] private GameObject S2C_CenterPointerObject_Prefab;
        public List<Material> PartitionedSpaceMaterials = new List<Material>();

        // Object Pools / Lists for visual elements
        List<GameObject> list_VoronoiVertexMarker = new List<GameObject>();
        List<GameObject> list_seedPointVisual = new List<GameObject>();
        List<GameObject> list_S2C_CenterPointer = new List<GameObject>();
        List<Vector2> list_WayPoint_vertices = new List<Vector2>(); // Used for obstacle mesh info
        #endregion

        #region Velocity Tracker (OSP Dynamic Offset)
        [Header("=== OSP Algorithm Settings ===")]
        [Tooltip("是否启用基于速度的种子点偏移")]
        public bool bUseVelocityOffset = true;

        [Header("=== Velocity Settings ===")]
        public float alphaMax = 0.15f;
        public float vMax = 0.6f;
        public float velocityToOffsetFactor = 0.3f;
        public float maxVelocityOffsetDist = 0.5f; // Limit
        public float stopThreshold = 0.05f;

        // Internal velocity tracking state
        private Vector3[] _userLastPos;
        private Vector3[] _userSmoothV1;
        private Vector3[] _userSmoothV2;
        private Vector3[] _userFrozenOffset;
        private Vector3[] _userCurrentOffset;
        private bool[] _userIsStopped;
        private bool _velocityInit = false;
        #endregion

        #region UI References
        [Header("=== UI References ===")]
        /// <summary>
        /// Text for checking the current episode count
        /// </summary>        
        [SerializeField]
        private Text text_currentEpi;
        #endregion

        //------------------------

        private void Awake()
        {
            instance = this;

            //Academy.Instance.AutomaticSteppingEnabled = false;
        }

        private void Start()
        {
            if (GM_DataRecord.instance == null)
            {
                Debug.LogError("OSP_Agent Start: GM_DataRecord.instance is NULL! Please ensure GM_DataRecord script is attached to an active GameObject in the scene.");
            }
            else
            {
                Debug.Log("OSP_Agent Start: GM_DataRecord.instance found successfully.");
            }
         
            InitializeInfoDics();
            
            InitializeInfoLists(Vector2.zero);

            /// init object pool for visualization center-points of S2C
            for (int i = 0; i < totalUserCount; i++)
            {
                list_S2C_CenterPointer.Add(Instantiate(S2C_CenterPointerObject_Prefab));

                list_S2C_CenterPointer[i].SetActive(false);
            }

            if(totalUserCount < 3 && bEnable_InitPhyUserPosUni)
            {
                bEnable_InitPhyUserPosUni = false;
            }

            InitializeVelocityTracker();

            // Manually trigger the first episode since we removed ML-Agents
            ResetEpisode();
        }

        private void InitializeVelocityTracker()
        {
            _userLastPos = new Vector3[totalUserCount];
            _userSmoothV1 = new Vector3[totalUserCount];
            _userSmoothV2 = new Vector3[totalUserCount];
            _userFrozenOffset = new Vector3[totalUserCount];
            _userCurrentOffset = new Vector3[totalUserCount];
            _userIsStopped = new bool[totalUserCount];
            
            _velocityInit = false; // 等待SetResetParameters填充
        }

        private void UpdateVelocityTracker()
        {
            if (!_velocityInit) return;

            float dt = Time.fixedDeltaTime;
            // Safety check
            if (dt <= 0.0001f) return;

            for (int i = 0; i < totalUserCount; i++)
            {
                // Safety check: ensure index is valid for physical users list
                if (list_physical_simulatedUsers == null || i >= list_physical_simulatedUsers.Count)
                    continue;

                if (list_physical_simulatedUsers[i] == null) continue;

                Vector3 currentPos = list_physical_simulatedUsers[i].transform.position;


                // 1. Calc Raw
                Vector3 rawVelocity = (currentPos - _userLastPos[i]) / dt;
                rawVelocity.y = 0;
                float rawSpeed = rawVelocity.magnitude;

                // 2. Dynamic Alpha
                float speedRatio = Mathf.Clamp01(rawSpeed / vMax);
                float alpha = alphaMax * (speedRatio * speedRatio);

                // 3. Double Exponential Smoothing
                _userSmoothV1[i] = alpha * rawVelocity + (1f - alpha) * _userSmoothV1[i];
                _userSmoothV2[i] = alpha * _userSmoothV1[i] + (1f - alpha) * _userSmoothV2[i];

                Vector3 finalVelocity = _userSmoothV2[i];
                float finalSpeed = finalVelocity.magnitude;

                // 4. Target Offset
                Vector3 offsetDir = finalVelocity.normalized;
                // Use velocityToOffsetFactor to scale speed to distance
                float targetOffsetDist = Mathf.Clamp(finalSpeed * velocityToOffsetFactor, 0f, maxVelocityOffsetDist);
                Vector3 computedOffset = offsetDir * targetOffsetDist;

                // 5. Freeze / Decay
                if (finalSpeed < stopThreshold)
                {
                    if (!_userIsStopped[i])
                    {
                        _userFrozenOffset[i] = computedOffset;
                        _userIsStopped[i] = true;
                    }
                    
                    // Slow decay
                    _userFrozenOffset[i] = Vector3.Lerp(_userFrozenOffset[i], Vector3.zero, dt * 0.5f);
                    computedOffset = _userFrozenOffset[i];
                }
                else
                {
                    _userIsStopped[i] = false;
                }

                // Update LastPos
                _userLastPos[i] = currentPos;

                // Store result
                _userCurrentOffset[i] = computedOffset;
            }
        }

        private void OnDestroy()
        {
            if (voronoi != null)
            {
                voronoi.Dispose();
                voronoi = null;
            }
        }

        public void ResetEpisode()
        {
            SetResetParameters();
        }

        /// <summary>
        /// Main simulation loop. 
        /// Previously this logic was in OnActionReceived (ML-Agents), now it is called every FixedUpdate to drive the simulation.
        /// </summary>
        private void ProcessStep()
        {
            if (!RDWSimulationManager.instance.BStart)
                return;

            // Safety Check: ensure users are initialized before processing calculation
            if (list_physical_simulatedUsers == null || list_physical_simulatedUsers.Count < totalUserCount) 
                return;
            
            // --- Distance Calculation ---
            for (int i = 0; i < totalUserCount; i++)
            {
                if (list_physical_simulatedUsers[i] == null) continue;
                
                // If resetting, update pre-pos but don't add distance
                if (RDWSimulationManager.instance.GetRedirectedUnits != null && 
                    i < RDWSimulationManager.instance.GetRedirectedUnits.Length &&
                    RDWSimulationManager.instance.GetRedirectedUnits[i] != null &&
                    RDWSimulationManager.instance.GetRedirectedUnits[i].IsResetting)
                {
                    list_UsersPrePhysicalPos[i] = new Vector2(list_physical_simulatedUsers[i].transform.position.x, list_physical_simulatedUsers[i].transform.position.z);
                    continue;
                }

                Vector2 currentPos = new Vector2(list_physical_simulatedUsers[i].transform.position.x, list_physical_simulatedUsers[i].transform.position.z);
                float dist = Vector2.Distance(currentPos, list_UsersPrePhysicalPos[i]);
                
                // Filter large jumps
                if (dist < 2.0f) 
                {
                    currentEpisodeTotalDistance += dist;
                    if (i < list_UsersCumulativeDist.Count)
                    {
                        list_UsersCumulativeDist[i] += dist;
                    }
                }
                
                list_UsersPrePhysicalPos[i] = currentPos;
            }

            // Condition: Total Distance >= Total Users * Target Distance
            float targetTotalDistance = totalUserCount * TargetDistancePerUser;

            /// Init Obstacle mesh info for next episode
            if (currentEpisodeTotalDistance >= targetTotalDistance)
            {
                Debug.Log($"Episode Finished. Total Dist: {currentEpisodeTotalDistance}");

                CalcResultPerEpisode();
                ResetEpisode();
                return;
            }

            // Dynamic Voronoi Update
            {
                // 1. 计算偏移限制半径 (仅在使用偏移时需要)
                float maxSafeRadius = 0f;
                if (bUseVelocityOffset)
                {
                    maxSafeRadius = CalcStableAreaRadius();
                    maxSafeRadius = Mathf.Max(0f, maxSafeRadius);
                }

                for (int i = 0; i < totalUserCount; i++)
                {
                    Vector3 userPos = list_physical_simulatedUsers[i].transform.position;
                    
                    Vector3 offsetVector = Vector3.zero;

                    // 若开启偏移，则计算基于速度的偏移向量
                    if (bUseVelocityOffset)
                    {
                        offsetVector = _userCurrentOffset[i];

                        // 限制偏移量不超过安全半径
                        if (offsetVector.magnitude > maxSafeRadius)
                        {
                            offsetVector = offsetVector.normalized * maxSafeRadius;
                        }
                    }

                    // 计算目标种子点位置
                    Vector3 virtualPos = userPos + offsetVector;

                    // 2. 房间边界限制 (保持不变)
                    float safeHalfW = physicalRoom_width_half - 0.1f;
                    float safeHalfH = physicalRoom_height_half - 0.1f;
                    float clampedX = Mathf.Clamp(virtualPos.x, -safeHalfW, safeHalfW);
                    float clampedZ = Mathf.Clamp(virtualPos.z, -safeHalfH, safeHalfH);

                    // 3. 平滑赋值 (双重平滑本身已经很平滑，这里可以保留或减少额外Lerp)
                    // 保留一点点Lerp用于防止边界Clamp时的突变
                    Vector2 targetSeed = new Vector2(clampedX, clampedZ);
                    Vector2 currentSeed = new Vector2(list_VoronoiSeedPoint[i].x, list_VoronoiSeedPoint[i].y);
                    Vector2 smoothedSeed = Vector2.Lerp(currentSeed, targetSeed, Time.deltaTime * 10f);

                    list_VoronoiSeedPoint[i] = new Vector2f(smoothedSeed.x, smoothedSeed.y);
                }



                /// display seed points
                for (int i = 0; i < totalUserCount; i++)
                {
                    list_seedPointVisual[i].transform.position = new Vector3(list_VoronoiSeedPoint[i].x, 0.0f, list_VoronoiSeedPoint[i].y);
                }


                /// Update Voronoi Diagram for current state
                UpdateVoronoiDiagram();
            }


            // Removed bEnable_InitPhyUserPosUni one-frame lock logic
            // Removed bOneframetimerblockVoronoi logic



            /// Simulate the designated redirection controller
            RDWSimulationManager.instance.SimulateRDW();

            // AddRewards(); // Removed
        }

        /// <summary>
        /// Update Vornoi Diagram for current frame
        /// </summary>
        private void UpdateVoronoiDiagram()
        {
            /// define physcial boundary for Voronoi Dia gram 
            Rectf bounds = new Rectf(-physicalRoom_width_half, -physicalRoom_height_half, physicalRoom_width_half * 2, physicalRoom_height_half * 2);

            if (voronoi != null)
            {
                voronoi.Dispose();
            }

            /// Calculate Voronoi Diagram                                                                                                              
            voronoi = new Voronoi(list_VoronoiSeedPoint, bounds);
            sites = voronoi.SitesIndexedByLocation;
            edges = voronoi.Edges;

            /// make Voronoi vertices invisible
            foreach (var item in list_VoronoiVertexMarker)
            {
                item.GetComponent<MeshRenderer>().enabled = false;
            }

            int tempCount = 0;
            int VoronoiEdgeCount = 0;

            ///Update Voronoi edge point marker
            foreach (Edge edge in edges)
            {
                if (edge.ClippedEnds == null)
                    continue;

                list_VoronoiVertexMarker[tempCount].transform.position = new Vector3(edge.ClippedEnds[LR.LEFT].x, 0.0f, edge.ClippedEnds[LR.LEFT].y);
                list_VoronoiVertexMarker[tempCount++].GetComponent<MeshRenderer>().enabled = true;
                list_VoronoiVertexMarker[tempCount].transform.position = new Vector3(edge.ClippedEnds[LR.RIGHT].x, 0.0f, edge.ClippedEnds[LR.RIGHT].y);
                list_VoronoiVertexMarker[tempCount++].GetComponent<MeshRenderer>().enabled = true;
                VoronoiEdgeCount++;
            }


            #region for totalUserCount > 2
            /// Update Voronoi Centronoid and assign centronoi redirection target each user
            if (totalUserCount > 2)
            {
                list_voronoiCentroid.Clear();
                list_voronoiArea.Clear();
                List<Vector2f> centroids = new List<Vector2f>();

                SiteList sites_forcentronoid = new SiteList();
                centroids = voronoi.GetCentroid_site(totalUserCount, ref list_voronoiArea, ref sites_forcentronoid);

                for (int i = 0; i < totalUserCount; i++)
                {
                    list_voronoiCentroid.Add(new Vector2(centroids[i].x, centroids[i].y));
                }

                if (list_voronoiCentroid.Count == totalUserCount && !float.IsNaN(list_voronoiCentroid[0].x) && !float.IsNaN(list_voronoiCentroid[0].y))
                {
                    for (int i = 0; i < totalUserCount; i++)
                    {
                        int targetIndex = 0;
                        for (int j = 0; j < totalUserCount; j++)
                        {
                            if (Mathf.Abs(list_VoronoiSeedPoint[i].x - sites_forcentronoid.GetSite_byIndex(j).x) < eps
                                && Mathf.Abs(list_VoronoiSeedPoint[i].y - sites_forcentronoid.GetSite_byIndex(j).y) < eps)
                            {
                                targetIndex = j;
                            }
                            else
                            {
                                continue;
                            }
                        }

                        // if (bUseVecOberv)
                        
                        list_S2C_CenterPointer[i].transform.position = new Vector3(list_voronoiCentroid[targetIndex].x, 0.0f, list_voronoiCentroid[targetIndex].y);
                        

                        list_S2C_CenterPointer[i].name = "centroid for user" + targetIndex;

                        List<Vector2> list_AreaSegmentsVertex = new List<Vector2>();
                        dic_AreaSegmentsVertex.TryGetValue(i, out list_AreaSegmentsVertex);
                        list_AreaSegmentsVertex.Clear();

                        List<Vector2f> region = sites_forcentronoid.GetSite_byIndex(targetIndex).Region(bounds);

                        for (int k = region.Count - 1; k >= 0; k--)
                        {
                            list_AreaSegmentsVertex.Add(new Vector2(region[k].x, region[k].y));
                        }

                        //just a moment shut down code (for APF)
                        if (RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector() is S2CRedirector)
                        {
                            ((S2CRedirector)RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector()).SetCenterPoint(list_S2C_CenterPointer[i].transform.position);
                            list_S2C_CenterPointer[i].gameObject.SetActive(true);
                        }

                    }
                }
                else
                {
                    Debug.LogError("IsNaN");
                }
            }
            #endregion

            #region for totalUserCount == 2
            ///Update voronoi Centronoid and assign centronoi redirection target each user
            if (totalUserCount == 2)
            {
                list_voronoiCentroid.Clear();
                list_voronoiArea.Clear();
                List<Vector2f> centroids = new List<Vector2f>();
                List<float> areas = new List<float>();
                float areavalue = 0.0f;
                (Vector2f, List<Vector2f>) item = voronoi.GetCentroidTwin_region(0, true, ref areavalue);
                (Vector2f, List<Vector2f>) item2 = voronoi.GetCentroidTwin_region(1, false, ref areavalue);
                centroids.Add(item.Item1);
                areas.Add(areavalue);
                centroids.Add(item2.Item1);
                areas.Add(areavalue);

                Vector3 vec1 = list_VoronoiVertexMarker[1].transform.position - list_VoronoiVertexMarker[0].transform.position;
                Vector3 cntr1 = new Vector3(centroids[0].x, 0.0f, centroids[0].y) - list_VoronoiVertexMarker[0].transform.position;
                Vector3 vec2 = list_physical_simulatedUsers[0].transform.position - list_VoronoiVertexMarker[0].transform.position;

                List<Vector2> list_AreaSegmentsVertex0 = new List<Vector2>();
                dic_AreaSegmentsVertex.TryGetValue(0, out list_AreaSegmentsVertex0);
                list_AreaSegmentsVertex0.Clear();

                List<Vector2> list_AreaSegmentsVertex1 = new List<Vector2>();
                dic_AreaSegmentsVertex.TryGetValue(1, out list_AreaSegmentsVertex1);
                list_AreaSegmentsVertex1.Clear();

                List<Vector2f> aa = new List<Vector2f>();

                if (Mathf.Sign(Vector3.Cross(vec1, cntr1).y) == Mathf.Sign(Vector3.Cross(vec1, vec2).y))
                {
                    //Debug.Log("true");
                    list_voronoiCentroid.Add(new Vector2(centroids[0].x, centroids[0].y));
                    list_voronoiArea.Add(areas[0]);
                    for (int j = item.Item2.Count - 1; j >= 0; j--)
                    {
                        list_AreaSegmentsVertex0.Add(new Vector2(item.Item2[j].x, item.Item2[j].y));
                    }

                    list_voronoiCentroid.Add(new Vector2(centroids[1].x, centroids[1].y));
                    list_voronoiArea.Add(areas[1]);
                    for (int j = item2.Item2.Count - 1; j >= 0; j--)
                    {
                        list_AreaSegmentsVertex1.Add(new Vector2(item2.Item2[j].x, item2.Item2[j].y));
                    }
                }
                else
                {
                    //Debug.Log("false");
                    list_voronoiArea.Add(areas[1]);
                    list_voronoiCentroid.Add(new Vector2(centroids[1].x, centroids[1].y));
                    for (int j = item2.Item2.Count - 1; j >= 0; j--)
                    {
                        list_AreaSegmentsVertex0.Add(new Vector2(item2.Item2[j].x, item2.Item2[j].y));
                    }

                    list_voronoiArea.Add(areas[0]);
                    list_voronoiCentroid.Add(new Vector2(centroids[0].x, centroids[0].y));
                    for (int j = item.Item2.Count - 1; j >= 0; j--)
                    {
                        list_AreaSegmentsVertex1.Add(new Vector2(item.Item2[j].x, item.Item2[j].y));
                    }
                }


                if (list_voronoiCentroid.Count == totalUserCount && !float.IsNaN(list_voronoiCentroid[0].x) && !float.IsNaN(list_voronoiCentroid[0].y))
                {
                    for (int i = 0; i < totalUserCount; i++)
                    {
                        // if (bUseVecOberv)
                        
                        list_S2C_CenterPointer[i].transform.position = new Vector3(list_voronoiCentroid[i].x, 0.0f, list_voronoiCentroid[i].y);
                        

                        //just a moment shut down code (for APF)
                        if (RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector() is S2CRedirector)
                        {
                            ((S2CRedirector)RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector()).SetCenterPoint(list_S2C_CenterPointer[i].transform.position);
                            list_S2C_CenterPointer[i].gameObject.SetActive(true);
                        }


                    }

                }
            }
            #endregion

            int VoronoiVertexCount = 0;
            int count2 = (totalUserCount * (totalUserCount - 1));
            int count3 = 0;

            while (VoronoiVertexCount + 2 <= count2)
            {
                list_WayPoint_vertices.Clear();

                if (VoronoiVertexCount >= VoronoiEdgeCount * 2)
                {
                    Vector2 dir = new Vector2(list_VoronoiVertexMarker[1].transform.position.x, list_VoronoiVertexMarker[1].transform.position.z) - new Vector2(list_VoronoiVertexMarker[0].transform.position.x, list_VoronoiVertexMarker[0].transform.position.z);
                    Vector2 dir_perpendicular = Vector2.Perpendicular(dir);
                    Vector2 dir_perpendicular_unit = dir_perpendicular.normalized * (shutterWidth / 2);

                    list_WayPoint_vertices.Add(new Vector2(-51, 1));
                    list_WayPoint_vertices.Add(new Vector2(-49, 1));
                    list_WayPoint_vertices.Add(new Vector2(-49, -1));
                    list_WayPoint_vertices.Add(new Vector2(-51, -1));
                }
                else
                {
                    Vector2 dir = new Vector2(list_VoronoiVertexMarker[VoronoiVertexCount + 1].transform.position.x, list_VoronoiVertexMarker[VoronoiVertexCount + 1].transform.position.z) - new Vector2(list_VoronoiVertexMarker[VoronoiVertexCount].transform.position.x, list_VoronoiVertexMarker[VoronoiVertexCount].transform.position.z);
                    Vector2 dir_perpendicular = Vector2.Perpendicular(dir);
                    Vector2 dir_perpendicular_unit = dir_perpendicular.normalized * (shutterWidth / 2);

                    if ((list_VoronoiVertexMarker[VoronoiVertexCount + 1].transform.position - list_VoronoiVertexMarker[VoronoiVertexCount].transform.position).magnitude < 0.1f)
                    {
                        list_WayPoint_vertices.Add(new Vector2(-51, 1));
                        list_WayPoint_vertices.Add(new Vector2(-49, 1));
                        list_WayPoint_vertices.Add(new Vector2(-49, -1));
                        list_WayPoint_vertices.Add(new Vector2(-51, -1));
                    }
                    else
                    {
                        list_WayPoint_vertices.Add(new Vector2(list_VoronoiVertexMarker[VoronoiVertexCount].transform.position.x - dir_perpendicular_unit.x, list_VoronoiVertexMarker[VoronoiVertexCount].transform.position.z - dir_perpendicular_unit.y));
                        list_WayPoint_vertices.Add(new Vector2(list_VoronoiVertexMarker[VoronoiVertexCount].transform.position.x + dir_perpendicular_unit.x, list_VoronoiVertexMarker[VoronoiVertexCount].transform.position.z + dir_perpendicular_unit.y));
                        list_WayPoint_vertices.Add(new Vector2(list_VoronoiVertexMarker[VoronoiVertexCount + 1].transform.position.x + dir_perpendicular_unit.x, list_VoronoiVertexMarker[VoronoiVertexCount + 1].transform.position.z + dir_perpendicular_unit.y));
                        list_WayPoint_vertices.Add(new Vector2(list_VoronoiVertexMarker[VoronoiVertexCount + 1].transform.position.x - dir_perpendicular_unit.x, list_VoronoiVertexMarker[VoronoiVertexCount + 1].transform.position.z - dir_perpendicular_unit.y));
                    }
                }

                VoronoiVertexCount += 2;
                count3++;
            }
        }

        /// <summary>
        /// 计算安全半径
        /// 必须使用 list_physical_simulatedUsers (真实物理位置) 来计算距离
        /// 不能使用 list_VoronoiSeedPoint，因为偏移后种子点距离会虚高
        /// </summary>
        private float CalcStableAreaRadius()
        {
            Dictionary<string, float> dist_dic = new Dictionary<string, float>();
            
            // 安全检查：如果用户列表为空或数量不足，直接返回0
            if (list_physical_simulatedUsers == null) return 0f;

            for (int i = 0; i < totalUserCount; i++)
            {
                if (i >= list_physical_simulatedUsers.Count || list_physical_simulatedUsers[i] == null) continue;

                // 【修改点 1】获取真实用户的 Transform 位置
                // 注意：这里要确保 list_physical_simulatedUsers 已经正确赋值
                Vector3 me = list_physical_simulatedUsers[i].transform.position; 
                
                Vector3 target = Vector3.zero;

                for (int j = i; j < totalUserCount; j++)
                {
                    if (i == j)
                        continue;
                    
                    if (j >= list_physical_simulatedUsers.Count || list_physical_simulatedUsers[j] == null) continue;

                    // 【修改点 2】获取对方真实用户的位置
                    target = list_physical_simulatedUsers[j].transform.position;
                    
                    // 计算水平面上的物理距离（忽略高度差）
                    Vector3 distVec_users00_01 = new Vector3(me.x, 0, me.z) - new Vector3(target.x, 0, target.z);

                    dist_dic.Add(i.ToString() + j.ToString(), distVec_users00_01.magnitude);
                }
            }

            if (dist_dic.Count == 0) return 0f; // 防止只有1个用户时报错

            ///calc min dist
            var keyAndValue = dist_dic.OrderBy(kvp => kvp.Value).First();
            float minDistValue = keyAndValue.Value;

            ///calc maximum available radius
            // 这个半径决定了种子点能乱动的范围，必须基于物理实体的最小间距来限制
            return ((minDistValue - shutterWidth) / 2) - userRadius;
        }

        /// <summary>
        /// Calculate resaults per episode
        /// </summary>
        private void CalcResultPerEpisode()
        {
            if (true) // Condition controlled by ProcessStep
            {
                var units = RDWSimulationManager.instance.GetRedirectedUnits;
                
                ///wall reset count
                List<int> list_userWallReset = new List<int>();
                for (int i = 0; i < totalUserCount; i++)
                {
                    if (units != null && i < units.Length && units[i] != null)
                        list_userWallReset.Add((int)(units[i].resultData.getWallReset()));
                }
                int wallResetSum = list_userWallReset.Sum();

                ///user reset count
                //List<int> list_useruserReset = new List<int>();
                //for (int i = 0; i < totalUserCount; i++)
                //{
                //    list_useruserReset.Add((int)(RDWSimulationManager.instance.GetRedirectedUnits[i].resultData.getUserReset()));
                //}
                //int userResetSum = list_useruserReset.Sum();
                //int userResetSum = list_useruserReset.Sum();

                ///shutter reset count
                List<int> list_userShutterReset = new List<int>();
                for (int i = 0; i < totalUserCount; i++)
                {
                    list_userShutterReset.Add((int)(RDWSimulationManager.instance.GetRedirectedUnits[i].resultData.getShutterReset()));
                }
                int ShutterResetSum = list_userShutterReset.Sum();

                /// user reset zero concept
                //int userbet = 0;
                //int userbet = userResetSum;
                int userbet = RDWSimulationManager.instance.Calc_UserResetFilter();


                int totalResets = wallResetSum + userbet + ShutterResetSum;
                list_usersTotalReset_perEpisode.Add(totalResets);
                list_usersShutterReset_perEpisode.Add(ShutterResetSum);
                list_userbetReset_perEpisode.Add(userbet);

                // Use accumulated distance instead of list average, and divide by total resets for true MDbR
                // If resets are 0, use total distance (or handle as needed)
                float MDbR_AVG = 0f;
                if (totalResets > 0)
                {
                    MDbR_AVG = list_UsersCumulativeDist.Sum() / totalResets;
                }
                else
                {
                    MDbR_AVG = list_UsersCumulativeDist.Sum(); 
                }

                Debug.Log(string.Format("walllreset {0} / userreset {1} /shutterreset {2} / MDbR AVg. {3}", wallResetSum, userbet, ShutterResetSum, MDbR_AVG));
                Debug.LogWarning(string.Format("walllreset {0} / userreset {1} /shutterreset {2} / MDbR AVg. {3}", wallResetSum, userbet, ShutterResetSum, MDbR_AVG));


                list_MDbR_perEpisode.Add(MDbR_AVG);

                StringBuilder sb = new StringBuilder();

                sb.Append(',');
                sb.Append(',');
                sb.Append(wallResetSum).Append(',');
                sb.Append(wallResetSum + ShutterResetSum + userbet).Append(',');
                sb.Append(userbet).Append(',');
                sb.Append(ShutterResetSum).Append(',');
                sb.Append(MDbR_AVG).Append(',');

                //sb.AppendFormat("{0:F4}", GetCumulativeReward()).Append(',');

                if (sb.Length > 0 && sb[sb.Length - 1] == ',')
                {
                    sb.Remove(sb.Length - 1, 1);
                }


                if (GM_DataRecord.instance != null)
                {
                    GM_DataRecord.instance.Enequeue_Data(sb.ToString());
                }

                currentSimulationCount++;

                if (text_currentEpi != null)
                {
                    text_currentEpi.text = "Current Episode : " + (currentSimulationCount + 1);
                }


                if (currentSimulationCount == SimulationCount_max)
                {
                    currentSimulationCount = 0;
                    if (GM_DataRecord.instance != null)
                    {
                        GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.TotalResetMean, list_usersTotalReset_perEpisode.Average().ToString("F3"));
                        GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.TotalResetMean, getStandardDeviation(list_usersTotalReset_perEpisode).ToString("F3"));
                        GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UserbetResetMean, list_userbetReset_perEpisode.Average().ToString("F3"));
                        GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UserbetResetMean, getStandardDeviation(list_userbetReset_perEpisode).ToString("F3"));
                        GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UsershutterResetMean, list_usersShutterReset_perEpisode.Average().ToString("F3"));
                        GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UsershutterResetMean, getStandardDeviation(list_usersShutterReset_perEpisode).ToString("F3"));
                        GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.MeanDistBetResets, list_MDbR_perEpisode.Average().ToString("F3"));
                        GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.MeanDistBetResets, getStandardDeviation(list_MDbR_perEpisode).ToString("F3"));
                        GM_DataRecord.instance.Save_SteamingData_Batch();
                    }

                    list_usersTotalReset_perEpisode.Clear();
                    list_userbetReset_perEpisode.Clear();
                    list_usersShutterReset_perEpisode.Clear();
                    list_MDbR_perEpisode.Clear();
                }
            }
        }

        private void FixedUpdate()
        {
            UpdateVelocityTracker();
            ProcessStep();
        }

        // Heuristic removed
        // public override void Heuristic(in ActionBuffers actionsOut) {}

        /// <summary>
        /// Initialize Dictionary for spatial information
        /// </summary>
        private void InitializeInfoDics()
        {
            edgeMaxcount = (totalUserCount * (totalUserCount - 1)) / 2;

            for (int i = 0; i < 100; i++)
            {
                list_VoronoiVertexMarker.Add(Instantiate(prefab_VoronoiVertex, new Vector3(-50.0f, 0.0f, 0.0f), Quaternion.identity));
                list_VoronoiVertexMarker[i].SetActive(false);
                list_VoronoiVertexMarker[i].name = "VoronoiVertexMarker " + i;
                list_VoronoiVertexMarker[i].hideFlags = HideFlags.HideInHierarchy;
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                list_seedPointVisual.Add(Instantiate(prefab_VoronoiSeedPoint, new Vector3(-50.0f, 0.0f, 0.0f), Quaternion.identity));
                list_seedPointVisual[i].name = "seedpointView " + i;
                //list_seedPointVisual[i].SetActive(false);
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                List<Vector2> list = new List<Vector2>();
                dic_AreaSegmentsVertex.Add(i, list);
            }
        }

        /// <summary>
        /// Initialize list for spatial information
        /// </summary>
        /// <param name="_initUserPhyiscalPos"></param>
        private void InitializeInfoLists(Vector2 _initUserPhyiscalPos)
        {
            list_UsersCumulativeDist.Clear();
            list_UsersCurrentPhysicalPos.Clear();
            list_UsersPrePhysicalPos.Clear();
            
            // Initialization for Voronoi Seed Points
            list_VoronoiSeedPoint.Clear();
            
            for (int i = 0; i < totalUserCount; i++)
            {
                list_UsersCumulativeDist.Add(0.0f);
                list_UsersCurrentPhysicalPos.Add(_initUserPhyiscalPos);
                list_UsersPrePhysicalPos.Add(_initUserPhyiscalPos);

                // Initialize Seed Point at center (or _initUserPhyiscalPos)
                list_VoronoiSeedPoint.Add(new Vector2f(_initUserPhyiscalPos.x, _initUserPhyiscalPos.y));
            }
        }

        /// <summary>
        /// Reset the parameters
        /// </summary>
        public void SetResetParameters()
        {
            RDWSimulationManager.instance.BStart = false;
            RDWSimulationManager.instance.StartSimulation();

            physicalRoom_width_half = Mathf.Abs(RDWSimulationManager.instance.simulationSetting.realSpaceSetting.spaceObjectSetting.vertices[0].x);
            physicalRoom_height_half = Mathf.Abs(RDWSimulationManager.instance.simulationSetting.realSpaceSetting.spaceObjectSetting.vertices[0].y);

            virtualRoom_width_half = Mathf.Abs(RDWSimulationManager.instance.simulationSetting.virtualSpaceSetting.spaceObjectSetting.vertices[0].x);
            virtualRoom_height_Half = Mathf.Abs(RDWSimulationManager.instance.simulationSetting.virtualSpaceSetting.spaceObjectSetting.vertices[0].y);

            actual_halfRoomsize_default = physicalRoom_height_half * 2 * physicalRoom_width_half;
            actual_roomhypotenuse = Mathf.Sqrt(Mathf.Pow(physicalRoom_width_half, 2) + Mathf.Pow(physicalRoom_height_half, 2));

            /// refresh pointers for physical user 
            // 清理并重新获取用户列表
            if (list_physical_simulatedUsers == null) list_physical_simulatedUsers = new List<GameObject>();
            list_physical_simulatedUsers.Clear();
            
            if (list_virtual_simulatedUsers == null) list_virtual_simulatedUsers = new List<GameObject>();
            list_virtual_simulatedUsers.Clear();

            // 从 RDWSimulationManager 获取用户对象，不再依赖 Tag 查找
            var manager = RDWSimulationManager.instance;
            if (manager != null && manager.GetRedirectedUnits != null)
            {
                var units = manager.GetRedirectedUnits;
                for (int i = 0; i < totalUserCount; i++)
                {
                    if (i < units.Length && units[i] != null)
                    {
                        // 获取物理用户
                        if (units[i].realUser != null)
                        {
                            list_physical_simulatedUsers.Add(units[i].realUser.gameObject);
                        }
                        
                        // 获取虚拟用户
                        if (units[i].virtualUser != null)
                        {
                            GameObject viruser = units[i].virtualUser.gameObject;
                            // 禁用Collider逻辑保留
                            var collider = viruser.GetComponent<CapsuleCollider>();
                            if (collider != null) collider.enabled = false;
                            list_virtual_simulatedUsers.Add(viruser);
                        }
                    }
                }
            }

            // 【新增】每回合重置时，重新初始化速度追踪器的位置点
            if (_userLastPos != null && _userLastPos.Length == totalUserCount)
            {
                for (int i = 0; i < totalUserCount; i++)
                {
                    // 安全检查：只有当列表长度足够且对象非空时才读取位置
                    if (i < list_physical_simulatedUsers.Count && list_physical_simulatedUsers[i] != null)
                    {
                        _userLastPos[i] = list_physical_simulatedUsers[i].transform.position;
                    }
                    else
                    {
                        // 如果找不到用户，此时保持原值或设为0 (视具体需求，这里设为0防止错误跳变)
                        _userLastPos[i] = Vector3.zero;
                    }
                    
                    // 重置相关状态
                    _userSmoothV1[i] = Vector3.zero;
                    _userSmoothV2[i] = Vector3.zero;
                    _userFrozenOffset[i] = Vector3.zero;
                    _userCurrentOffset[i] = Vector3.zero;
                    _userIsStopped[i] = false;
                }
                _velocityInit = true;
            }

            //Debug.Log("A");

            if (bEnable_InitPhyUserPosUni && currentSimulationCount == 0)
            {
                list_VoronoiSeedPoint_Fixed.Clear();

                for (int i = 0; i < totalUserCount; i++)
                {
                    list_VoronoiSeedPoint_Fixed.Add(new Vector2f(Random.Range(-physicalRoom_width_half, physicalRoom_width_half), Random.Range(-physicalRoom_height_half, physicalRoom_height_half)));
                }
                Rectf bounds = new Rectf(-physicalRoom_width_half, -physicalRoom_height_half, physicalRoom_width_half * 2, physicalRoom_height_half * 2);

                //Debug.Log("B:" + list_VoronoiSeedPoint_Fixed.Count);

                if (voronoi != null)
                {
                    voronoi.Dispose();
                }
                voronoi = new Voronoi(list_VoronoiSeedPoint_Fixed, bounds, 5000);
                sites = voronoi.SitesIndexedByLocation;
                int count = 0;
                foreach (KeyValuePair<Vector2f, Site> kv in sites)
                {
                    list_VoronoiSeedPoint_Fixed[count++] = new Vector2f(kv.Key.x, kv.Key.y);
                }
                //Debug.Log("C:" + list_VoronoiSeedPoint_Fixed.Count);

                List<Vector2f> centroids = new List<Vector2f>();

                SiteList sites_forcentronoid = new SiteList();
                centroids = voronoi.GetCentroid_site(totalUserCount, ref list_voronoiArea, ref sites_forcentronoid);
                int targetIndex = 0;
                List<Vector2f> list_VoronoiSeedPoint_Fixed_temp = new List<Vector2f>();
                //Debug.Log("D:" + list_VoronoiSeedPoint_Fixed.Count);

                for (int i = 0; i < totalUserCount; i++)
                {
                    for (int j = 0; j < totalUserCount; j++)
                    {
                        if (Mathf.Abs(list_VoronoiSeedPoint_Fixed[i].x - sites_forcentronoid.GetSite_byIndex(j).x) < eps
                            && Mathf.Abs(list_VoronoiSeedPoint_Fixed[i].y - sites_forcentronoid.GetSite_byIndex(j).y) < eps)
                        {
                            targetIndex = j;
                        }
                        else
                        {
                            continue;
                        }

                        list_VoronoiSeedPoint_Fixed_temp.Add(list_VoronoiSeedPoint_Fixed[targetIndex]);

                    }
                }
                //Debug.Log("E:" + list_VoronoiSeedPoint_Fixed.Count);
                list_VoronoiSeedPoint_Fixed = list_VoronoiSeedPoint_Fixed_temp;
            }

            //Debug.Log("F:" + list_VoronoiSeedPoint_Fixed.Count);

            if (bEnable_InitPhyUserPosUni)
            {
                //Debug.Log("G:" + list_physical_simulatedUsers.Count);

                for (int i = 0; i < totalUserCount; i++)
                {
                    list_physical_simulatedUsers[i].transform.position = new Vector3(list_VoronoiSeedPoint_Fixed[i].x, 0.0f, list_VoronoiSeedPoint_Fixed[i].y);
                }

                if(totalUserCount == 4)
                {
                    for (int i = 0; i < totalUserCount; i++)
                    {
                        list_physical_simulatedUsers[i].transform.Translate(Vector3.forward * Random.Range(-0.001f, 0.001f));
                    }
                }
            }


            InitializeInfoLists(Vector2.zero);

            // [FIX] Update pre-positions to actual user positions to avoid initial distance jump
            if (list_physical_simulatedUsers != null)
            {
                for(int i=0; i<totalUserCount; i++)
                {
                    if (i < list_physical_simulatedUsers.Count && list_physical_simulatedUsers[i] != null)
                    {
                        Vector2 currentPos = new Vector2(list_physical_simulatedUsers[i].transform.position.x, list_physical_simulatedUsers[i].transform.position.z);
                        if(i < list_UsersPrePhysicalPos.Count)
                        {
                            list_UsersPrePhysicalPos[i] = currentPos;
                            list_UsersCurrentPhysicalPos[i] = currentPos;
                        }
                    }
                }
            }
            currentEpisodeTotalDistance = 0.0f;

            InitializeInfoQueues();
        }

        /// </summary>
        public void InitializeInfoQueues()
        {
        }

        private void CalculateCircleTangentPoint(out Vector3 P1, Vector3 circleCenterlPoint, Vector3 externalPoint, float radius)
        {
            float distanceBetP_C = Mathf.Sqrt(Mathf.Pow(externalPoint.x - circleCenterlPoint.x, 2) + Mathf.Pow(externalPoint.z - circleCenterlPoint.z, 2));
            float theta = Mathf.Acos(radius / distanceBetP_C);
            float d = Mathf.Atan2(externalPoint.z - circleCenterlPoint.z, externalPoint.x - circleCenterlPoint.x);
            float d1 = d + theta;
            float d2 = d - theta;
            Vector3 T1 = new Vector3(circleCenterlPoint.x + radius * Mathf.Cos(d1), 0.0f, circleCenterlPoint.z + radius * Mathf.Sin(d1));
            Vector3 T2 = new Vector3(circleCenterlPoint.x + radius * Mathf.Cos(d2), 0.0f, circleCenterlPoint.z + radius * Mathf.Sin(d2));

            P1 = T1;
        }

        private bool LineLineIntersection(out Vector3 intersection, Vector3 linePoint1, Vector3 lineVec1, Vector3 linePoint2, Vector3 lineVec2)
        {
            Vector3 lineVec3 = linePoint2 - linePoint1;
            Vector3 crossVec1and2 = Vector3.Cross(lineVec1, lineVec2);
            Vector3 crossVec3and2 = Vector3.Cross(lineVec3, lineVec2);

            float planarFactor = Vector3.Dot(lineVec3, crossVec1and2);

            //is coplanar, and not parallel
            if (Mathf.Abs(planarFactor) < 0.0001f
                    && crossVec1and2.sqrMagnitude > 0.0001f)
            {
                float s = Vector3.Dot(crossVec3and2, crossVec1and2) / crossVec1and2.sqrMagnitude;
                intersection = linePoint1 + (lineVec1 * s);
                return true;
            }
            else
            {
                intersection = Vector3.zero;
                return false;
            }
        }

        private float GetSignedAngle(Vector3 vStart, Vector3 vEnd)
        {
            Vector3 v = vEnd - vStart;

            return Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        }

        private Vector3 RotateAroundSpecialAxis(Vector3 position, Vector3 center, Vector3 axis, float angle)
        {
            Vector3 point = Quaternion.AngleAxis(angle, axis) * (position - center);
            Vector3 resultVec3 = center + point;

            return resultVec3;
        }

        public static Vector2 rotateVec2D(Vector2 v, float delta)
        {
            return new Vector2(
                v.x * Mathf.Cos(delta) - v.y * Mathf.Sin(delta),
                v.x * Mathf.Sin(delta) + v.y * Mathf.Cos(delta)
            );
        }

        private float getStandardDeviation(List<float> floatList)
        {
            float average = floatList.Average();
            float sumOfDerivation = 0;
            foreach (float value in floatList)
            {
                sumOfDerivation += (value) * (value);
            }
            float sumOfDerivationAverage = sumOfDerivation / floatList.Count;
            return Mathf.Sqrt(sumOfDerivationAverage - (average * average));
        }

        private double getStandardDeviation(List<int> floatList)
        {
            double average = floatList.Average();
            int sumOfDerivation = 0;
            foreach (int value in floatList)
            {
                sumOfDerivation += (value) * (value);
            }
            int sumOfDerivationAverage = sumOfDerivation / floatList.Count;
            return Mathf.Sqrt((float)(sumOfDerivationAverage - (average * average)));
        }
    }
}