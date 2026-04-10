using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityEngine.UI;
using csDelaunay;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace _GCM
{
    public class GlobalCoordinationManager : MonoBehaviour
    {
        #region singleton pattern
        /// <summary>
        /// Singleton instance
        /// </summary>
        public static GlobalCoordinationManager instance = null;
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
        /// Target walking distance per user to finish an episode
        /// </summary>
        public float TargetDistancePerUser = 200.0f;

        /// <summary>
        /// Enable mixed exploration logic (if applicable)
        /// </summary>
        public bool bMixedExploration = true;

        [Header("=== Experiment Mode ===")]
        [Tooltip("启用后使用固定种子文件进行对比实验；关闭后使用随机实验")]
        [SerializeField]
        private bool bUseCompareExperiment = true;

        private const string CompareSeedFileName = "experiment_seeds_100.txt";
        private readonly List<int> compareExperimentSeeds = new List<int>();
        private bool compareSeedsLoaded = false;
        private int currentEpisodeSeed = int.MinValue;

        /// <summary>
        /// Use Lloyd Relaxation for initial uniform distribution
        /// </summary>
        public bool bEnable_InitPhyUserPosUni = false;
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
        private StateCollector stateCollector;
        #endregion

        #region Statistics & Data Recording
        [Header("=== Statistics Recording ===")]
        private EpisodeService episodeService;
        #endregion

        #region Voronoi & Spatial Partitioning
        [Header("=== Voronoi Data ===")]
        private VoronoiPartitioner voronoiPartitioner;
        private PartitionResult latestPartitionResult;

        /// <summary>
        /// Fixed seed points (if used)
        /// </summary>
        List<Vector2> list_VoronoiSeedPoint_Fixed = new List<Vector2>();

        /// <summary>
        /// Vertices for area segments
        /// </summary>
        public Dictionary<int, List<Vector2>> dic_AreaSegmentsVertex = new Dictionary<int, List<Vector2>>();


        
        private const float eps = 0.001f;
        #endregion

        #region Visualization & Prefabs
        [Header("=== Visualization & Prefabs ===")]
        [SerializeField] private GameObject prefab_VoronoiVertex;
        [SerializeField] private GameObject prefab_VoronoiSeedPoint;
        [SerializeField] private GameObject S2C_CenterPointerObject_Prefab;
        public List<Material> PartitionedSpaceMaterials = new List<Material>();

        private PartitionVisualizer partitionVisualizer;
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

        private VelocityPredictor velocityPredictor;
        #endregion

        #region Prediction Evaluation
        [Header("=== Prediction Evaluation ===")]
        [SerializeField] private bool bEnablePredictionEvaluation = false;
        [SerializeField] private bool bExportPredictionSamples = false;
        [SerializeField] private int predictionSampleEveryNFrames = 10;
        [SerializeField] private string trajectoryModeLabel = "DefaultMode";

        [SerializeField] private List<float> predictionEvaluationHorizons = new List<float> { 0.3f, 0.5f, 1.0f, 1.5f };

        private PredictionEvaluator predictionEvaluator;
        private string predictionExperimentFolderPath = string.Empty;
        private int simulationFrameIndex = 0;
        private float simulationElapsedTime = 0f;

        [Header("=== Predictive Occupancy ===")]
        [SerializeField] private bool bEnablePredictiveOccupancy = true;
        [SerializeField] private List<float> predictiveOccupancyHorizons = new List<float> { 0.3f, 0.5f, 1.0f, 1.5f };
        [SerializeField] private bool bEnablePredictiveOccupancyGizmos = true;
        [SerializeField] private float predictiveOccupancyGizmoHeight = 0.03f;

        [Header("=== Horizon Selection (from Eval) ===")]
        [SerializeField] private float trustedHorizonMeanErrorThreshold = 0.60f;
        [SerializeField] private float trustedHorizonStdErrorThreshold = 0.45f;
        [SerializeField] private int trustedHorizonMaxCensoredCount = 0;

        private PredictiveOccupancyBuilder predictiveOccupancyBuilder;
        private PredictionUncertaintyModel predictiveUncertaintyModel;
        private PredictedOccupancyFrame latestPredictedOccupancyFrame;
        private PredictionHorizonSelector horizonSelector;
        private PredictiveOccupancyVisualizer predictiveOccupancyVisualizer;

        [Header("=== Partition Risk Evaluation (Read-Only) ===")]
        [SerializeField] private bool bEnablePartitionRiskEvaluation = true;
        [SerializeField] private bool bEnableRiskLogging = true;
        [SerializeField] private bool bEnableRiskVisualization = true;
        [SerializeField] private int riskOutputEveryNFrames = 10;

        [SerializeField] private float riskCellBoundarySafeClearance = 0.35f;
        [SerializeField] private float riskPhysicalBoundarySafeClearance = 0.50f;
        [SerializeField] private float riskPairSafeSeparation = 0.40f;
        [SerializeField] private float riskSeedDeltaReference = 0.25f;

        [SerializeField] private float riskWeightCellBoundary = 0.40f;
        [SerializeField] private float riskWeightUser = 0.40f;
        [SerializeField] private float riskWeightSmooth = 0.20f;

        [SerializeField] private float riskAdjacencyThreshold = 0.35f;
        [SerializeField] private float riskDominantNoneThreshold = 0.10f;
        [SerializeField] private float riskDominantMixedGap = 0.08f;

        private PartitionRiskEvaluator partitionRiskEvaluator;
        private PartitionRiskLogger partitionRiskLogger;
        private PartitionRiskVisualizer partitionRiskVisualizer;
        private PartitionRiskFrame latestPartitionRiskFrame;
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
                Debug.LogError("GlobalCoordinationManager Start: GM_DataRecord.instance is NULL! Please ensure GM_DataRecord script is attached to an active GameObject in the scene.");
            }
            else
            {
                Debug.Log("GlobalCoordinationManager Start: GM_DataRecord.instance found successfully.");
            }
         
            InitializeInfoDics();
            stateCollector = new StateCollector();
            voronoiPartitioner = new VoronoiPartitioner(totalUserCount, userRadius, shutterWidth, eps);
            episodeService = new EpisodeService(totalUserCount, SimulationCount_max, TargetDistancePerUser, text_currentEpi);
            partitionVisualizer = new PartitionVisualizer(totalUserCount, prefab_VoronoiVertex, prefab_VoronoiSeedPoint, S2C_CenterPointerObject_Prefab);
            partitionVisualizer.InitializePools();

            InitializeInfoLists(Vector2.zero);

            if(totalUserCount < 3 && bEnable_InitPhyUserPosUni)
            {
                bEnable_InitPhyUserPosUni = false;
            }

            LoadCompareSeedsIfNeeded();

            InitializeVelocityTracker();
            InitializePredictionEvaluator();
            InitializePredictionExportFolderForExperiment();
            InitializePredictiveOccupancy();
            InitializePartitionRiskLayer();

            // Manually trigger the first episode since we removed ML-Agents
            ResetEpisode();
        }

        public void SetCompareExperimentMode(bool useCompareExperiment)
        {
            bUseCompareExperiment = useCompareExperiment;
            compareSeedsLoaded = false;
            LoadCompareSeedsIfNeeded();
        }

        private void LoadCompareSeedsIfNeeded()
        {
            if (compareSeedsLoaded)
                return;

            compareSeedsLoaded = true;
            compareExperimentSeeds.Clear();

            if (!bUseCompareExperiment)
                return;

            string filePath = Path.Combine(Application.streamingAssetsPath, CompareSeedFileName);
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"Compare experiment enabled, but seed file not found: {filePath}. Fallback to random mode.");
                return;
            }

            string[] lines = File.ReadAllLines(filePath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (int.TryParse(line, out int seedValue))
                {
                    compareExperimentSeeds.Add(seedValue);
                }
                else
                {
                    Debug.LogWarning($"Invalid seed value at line {i + 1} in {CompareSeedFileName}: {lines[i]}");
                }
            }

            if (compareExperimentSeeds.Count != 100)
            {
                Debug.LogWarning($"Expected 100 seeds in {CompareSeedFileName}, but loaded {compareExperimentSeeds.Count}.");
            }
        }

        private bool TryApplyCompareSeedForEpisode()
        {
            currentEpisodeSeed = int.MinValue;

            if (!bUseCompareExperiment)
                return false;

            LoadCompareSeedsIfNeeded();
            if (compareExperimentSeeds.Count == 0)
                return false;

            int seedIndex = episodeService.CurrentSimulationCount % compareExperimentSeeds.Count;
            currentEpisodeSeed = compareExperimentSeeds[seedIndex];
            Random.InitState(currentEpisodeSeed);
            return true;
        }

        private void InitializeVelocityTracker()
        {
            velocityPredictor = new VelocityPredictor(totalUserCount)
            {
                AlphaMax = alphaMax,
                VMax = vMax,
                VelocityToOffsetFactor = velocityToOffsetFactor,
                MaxVelocityOffsetDist = maxVelocityOffsetDist,
                StopThreshold = stopThreshold
            };
        }

        private void InitializePredictionEvaluator()
        {
            predictionEvaluator = new PredictionEvaluator
            {
                EnableEvaluation = bEnablePredictionEvaluation,
                ExportResolvedSamples = bExportPredictionSamples,
                SampleEveryNFrames = predictionSampleEveryNFrames
            };

            predictionEvaluator.Horizons.Clear();
            if (predictionEvaluationHorizons != null)
            {
                for (int i = 0; i < predictionEvaluationHorizons.Count; i++)
                {
                    if (predictionEvaluationHorizons[i] > 0f)
                        predictionEvaluator.Horizons.Add(predictionEvaluationHorizons[i]);
                }
            }

            predictionEvaluator.ClearAll();
        }

        private void InitializePredictiveOccupancy()
        {
            predictiveOccupancyBuilder = new PredictiveOccupancyBuilder();
            predictiveUncertaintyModel = new PredictionUncertaintyModel();
            latestPredictedOccupancyFrame = new PredictedOccupancyFrame();

            predictiveOccupancyVisualizer = new PredictiveOccupancyVisualizer();
            predictiveOccupancyVisualizer.ColorResolver = ResolvePartitionColor;

            horizonSelector = new PredictionHorizonSelector
            {
                MeanErrorThreshold = trustedHorizonMeanErrorThreshold,
                StdErrorThreshold = trustedHorizonStdErrorThreshold,
                MaxCensoredCount = trustedHorizonMaxCensoredCount
            };
        }

        private void InitializePredictionExportFolderForExperiment()
        {
            if (!bEnablePredictionEvaluation || !bExportPredictionSamples)
                return;

            string predictionRoot = Path.Combine(Directory.GetCurrentDirectory(), "CGnA_DataLog", "predictionEvaluate");
            Directory.CreateDirectory(predictionRoot);

            string folderNameBase = $"prediction_eval_{DateTime.Now:yyyyMMdd_HHmmss}";
            string candidateFolderPath = Path.Combine(predictionRoot, folderNameBase);
            int suffix = 1;

            while (Directory.Exists(candidateFolderPath))
            {
                candidateFolderPath = Path.Combine(predictionRoot, $"{folderNameBase}_{suffix:00}");
                suffix++;
            }

            Directory.CreateDirectory(candidateFolderPath);
            predictionExperimentFolderPath = candidateFolderPath;

            Debug.Log($"[PredictionEvaluator] Experiment export folder: {predictionExperimentFolderPath}");
        }

        private void InitializePartitionRiskLayer()
        {
            PartitionRiskConfig config = new PartitionRiskConfig
            {
                OutputEveryNFrames = Mathf.Max(1, riskOutputEveryNFrames),
                CellBoundarySafeClearance = riskCellBoundarySafeClearance,
                PhysicalBoundarySafeClearance = riskPhysicalBoundarySafeClearance,
                PairSafeSeparation = riskPairSafeSeparation,
                SeedDeltaReference = riskSeedDeltaReference,
                AdjacencyRiskThreshold = riskAdjacencyThreshold,
                DominantRiskNoneThreshold = riskDominantNoneThreshold,
                DominantRiskMixedGap = riskDominantMixedGap,
                Weights = new RiskWeights
                {
                    CellBoundaryWeight = riskWeightCellBoundary,
                    UserWeight = riskWeightUser,
                    SmoothWeight = riskWeightSmooth
                }
            };

            partitionRiskEvaluator = new PartitionRiskEvaluator(config);
            partitionRiskLogger = new PartitionRiskLogger(config.OutputEveryNFrames);
            partitionRiskVisualizer = new PartitionRiskVisualizer();
            latestPartitionRiskFrame = null;
        }

        private int GetCurrentEpisodeId()
        {
            return episodeService.CurrentSimulationCount + 1;
        }

        private void OnDestroy()
        {
            if (voronoiPartitioner != null)
            {
                voronoiPartitioner.Dispose();
                voronoiPartitioner = null;
            }

            if (partitionVisualizer != null)
            {
                partitionVisualizer.Dispose();
                partitionVisualizer = null;
            }
        }

        public void ResetEpisode()
        {
            simulationFrameIndex = 0;
            simulationElapsedTime = 0f;

            SetResetParameters();

            if (predictionEvaluator != null)
            {
                predictionEvaluator.BeginEpisode(GetCurrentEpisodeId());
            }

            if (partitionRiskEvaluator != null)
            {
                partitionRiskEvaluator.ResetTemporalState();
            }

            latestPartitionRiskFrame = null;
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
            if (stateCollector == null || !stateCollector.HasReadyUsers(totalUserCount)) 
                return;

            FrameState frameState = stateCollector.CaptureFrameState(totalUserCount);

            simulationFrameIndex++;
            simulationElapsedTime += Time.fixedDeltaTime;

            velocityPredictor.AlphaMax = alphaMax;
            velocityPredictor.VMax = vMax;
            velocityPredictor.VelocityToOffsetFactor = velocityToOffsetFactor;
            velocityPredictor.MaxVelocityOffsetDist = maxVelocityOffsetDist;
            velocityPredictor.StopThreshold = stopThreshold;
            velocityPredictor.Update(frameState.PhysicalUsers, Time.fixedDeltaTime, bUseVelocityOffset);

            if (bEnablePredictiveOccupancy && predictiveOccupancyBuilder != null)
            {
                latestPredictedOccupancyFrame = predictiveOccupancyBuilder.BuildFrame(
                    frameState.PhysicalUsers,
                    velocityPredictor,
                    predictiveOccupancyHorizons,
                    predictiveUncertaintyModel,
                    simulationFrameIndex,
                    simulationElapsedTime);
            }

            // 先注册当前帧预测
            if (predictionEvaluator != null && predictionEvaluator.ShouldSample(simulationFrameIndex))
            {
                predictionEvaluator.RegisterPredictions(
                    GetCurrentEpisodeId(),
                    simulationFrameIndex,
                    simulationElapsedTime,
                    frameState.PhysicalUsers,
                    velocityPredictor,
                    trajectoryModeLabel
                );
            }

            // 再尝试回填已到期的样本
            if (predictionEvaluator != null)
            {
                predictionEvaluator.ResolveDuePredictions(simulationElapsedTime, frameState.PhysicalUsers);
            }

            episodeService.SimulationCountMax = SimulationCount_max;
            episodeService.TargetDistancePerUser = TargetDistancePerUser;
            episodeService.Tick(stateCollector);

            /// Init Obstacle mesh info for next episode
            if (episodeService.ShouldEndEpisode(stateCollector))
            {
                Debug.Log($"Episode Finished. Total Dist: {stateCollector.CurrentEpisodeTotalDistance}");

                int endedEpisodeId = GetCurrentEpisodeId();
                bool isLastEpisodeInExperiment = endedEpisodeId >= SimulationCount_max;

                if (predictionEvaluator != null && bEnablePredictionEvaluation)
                {
                    List<EpisodePredictionSummary> episodeSummaries = predictionEvaluator.EndEpisode(true);
                    Debug.Log("[PredictionEvaluator] " + predictionEvaluator.BuildReadableSummary(episodeSummaries));

                    if (horizonSelector != null)
                    {
                        HorizonSelectionResult selection = horizonSelector.SelectTrustedHorizons(episodeSummaries, trajectoryModeLabel);
                        Debug.Log($"[HorizonSelector] Trusted horizons: {string.Join(", ", selection.TrustedHorizons)} | Untrusted horizons: {string.Join(", ", selection.UntrustedHorizons)}");
                    }

                    if (bExportPredictionSamples)
                    {
                        if (string.IsNullOrEmpty(predictionExperimentFolderPath) || !Directory.Exists(predictionExperimentFolderPath))
                        {
                            InitializePredictionExportFolderForExperiment();
                        }

                        string episodeSamplePath = Path.Combine(
                            predictionExperimentFolderPath,
                            $"prediction_eval_episode_{endedEpisodeId:000}.csv");
                        predictionEvaluator.ExportEpisodeSamplesCsv(episodeSamplePath);
                    }

                    predictionEvaluator.ClearEpisodeData();
                }

                episodeService.FinalizeEpisode(stateCollector);

                if (predictionEvaluator != null && bEnablePredictionEvaluation && isLastEpisodeInExperiment)
                {
                    if (bExportPredictionSamples)
                    {
                        if (string.IsNullOrEmpty(predictionExperimentFolderPath) || !Directory.Exists(predictionExperimentFolderPath))
                        {
                            InitializePredictionExportFolderForExperiment();
                        }

                        string overallSummaryPath = Path.Combine(
                            predictionExperimentFolderPath,
                            "prediction_eval_overall_summary.csv");
                        predictionEvaluator.ExportOverallSummaryCsv(overallSummaryPath);
                    }

                    predictionEvaluator.ClearAll();

                    if (bExportPredictionSamples)
                    {
                        InitializePredictionExportFolderForExperiment();
                    }
                }

                ResetEpisode();
                return;
            }

            IReadOnlyList<Vector3> offsetResult = velocityPredictor.GetOffsets();
            latestPartitionResult = voronoiPartitioner.Build(
                frameState,
                offsetResult,
                Time.deltaTime,
                bUseVelocityOffset,
                physicalRoom_width_half,
                physicalRoom_height_half);

            ApplyPartitionResult(latestPartitionResult);

            if (bEnablePartitionRiskEvaluation && partitionRiskEvaluator != null)
            {
                latestPartitionRiskFrame = partitionRiskEvaluator.Evaluate(
                    simulationFrameIndex,
                    simulationElapsedTime,
                    latestPartitionResult,
                    latestPredictedOccupancyFrame,
                    frameState.PhysicalUsers,
                    physicalRoom_width_half,
                    physicalRoom_height_half);

                if (bEnableRiskLogging && partitionRiskLogger != null)
                {
                    partitionRiskLogger.TryLogFrame(latestPartitionRiskFrame);
                }
            }


            // Removed bEnable_InitPhyUserPosUni one-frame lock logic
            // Removed bOneframetimerblockVoronoi logic



            /// Simulate the designated redirection controller
            RDWSimulationManager.instance.SimulateRDW();

            // AddRewards(); // Removed
        }
        private void ApplyPartitionResult(PartitionResult partitionResult)
        {
            if (partitionResult == null)
                return;

            partitionVisualizer.UpdatePartitionVisuals(partitionResult);

            for (int i = 0; i < totalUserCount; i++)
            {
                if (!dic_AreaSegmentsVertex.TryGetValue(i, out List<Vector2> areaVertices))
                {
                    areaVertices = new List<Vector2>();
                    dic_AreaSegmentsVertex[i] = areaVertices;
                }

                areaVertices.Clear();
                if (partitionResult.RegionVertices.TryGetValue(i, out List<Vector2> regionVertices))
                {
                    areaVertices.AddRange(regionVertices);
                }

                if (i >= partitionResult.Centroids.Count)
                    continue;

                if (RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector() is S2CRedirector)
                {
                    Vector3 centerPoint = partitionVisualizer.GetCenterPointerPosition(i);
                    ((S2CRedirector)RDWSimulationManager.instance.GetRedirectedUnits[i].GetRedirector()).SetCenterPoint(centerPoint);
                    partitionVisualizer.SetCenterPointerActive(i, true);
                }
                else
                {
                    partitionVisualizer.SetCenterPointerActive(i, false);
                }
            }
        }

        private void FixedUpdate()
        {
            ProcessStep();
        }

        private void OnDrawGizmos()
        {
            if (bEnablePredictiveOccupancyGizmos && predictiveOccupancyVisualizer != null && latestPredictedOccupancyFrame != null)
            {
                predictiveOccupancyVisualizer.Draw(latestPredictedOccupancyFrame, predictiveOccupancyGizmoHeight);
            }

            if (bEnableRiskVisualization && partitionRiskVisualizer != null && latestPartitionRiskFrame != null && stateCollector != null)
            {
                partitionRiskVisualizer.Draw(latestPartitionRiskFrame, stateCollector.PhysicalUsers);
            }

            DrawPartitionAreaGizmos();
        }



        private void DrawPartitionAreaGizmos()
        {
            if (dic_AreaSegmentsVertex == null || dic_AreaSegmentsVertex.Count == 0)
                return;

            const float gizmoHeight = 0.02f;
            const float outlineHeightOffset = 0.005f;
            const float fillAlpha = 0.24f;

            foreach (var kv in dic_AreaSegmentsVertex)
            {
                int userIndex = kv.Key;
                List<Vector2> vertices2D = kv.Value;

                if (vertices2D == null || vertices2D.Count < 3)
                    continue;

                Color baseColor = ResolvePartitionColor(userIndex);
                Vector3[] points = new Vector3[vertices2D.Count];

                for (int i = 0; i < vertices2D.Count; i++)
                {
                    points[i] = new Vector3(vertices2D[i].x, gizmoHeight, vertices2D[i].y);
                }

#if UNITY_EDITOR
                Color fillColor = baseColor;
                fillColor.a = fillAlpha;
                Handles.color = fillColor;
                Handles.DrawAAConvexPolygon(points);
#endif

                Color outlineColor = baseColor;
                outlineColor.a = 0.95f;
                Gizmos.color = outlineColor;

                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 a = points[i] + Vector3.up * outlineHeightOffset;
                    Vector3 b = points[(i + 1) % points.Length] + Vector3.up * outlineHeightOffset;
                    Gizmos.DrawLine(a, b);
                }
            }
        }

        private Color ResolvePartitionColor(int userIndex)
        {
            if (PartitionedSpaceMaterials != null && userIndex >= 0 && userIndex < PartitionedSpaceMaterials.Count)
            {
                Material mat = PartitionedSpaceMaterials[userIndex];
                if (mat != null)
                    return mat.color;
            }

            return Color.HSVToRGB(Mathf.Repeat(userIndex * 0.173f, 1f), 0.75f, 1f);
        }

        // Heuristic removed
        // public override void Heuristic(in ActionBuffers actionsOut) {}

        /// <summary>
        /// Initialize Dictionary for spatial information
        /// </summary>
        private void InitializeInfoDics()
        {
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
            if (stateCollector != null)
            {
                stateCollector.ResetTracking(totalUserCount, _initUserPhyiscalPos);
            }
        }

        /// <summary>
        /// Reset the parameters
        /// </summary>
        public void SetResetParameters()
        {
            RDWSimulationManager.instance.BStart = false;
            RDWSimulationManager.instance.StartSimulation();

            bool seeded = TryApplyCompareSeedForEpisode();
            if (seeded)
            {
                Debug.Log($"[CompareExperiment] Episode {episodeService.CurrentSimulationCount + 1} uses seed {currentEpisodeSeed}.");
            }

            physicalRoom_width_half = Mathf.Abs(RDWSimulationManager.instance.simulationSetting.realSpaceSetting.spaceObjectSetting.vertices[0].x);
            physicalRoom_height_half = Mathf.Abs(RDWSimulationManager.instance.simulationSetting.realSpaceSetting.spaceObjectSetting.vertices[0].y);

            virtualRoom_width_half = Mathf.Abs(RDWSimulationManager.instance.simulationSetting.virtualSpaceSetting.spaceObjectSetting.vertices[0].x);
            virtualRoom_height_Half = Mathf.Abs(RDWSimulationManager.instance.simulationSetting.virtualSpaceSetting.spaceObjectSetting.vertices[0].y);

            actual_halfRoomsize_default = physicalRoom_height_half * 2 * physicalRoom_width_half;
            actual_roomhypotenuse = Mathf.Sqrt(Mathf.Pow(physicalRoom_width_half, 2) + Mathf.Pow(physicalRoom_height_half, 2));

            /// refresh pointers for physical user
            stateCollector.RefreshUsersFromSimulation(totalUserCount);

            velocityPredictor.ResetWithUsers(stateCollector.PhysicalUsers);

            //Debug.Log("A");

            if (bEnable_InitPhyUserPosUni && episodeService.CurrentSimulationCount == 0)
            {
                list_VoronoiSeedPoint_Fixed = voronoiPartitioner.BuildInitialUniformSeeds(physicalRoom_width_half, physicalRoom_height_half);
            }

            //Debug.Log("F:" + list_VoronoiSeedPoint_Fixed.Count);

            if (bEnable_InitPhyUserPosUni)
            {
                for (int i = 0; i < totalUserCount; i++)
                {
                    stateCollector.PhysicalUsers[i].transform.position = new Vector3(list_VoronoiSeedPoint_Fixed[i].x, 0.0f, list_VoronoiSeedPoint_Fixed[i].y);
                }

                if(totalUserCount == 4)
                {
                    for (int i = 0; i < totalUserCount; i++)
                    {
                        stateCollector.PhysicalUsers[i].transform.Translate(Vector3.forward * Random.Range(-0.001f, 0.001f));
                    }
                }
            }


            InitializeInfoLists(Vector2.zero);
            voronoiPartitioner.ResetSeedsFromUsers(stateCollector.PhysicalUsers);

            // [FIX] Update pre-positions to actual user positions to avoid initial distance jump
            stateCollector.SyncPreAndCurrentToPhysicalUsers(totalUserCount);
            episodeService.BeginEpisode(stateCollector);

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

    }
}
