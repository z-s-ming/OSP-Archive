using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Random = UnityEngine.Random;
using UnityEngine.UI;
using System.Text;
using System.Linq;
using JSB.RDW.Voronoi;

namespace _OSP
{
    /// <summary>
    /// OSP Agent using Weighted Voronoi Partitioning
    /// 使用加权Voronoi分区的OSP代理
    /// 
    /// 核心功能：
    /// 1. 初始化：创建物理/虚拟用户、种子点、快门等对象池
    /// 2. 空间分区：使用加权Voronoi算法将物理空间动态分割为多个子区域
    /// 3. 观察收集：收集用户位置、方向、距离等状态信息用于ML决策
    /// 4. 动作执行：根据AI决策调整种子点位置，改变空间分区边界
    /// 5. 奖励计算：基于重置次数、用户距离等多维指标计算奖励
    /// 6. 重定向模拟：运行RDW(重定向行走)模拟，评估分区质量
    /// 
    /// 关键概念：
    /// - Seed Point：种子点，代表每个用户对应的Voronoi分区中心
    /// - Voronoi Partition：网格化的分区数组，记录每个网格单元属于哪个用户区域
    /// - Virtual Shutter：虚拟快门，位于相邻区域边界处的碰撞体，用于模拟物理隔挡
    /// - Weighted Voronoi：考虑用户权重的Voronoi分区，权重由方向决定
    /// </summary>
    public class OSP_Agent_Weighted : Agent
    {
        /// <summary>
        /// for singleton pattern
        /// </summary>
        public static OSP_Agent_Weighted instance = null;

        /// <summary>
        /// enable to input state information
        /// </summary>
        public bool bUseVecOberv;

        /// <summary>
        /// mlagent Academic envParams
        /// </summary>
        EnvironmentParameters m_ResetParams;

        /// <summary>
        /// List for pointing each actual user object
        /// </summary>        
        private List<GameObject> list_physical_simulatedUsers = new List<GameObject>();

        /// <summary>
        /// List for pointing each virtual user object
        /// </summary>        
        private List<GameObject> list_virtual_simulatedUsers = new List<GameObject>();
 
        private float prev_wallReset_mean = 0;
        private float prev_userReset_mean = 0;
        private float prev_shutterReset_mean = 0;

        /// <summary>
        /// Text for checking the current episode count
        /// </summary>        
        [SerializeField]
        private Text text_currentEpi;
        /// <summary>
        /// Text for checking step count of current episode 
        /// </summary>    
        [SerializeField]
        private Text text_currentStep;
        /// <summary>
        /// Text for checking cumulative reward of current episode 
        /// </summary>    
        [SerializeField]
        private Text text_currentReward;

        /// <summary>
        /// Dictionary for pointing a queue of the position X of each physical user
        /// (Divided 2D vector separately to use X values as a window)
        /// </summary>
        Dictionary<int, Queue<float>> dic_physicalUsers_pos_X = new Dictionary<int, Queue<float>>();
        /// <summary>
        /// Dictionary for pointing a queue of the position Z of each physical user
        /// (Divided 2D vector separately to use Z values as a window)
        /// </summary>
        Dictionary<int, Queue<float>> dic_physicalUsers_pos_Z = new Dictionary<int, Queue<float>>();
        /// <summary>
        /// Dictionary for pointing a queue of the 1D orientation of each physical user
        /// </summary>
        Dictionary<int, Queue<float>> dic_physicalUsers_orient_Y = new Dictionary<int, Queue<float>>();
        /// <summary>
        /// Dictionary for pointing a queue of the position X of each virtual user
        /// (Divided 2D vector separately to use X values as a window)
        /// </summary>
        Dictionary<int, Queue<float>> dic_virtualUsers_pos_X = new Dictionary<int, Queue<float>>();
        /// <summary>
        /// Dictionary for pointing a queue of the position Z of each virtual user
        /// (Divided 2D vector separately to use Z values as a window)
        /// </summary>
        Dictionary<int, Queue<float>> dic_VirtualUsers_Position_Z = new Dictionary<int, Queue<float>>();
        /// <summary>
        /// Dictionary for pointing a queue of the 1D orientation of each virtual user
        /// </summary>
        Dictionary<int, Queue<float>> dic_VirtualUsers_Orient_Y = new Dictionary<int, Queue<float>>();

        /// <summary>
        /// Dictionary for pointing a queue of the 1D orientation of each physical user
        /// (Divided 8D vector separately to use values of each direction as a window)
        /// </summary>
        private Dictionary<int, Dictionary<int, Queue<float>>> doubleDic_physicalUsers_8wayWallDist = new Dictionary<int, Dictionary<int, Queue<float>>>();
        /// <summary>
        /// Dictionary for pointing a queue of the 1D orientation of each virtual user
        /// (Divided 8D vector separately to use values of each direction as a window)
        /// </summary>
        private Dictionary<int, Dictionary<int, Queue<float>>> doubleDic_virtualUsers_8wayWallDist = new Dictionary<int, Dictionary<int, Queue<float>>>();

        /// <summary>
        /// Dictionary for pointing a queue of area value of sub-space for each physical user
        /// </summary>
        Dictionary<int, Queue<float>> dic_physicalRoomSize_users = new Dictionary<int, Queue<float>>();


        /// <summary>
        /// 
        /// </summary>
        private float physicalRoom_width_half = 0;
        /// <summary>
        /// 
        /// </summary>
        private float physicalRoom_height_half = 0;
        /// <summary>
        /// 
        /// </summary>
        private float virtualRoom_width_half = 0;
        /// <summary>
        /// 
        /// </summary>
        private float virtualRoom_height_Half = 0;
        /// <summary>
        /// 
        /// </summary>
        private float actual_halfRoomsize_default = 36;
        /// <summary>
        /// 
        /// </summary>
        private float actual_roomhypotenuse = 0.0f;

        /// <summary>
        /// 
        /// </summary>
        private int currentSimulationCount = 0;
        /// <summary>
        /// 
        /// </summary>
        public int SimulationCount_max = 100;

        /// <summary>
        /// 
        /// </summary>
        List<int> list_usersTotalReset_perEpisode = new List<int>();
        /// <summary>
        /// 
        /// </summary>
        List<int> list_userbetReset_perEpisode = new List<int>();
        /// <summary>
        /// 
        /// </summary>
        List<int> list_usersShutterReset_perEpisode = new List<int>();

        /// <summary>
        /// 
        /// </summary>
        private RaycastHit rayCastHit;

        /// <summary>
        /// Weighted Voronoi space partition (grid-based)
        /// </summary>
        private int[,] weightedVoronoiPartition;

        /// <summary>
        /// Region metrics computed from weighted Voronoi partition
        /// </summary>
        private List<WeightedVoronoiUtility.RegionMetrics> voronoiRegionMetrics;

        /// <summary>
        /// 
        /// </summary>
        List<WeightedVoronoiGrid.SeedPoint> list_VoronoiSeedPoint = new List<WeightedVoronoiGrid.SeedPoint>();

        /// <summary>
        /// 
        /// </summary>
        List<GameObject> list_VoronoiVertexMarker = new List<GameObject>();
        /// <summary>
        /// 
        /// </summary>
        List<Vector2> list_WayPoint_vertices = new List<Vector2>();
        /// <summary>
        /// 
        /// </summary>
        List<Vector2> list_voronoiCentroid = new List<Vector2>();
        /// <summary>
        /// 
        /// </summary>
        List<float> list_voronoiArea = new List<float>();

        /// <summary>
        /// for Uniform Partitioning Simulation (with Lloyd Relaxation)
        /// </summary>
        public bool bEnable_InitPhyUserPosUni = false;

        /// <summary>
        /// Preserve initial Voronoi Diagram 
        /// </summary>
        private bool bLock_VoronoiDiagram = false;

        /// <summary>
        /// voronoi vertex
        /// </summary>
        [SerializeField]
        private GameObject prefab_VoronoiVertex;

        /// <summary>
        /// seed
        /// </summary>
        [SerializeField]
        private GameObject prefab_VoronoiSeedPoint;

        /// <summary>
        /// seed
        /// </summary>
        List<GameObject> list_seedPointVisual = new List<GameObject>();

        /// <summary>
        /// AI动作对种子点的有限偏移（每个用户对应一个偏移向量）
        /// 由模型/策略输出，大小受CalcStableAreaRadius()限制
        /// </summary>
        private List<Vector2> list_currentActionOffset = new List<Vector2>();

        /// <summary>
        /// 
        /// </summary>
        private List<GameObject> list_VirtualSutterDelegate = new List<GameObject>();

        /// <summary>
        /// 
        /// </summary>
        public int totalUserCount = 2;

        /// <summary>
        /// 
        /// </summary>
        [SerializeField]
        private float userRadius = 0.5f;

        /// <summary>
        /// 
        /// </summary>
        [SerializeField]
        private float shutterWidth = 0.5f;

        /// <summary>
        /// Grid cell size for weighted Voronoi (meters)
        /// </summary>
        [SerializeField]
        private float gridCellSize = 0.1f;

        /// <summary>
        /// 
        /// </summary>
        public bool bMixedExploration = true;

        /// <summary>
        /// 
        /// </summary>
        public Enum_CurriculumState currnet_CurriculumState = Enum_CurriculumState._1stQuater;

        /// <summary>
        /// 
        /// </summary>
        private List<Vector2> list_UsersPrePhysicalPos = new List<Vector2>();
        /// <summary>
        /// 
        /// </summary>
        private List<Vector2> list_UsersCurrentPhysicalPos = new List<Vector2>();
        /// <summary>
        /// 
        /// </summary>
        private List<float> list_UsersCumulativeDist = new List<float>();
        /// <summary>
        /// 
        /// </summary>
        private List<float> users_wallReset_pre = new List<float>();
        /// <summary>
        /// 
        /// </summary>
        private List<float> users_userReset_pre = new List<float>();
        /// <summary>
        /// 
        /// </summary>
        private List<float> users_shutterReset_pre = new List<float>();
        /// <summary>
        /// 
        /// </summary>
        private List<float> UsersCumulative_MDbR_sqeuence = new List<float>();
        /// <summary>
        /// 
        /// </summary>
        private List<float> UsersCumulative_MDbR_SimulationCount_max = new List<float>();

        /// <summary>
        /// 
        /// </summary>
        [SerializeField]
        private List<float> list_rewardWeight = new List<float>();

        /// <summary>
        /// 
        /// </summary>
        [SerializeField]
        private GameObject S2C_CenterPointerObject_Prefab;
        /// <summary>
        /// 
        /// </summary>
        List<GameObject> list_S2C_CenterPointer = new List<GameObject>();

        /// <summary>
        /// 
        /// </summary>
        public Dictionary<int, List<Vector2>> dic_AreaSegmentsVertex = new Dictionary<int, List<Vector2>>();

        /// <summary>
        /// Partition space materials
        /// </summary>
        public List<Material> PartitionedSpaceMaterials = new List<Material>();

        /// <summary>
        /// Renderer for displaying Voronoi texture
        /// </summary>
        [SerializeField]
        private Renderer voronoiRenderer;

        /// <summary>
        /// Color palette for Voronoi visualization
        /// </summary>
        [SerializeField]
        private Color[] voronoiPalette = new Color[]
        {
            new Color(0.93f, 0.49f, 0.19f),
            new Color(0.25f, 0.76f, 0.47f),
            new Color(0.22f, 0.55f, 0.91f),
            new Color(0.96f, 0.82f, 0.26f),
        };

        /// <summary>
        /// 
        /// </summary>
        private int edgeMaxcount = 45;

        /// <summary>
        /// 
        /// </summary>
        private int sptialInfo_windowSize = 450;

        /// <summary>
        /// epsilon for floating point error
        /// </summary>
        private float eps = 0.001f;

        private bool bOneframetimerblockVoronoi = false;


        //------------------------

        private void Awake()
        {
            instance = this;

            //Academy.Instance.AutomaticSteppingEnabled = false;
        }

        private void Start()
        {
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
                bOneframetimerblockVoronoi = true;
            }
        }

        /// <summary>
        /// ML-agent Framework API 
        /// </summary>
        public override void Initialize()
        {
            m_ResetParams = Academy.Instance.EnvironmentParameters;
            //Academy.Instance.AutomaticSteppingEnabled = false;
        }

        /// <summary>
        /// ML-agent Framework API 
        /// </summary>
        public override void OnEpisodeBegin()
        {
            SetResetParameters();
        }

        /// <summary>
        /// ML-agent Framework API
        /// 每步收集观察信息，用于训练神经网络
        /// 
        /// 观察内容：
        /// 1. 物理用户状态(位置XZ、方向Y、到障碍距离8方向、分配空间面积)
        /// 2. 虚拟用户状态(位置XZ、方向Y、到障碍距离8方向)
        /// 3. 种子点被动更新(由物理用户位置驱动)
        /// </summary>
        public override void CollectObservations(VectorSensor sensor)
        {
            if (!RDWSimulationManager.instance.BStart)
                return;

            if (bUseVecOberv)
            {
                /// dummy contatiners
                Queue<float> queue_data = new Queue<float>();
                Dictionary<int, Queue<float>> dic_data = new Dictionary<int, Queue<float>>();

                for (int i = 0; i < totalUserCount; i++)
                {
                    /// physical user pos X
                    dic_physicalUsers_pos_X.TryGetValue(i, out queue_data);
                    float[] array_physicalUser_pos_X = queue_data.ToArray();
                    sensor.AddObservation(array_physicalUser_pos_X);

                    /// physical user pos Z
                    dic_physicalUsers_pos_Z.TryGetValue(i, out queue_data);
                    float[] array_physicalUser_pos_Z = queue_data.ToArray();
                    sensor.AddObservation(array_physicalUser_pos_Z);

                    /// physical user orient Y
                    dic_physicalUsers_orient_Y.TryGetValue(i, out queue_data);
                    float[] array_physicalUser_orient_Y = queue_data.ToArray();
                    sensor.AddObservation(array_physicalUser_orient_Y);

                    /// 8-way distances from physical walls/obstacles
                    doubleDic_physicalUsers_8wayWallDist.TryGetValue(i, out dic_data);
                    for (int j = 0; j < 8; j++)
                    {
                        dic_data.TryGetValue(j, out queue_data);
                        float[] array_data = queue_data.ToArray();
                        sensor.AddObservation(array_data);
                    }

                    /// physical sub-space room size
                    dic_physicalRoomSize_users.TryGetValue(i, out queue_data);
                    float[] array_RoomSize = queue_data.ToArray();
                    sensor.AddObservation(array_RoomSize);

                    /// virtual user pos X
                    dic_virtualUsers_pos_X.TryGetValue(i, out queue_data);
                    float[] array_virtualUser_pos_X = queue_data.ToArray();
                    sensor.AddObservation(array_virtualUser_pos_X);

                    /// virtual user pos Z
                    dic_VirtualUsers_Position_Z.TryGetValue(i, out queue_data);
                    float[] array_virtualUser_pos_Z = queue_data.ToArray();
                    sensor.AddObservation(array_virtualUser_pos_Z);

                    /// virtual user orient Y
                    dic_VirtualUsers_Orient_Y.TryGetValue(i, out queue_data);
                    float[] array_virtualUser_orient_Y = queue_data.ToArray();
                    sensor.AddObservation(array_virtualUser_orient_Y);

                    /// 8-way distances from virtual walls/obstacles
                    doubleDic_virtualUsers_8wayWallDist.TryGetValue(i, out dic_data);
                    for (int j = 0; j < 8; j++)
                    {
                        dic_data.TryGetValue(j, out queue_data);
                        float[] array_data = queue_data.ToArray();
                        sensor.AddObservation(array_data);
                    }
                }
            }

            /// Update seed points with user positions + AI model offset
            UpdateSeedPointsWithOffset();
            /// Update Voronoi Diagram based on seed points (which include model offset)
            UpdateVoronoiDiagram();

            /// Display seed points
            for (int i = 0; i < totalUserCount; i++)
            {
                list_seedPointVisual[i].transform.position = new Vector3(list_VoronoiSeedPoint[i].position.x, 0.0f, list_VoronoiSeedPoint[i].position.y);
            }


        }

        /// <summary>
        /// ML-agent Framework API
        /// 执行AI决策动作，更新系统状态
        /// 
        /// 关键改变 - RDW驱动的用户移动流程：
        /// 1. 检查Episode是否结束，若结束则初始化障碍信息并计算结果
        /// 2. 执行RDW重定向模拟(根据Voronoi快门，逐步移动用户)
        ///    - 用户根据物理约束逐帧移动，不是瞬移
        /// 3. 计算当前步的奖励(基于实际移动距离)
        /// 4. 更新空间信息队列(位置、距离等)
        /// 
        /// 种子点和Voronoi更新延迟到下一帧CollectObservations()：
        /// - 在CollectObservations()中调用UpdateSeedPoints()被动跟踪用户新位置
        /// - 然后调用UpdateVoronoiDiagram()基于新位置重新分割空间
        /// - 这确保Voronoi边界和虚拟快门始终反映用户的实际位置
        /// </summary>
        public override void OnActionReceived(ActionBuffers actionBuffers)
        {
            if (!RDWSimulationManager.instance.BStart)
                return;


            /// Init Obstacle mesh info for next episode
            if (StepCount == MaxStep)
            {
                for (int i = 0; i < edgeMaxcount; i++)
                {
                    RDWSimulationManager.instance.InitObstacleInfo(list_VirtualSutterDelegate[i].transform);
                }

                CalcResultPerEpisode();

                return;
            }


            if (!bLock_VoronoiDiagram)
            {
                /// Compute AI action offset to guide Voronoi partition
                /// This offset will be applied to seed points in next CollectObservations
                float allowedRange = CalcStableAreaRadius() - eps;
                int actionValueCount = 0;

                for (int i = 0; i < totalUserCount; i++)
                {
                    /// Parse continuous actions: distance and direction
                    var actionDistance = Mathf.Clamp(actionBuffers.ContinuousActions[actionValueCount++], 0.0f, 1.0f) * allowedRange;
                    var actionDirection = Mathf.Clamp(actionBuffers.ContinuousActions[actionValueCount++], 0.0f, 1.0f) * 360.0f;

                    /// Calculate the offset vector (limited by allowedRange)
                    /// This offset will push the Voronoi partition to reduce resets
                    list_currentActionOffset[i] = rotateVec2D(Vector2.right, actionDirection) * actionDistance;
                    
                    /// Store direction in seed point for weighted Voronoi calculation
                    list_VoronoiSeedPoint[i].direction = actionDirection;
                }
            }

            if (bEnable_InitPhyUserPosUni && !bLock_VoronoiDiagram)
            {
                bLock_VoronoiDiagram = true;
            }

            if (bOneframetimerblockVoronoi)
            {
                bLock_VoronoiDiagram = true;
            }


            /// Simulate the designated redirection controller
            /// Users move gradually driven by RDW, not teleport
            RDWSimulationManager.instance.SimulateRDW();

            /// Add rewards for current action
            AddRewards();

            /// Update sptial Information
            UpdateSptialInfo();
        }

        /// <summary>
        /// Update physical and virtual sptial Information
        /// </summary>
        private void UpdateSptialInfo()
        {
            Queue<float> queue_data = new Queue<float>();
            Dictionary<int, Queue<float>> dic_data = new Dictionary<int, Queue<float>>();
            Vector3 orient;

            for (int i = 0; i < totalUserCount; i++)
            {
                /// physical user pos X
                dic_physicalUsers_pos_X.TryGetValue(i, out queue_data);
                queue_data.Enqueue(list_physical_simulatedUsers[i].transform.position.x / physicalRoom_width_half);
                queue_data.Dequeue();

                /// physical user pos Z
                dic_physicalUsers_pos_Z.TryGetValue(i, out queue_data);
                queue_data.Enqueue(list_physical_simulatedUsers[i].transform.position.z / physicalRoom_height_half);
                queue_data.Dequeue();

                /// physical user orientation Y
                orient = list_physical_simulatedUsers[i].transform.rotation.eulerAngles;
                dic_physicalUsers_orient_Y.TryGetValue(i, out queue_data);
                queue_data.Enqueue(orient.y);
                queue_data.Dequeue();

                /// 8-way distances from physical walls/obstacles
                doubleDic_physicalUsers_8wayWallDist.TryGetValue(i, out dic_data);
                Calc_8Way_Distances(list_physical_simulatedUsers[i].transform, ref dic_data, true);

                /// physical sub-space room size
                dic_physicalRoomSize_users.TryGetValue(i, out queue_data);
                Enqueue_RoomSize(list_voronoiArea[i], ref queue_data);

                /// virtual user pos X
                dic_virtualUsers_pos_X.TryGetValue(i, out queue_data);
                queue_data.Enqueue(list_virtual_simulatedUsers[i].transform.position.x / virtualRoom_width_half);
                queue_data.Dequeue();

                /// virtual user pos Z
                dic_VirtualUsers_Position_Z.TryGetValue(i, out queue_data);
                queue_data.Enqueue(list_virtual_simulatedUsers[i].transform.position.z / virtualRoom_height_Half);
                queue_data.Dequeue();

                /// virtual user orientation Y
                orient = list_virtual_simulatedUsers[i].transform.rotation.eulerAngles;
                dic_VirtualUsers_Orient_Y.TryGetValue(i, out queue_data);
                queue_data.Enqueue(orient.y);
                queue_data.Dequeue();

                /// 8-way distances from virtual walls/obstacles
                doubleDic_virtualUsers_8wayWallDist.TryGetValue(i, out dic_data);
                Calc_8Way_Distances(list_virtual_simulatedUsers[i].transform, ref dic_data, false);
            }
        }

        /// <summary>
        /// 添加奖励
        /// 
        /// 奖励构成：
        /// 1. 基础奖励：+10 (每步都有)
        /// 2. 距离奖励：用户移动距离 * rewardWeight[0] (鼓励运动)
        /// 3. 墙壁重置惩罚：重置增加数 * rewardWeight[1] (避免碰墙)
        /// 4. 快门重置惩罚：重置增加数 * rewardWeight[2] (避免穿过快门)
        /// 5. 公平性惩罚：用户重置差异 * rewardWeight[3] (促进均衡)
        /// </summary>
        private void AddRewards()
        {
            /// basic reward (positive reward)
            AddReward(10.0f);

            /// wall reset count
            List<float> list_currentTotalWallReset = new List<float>();
            for (int i = 0; i < totalUserCount; i++)
            {
                list_currentTotalWallReset.Add(RDWSimulationManager.instance.GetRedirectedUnits[i].resultData.getWallReset());
            }
            float current_wallReset_mean = list_currentTotalWallReset.Average();
            float gap_wallReset = (current_wallReset_mean - prev_wallReset_mean) * totalUserCount;
            prev_wallReset_mean = current_wallReset_mean;

            /// user reset count
            List<float> list_currentTotalUserReset = new List<float>();
            for (int i = 0; i < totalUserCount; i++)
            {
                list_currentTotalUserReset.Add(RDWSimulationManager.instance.GetRedirectedUnits[i].resultData.getUserReset());
            }
            float current_userReset_mean = list_currentTotalUserReset.Average();
            float gap_userReset = (current_userReset_mean - prev_userReset_mean) * totalUserCount;
            prev_userReset_mean = current_userReset_mean;

            /// shutter reset count
            List<float> list_currentTotalSutterReset = new List<float>();
            for (int i = 0; i < totalUserCount; i++)
            {
                list_currentTotalSutterReset.Add(RDWSimulationManager.instance.GetRedirectedUnits[i].resultData.getShutterReset());
            }
            float current_shutterReset_mean = list_currentTotalSutterReset.Average();
            float gap_shutterReset = (current_shutterReset_mean - prev_shutterReset_mean) * totalUserCount;
            prev_shutterReset_mean = current_shutterReset_mean;

            /// one reset occurs = panelty
            if (gap_wallReset > 0)
            {
                AddReward(list_rewardWeight[1] * gap_wallReset);
            }

            /// one reset occurs = panelty
            if (gap_shutterReset > 0)
            {
                AddReward(list_rewardWeight[2] * gap_shutterReset);
            }

            /// positive deviation occurs = panelty
            if (gap_wallReset > 0 || gap_shutterReset > 0)
            {
                // check all users
                for (int i = 0; i < totalUserCount; i++)
                {
                    float totalReset_deviation = (list_currentTotalWallReset[i] + list_currentTotalSutterReset[i]) - (current_wallReset_mean + current_shutterReset_mean);

                    if (totalReset_deviation > 0)
                    {
                        AddReward(list_rewardWeight[3] * totalReset_deviation);
                    }
                }
            }

            /// acculmulate distance
            for (int i = 0; i < totalUserCount; i++)
            {
                list_UsersCurrentPhysicalPos[i] = new Vector2(RDWSimulationManager.instance.GetRedirectedUnits[i].realUser.transform2D.position.x, RDWSimulationManager.instance.GetRedirectedUnits[i].realUser.transform2D.position.y);

                // In simulation environment, the displacement will be zero when the simulated user resets, but not in user study.
                if (!RDWSimulationManager.instance.GetRedirectedUnits[i].IsResetting)
                {
                    float smalldist = (list_UsersCurrentPhysicalPos[i] - list_UsersPrePhysicalPos[i]).magnitude;
                    list_UsersCumulativeDist[i] += smalldist;

                    // per one step , positive Rewards for physical distnace of simulated users 
                    AddReward(list_rewardWeight[0] * smalldist);
                }

                list_UsersPrePhysicalPos[i] = new Vector2(list_UsersCurrentPhysicalPos[i].x, list_UsersCurrentPhysicalPos[i].y);
            }

            /// acculmulate Mean distance between resets (for wall reset)
            for (int i = 0; i < totalUserCount; i++)
            {
                if (list_currentTotalWallReset[i] != users_wallReset_pre[i])
                {
                    float MDbR_wall = list_UsersCumulativeDist[i];
                    UsersCumulative_MDbR_sqeuence.Add(MDbR_wall);
                    list_UsersCumulativeDist[i] = 0;
                }
            }

            /// acculmulate Mean distance between resets (for user reset)
            for (int i = 0; i < totalUserCount; i++)
            {
                if (list_currentTotalUserReset[i] != users_userReset_pre[i])
                {
                    float MDbR_user = list_UsersCumulativeDist[i];

                    UsersCumulative_MDbR_sqeuence.Add(MDbR_user);
                    list_UsersCumulativeDist[i] = 0;
                }
            }

            ///  acculmulate Mean distance between resets (for shutter reset)
            for (int i = 0; i < totalUserCount; i++)
            {
                if (list_currentTotalSutterReset[i] != users_shutterReset_pre[i])
                {
                    float MDbR_shutter = list_UsersCumulativeDist[i];
                    UsersCumulative_MDbR_sqeuence.Add(MDbR_shutter);
                    list_UsersCumulativeDist[i] = 0;
                }
            }

            users_wallReset_pre = new List<float>(list_currentTotalWallReset);
            users_userReset_pre = new List<float>(list_currentTotalUserReset);
            users_shutterReset_pre = new List<float>(list_currentTotalSutterReset);

            text_currentStep.text = "Current Step : " + StepCount;
            text_currentReward.text = "Current Reward : " + GetCumulativeReward();
        }

        /// <summary>
        /// 更新加权Voronoi图
        /// 
        /// 核心步骤：
        /// 1. 根据当前种子点位置生成网格化的Voronoi分区
        /// 2. 计算每个分区的指标(面积、重心、边界分段)
        /// 3. 更新种子点中心指针(S2C重定向中心)
        /// 4. 根据实际边界分段数动态创建/销毁快门碰撞体
        /// 5. 设置虚拟快门委托的位置和碰撞信息
        /// 
        /// 注意：快门数量由实际边界分段数决定(非预设固定值)
        /// </summary>
        private void UpdateVoronoiDiagram()
        {
            // Create physical space rectangle
            Rect physicalRect = new Rect(
                -physicalRoom_width_half,
                -physicalRoom_height_half,
                physicalRoom_width_half * 2,
                physicalRoom_height_half * 2);

            // Generate weighted Voronoi partition
            weightedVoronoiPartition = WeightedVoronoiUtility.GeneratePartition(
                list_VoronoiSeedPoint,
                physicalRect,
                gridCellSize);

            // Compute region metrics (area, centroid, boundary segments)
            voronoiRegionMetrics = WeightedVoronoiUtility.ComputeRegionMetrics(
                weightedVoronoiPartition,
                list_VoronoiSeedPoint,
                physicalRect,
                gridCellSize);

            // Render partition texture for visualization (optional)
            if (voronoiRenderer != null)
            {
                int gridW = weightedVoronoiPartition.GetLength(0);
                int gridH = weightedVoronoiPartition.GetLength(1);

                WeightedVoronoiUtility.AssignColorsToSeeds(
                    list_VoronoiSeedPoint,
                    weightedVoronoiPartition,
                    gridW,
                    gridH,
                    voronoiPalette);

                var tex = WeightedVoronoiUtility.RenderPartitionTexture(
                    weightedVoronoiPartition,
                    gridW,
                    gridH,
                    list_VoronoiSeedPoint,
                    voronoiPalette,
                    Color.gray);

                // Avoid leaking textures: replace material instance texture
                voronoiRenderer.material.mainTexture = tex;

                // Adjust tiling so texture spans the physical room size
                var tiling = new Vector2(
                    physicalRoom_width_half * 2f / gridCellSize / gridW,
                    physicalRoom_height_half * 2f / gridCellSize / gridH);
                voronoiRenderer.material.mainTextureScale = tiling;
            }

            // Update centroid and area information
            list_voronoiCentroid.Clear();
            list_voronoiArea.Clear();

            for (int i = 0; i < voronoiRegionMetrics.Count; i++)
            {
                list_voronoiCentroid.Add(voronoiRegionMetrics[i].centroid);
                list_voronoiArea.Add(voronoiRegionMetrics[i].area);

                // Update S2C center pointers if using vector observations
                if (bUseVecOberv && i < list_S2C_CenterPointer.Count)
                {
                    list_S2C_CenterPointer[i].transform.position = new Vector3(
                        voronoiRegionMetrics[i].centroid.x,
                        0.0f,
                        voronoiRegionMetrics[i].centroid.y);
                    list_S2C_CenterPointer[i].SetActive(true);
                }

                // Update redirector center points if applicable
                if (RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector() is S2CRedirector)
                {
                    ((S2CRedirector)RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector())
                        .SetCenterPoint(new Vector3(
                            voronoiRegionMetrics[i].centroid.x,
                            0.0f,
                            voronoiRegionMetrics[i].centroid.y));
                }

                // Store boundary segments
                List<Vector2> list_AreaSegmentsVertex = new List<Vector2>();
                dic_AreaSegmentsVertex.TryGetValue(i, out list_AreaSegmentsVertex);
                list_AreaSegmentsVertex.Clear();
                
                foreach (var segment in voronoiRegionMetrics[i].boundarySegments)
                {
                    list_AreaSegmentsVertex.Add(segment.start);
                    list_AreaSegmentsVertex.Add(segment.end);
                }
            }

            // Update virtual shutter delegates along Voronoi edges (no 3D colliders)
            int delegateIndex = 0;
            for (int i = 0; i < voronoiRegionMetrics.Count && delegateIndex < list_VirtualSutterDelegate.Count; i++)
            {
                foreach (var segment in voronoiRegionMetrics[i].boundarySegments)
                {
                    if (delegateIndex >= list_VirtualSutterDelegate.Count)
                        break;

                    Vector2 dir = segment.end - segment.start;
                    Vector2 midpoint = (segment.start + segment.end) * 0.5f;

                    // Align delegate transform with edge for downstream obstacle updates
                    list_VirtualSutterDelegate[delegateIndex].transform.forward = new Vector3(dir.x, 0.0f, dir.y);
                    list_VirtualSutterDelegate[delegateIndex].transform.position = new Vector3(midpoint.x, 0.0f, midpoint.y) + Vector3.up * 0.02f;

                    RDWSimulationManager.instance.UpdateObstacleVertexInfo(
                        ref list_WayPoint_vertices,
                        list_VirtualSutterDelegate[delegateIndex].transform,
                        delegateIndex);

                    delegateIndex++;
                }
            }
        }

        /// Calculate the radius of a circular region where Voronoi Seed Points can be located
        private float CalcStableAreaRadius()
        {
            /// min distance of bet. users = calculate stable radius for new seed points
            Dictionary<string, float> dist_dic = new Dictionary<string, float>();
            for (int i = 0; i < totalUserCount; i++)
            {
                Vector3 me = new Vector3(list_VoronoiSeedPoint[i].position.x, 0.0f, list_VoronoiSeedPoint[i].position.y);

                for (int j = i + 1; j < totalUserCount; j++)
                {
                    Vector3 target = new Vector3(list_VoronoiSeedPoint[j].position.x, 0.0f, list_VoronoiSeedPoint[j].position.y);
                    float dist = Vector3.Distance(me, target);
                    string key = i + "_" + j;
                    dist_dic[key] = dist;
                }
            }

            ///calc min dist
            var keyAndValue = dist_dic.OrderBy(kvp => kvp.Value).First();
            float minDistValue = keyAndValue.Value;

            ///calc maximum available radius
            return ((minDistValue - shutterWidth) / 2) - userRadius;
        }

        /// <summary>
        /// Calculate resaults per episode
        /// </summary>
        private void CalcResultPerEpisode()
        {
            if (StepCount == 16200)
            {
                ///wall reset count
                List<int> list_userWallReset = new List<int>();
                for (int i = 0; i < totalUserCount; i++)
                {
                    list_userWallReset.Add((int)(RDWSimulationManager.instance.GetRedirectedUnits[i].resultData.getWallReset()));
                }
                int wallResetSum = list_userWallReset.Sum();

                ///shutter reset count
                List<int> list_userShutterReset = new List<int>();
                for (int i = 0; i < totalUserCount; i++)
                {
                    list_userShutterReset.Add((int)(RDWSimulationManager.instance.GetRedirectedUnits[i].resultData.getShutterReset()));
                }
                int ShutterResetSum = list_userShutterReset.Sum();

                int userbet = RDWSimulationManager.instance.Calc_UserResetFilter();

                list_usersTotalReset_perEpisode.Add(wallResetSum + userbet + ShutterResetSum);
                list_usersShutterReset_perEpisode.Add(ShutterResetSum);
                list_userbetReset_perEpisode.Add(userbet);

                float MDbR_AVG = 0.0f;
                if (UsersCumulative_MDbR_sqeuence.Count > 0)
                {
                    MDbR_AVG = UsersCumulative_MDbR_sqeuence.Average();
                }
                else
                {
                    MDbR_AVG = list_UsersCumulativeDist.Average();
                }

                Debug.LogWarning(string.Format("walllreset {0} / userreset {1} /shutterreset {2} / MDbR AVg. {3}", wallResetSum, userbet, ShutterResetSum, MDbR_AVG));

                UsersCumulative_MDbR_SimulationCount_max.Add(MDbR_AVG);

                StringBuilder sb = new StringBuilder();

                sb.Append(',');
                sb.Append(',');
                sb.Append(wallResetSum).Append(',');
                sb.Append(wallResetSum + ShutterResetSum + userbet).Append(',');
                sb.Append(userbet).Append(',');
                sb.Append(ShutterResetSum).Append(',');
                sb.Append(MDbR_AVG).Append(',');

                if (sb.Length > 0 && sb[sb.Length - 1] == ',')
                {
                    sb.Remove(sb.Length - 1, 1);
                }

                GM_DataRecord.instance.Enequeue_Data(sb.ToString());

                currentSimulationCount++;

                text_currentEpi.text = "Current Episode : " + (currentSimulationCount + 1);

                if(currentSimulationCount + 1  == 2500)
                {
                    currnet_CurriculumState = Enum_CurriculumState._2ndQuater;
                }
                else if (currentSimulationCount + 1 == 5000)
                {
                    currnet_CurriculumState = Enum_CurriculumState._3rdQuater;
                }
                else if (currentSimulationCount + 1 == 7500)
                {
                    currnet_CurriculumState = Enum_CurriculumState._4thQuater;
                }

                if (currentSimulationCount == SimulationCount_max)
                {
                    currentSimulationCount = 0;
                    GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.TotalResetMean, list_usersTotalReset_perEpisode.Average().ToString("F3"));
                    GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.TotalResetMean, getStandardDeviation(list_usersTotalReset_perEpisode).ToString("F3"));
                    GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UserbetResetMean, list_userbetReset_perEpisode.Average().ToString("F3"));
                    GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UserbetResetMean, getStandardDeviation(list_userbetReset_perEpisode).ToString("F3"));
                    GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UsershutterResetMean, list_usersShutterReset_perEpisode.Average().ToString("F3"));
                    GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.UsershutterResetMean, getStandardDeviation(list_usersShutterReset_perEpisode).ToString("F3"));
                    GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.MeanDistBetResets, UsersCumulative_MDbR_SimulationCount_max.Average().ToString("F3"));
                    GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.MeanDistBetResets, getStandardDeviation(UsersCumulative_MDbR_SimulationCount_max).ToString("F3"));
                    GM_DataRecord.instance.Save_SteamingData_Batch();

                    UsersCumulative_MDbR_SimulationCount_max.Clear();
                }
            }
        }

        private void FixedUpdate()
        {

        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {

        }

        /// <summary>
        /// 初始化数据结构字典
        /// 
        /// 创建内容：
        /// 1. 快门对象池：基于totalUserCount计算所需快门数量
        /// 2. 顶点标记池：Voronoi顶点可视化(预分配足够容量)
        /// 3. 种子点可视化：每个用户一个
        /// 4. 虚拟快门委托：每条边界一个
        /// 5. 观察数据字典：为每个用户创建位置/距离/面积队列
        /// 6. 分区段数据：为每个用户初始化分区信息存储
        /// </summary>
        private void InitializeInfoDics()
        {
            edgeMaxcount = (totalUserCount * (totalUserCount - 1)) / 2;

            // Create vertex markers for Voronoi diagram (enough for all possible vertices)
            int maxVertexCount = edgeMaxcount * 2;
            for (int i = 0; i < maxVertexCount; i++)
            {
                list_VoronoiVertexMarker.Add(Instantiate(prefab_VoronoiVertex, new Vector3(-50.0f, 0.0f, 0.0f), Quaternion.identity));
                list_VoronoiVertexMarker[i].SetActive(false);
                list_VoronoiVertexMarker[i].name = "VoronoiVertexMarker " + i;
            }

            // Create seed point visualizations for each user
            for (int i = 0; i < totalUserCount; i++)
            {
                list_seedPointVisual.Add(Instantiate(prefab_VoronoiSeedPoint, new Vector3(-50.0f, 0.0f, 0.0f), Quaternion.identity));
                list_seedPointVisual[i].name = "seedpointView " + i;
            }

            for (int i = 0; i < edgeMaxcount; i++)
            {
                GameObject go = new GameObject();
                list_VirtualSutterDelegate.Add(go);
                list_VirtualSutterDelegate[i].transform.position = Vector3.zero + Vector3.up * 0.02f;
                list_VirtualSutterDelegate[i].name = "VirtualSutterDelegate " + i;
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                Queue<float> queue = new Queue<float>();
                dic_physicalUsers_pos_X.Add(i, queue);
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                Queue<float> queue = new Queue<float>();
                dic_physicalUsers_pos_Z.Add(i, queue);
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                Queue<float> queue = new Queue<float>();
                dic_physicalUsers_orient_Y.Add(i, queue);
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                Queue<float> queue = new Queue<float>();
                dic_physicalRoomSize_users.Add(i, queue);
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                Queue<float> queue = new Queue<float>();
                dic_virtualUsers_pos_X.Add(i, queue);
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                Queue<float> queue = new Queue<float>();
                dic_VirtualUsers_Position_Z.Add(i, queue);
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                Queue<float> queue = new Queue<float>();
                dic_VirtualUsers_Orient_Y.Add(i, queue);
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                Dictionary<int, Queue<float>> dic_data = new Dictionary<int, Queue<float>>();
                for (int j = 0; j < 8; j++)
                {
                    Queue<float> queue = new Queue<float>();
                    dic_data.Add(j, queue);
                }
                doubleDic_physicalUsers_8wayWallDist.Add(i, dic_data);
            }

            for (int i = 0; i < totalUserCount; i++)
            {
                Dictionary<int, Queue<float>> dic_data = new Dictionary<int, Queue<float>>();
                for (int j = 0; j < 8; j++)
                {
                    Queue<float> queue = new Queue<float>();
                    dic_data.Add(j, queue);
                }
                doubleDic_virtualUsers_8wayWallDist.Add(i, dic_data);
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
        private void InitializeInfoLists(Vector2 _initUserPhyiscalPos)
        {
            list_UsersCumulativeDist.Clear();
            list_UsersCurrentPhysicalPos.Clear();
            list_UsersPrePhysicalPos.Clear();
            users_wallReset_pre.Clear();
            users_userReset_pre.Clear();
            users_shutterReset_pre.Clear();
            list_currentActionOffset.Clear();
            for (int i = 0; i < totalUserCount; i++)
            {
                list_UsersCumulativeDist.Add(0.0f);
                list_UsersCurrentPhysicalPos.Add(_initUserPhyiscalPos);
                list_UsersPrePhysicalPos.Add(_initUserPhyiscalPos);

                users_wallReset_pre.Add(0);
                users_userReset_pre.Add(0);
                users_shutterReset_pre.Add(0);
                list_currentActionOffset.Add(Vector2.zero);
            }
        }

        /// <summary>
        /// 重置参数(每个Episode开始)
        /// 
        /// 重置流程：
        /// 1. 获取房间尺寸和虚拟空间参数
        /// 2. 销毁并重建用户对象(物理和虚拟)
        /// 3. 清空所有历史数据
        /// 4. 一次性初始化种子点(位置+方向)
        /// 5. 生成初始Voronoi图和快门
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

            /// 直接引用RDWSimulationManager中的用户对象，而不创建重复拷贝
            list_physical_simulatedUsers.Clear();
            for (int i = 0; i < totalUserCount; i++)
            {
                list_physical_simulatedUsers.Add(RDWSimulationManager.instance.GetRedirectedUnits[i].realUser.gameObject);
            }

            list_virtual_simulatedUsers.Clear();
            for (int i = 0; i < totalUserCount; i++)
            {
                list_virtual_simulatedUsers.Add(RDWSimulationManager.instance.GetRedirectedUnits[i].virtualUser.gameObject);
            }

            bLock_VoronoiDiagram = false;

            prev_wallReset_mean = 0;

            InitializeInfoLists(Vector2.zero);

            UsersCumulative_MDbR_sqeuence.Clear();
            
            // Reset action offset for new episode
            list_currentActionOffset.Clear();
            for (int i = 0; i < totalUserCount; i++)
            {
                list_currentActionOffset.Add(Vector2.zero);
            }

            InitializeInfoQueues();

            /// Initialize seed points with user positions and orientations
            InitializeSeedPoints();

            /// Generate initial Voronoi diagram
            UpdateVoronoiDiagram();
        }

        /// <summary>
        /// Initialize queue for spatial information
        /// </summary>
        public void InitializeInfoQueues()
        {
            Queue<float> queue_data = new Queue<float>();
            Dictionary<int, Queue<float>> dic_data = new Dictionary<int, Queue<float>>();

            for (int i = 0; i < totalUserCount; i++)
            {
                dic_physicalUsers_pos_X.TryGetValue(i, out queue_data);
                queue_data.Clear();
                for (int j = 0; j < sptialInfo_windowSize; j++)
                {
                    queue_data.Enqueue(0.0f);
                }

                dic_physicalUsers_pos_Z.TryGetValue(i, out queue_data);
                queue_data.Clear();
                for (int j = 0; j < sptialInfo_windowSize; j++)
                {
                    queue_data.Enqueue(0.0f);
                }

                dic_physicalUsers_orient_Y.TryGetValue(i, out queue_data);
                queue_data.Clear();
                for (int j = 0; j < sptialInfo_windowSize; j++)
                {
                    queue_data.Enqueue(0.0f);
                }

                dic_physicalRoomSize_users.TryGetValue(i, out queue_data);
                queue_data.Clear();
                for (int j = 0; j < sptialInfo_windowSize; j++)
                {
                    queue_data.Enqueue(0.0f);
                }

                dic_virtualUsers_pos_X.TryGetValue(i, out queue_data);
                queue_data.Clear();
                for (int j = 0; j < sptialInfo_windowSize; j++)
                {
                    queue_data.Enqueue(0.0f);
                }

                dic_VirtualUsers_Position_Z.TryGetValue(i, out queue_data);
                queue_data.Clear();
                for (int j = 0; j < sptialInfo_windowSize; j++)
                {
                    queue_data.Enqueue(0.0f);
                }

                dic_VirtualUsers_Orient_Y.TryGetValue(i, out queue_data);
                queue_data.Clear();
                for (int j = 0; j < sptialInfo_windowSize; j++)
                {
                    queue_data.Enqueue(0.0f);
                }

                doubleDic_physicalUsers_8wayWallDist.TryGetValue(i, out dic_data);
                for (int k = 0; k < 8; k++)
                {
                    dic_data.TryGetValue(k, out queue_data);
                    queue_data.Clear();
                    for (int j = 0; j < sptialInfo_windowSize; j++)
                    {
                        queue_data.Enqueue(0.0f);
                    }
                }

                doubleDic_virtualUsers_8wayWallDist.TryGetValue(i, out dic_data);
                for (int k = 0; k < 8; k++)
                {
                    dic_data.TryGetValue(k, out queue_data);
                    queue_data.Clear();
                    for (int j = 0; j < sptialInfo_windowSize; j++)
                    {
                        queue_data.Enqueue(0.0f);
                    }
                }
            }
        }

        /// <summary>
        /// 一次性初始化种子点(Episode开始时调用)
        /// 
        /// 功能：
        /// 1. 清空现有种子点列表
        /// 2. 从物理用户当前位置创建新种子点
        /// 3. 设置用户朝向作为种子点方向(用于加权计算)
        /// </summary>
        private void InitializeSeedPoints()
        {
            list_VoronoiSeedPoint.Clear();
            for (int i = 0; i < totalUserCount; i++)
            {
                // Create seed point with user's current position and orientation
                Vector2 seedPosition = new Vector2(
                    list_physical_simulatedUsers[i].transform.position.x,
                    list_physical_simulatedUsers[i].transform.position.z);
                float seedDirection = list_physical_simulatedUsers[i].transform.rotation.eulerAngles.y;
                
                list_VoronoiSeedPoint.Add(new WeightedVoronoiGrid.SeedPoint(seedPosition, seedDirection));
            }
        }

        /// <summary>
        /// 运行时更新种子点(每帧在CollectObservations中调用)
        /// 
        /// 功能：
        /// 1. 跟踪物理用户的实时位置
        /// 2. 应用AI模型的有限偏移（推动空间分割减少重置）
        /// 3. 更新每个种子点的位置和方向
        /// 4. 驱动Voronoi图的动态更新
        /// 
        /// 注意：种子点位置 = 用户位置 + 有限偏移（来自模型）
        /// </summary>
        private void UpdateSeedPointsWithOffset()
        {
            for (int i = 0; i < totalUserCount && i < list_VoronoiSeedPoint.Count; i++)
            {
                // Update seed point position from user's current position
                list_VoronoiSeedPoint[i].position = new Vector2(
                    list_physical_simulatedUsers[i].transform.position.x,
                    list_physical_simulatedUsers[i].transform.position.z);
                
                // Update seed point orientation from user's current rotation
                list_VoronoiSeedPoint[i].direction = list_physical_simulatedUsers[i].transform.rotation.eulerAngles.y;
            }
        }

        /// <summary>
        /// calculate 8 distances from physical/virtual wall, obstacle, shutter
        /// </summary>
        public void Calc_8Way_Distances(Transform _origintransform, ref Dictionary<int, Queue<float>> _distanceDic, bool bActual)
        {
            float distance = 0.0f;

            List<Vector3> direction = new List<Vector3>();

            direction.Add(Vector3.right);
            direction.Add((Vector3.right + -Vector3.forward).normalized);
            direction.Add(-Vector3.forward);
            direction.Add((-Vector3.right + -Vector3.forward).normalized);
            direction.Add(-Vector3.right);
            direction.Add((-Vector3.right + Vector3.forward).normalized);
            direction.Add(Vector3.forward);
            direction.Add((Vector3.right + Vector3.forward).normalized);

            for (int i = 0; i < direction.Count; i++)
            {
                if (Physics.Raycast(_origintransform.position, direction[i], out rayCastHit, 100.0f))
                {
                    if (bActual)
                    {
                        if (rayCastHit.collider.gameObject.layer == LayerMask.NameToLayer("PhysicalWall"))
                        {
                            distance = rayCastHit.distance;
                        }
                    }
                    else
                    {
                        if (rayCastHit.collider.gameObject.layer == LayerMask.NameToLayer("VirtualWall"))
                        {
                            distance = rayCastHit.distance;
                        }
                    }
                }
            }

            Queue<float> queue_dist = new Queue<float>();
            _distanceDic.TryGetValue(0, out queue_dist);
            queue_dist.Enqueue(distance);
            queue_dist.Dequeue();
        }

        public void Enqueue_RoomSize(float element, ref Queue<float> _queue)
        {
            _queue.Enqueue(element);
            _queue.Dequeue();
        }

        public void Enqueue_Veloc(float element, ref Queue<float> _queue)
        {
            _queue.Enqueue(element);
            _queue.Dequeue();
        }

        public static Vector2 rotateVec2D(Vector2 v, float delta)
        {
            return new Vector2(
                v.x * Mathf.Cos(delta * Mathf.Deg2Rad) - v.y * Mathf.Sin(delta * Mathf.Deg2Rad),
                v.x * Mathf.Sin(delta * Mathf.Deg2Rad) + v.y * Mathf.Cos(delta * Mathf.Deg2Rad)
            );
        }

        private float getStandardDeviation(List<float> floatList)
        {
            float average = floatList.Average();
            float sumOfDerivation = 0;
            foreach (float value in floatList)
            {
                sumOfDerivation += (value - average) * (value - average);
            }
            float sumOfDerivationAverage = sumOfDerivation / floatList.Count;
            return Mathf.Sqrt(sumOfDerivationAverage);
        }

        private double getStandardDeviation(List<int> floatList)
        {
            double average = floatList.Average();
            int sumOfDerivation = 0;
            foreach (int value in floatList)
            {
                sumOfDerivation += (int)((value - average) * (value - average));
            }
            int sumOfDerivationAverage = sumOfDerivation / floatList.Count;
            return Mathf.Sqrt((float)(sumOfDerivationAverage));
        }
    }
}
