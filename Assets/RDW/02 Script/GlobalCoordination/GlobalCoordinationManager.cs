using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityEngine.UI;
using _GCM.PartitionUpdate;
using RDW.Coordination.LocalSafeTarget;
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

        [Header("=== Module Config ===")]
        [SerializeField] private GlobalCoordinationModuleConfig moduleConfig = new GlobalCoordinationModuleConfig();
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
        #endregion

        #region User Management
        [Header("=== User Management ===")]
        private StateCollector stateCollector;
        private UserRuntimeStateProvider userRuntimeStateProvider;
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
        private VelocityPredictor velocityPredictor;
        #endregion

        #region Prediction Evaluation
        private PredictionEvaluator predictionEvaluator;
        private string predictionExperimentFolderPath = string.Empty;
        private int simulationFrameIndex = 0;
        private float simulationElapsedTime = 0f;

        private PredictiveOccupancyBuilder predictiveOccupancyBuilder;
        private PredictionUncertaintyModel predictiveUncertaintyModel;
        private PredictedOccupancyFrame latestPredictedOccupancyFrame;
        private PredictionHorizonSelector horizonSelector;
        private PredictiveOccupancyVisualizer predictiveOccupancyVisualizer;

        private PartitionRiskEvaluator partitionRiskEvaluator;
        private PartitionRiskLogger partitionRiskLogger;
        private PartitionRiskVisualizer partitionRiskVisualizer;
        private PartitionRiskFrame latestPartitionRiskFrame;

        private PartitionUpdateTriggerEvaluator partitionUpdateTriggerEvaluator;
        private RiskDrivenSeedUpdater riskDrivenSeedUpdater;
        private PartitionUpdateAcceptancePolicy partitionUpdateAcceptancePolicy;
        private PartitionUpdateCoordinator partitionUpdateCoordinator;
        private PartitionUpdateLogger partitionUpdateLogger;
        private PartitionUpdateVisualizer partitionUpdateVisualizer;
        private RiskDrivenSeedUpdateState[] partitionUpdateStates;
        private readonly List<PartitionUpdateAttempt> latestPartitionUpdateAttempts = new List<PartitionUpdateAttempt>();
        #endregion

        #region Phase 5: Local Safe Target Selection
        private LocalSafeTargetSelector localSafeTargetSelector;
        private Dictionary<int, LocalTargetResult> latestLocalTargets = new Dictionary<int, LocalTargetResult>();
        private string localTargetExperimentFolderPath = string.Empty;
        private RDW.Coordination.Visualization.LocalSafeTargetVisualizer localSafeTargetVisualizer;
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
            if (moduleConfig == null)
            {
                moduleConfig = new GlobalCoordinationModuleConfig();
            }

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
            userRuntimeStateProvider = new UserRuntimeStateProvider(
                stateCollector,
                dic_AreaSegmentsVertex,
                () => RDWSimulationManager.instance != null ? RDWSimulationManager.instance.GetRedirectedUnits : null);
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
            InitializePartitionUpdateLayer();
            InitializeLocalSafeTargetSelection();

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
                AlphaMax = moduleConfig.Velocity.AlphaMax,
                VMax = moduleConfig.Velocity.VMax,
                VelocityToOffsetFactor = moduleConfig.Velocity.VelocityToOffsetFactor,
                MaxVelocityOffsetDist = moduleConfig.Velocity.MaxVelocityOffsetDist,
                StopThreshold = moduleConfig.Velocity.StopThreshold
            };
        }

        private void InitializePredictionEvaluator()
        {
            predictionEvaluator = new PredictionEvaluator
            {
                EnableEvaluation = moduleConfig.Prediction.EnableEvaluation,
                ExportResolvedSamples = moduleConfig.Prediction.ExportResolvedSamples,
                SampleEveryNFrames = moduleConfig.Prediction.SampleEveryNFrames
            };

            predictionEvaluator.Horizons.Clear();
            if (moduleConfig.Prediction.EvaluationHorizons != null)
            {
                for (int i = 0; i < moduleConfig.Prediction.EvaluationHorizons.Count; i++)
                {
                    if (moduleConfig.Prediction.EvaluationHorizons[i] > 0f)
                        predictionEvaluator.Horizons.Add(moduleConfig.Prediction.EvaluationHorizons[i]);
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
                MeanErrorThreshold = moduleConfig.PredictiveOccupancy.TrustedHorizonMeanErrorThreshold,
                StdErrorThreshold = moduleConfig.PredictiveOccupancy.TrustedHorizonStdErrorThreshold,
                MaxCensoredCount = moduleConfig.PredictiveOccupancy.TrustedHorizonMaxCensoredCount
            };
        }

        private void InitializePredictionExportFolderForExperiment()
        {
            if (!moduleConfig.Prediction.EnableEvaluation || !moduleConfig.Prediction.ExportResolvedSamples)
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
                OutputEveryNFrames = Mathf.Max(1, moduleConfig.PartitionRisk.OutputEveryNFrames),
                CellBoundarySafeClearance = moduleConfig.PartitionRisk.CellBoundarySafeClearance,
                PhysicalBoundarySafeClearance = moduleConfig.PartitionRisk.PhysicalBoundarySafeClearance,
                PairSafeSeparation = moduleConfig.PartitionRisk.PairSafeSeparation,
                SeedDeltaReference = moduleConfig.PartitionRisk.SeedDeltaReference,
                AdjacencyRiskThreshold = moduleConfig.PartitionRisk.AdjacencyThreshold,
                DominantRiskNoneThreshold = moduleConfig.PartitionRisk.DominantNoneThreshold,
                DominantRiskMixedGap = moduleConfig.PartitionRisk.DominantMixedGap,
                Weights = new RiskWeights
                {
                    CellBoundaryWeight = moduleConfig.PartitionRisk.WeightCellBoundary,
                    UserWeight = moduleConfig.PartitionRisk.WeightUser,
                    SmoothWeight = moduleConfig.PartitionRisk.WeightSmooth
                }
            };

            partitionRiskEvaluator = new PartitionRiskEvaluator(config);
            partitionRiskLogger = new PartitionRiskLogger(config.OutputEveryNFrames);
            partitionRiskVisualizer = new PartitionRiskVisualizer();
            latestPartitionRiskFrame = null;
        }

        private void InitializePartitionUpdateLayer()
        {
            partitionUpdateTriggerEvaluator = new PartitionUpdateTriggerEvaluator(
                moduleConfig.PartitionUpdate.TauCellRiskEnter,
                moduleConfig.PartitionUpdate.TauCellRiskExit,
                moduleConfig.PartitionUpdate.TauNeighborRiskEnter,
                moduleConfig.PartitionUpdate.TauNeighborRiskExit,
                moduleConfig.PartitionUpdate.PersistFramesCell,
                moduleConfig.PartitionUpdate.PersistFramesNeighbor,
                moduleConfig.PartitionUpdate.SeedUpdateCooldown,
                moduleConfig.PartitionUpdate.SeedTriggerMoveThreshold);

            riskDrivenSeedUpdater = new RiskDrivenSeedUpdater(
                moduleConfig.PartitionUpdate.SeedTrendWeight,
                moduleConfig.PartitionUpdate.SeedNeighborWeight,
                moduleConfig.PartitionUpdate.SeedAnchorWeight,
                moduleConfig.PartitionUpdate.SeedStepLow,
                moduleConfig.PartitionUpdate.SeedStepMedium,
                moduleConfig.PartitionUpdate.SeedStepHigh,
                moduleConfig.PartitionUpdate.MaxSeedShiftPerUpdate,
                moduleConfig.PartitionUpdate.MaxSeedOffsetFromUser);

            partitionUpdateAcceptancePolicy = new PartitionUpdateAcceptancePolicy(
                moduleConfig.PartitionUpdate.PartitionAcceptEpsilon,
                moduleConfig.PartitionUpdate.PartitionCellRiskWeight,
                moduleConfig.PartitionUpdate.PartitionNeighborRiskWeight,
                moduleConfig.PartitionUpdate.PartitionMaxAllowedCellRiskWorsen,
                moduleConfig.PartitionUpdate.PartitionMaxAllowedNeighborRiskWorsen);

            partitionUpdateLogger = new PartitionUpdateLogger();
            partitionUpdateVisualizer = new PartitionUpdateVisualizer();
            partitionUpdateCoordinator = new PartitionUpdateCoordinator(
                totalUserCount,
                voronoiPartitioner,
                partitionRiskEvaluator,
                partitionUpdateTriggerEvaluator,
                riskDrivenSeedUpdater,
                partitionUpdateAcceptancePolicy,
                partitionUpdateLogger);

            partitionUpdateStates = new RiskDrivenSeedUpdateState[Mathf.Max(0, totalUserCount)];
            for (int i = 0; i < partitionUpdateStates.Length; i++)
            {
                partitionUpdateStates[i] = new RiskDrivenSeedUpdateState();
            }

            latestPartitionUpdateAttempts.Clear();
        }

        private void InitializeLocalSafeTargetSelection()
        {
            var config = new LocalSafeTargetConfig
            {
                FanHalfAngleDegrees = moduleConfig.LocalSafeTarget.FanHalfAngleDeg,
                SearchRadiusMin = moduleConfig.LocalSafeTarget.SearchRadiusMin,
                SearchRadiusMax = moduleConfig.LocalSafeTarget.SearchRadiusMax,
                BoundaryBufferMin = moduleConfig.LocalSafeTarget.BoundaryBufferMin,
                BoundaryBufferMax = moduleConfig.LocalSafeTarget.BoundaryBufferMax,
                GridResolutionMin = moduleConfig.LocalSafeTarget.GridResolutionMin,
                GridResolutionMax = moduleConfig.LocalSafeTarget.GridResolutionMax,
                WeightBoundaryDist = moduleConfig.LocalSafeTarget.WeightBoundary,
                WeightOccupancyDist = moduleConfig.LocalSafeTarget.WeightOccupancy,
                WeightDistancePenalty = moduleConfig.LocalSafeTarget.WeightDistance,
                SampleDensityPerM2 = moduleConfig.LocalSafeTarget.SampleDensityPerM2,
                MinSamplesPerUser = moduleConfig.LocalSafeTarget.MinSamples,
                MaxSamplesPerUser = moduleConfig.LocalSafeTarget.MaxSamples
            };

            localSafeTargetSelector = new LocalSafeTargetSelector(config);
            latestLocalTargets.Clear();

            // Initialize visualizer
            localSafeTargetVisualizer = gameObject.GetComponent<RDW.Coordination.Visualization.LocalSafeTargetVisualizer>();
            if (localSafeTargetVisualizer == null)
            {
                localSafeTargetVisualizer = gameObject.AddComponent<RDW.Coordination.Visualization.LocalSafeTargetVisualizer>();
            }
            localSafeTargetVisualizer.Initialize(stateCollector);
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

            if (partitionUpdateLogger != null)
            {
                partitionUpdateLogger.ResetSession();
            }

            latestPartitionRiskFrame = null;
            latestPartitionUpdateAttempts.Clear();
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
            AdvanceSimulationClock();
            UpdateVelocityAndPrediction(frameState);
            UpdateEpisodeProgress();

            if (TryHandleEpisodeEnd())
                return;

            UpdatePartitionRiskAndTargets(frameState);
            SimulateRedirectedUsers();
        }

        private void AdvanceSimulationClock()
        {
            simulationFrameIndex++;
            simulationElapsedTime += Time.fixedDeltaTime;
        }

        private void UpdateVelocityAndPrediction(FrameState frameState)
        {
            velocityPredictor.AlphaMax = moduleConfig.Velocity.AlphaMax;
            velocityPredictor.VMax = moduleConfig.Velocity.VMax;
            velocityPredictor.VelocityToOffsetFactor = moduleConfig.Velocity.VelocityToOffsetFactor;
            velocityPredictor.MaxVelocityOffsetDist = moduleConfig.Velocity.MaxVelocityOffsetDist;
            velocityPredictor.StopThreshold = moduleConfig.Velocity.StopThreshold;
            velocityPredictor.Update(frameState.PhysicalUsers, Time.fixedDeltaTime, moduleConfig.Velocity.UseVelocityOffset);

            if (moduleConfig.PredictiveOccupancy.Enable && predictiveOccupancyBuilder != null)
            {
                latestPredictedOccupancyFrame = predictiveOccupancyBuilder.BuildFrame(
                    frameState.PhysicalUsers,
                    velocityPredictor,
                    moduleConfig.PredictiveOccupancy.Horizons,
                    predictiveUncertaintyModel,
                    simulationFrameIndex,
                    simulationElapsedTime);
            }

            if (predictionEvaluator != null && predictionEvaluator.ShouldSample(simulationFrameIndex))
            {
                predictionEvaluator.RegisterPredictions(
                    GetCurrentEpisodeId(),
                    simulationFrameIndex,
                    simulationElapsedTime,
                    frameState.PhysicalUsers,
                    velocityPredictor,
                    moduleConfig.Prediction.TrajectoryModeLabel
                );
            }

            if (predictionEvaluator != null)
            {
                predictionEvaluator.ResolveDuePredictions(simulationElapsedTime, frameState.PhysicalUsers);
            }
        }

        private void UpdateEpisodeProgress()
        {
            episodeService.SimulationCountMax = SimulationCount_max;
            episodeService.TargetDistancePerUser = TargetDistancePerUser;
            episodeService.Tick(stateCollector);
        }

        private bool TryHandleEpisodeEnd()
        {
            if (!episodeService.ShouldEndEpisode(stateCollector))
                return false;

            Debug.Log($"Episode Finished. Total Dist: {stateCollector.CurrentEpisodeTotalDistance}");

            int endedEpisodeId = GetCurrentEpisodeId();
            bool isLastEpisodeInExperiment = endedEpisodeId >= SimulationCount_max;

            HandlePredictionEpisodeEnd(endedEpisodeId);

            episodeService.FinalizeEpisode(stateCollector);

            HandlePredictionExperimentEndIfNeeded(isLastEpisodeInExperiment);

            ResetEpisode();
            return true;
        }

        private void HandlePredictionEpisodeEnd(int endedEpisodeId)
        {
            if (predictionEvaluator == null || !moduleConfig.Prediction.EnableEvaluation)
                return;

            List<EpisodePredictionSummary> episodeSummaries = predictionEvaluator.EndEpisode(true);
            Debug.Log("[PredictionEvaluator] " + predictionEvaluator.BuildReadableSummary(episodeSummaries));

            if (horizonSelector != null)
            {
                HorizonSelectionResult selection = horizonSelector.SelectTrustedHorizons(episodeSummaries, moduleConfig.Prediction.TrajectoryModeLabel);
                Debug.Log($"[HorizonSelector] Trusted horizons: {string.Join(", ", selection.TrustedHorizons)} | Untrusted horizons: {string.Join(", ", selection.UntrustedHorizons)}");
            }

            if (moduleConfig.Prediction.ExportResolvedSamples)
            {
                EnsurePredictionExportFolderExists();

                string episodeSamplePath = Path.Combine(
                    predictionExperimentFolderPath,
                    $"prediction_eval_episode_{endedEpisodeId:000}.csv");
                predictionEvaluator.ExportEpisodeSamplesCsv(episodeSamplePath);
            }

            predictionEvaluator.ClearEpisodeData();
        }

        private void HandlePredictionExperimentEndIfNeeded(bool isLastEpisodeInExperiment)
        {
            if (predictionEvaluator == null || !moduleConfig.Prediction.EnableEvaluation || !isLastEpisodeInExperiment)
                return;

            if (moduleConfig.Prediction.ExportResolvedSamples)
            {
                EnsurePredictionExportFolderExists();

                string overallSummaryPath = Path.Combine(
                    predictionExperimentFolderPath,
                    "prediction_eval_overall_summary.csv");
                predictionEvaluator.ExportOverallSummaryCsv(overallSummaryPath);
            }

            predictionEvaluator.ClearAll();

            if (moduleConfig.Prediction.ExportResolvedSamples)
            {
                InitializePredictionExportFolderForExperiment();
            }
        }

        private void EnsurePredictionExportFolderExists()
        {
            if (string.IsNullOrEmpty(predictionExperimentFolderPath) || !Directory.Exists(predictionExperimentFolderPath))
            {
                InitializePredictionExportFolderForExperiment();
            }
        }

        private void UpdatePartitionRiskAndTargets(FrameState frameState)
        {
            IReadOnlyList<Vector3> offsetResult = velocityPredictor.GetOffsets();
            latestPartitionResult = voronoiPartitioner.Build(
                frameState,
                offsetResult,
                Time.deltaTime,
                moduleConfig.Velocity.UseVelocityOffset,
                physicalRoom_width_half,
                physicalRoom_height_half);

            ApplyPartitionResult(latestPartitionResult);

            latestPartitionRiskFrame = null;
            if (moduleConfig.PartitionRisk.EnableEvaluation && partitionRiskEvaluator != null)
            {
                latestPartitionRiskFrame = partitionRiskEvaluator.Evaluate(
                    simulationFrameIndex,
                    simulationElapsedTime,
                    latestPartitionResult,
                    latestPredictedOccupancyFrame,
                    frameState.PhysicalUsers,
                    physicalRoom_width_half,
                    physicalRoom_height_half);

                if (moduleConfig.PartitionUpdate.EnableRiskDrivenUpdate)
                {
                    TryApplyRiskDrivenPartitionUpdate(frameState, offsetResult);
                }

                if (moduleConfig.PartitionRisk.EnableLogging && partitionRiskLogger != null)
                {
                    partitionRiskLogger.TryLogFrame(latestPartitionRiskFrame);
                }
            }

            if (moduleConfig.LocalSafeTarget.EnableSelection && localSafeTargetSelector != null && latestPartitionResult != null)
            {
                TrySelectLocalSafeTargets(frameState, latestPartitionResult, latestPartitionRiskFrame, latestPredictedOccupancyFrame);
                ApplyLocalTargetsToRedirectors();

                if (moduleConfig.LocalSafeTarget.EnableVisualization && localSafeTargetVisualizer != null)
                {
                    localSafeTargetVisualizer.UpdateTargets(latestLocalTargets);
                    localSafeTargetVisualizer.UpdateCellVertices(dic_AreaSegmentsVertex);
                }
            }
        }

        private static void SimulateRedirectedUsers()
        {
            RDWSimulationManager.instance.SimulateRDW();
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

        private void SyncPartitionUpdateStatesWithCurrentSeeds()
        {
            if (partitionUpdateTriggerEvaluator == null || partitionUpdateStates == null)
                return;

            List<Vector2> currentSeeds = voronoiPartitioner != null ? voronoiPartitioner.GetSeedPointsCopy() : null;
            partitionUpdateTriggerEvaluator.ResetAll(partitionUpdateStates, currentSeeds);
        }

        private void TryApplyRiskDrivenPartitionUpdate(FrameState frameState, IReadOnlyList<Vector3> offsetResult)
        {
            if (partitionUpdateCoordinator == null)
                return;

            partitionUpdateCoordinator.Execute(
                moduleConfig.PartitionUpdate.EnableRiskDrivenUpdate,
                frameState,
                offsetResult,
                velocityPredictor,
                latestPredictedOccupancyFrame,
                partitionUpdateStates,
                latestPartitionUpdateAttempts,
                simulationFrameIndex,
                simulationElapsedTime,
                Time.deltaTime,
                moduleConfig.Velocity.UseVelocityOffset,
                physicalRoom_width_half,
                physicalRoom_height_half,
                ref latestPartitionResult,
                ref latestPartitionRiskFrame,
                ApplyPartitionResult);
        }

        /// <summary>
        /// Phase 5: Select local safe targets for each user within their Voronoi cell
        /// </summary>
        private void TrySelectLocalSafeTargets(
            FrameState frameState,
            PartitionResult partitionResult,
            PartitionRiskFrame riskFrame,
            PredictedOccupancyFrame occupancyFrame)
        {
            if (localSafeTargetSelector == null || frameState == null || partitionResult == null || userRuntimeStateProvider == null)
                return;

            latestLocalTargets.Clear();
            List<Vector2> allUserPositions = userRuntimeStateProvider.GetAllPhysicalPositions(frameState);
            var occupancyBands = occupancyFrame?.Bands ?? new List<PredictedOccupancyBand>();

            // Select target for each user
            for (int userId = 0; userId < totalUserCount; userId++)
            {
                if (!userRuntimeStateProvider.TryGetUserRuntimeState(frameState, userId, out UserRuntimeState runtimeState))
                    continue;

                if (runtimeState.CellVertices.Count < 3)
                    continue;

                // Run selector
                var result = localSafeTargetSelector.SelectTargetForUser(
                    userId,
                    runtimeState.PhysicalPosition,
                    runtimeState.PhysicalHeading,
                    runtimeState.CellVertices,
                    runtimeState.CellCentroid,
                    allUserPositions,
                    occupancyBands,
                    riskFrame);

                latestLocalTargets[userId] = result;
            }
        }

        /// <summary>
        /// Unified runtime query interface for other modules.
        /// Returns physical/virtual pose and current cell geometry for one user.
        /// </summary>
        public bool TryGetUserRuntimeState(int userId, out UserRuntimeState runtimeState)
        {
            runtimeState = null;

            if (userRuntimeStateProvider == null)
                return false;

            return userRuntimeStateProvider.TryGetLiveUserRuntimeState(userId, totalUserCount, out runtimeState);
        }

        /// <summary>
        /// Apply selected safe targets to steering redirectors
        /// </summary>
        private void ApplyLocalTargetsToRedirectors()
        {
            var redirectedUnits = RDWSimulationManager.instance.GetRedirectedUnits;
            if (redirectedUnits == null)
                return;

            foreach (var kvp in latestLocalTargets)
            {
                int userId = kvp.Key;
                LocalTargetResult targetResult = kvp.Value;

                if (userId < 0 || userId >= redirectedUnits.Length)
                    continue;

                var redirectedUnit = redirectedUnits[userId];
                if (redirectedUnit == null)
                    continue;

                var redirector = redirectedUnit.GetRedirector();
                if (redirector == null)
                    continue;

                if (redirector is S2CRedirector steerToTargetRedir)
                {
                    if (targetResult.HasValidTarget)
                        steerToTargetRedir.SetExternalSafeTarget(targetResult.TargetPosition);
                    else
                        steerToTargetRedir.ClearExternalSafeTarget();
                }
                else if (redirector is LocalSafeCurvatureRedirector localSafeCurvatureRedirector)
                {
                    if (targetResult.HasValidTarget)
                        localSafeCurvatureRedirector.SetExternalSafeTarget(targetResult.TargetPosition);
                    else
                        localSafeCurvatureRedirector.ClearExternalSafeTarget();
                }
                // Note: APFRedirector already uses cell vertices, doesn't need explicit target
            }
        }

        private void FixedUpdate()
        {
            ProcessStep();
        }

        private void OnDrawGizmos()
        {
            if (moduleConfig.PredictiveOccupancy.EnableGizmos && predictiveOccupancyVisualizer != null && latestPredictedOccupancyFrame != null)
            {
                predictiveOccupancyVisualizer.Draw(latestPredictedOccupancyFrame, moduleConfig.PredictiveOccupancy.GizmoHeight);
            }

            if (moduleConfig.PartitionRisk.EnableVisualization && partitionRiskVisualizer != null && latestPartitionRiskFrame != null && stateCollector != null)
            {
                partitionRiskVisualizer.Draw(latestPartitionRiskFrame, stateCollector.PhysicalUsers);
            }

            if (moduleConfig.PartitionUpdate.EnableVisualization && partitionUpdateVisualizer != null)
            {
                partitionUpdateVisualizer.Draw(latestPartitionUpdateAttempts, latestPartitionResult);
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

            if (!TryResolvePhysicalRoomDimensions(RDWSimulationManager.instance.simulationSetting, out physicalRoom_width_half, out physicalRoom_height_half))
            {
                Debug.LogWarning("Failed to resolve room dimensions from SpaceSetting. Keep previous room bounds.");
            }

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
            SyncPartitionUpdateStatesWithCurrentSeeds();

            // [FIX] Update pre-positions to actual user positions to avoid initial distance jump
            stateCollector.SyncPreAndCurrentToPhysicalUsers(totalUserCount);
            episodeService.BeginEpisode(stateCollector);

            InitializeInfoQueues();
        }

        /// </summary>
        public void InitializeInfoQueues()
        {
        }

        private bool TryResolvePhysicalRoomDimensions(SimulationSetting simulationSetting, out float halfWidth, out float halfHeight)
        {
            halfWidth = physicalRoom_width_half;
            halfHeight = physicalRoom_height_half;

            if (simulationSetting == null || simulationSetting.realSpaceSetting == null || simulationSetting.realSpaceSetting.spaceObjectSetting == null)
                return false;

            List<Vector2> vertices = simulationSetting.realSpaceSetting.spaceObjectSetting.vertices;
            if (vertices == null || vertices.Count == 0)
                return false;

            float resolvedHalfWidth = 0f;
            float resolvedHalfHeight = 0f;

            for (int i = 0; i < vertices.Count; i++)
            {
                resolvedHalfWidth = Mathf.Max(resolvedHalfWidth, Mathf.Abs(vertices[i].x));
                resolvedHalfHeight = Mathf.Max(resolvedHalfHeight, Mathf.Abs(vertices[i].y));
            }

            halfWidth = resolvedHalfWidth;
            halfHeight = resolvedHalfHeight;
            return true;
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
