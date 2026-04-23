using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityEngine.UI;
using _GCM.PartitionUpdate;
using RDW.Coordination.LocalSafeTarget;
namespace _GCM
{
    public class GlobalCoordinationManager : MonoBehaviour
    {
        private const float BI_RECOVERABILITY_PRECHECK_HORIZON_SECONDS = 1.5f;
        private const int BI_RECOVERABILITY_PRECHECK_SAMPLE_COUNT = 60;
        private const float BI_RECOVERABILITY_DIRECTION_EPSILON = 0.0001f;
        private const float BI_RECOVERABILITY_CLOSING_SPEED_THRESHOLD = 0.05f;
        private const float PROACTIVE_ARBITRATION_M_EPSILON = 0.02f;
        private const float PROACTIVE_ARBITRATION_C_EPSILON = 0.05f;
        private const float SIMPLE_PROACTIVE_TRIGGER_DISTANCE = 2.0f;
        private const float SIMPLE_PROACTIVE_TRIGGER_CLOSING_SPEED_EPSILON = 0.05f;

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
        [Tooltip("Use fixed seed file for comparison experiments when enabled; use random experiments when disabled.")]
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

        #region Module Runtime
        private VelocityPredictor velocityPredictor;
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

        private LocalSafeTargetSelector localSafeTargetSelector;
        private Dictionary<int, LocalTargetResult> latestLocalTargets = new Dictionary<int, LocalTargetResult>();
        private string localTargetExperimentFolderPath = string.Empty;
        private RDW.Coordination.Visualization.LocalSafeTargetVisualizer localSafeTargetVisualizer;

        private struct ProactiveIntentCandidate
        {
            public int SelectedUserId;
            public int OtherUserId;
            public Vector2 ResetDirection;
            public float KeepMargin;
            public float SelectedM;
            public float SelectedCSelf;
        }
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

        private void OnValidate()
        {
            EnsureModuleConfig();
        }

        private void EnsureModuleConfig()
        {
            if (moduleConfig == null) moduleConfig = new GlobalCoordinationModuleConfig();
            if (moduleConfig.Velocity == null) moduleConfig.Velocity = new VelocityModuleConfig();
            if (moduleConfig.Prediction == null) moduleConfig.Prediction = new PredictionModuleConfig();
            if (moduleConfig.PredictiveOccupancy == null) moduleConfig.PredictiveOccupancy = new PredictiveOccupancyModuleConfig();
            if (moduleConfig.PartitionRisk == null) moduleConfig.PartitionRisk = new PartitionRiskModuleConfig();
            if (moduleConfig.PartitionUpdate == null) moduleConfig.PartitionUpdate = new PartitionUpdateModuleConfig();
            if (moduleConfig.LocalSafeTarget == null) moduleConfig.LocalSafeTarget = new LocalSafeTargetModuleConfig();
        }

        private void Awake()
        {
            EnsureModuleConfig();
            instance = this;

            //Academy.Instance.AutomaticSteppingEnabled = false;
        }

        private void Start()
        {
            EnsureModuleConfig();

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
            InitializePartitionUpdateLayer();
            InitializeLocalSafeTargetSelection();

            // Manually trigger the first episode during startup.
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
            VelocityModuleConfig velocityConfig = moduleConfig.Velocity;
            velocityPredictor = new VelocityPredictor(totalUserCount)
            {
                AlphaMax = velocityConfig.AlphaMax,
                VMax = velocityConfig.VMax,
                VelocityToOffsetFactor = velocityConfig.VelocityToOffsetFactor,
                MaxVelocityOffsetDist = velocityConfig.MaxVelocityOffsetDist,
                StopThreshold = velocityConfig.StopThreshold
            };
        }

        private void InitializePredictionEvaluator()
        {
            PredictionModuleConfig predictionConfig = moduleConfig.Prediction;
            predictionEvaluator = new PredictionEvaluator
            {
                EnableEvaluation = predictionConfig.EnableEvaluation,
                ExportResolvedSamples = predictionConfig.ExportResolvedSamples,
                SampleEveryNFrames = predictionConfig.SampleEveryNFrames
            };

            predictionEvaluator.Horizons.Clear();
            if (predictionConfig.EvaluationHorizons != null)
            {
                for (int i = 0; i < predictionConfig.EvaluationHorizons.Count; i++)
                {
                    if (predictionConfig.EvaluationHorizons[i] > 0f)
                        predictionEvaluator.Horizons.Add(predictionConfig.EvaluationHorizons[i]);
                }
            }

            predictionEvaluator.ClearAll();
        }

        private void InitializePredictiveOccupancy()
        {
            PredictiveOccupancyModuleConfig occupancyConfig = moduleConfig.PredictiveOccupancy;
            predictiveOccupancyBuilder = new PredictiveOccupancyBuilder();
            predictiveUncertaintyModel = new PredictionUncertaintyModel();
            latestPredictedOccupancyFrame = new PredictedOccupancyFrame();

            predictiveOccupancyVisualizer = new PredictiveOccupancyVisualizer();
            predictiveOccupancyVisualizer.ColorResolver = userIndex =>
                PartitionVisualizer.ResolvePartitionColor(userIndex, PartitionedSpaceMaterials);

            horizonSelector = new PredictionHorizonSelector
            {
                MeanErrorThreshold = occupancyConfig.TrustedHorizonMeanErrorThreshold,
                StdErrorThreshold = occupancyConfig.TrustedHorizonStdErrorThreshold,
                MaxCensoredCount = occupancyConfig.TrustedHorizonMaxCensoredCount
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
            PartitionRiskModuleConfig riskConfig = moduleConfig.PartitionRisk;
            PartitionRiskConfig config = new PartitionRiskConfig
            {
                OutputEveryNFrames = Mathf.Max(1, riskConfig.OutputEveryNFrames),
                CellBoundarySafeClearance = riskConfig.CellBoundarySafeClearance,
                PhysicalBoundarySafeClearance = riskConfig.PhysicalBoundarySafeClearance,
                PairSafeSeparation = riskConfig.PairSafeSeparation,
                SeedDeltaReference = riskConfig.SeedDeltaReference,
                AdjacencyRiskThreshold = riskConfig.AdjacencyThreshold,
                DominantRiskNoneThreshold = riskConfig.DominantNoneThreshold,
                DominantRiskMixedGap = riskConfig.DominantMixedGap,
                Weights = new RiskWeights
                {
                    CellBoundaryWeight = riskConfig.WeightCellBoundary,
                    UserWeight = riskConfig.WeightUser,
                    SmoothWeight = riskConfig.WeightSmooth
                }
            };

            partitionRiskEvaluator = new PartitionRiskEvaluator(config);
            partitionRiskLogger = new PartitionRiskLogger(config.OutputEveryNFrames);
            partitionRiskVisualizer = new PartitionRiskVisualizer();
            latestPartitionRiskFrame = null;
        }

        private void InitializePartitionUpdateLayer()
        {
            PartitionUpdateModuleConfig updateConfig = moduleConfig.PartitionUpdate;
            partitionUpdateTriggerEvaluator = new PartitionUpdateTriggerEvaluator(
                updateConfig.TauCellRiskEnter,
                updateConfig.TauCellRiskExit,
                updateConfig.TauNeighborRiskEnter,
                updateConfig.TauNeighborRiskExit,
                updateConfig.PersistFramesCell,
                updateConfig.PersistFramesNeighbor,
                updateConfig.SeedUpdateCooldown,
                updateConfig.SeedTriggerMoveThreshold);

            riskDrivenSeedUpdater = new RiskDrivenSeedUpdater(
                updateConfig.SeedTrendWeight,
                updateConfig.SeedNeighborWeight,
                updateConfig.SeedAnchorWeight,
                updateConfig.SeedStepLow,
                updateConfig.SeedStepMedium,
                updateConfig.SeedStepHigh,
                updateConfig.MaxSeedShiftPerUpdate,
                updateConfig.MaxSeedOffsetFromUser);

            partitionUpdateAcceptancePolicy = new PartitionUpdateAcceptancePolicy(
                updateConfig.PartitionAcceptEpsilon,
                updateConfig.PartitionCellRiskWeight,
                updateConfig.PartitionNeighborRiskWeight,
                updateConfig.PartitionMaxAllowedCellRiskWorsen,
                updateConfig.PartitionMaxAllowedNeighborRiskWorsen);

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
            LocalSafeTargetModuleConfig localConfig = moduleConfig.LocalSafeTarget;
            var config = new LocalSafeTargetConfig
            {
                FanHalfAngleDegrees = localConfig.FanHalfAngleDeg,
                SearchRadiusMin = localConfig.SearchRadiusMin,
                SearchRadiusMax = localConfig.SearchRadiusMax,
                BoundaryBufferMin = localConfig.BoundaryBufferMin,
                BoundaryBufferMax = localConfig.BoundaryBufferMax,
                GridResolutionMin = localConfig.GridResolutionMin,
                GridResolutionMax = localConfig.GridResolutionMax,
                WeightBoundaryDist = localConfig.WeightBoundaryDist,
                WeightOccupancyDist = localConfig.WeightOccupancyDist,
                WeightDistancePenalty = localConfig.WeightDistancePenalty,
                SampleDensityPerM2 = localConfig.SampleDensityPerM2,
                MinSamplesPerUser = localConfig.MinSamplesPerUser,
                MaxSamplesPerUser = localConfig.MaxSamplesPerUser,
                AngleSampleCount = localConfig.AngleSamples,
                RadiusSampleCount = localConfig.RadiusSamples,
                WeightSelfOpen = localConfig.WeightSelfOpen,
                WeightFrontMargin = localConfig.WeightFrontMargin,
                WeightNeighborImpact = localConfig.WeightNeighborImpact,
                WeightHeadingDeviation = localConfig.WeightHeadingDeviation,
                NeighborSafetyBuffer = localConfig.NeighborSafetyBuffer
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
            if (episodeService != null && episodeService.IsExperimentCompleted)
            {
                if (RDWSimulationManager.instance != null)
                {
                    RDWSimulationManager.instance.BStart = false;
                }
                return;
            }

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

            BidirectionalCollisionRecoverabilityEvaluator.ResetTemporalState();
            BidirectionalCollisionRecoverabilityLogger.ResetSession();
            BidirectionalCollisionDebugVisualizer.ResetSession();

            latestPartitionRiskFrame = null;
            latestPartitionUpdateAttempts.Clear();
        }

        /// <summary>
        /// Main simulation loop. 
        /// This logic now runs in FixedUpdate to drive the simulation.
        /// </summary>
        private void ProcessStep()
        {
            if (!RDWSimulationManager.instance.BStart)
                return;

            if (episodeService != null && episodeService.IsExperimentCompleted)
            {
                RDWSimulationManager.instance.BStart = false;
                return;
            }

            // Safety Check: ensure users are initialized before processing calculation
            if (stateCollector == null || !stateCollector.HasReadyUsers(totalUserCount)) 
                return;

            FrameState frameState = stateCollector.CaptureFrameState(totalUserCount);
            VelocityModuleConfig velocityConfig = moduleConfig.Velocity;
            PredictionModuleConfig predictionConfig = moduleConfig.Prediction;
            PredictiveOccupancyModuleConfig occupancyConfig = moduleConfig.PredictiveOccupancy;
            PartitionRiskModuleConfig riskConfig = moduleConfig.PartitionRisk;
            PartitionUpdateModuleConfig updateConfig = moduleConfig.PartitionUpdate;
            LocalSafeTargetModuleConfig localSafeTargetConfig = moduleConfig.LocalSafeTarget;

            AdvanceSimulationClock();
            UpdateVelocityTracker(frameState, velocityConfig);
            UpdatePredictiveOccupancy(frameState, occupancyConfig);
            UpdatePredictionSampling(frameState, predictionConfig);

            if (TryEndEpisodeIfNeeded(predictionConfig))
                return;

            EvaluatePartitionAndRisk(frameState, velocityConfig, riskConfig, updateConfig);
            UpdateLocalSafeTargetModule(frameState, localSafeTargetConfig);

            // Removed bEnable_InitPhyUserPosUni one-frame lock logic
            // Removed bOneframetimerblockVoronoi logic

            TryEvaluateBidirectionalRecoverabilityCandidates(latestPartitionResult);
            BidirectionalCollisionDebugVisualizer.Tick();

            /// Simulate the designated redirection controller
            RDWSimulationManager.instance.SimulateRDW();

            // AddRewards(); // Removed
        }

        private void AdvanceSimulationClock()
        {
            simulationFrameIndex++;
            simulationElapsedTime += Time.fixedDeltaTime;
        }

        private void UpdateVelocityTracker(FrameState frameState, VelocityModuleConfig velocityConfig)
        {
            velocityPredictor.AlphaMax = velocityConfig.AlphaMax;
            velocityPredictor.VMax = velocityConfig.VMax;
            velocityPredictor.VelocityToOffsetFactor = velocityConfig.VelocityToOffsetFactor;
            velocityPredictor.MaxVelocityOffsetDist = velocityConfig.MaxVelocityOffsetDist;
            velocityPredictor.StopThreshold = velocityConfig.StopThreshold;
            velocityPredictor.Update(frameState.PhysicalUsers, Time.fixedDeltaTime, velocityConfig.UseVelocityOffset);
        }

        private void UpdatePredictiveOccupancy(FrameState frameState, PredictiveOccupancyModuleConfig occupancyConfig)
        {
            if (!occupancyConfig.Enable || predictiveOccupancyBuilder == null)
                return;

            latestPredictedOccupancyFrame = predictiveOccupancyBuilder.BuildFrame(
                frameState.PhysicalUsers,
                velocityPredictor,
                occupancyConfig.Horizons,
                predictiveUncertaintyModel,
                simulationFrameIndex,
                simulationElapsedTime);
        }

        private void UpdatePredictionSampling(FrameState frameState, PredictionModuleConfig predictionConfig)
        {
            if (predictionEvaluator == null)
                return;

            if (predictionEvaluator.ShouldSample(simulationFrameIndex))
            {
                predictionEvaluator.RegisterPredictions(
                    GetCurrentEpisodeId(),
                    simulationFrameIndex,
                    simulationElapsedTime,
                    frameState.PhysicalUsers,
                    velocityPredictor,
                    predictionConfig.TrajectoryModeLabel);
            }

            predictionEvaluator.ResolveDuePredictions(simulationElapsedTime, frameState.PhysicalUsers);
        }

        private bool TryEndEpisodeIfNeeded(PredictionModuleConfig predictionConfig)
        {
            episodeService.SimulationCountMax = SimulationCount_max;
            episodeService.TargetDistancePerUser = TargetDistancePerUser;
            episodeService.Tick(stateCollector);

            if (!episodeService.ShouldEndEpisode(stateCollector))
                return false;

            Debug.Log($"Episode Finished. Total Dist: {stateCollector.CurrentEpisodeTotalDistance}");

            int endedEpisodeId = GetCurrentEpisodeId();
            bool isLastEpisodeInExperiment = endedEpisodeId >= SimulationCount_max;

            if (predictionEvaluator != null && predictionConfig.EnableEvaluation)
            {
                List<EpisodePredictionSummary> episodeSummaries = predictionEvaluator.EndEpisode(true);
                Debug.Log("[PredictionEvaluator] " + predictionEvaluator.BuildReadableSummary(episodeSummaries));

                if (horizonSelector != null)
                {
                    HorizonSelectionResult selection = horizonSelector.SelectTrustedHorizons(episodeSummaries, predictionConfig.TrajectoryModeLabel);
                    Debug.Log($"[HorizonSelector] Trusted horizons: {string.Join(", ", selection.TrustedHorizons)} | Untrusted horizons: {string.Join(", ", selection.UntrustedHorizons)}");
                }

                if (predictionConfig.ExportResolvedSamples)
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

            if (predictionEvaluator != null && predictionConfig.EnableEvaluation && isLastEpisodeInExperiment)
            {
                if (predictionConfig.ExportResolvedSamples)
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

                if (predictionConfig.ExportResolvedSamples)
                {
                    InitializePredictionExportFolderForExperiment();
                }
            }

            if (episodeService.IsExperimentCompleted)
            {
                RDWSimulationManager.instance.BStart = false;
                Debug.Log($"Experiment completed at episode {episodeService.CurrentSimulationCount}/{episodeService.SimulationCountMax}. Simulation stopped.");
                return true;
            }

            ResetEpisode();
            return true;
        }

        private void EvaluatePartitionAndRisk(
            FrameState frameState,
            VelocityModuleConfig velocityConfig,
            PartitionRiskModuleConfig riskConfig,
            PartitionUpdateModuleConfig updateConfig)
        {
            IReadOnlyList<Vector3> offsetResult = velocityPredictor.GetOffsets();
            latestPartitionResult = voronoiPartitioner.Build(
                frameState,
                offsetResult,
                Time.deltaTime,
                velocityConfig.UseVelocityOffset,
                physicalRoom_width_half,
                physicalRoom_height_half);

            ApplyPartitionResult(latestPartitionResult);

            latestPartitionRiskFrame = null;
            if (!riskConfig.EnableEvaluation || partitionRiskEvaluator == null)
                return;

            latestPartitionRiskFrame = partitionRiskEvaluator.Evaluate(
                simulationFrameIndex,
                simulationElapsedTime,
                latestPartitionResult,
                latestPredictedOccupancyFrame,
                frameState.PhysicalUsers,
                physicalRoom_width_half,
                physicalRoom_height_half);

            if (updateConfig.EnableRiskDrivenUpdate)
            {
                TryApplyRiskDrivenPartitionUpdate(frameState, offsetResult);
            }

            if (riskConfig.EnableLogging && partitionRiskLogger != null)
            {
                partitionRiskLogger.TryLogFrame(latestPartitionRiskFrame);
            }
        }

        private void UpdateLocalSafeTargetModule(FrameState frameState, LocalSafeTargetModuleConfig localSafeTargetConfig)
        {
            if (!localSafeTargetConfig.EnableSelection || localSafeTargetSelector == null || latestPartitionResult == null)
                return;

            TrySelectLocalSafeTargets(frameState, latestPartitionResult, latestPartitionRiskFrame, latestPredictedOccupancyFrame);
            ApplyLocalTargetsToRedirectors();

            if (localSafeTargetConfig.EnableVisualization && localSafeTargetVisualizer != null)
            {
                localSafeTargetVisualizer.UpdateTargets(latestLocalTargets);
                localSafeTargetVisualizer.UpdateCellVertices(dic_AreaSegmentsVertex);
            }
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

        private void TryEvaluateBidirectionalRecoverabilityCandidates(PartitionResult partitionResult)
        {
            RDWSimulationManager simulationManager = RDWSimulationManager.instance;
            if (simulationManager == null || simulationManager.simulationSetting == null)
                return;

            bool proactiveEnabled = simulationManager.simulationSetting.enableProactiveUserResetArbitration &&
                                    simulationManager.simulationSetting.bAllowUserReset;
            bool useSimpleProactiveTrigger = simulationManager.simulationSetting.useSimpleProactiveTrigger;
            bool shouldRunPrecheck = proactiveEnabled ||
                                     simulationManager.simulationSetting.enableBiRecoverabilityLogging ||
                                     BidirectionalCollisionDebugVisualizer.IsEnabled();
            if (!shouldRunPrecheck)
                return;

            if (partitionResult == null || partitionResult.CellAdjacency == null)
                return;

            RedirectedUnit[] units = simulationManager.GetRedirectedUnits;
            if (units == null || units.Length == 0)
                return;

            if (proactiveEnabled)
            {
                for (int i = 0; i < units.Length; i++)
                {
                    units[i]?.ClearProactiveUserResetIntent();
                }
            }

            Dictionary<int, ProactiveIntentCandidate> selectedCandidateByUser = proactiveEnabled
                ? new Dictionary<int, ProactiveIntentCandidate>()
                : null;

            for (int userId = 0; userId < units.Length; userId++)
            {
                if (!partitionResult.CellAdjacency.TryGetValue(userId, out HashSet<int> adjacentUsers) || adjacentUsers == null)
                    continue;

                RedirectedUnit unitA = units[userId];
                if (unitA == null || unitA.GetRealUser() == null)
                    continue;

                foreach (int adjacentUserId in adjacentUsers)
                {
                    if (adjacentUserId <= userId || adjacentUserId < 0 || adjacentUserId >= units.Length)
                        continue;

                    RedirectedUnit unitB = units[adjacentUserId];
                    if (unitB == null || unitB.GetRealUser() == null)
                        continue;

                    Vector2 movementA = unitA.GetLastMovementDirection();
                    Vector2 movementB = unitB.GetLastMovementDirection();
                    if (movementA.sqrMagnitude <= BI_RECOVERABILITY_DIRECTION_EPSILON ||
                        movementB.sqrMagnitude <= BI_RECOVERABILITY_DIRECTION_EPSILON)
                    {
                        continue;
                    }

                    float speedA = Mathf.Max(unitA.GetResetter().GetTranslationSpeed(), 0.0f);
                    float speedB = Mathf.Max(unitB.GetResetter().GetTranslationSpeed(), 0.0f);
                    Vector2 offsetAB = unitB.GetRealUser().transform2D.localPosition - unitA.GetRealUser().transform2D.localPosition;
                    float closingSpeed = ResolveClosingSpeedFromKinematics(offsetAB, movementA.normalized * speedA, movementB.normalized * speedB);
                    if (closingSpeed <= BI_RECOVERABILITY_CLOSING_SPEED_THRESHOLD)
                        continue;

                    BidirectionalCollisionRecoverabilityAssessment assessment = BidirectionalCollisionRecoverabilityEvaluator.Evaluate(
                        unitA,
                        unitB,
                        BI_RECOVERABILITY_PRECHECK_HORIZON_SECONDS,
                        BI_RECOVERABILITY_PRECHECK_SAMPLE_COUNT,
                        "precheck_candidate",
                        true);

                    bool proactiveTriggerFired = useSimpleProactiveTrigger
                        ? IsSimpleProactiveTriggerSatisfied(unitA, unitB)
                        : assessment.RiskConfirmed;

                    if (!proactiveEnabled || !proactiveTriggerFired)
                        continue;

                    //Debug.Log($"[主动重置触发] pair ({userId}, {adjacentUserId}) trigger={(useSimpleProactiveTrigger ? "Simple" : "RiskConfirmed")}");

                    if (!ProactiveUserResetArbitrationService.TryArbitratePair(
                            unitA,
                            userId,
                            unitB,
                            adjacentUserId,
                            PROACTIVE_ARBITRATION_M_EPSILON,
                            PROACTIVE_ARBITRATION_C_EPSILON,
                            out ProactiveUserResetArbitrationService.ArbitrationResult arbitrationResult))
                    {
                        continue;
                    }

                    ProactiveIntentCandidate candidate = new ProactiveIntentCandidate
                    {
                        SelectedUserId = arbitrationResult.SelectedUnitIndex,
                        OtherUserId = arbitrationResult.OtherUnitIndex,
                        ResetDirection = arbitrationResult.SelectedResetDirection,
                        KeepMargin = arbitrationResult.KeepMargin,
                        SelectedM = arbitrationResult.SelectedM,
                        SelectedCSelf = arbitrationResult.SelectedCSelf
                    };

                    if (!selectedCandidateByUser.TryGetValue(candidate.SelectedUserId, out ProactiveIntentCandidate existing) ||
                        ShouldReplaceProactiveCandidate(existing, candidate))
                    {
                        selectedCandidateByUser[candidate.SelectedUserId] = candidate;
                    }
                }
            }

            if (!proactiveEnabled || selectedCandidateByUser.Count == 0)
                return;

            foreach (KeyValuePair<int, ProactiveIntentCandidate> kvp in selectedCandidateByUser)
            {
                ProactiveIntentCandidate candidate = kvp.Value;
                if (candidate.SelectedUserId < 0 || candidate.SelectedUserId >= units.Length)
                    continue;
                if (candidate.OtherUserId < 0 || candidate.OtherUserId >= units.Length)
                    continue;

                RedirectedUnit selectedUnit = units[candidate.SelectedUserId];
                RedirectedUnit otherUnit = units[candidate.OtherUserId];
                if (selectedUnit == null || selectedUnit.GetRealUser() == null || otherUnit == null || otherUnit.GetRealUser() == null)
                    continue;

                if (!string.Equals(selectedUnit.GetStatus(), "IDLE", StringComparison.Ordinal))
                    continue;

                selectedUnit.SetProactiveUserResetIntent(otherUnit.GetRealUser(), candidate.ResetDirection, true);

            }
        }

        private static float ResolveClosingSpeedFromKinematics(Vector2 offsetAB, Vector2 velocityA, Vector2 velocityB)
        {
            if (offsetAB.sqrMagnitude <= BI_RECOVERABILITY_DIRECTION_EPSILON)
                return 0.0f;

            Vector2 towardB = offsetAB.normalized;
            Vector2 relativeVelocity = velocityB - velocityA;
            return -Vector2.Dot(relativeVelocity, towardB);
        }

        private static bool ShouldReplaceProactiveCandidate(ProactiveIntentCandidate current, ProactiveIntentCandidate incoming)
        {
            const float tolerance = 0.0001f;

            if (incoming.KeepMargin < current.KeepMargin - tolerance)
                return true;
            if (incoming.KeepMargin > current.KeepMargin + tolerance)
                return false;

            if (incoming.SelectedM > current.SelectedM + tolerance)
                return true;
            if (incoming.SelectedM < current.SelectedM - tolerance)
                return false;

            if (incoming.SelectedCSelf < current.SelectedCSelf - tolerance)
                return true;
            if (incoming.SelectedCSelf > current.SelectedCSelf + tolerance)
                return false;

            if (incoming.OtherUserId < current.OtherUserId)
                return true;

            return false;
        }

        private static bool IsSimpleProactiveTriggerSatisfied(RedirectedUnit unitA, RedirectedUnit unitB)
        {
            if (unitA == null || unitB == null || unitA.GetRealUser() == null || unitB.GetRealUser() == null)
                return false;

            Vector2 positionA = unitA.GetRealUser().transform2D.localPosition;
            Vector2 positionB = unitB.GetRealUser().transform2D.localPosition;
            Vector2 offsetAB = positionB - positionA;
            float distance = offsetAB.magnitude;
            if (distance <= BI_RECOVERABILITY_DIRECTION_EPSILON || distance > SIMPLE_PROACTIVE_TRIGGER_DISTANCE)
                return false;

            float speedA = Mathf.Max(unitA.GetResetter().GetTranslationSpeed(), 0.0f);
            float speedB = Mathf.Max(unitB.GetResetter().GetTranslationSpeed(), 0.0f);
            Vector2 movementA = unitA.GetLastMovementDirection();
            Vector2 movementB = unitB.GetLastMovementDirection();
            if (movementA.sqrMagnitude <= BI_RECOVERABILITY_DIRECTION_EPSILON ||
                movementB.sqrMagnitude <= BI_RECOVERABILITY_DIRECTION_EPSILON)
            {
                return false;
            }

            Vector2 velocityA = movementA.normalized * speedA;
            Vector2 velocityB = movementB.normalized * speedB;
            if (Vector2.Dot(velocityA, velocityB) >= 0.0f)
                return false;

            float closingSpeed = ResolveClosingSpeedFromKinematics(offsetAB, velocityA, velocityB);
            return closingSpeed > SIMPLE_PROACTIVE_TRIGGER_CLOSING_SPEED_EPSILON;
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

            PartitionUpdateModuleConfig updateConfig = moduleConfig.PartitionUpdate;
            VelocityModuleConfig velocityConfig = moduleConfig.Velocity;
            partitionUpdateCoordinator.Execute(
                updateConfig.EnableRiskDrivenUpdate,
                frameState,
                offsetResult,
                velocityPredictor,
                latestPredictedOccupancyFrame,
                partitionUpdateStates,
                latestPartitionUpdateAttempts,
                simulationFrameIndex,
                simulationElapsedTime,
                Time.deltaTime,
                velocityConfig.UseVelocityOffset,
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
            if (localSafeTargetSelector == null || frameState == null || partitionResult == null)
                return;

            latestLocalTargets.Clear();

            // Select target for each user
            for (int userId = 0; userId < totalUserCount; userId++)
            {
                // Get user data
                if (userId >= frameState.PhysicalUsers.Count)
                    continue;

                var user = frameState.PhysicalUsers[userId];
                Vector2 userPosition = new Vector2(user.transform.position.x, user.transform.position.z);
                Vector2 userHeading = new Vector2(user.transform.forward.x, user.transform.forward.z).normalized;

                // Get cell vertices
                if (!dic_AreaSegmentsVertex.TryGetValue(userId, out List<Vector2> cellVertices) || cellVertices.Count < 3)
                    continue;

                // Get cell centroid
                Vector2 cellCentroid = Vector2.zero;
                foreach (var v in cellVertices)
                    cellCentroid += v;
                cellCentroid /= cellVertices.Count;

                // Get all user positions for occupancy context
                var allUserPositions = new List<Vector2>();
                foreach (var u in frameState.PhysicalUsers)
                {
                    allUserPositions.Add(new Vector2(u.transform.position.x, u.transform.position.z));
                }

                // Get occupancy bands
                var occupancyBands = occupancyFrame?.Bands ?? new List<PredictedOccupancyBand>();

                // Run selector
                var result = localSafeTargetSelector.SelectTargetForUser(
                    userId,
                    userPosition,
                    userHeading,
                    cellVertices,
                    cellCentroid,
                    allUserPositions,
                    occupancyBands,
                    riskFrame);

                latestLocalTargets[userId] = result;
            }
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

                // Only apply if valid target was found
                if (!targetResult.HasValidTarget)
                    continue;

                // Try to inject target into redirector (if it supports it)
                if (redirector is S2CRedirector steerToTargetRedir)
                {
                    steerToTargetRedir.SetExternalSafeTarget(targetResult.TargetPosition);
                }
                // Note: APFRedirector already uses cell vertices, doesn't need explicit target
            }
        }

        private void FixedUpdate()
        {
            ProcessStep();
        }

        public void SetBiCollisionDebugVisualization(bool isEnabled)
        {
            BidirectionalCollisionDebugVisualizer.SetEnabled(isEnabled);
        }

        private void OnDrawGizmos()
        {
            EnsureModuleConfig();

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

            BidirectionalCollisionDebugVisualizer.DrawGizmos();
            PartitionVisualizer.DrawPartitionAreaGizmos(dic_AreaSegmentsVertex, PartitionedSpaceMaterials);
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
