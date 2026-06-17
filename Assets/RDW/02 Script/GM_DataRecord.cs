using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Globalization;

using System.Text;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace _GCM
{

    public class GM_DataRecord : MonoBehaviour
    {

        public static GM_DataRecord instance = null;

        private string rootpath = string.Empty;
        private string folder_Path = string.Empty;

        private string folderName = "CGnA_DataLog";
        private string runId = string.Empty;
        private string runFolderPath = string.Empty;
        private string runRawFolderPath = string.Empty;
        private string runDerivedFolderPath = string.Empty;
        private string runPlotsFolderPath = string.Empty;
        private string runManifestPath = string.Empty;
        private string runEpisodeSummaryFilePath = string.Empty;
        private string runInterResetDistanceFilePath = string.Empty;
        private string runEpisodeInitialStateFilePath = string.Empty;

        private string str_DataCategory = string.Empty;
        private string str_InterResetDistDataCategory = string.Empty;

        [HideInInspector]
        public enum Experiment_Type { VS_Line };

        [SerializeField]
        private Experiment_Type curren_EX_Type;
        public Experiment_Type Curren_EX_Type { get { return curren_EX_Type; } }

        private float startTime = 0.0f;
        private float currentTime = 0.0f;

        // Added to prevent memory overflow
        private const int AUTO_SAVE_THRESHOLD = 5000;
        private const float INTER_RESET_MAX_ACCEPTED_STEP_DISTANCE = 2.0f;

        [HideInInspector]
        public enum Warning_Type
        {
            Ex_Start,
            Ex_End,
            TotalResetMean,
            User00ResetMean,
            User01ResetMean,
            UserbetResetMean,
            UsershutterResetMean,
            MeanDistBetResets
        }

        [HideInInspector]
        public Queue<string> Queue_EX_DATA = new Queue<string>();
        [HideInInspector]
        public Queue<string> Queue_INTER_RESET_DIST = new Queue<string>();
        [HideInInspector]

        public bool isCategoryPrinted;
        public bool isInterResetDistCategoryPrinted;

        private readonly Dictionary<int, Vector2> prevPhysicalPosByUnitId = new Dictionary<int, Vector2>();
        private readonly Dictionary<int, float> cumulativeDistByUnitId = new Dictionary<int, float>();
        private readonly Dictionary<int, float> lastResetCumulativeByUnitId = new Dictionary<int, float>();

        [SerializeField]
        private GameObject real_HMD;

        [SerializeField]
        private GameObject AppQuit_Pref;

        //-------------------------------

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
            }

            MakeFolder();
            //SetFileName();


        }

        private void Start()
        {
            startTime = currentTime = Time.time;

            // GM_DataRecord.instance.Write_Warning(GM_DataRecord.Warning_Type.Ex_Start, "");
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            Save_InterResetDistance_Batch();
        }


        private void Update()
        {
            //if (Input.GetKeyDown(KeyCode.Escape))
            //{
            //    Instantiate(AppQuit_Pref);
            //}


            if (Input.GetKeyDown(KeyCode.Slash))
            {
                Save_SteamingData_Batch();
                Save_InterResetDistance_Batch();

            }
        }

        private void FixedUpdate()
        {
            currentTime = Time.time;
            UpdateInterResetDistanceTracking();

        }

        private void MakeFolder()
        {
            rootpath = Directory.GetCurrentDirectory();

            folder_Path = System.IO.Path.Combine(rootpath, folderName);

            Directory.CreateDirectory(folder_Path);
        }

        public string GetRunId()
        {
            EnsureRunSession();
            return runId;
        }

        public string GetRunFolderPath()
        {
            EnsureRunSession();
            return runFolderPath;
        }

        public string GetRunRawLogPath(string fileNameInRaw)
        {
            EnsureRunSession();
            string safeFileName = SanitizePathSegment(string.IsNullOrEmpty(fileNameInRaw) ? "log.csv" : fileNameInRaw);
            return Path.Combine(runRawFolderPath, safeFileName);
        }

        public string BeginNewRunSession(string reason)
        {
            Save_SteamingData_Batch();
            Save_InterResetDistance_Batch();

            runId = string.Empty;
            runFolderPath = string.Empty;
            runRawFolderPath = string.Empty;
            runDerivedFolderPath = string.Empty;
            runPlotsFolderPath = string.Empty;
            runManifestPath = string.Empty;
            runEpisodeSummaryFilePath = string.Empty;
            runInterResetDistanceFilePath = string.Empty;
            runEpisodeInitialStateFilePath = string.Empty;

            Queue_EX_DATA.Clear();
            Queue_INTER_RESET_DIST.Clear();
            isCategoryPrinted = false;
            isInterResetDistCategoryPrinted = false;
            str_DataCategory = string.Empty;
            str_InterResetDistDataCategory = string.Empty;
            ResetInterResetDistanceTracking();
            startTime = currentTime = Time.time;

            EnsureRunSession();
            Debug.Log(string.Format("[LiveVR] Started new run session {0}. reason={1}", runId, string.IsNullOrEmpty(reason) ? "unspecified" : reason));
            return runId;
        }

        private void EnsureRunSession()
        {
            if (!string.IsNullOrEmpty(runFolderPath))
                return;

            DateTime now = DateTime.Now;
            runId = now.ToString("yyyyMMdd_HHmmss");
            string sceneName = SanitizePathSegment(SceneManager.GetActiveScene().name);
            string userCount = ResolveUserCountLabel();
            string methodLabel = ResolveMethodLabel();
            string runDirectoryName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}__scene-{1}__users-{2}__method-{3}",
                runId,
                sceneName,
                userCount,
                methodLabel);

            runFolderPath = Path.Combine(folder_Path, "runs", runDirectoryName);
            runRawFolderPath = Path.Combine(runFolderPath, "raw");
            runDerivedFolderPath = Path.Combine(runFolderPath, "derived");
            runPlotsFolderPath = Path.Combine(runFolderPath, "plots");
            runManifestPath = Path.Combine(runFolderPath, "manifest.json");
            runEpisodeSummaryFilePath = Path.Combine(runRawFolderPath, "episode_summary.csv");
            runInterResetDistanceFilePath = Path.Combine(runRawFolderPath, "inter_reset_distance.csv");
            runEpisodeInitialStateFilePath = Path.Combine(runRawFolderPath, "episode_initial_state.csv");

            Directory.CreateDirectory(runRawFolderPath);
            Directory.CreateDirectory(runDerivedFolderPath);
            Directory.CreateDirectory(runPlotsFolderPath);
            WriteRunManifest();
        }

        private void WriteRunManifest()
        {
            try
            {
                File.WriteAllText(runManifestPath, BuildRunManifestJson(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogWarning("GM_DataRecord WriteRunManifest ERROR : " + e);
            }
        }

        private string BuildRunManifestJson()
        {
            RDWSimulationManager manager = RDWSimulationManager.instance;
            SimulationSetting setting = manager != null ? manager.simulationSetting : null;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            AppendJsonProperty(sb, "runId", runId, true, 1);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "createdAt", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"), true, 1);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "sceneName", SceneManager.GetActiveScene().name, true, 1);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "unityFrameAtCreate", Time.frameCount.ToString(CultureInfo.InvariantCulture), false, 1);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "unityTimeAtCreate", Time.time.ToString("F6", CultureInfo.InvariantCulture), false, 1);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "userCount", ResolveUserCountLabel(), true, 1);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "runFolder", runFolderPath.Replace("\\", "/"), true, 1);
            sb.AppendLine(",");
            AppendJsonArrayProperty(sb, "rawFiles", new[]
            {
                "raw/episode_summary.csv",
                "raw/episode_initial_state.csv",
                "raw/inter_reset_distance.csv",
                "raw/proactive_trigger_frame.csv",
                "raw/proactive_candidate_frame.csv",
                "raw/live_vr_network.csv"
            }, 1);

            sb.AppendLine(",");
            AppendSpacesJson(sb, setting, manager, 1);
            sb.AppendLine(",");
            sb.AppendLine("  \"proactiveUserReset\": {");
            if (setting != null && setting.proactiveUserReset != null)
            {
                ProactiveUserResetSettings proactive = setting.proactiveUserReset;
                AppendJsonProperty(sb, "enableStrategy", proactive.enableStrategy ? "true" : "false", false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "judgeMode", proactive.judgeMode.ToString(), true, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "userSelectionMode", proactive.userSelectionMode.ToString(), true, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "predictionHorizonSeconds", proactive.predictionHorizonSeconds.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "predictionSampleCount", proactive.predictionSampleCount.ToString(CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "executionCooldownSeconds", proactive.executionCooldownSeconds.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "pairExecutionCooldownSeconds", proactive.pairExecutionCooldownSeconds.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "arbitrationScoreAlpha", proactive.arbitrationScoreAlpha.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "arbitrationScoreBeta", proactive.arbitrationScoreBeta.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "arbitrationScoreGamma", proactive.arbitrationScoreGamma.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "arbitrationScoreD0Meters", proactive.arbitrationScoreD0Meters.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "arbitrationScoreEpsilonMeters", proactive.arbitrationScoreEpsilonMeters.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "arbitrationScoreTieEpsilon", proactive.arbitrationScoreTieEpsilon.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "voronoiBoundaryDistanceThreshold", proactive.voronoiBoundaryDistanceThreshold.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "voronoiBoundaryReverseWallDistanceThreshold", proactive.voronoiBoundaryReverseWallDistanceThreshold.ToString("F6", CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "voronoiBoundaryTrendWindowFrames", proactive.voronoiBoundaryTrendWindowFrames.ToString(CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine(",");
                AppendJsonProperty(sb, "voronoiBoundaryTrendRequiredFrames", proactive.voronoiBoundaryTrendRequiredFrames.ToString(CultureInfo.InvariantCulture), false, 2);
                sb.AppendLine();
            }
            sb.AppendLine("  },");
            sb.AppendLine("  \"liveVR\": {");
            LiveVRNetworkManager liveVR = LiveVRNetworkManager.Instance;
            bool liveUserProfile = setting != null &&
                                   (setting.experimentProfile == ExperimentProfile.LiveUser ||
                                    setting.useLiveVRPhysicalUserInput);
            AppendJsonProperty(sb, "enabled", liveUserProfile ? "true" : "false", false, 2);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "experimentProfile", setting != null ? setting.experimentProfile.ToString() : "UNKNOWN", true, 2);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "mode", liveVR != null ? liveVR.Mode.ToString() : "UNKNOWN", true, 2);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "localUserId", liveVR != null ? liveVR.LocalUserId.ToString(CultureInfo.InvariantCulture) : "-1", false, 2);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "hostAddress", liveVR != null ? liveVR.HostAddress : string.Empty, true, 2);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "hostPort", liveVR != null ? liveVR.HostPosePort.ToString(CultureInfo.InvariantCulture) : "0", false, 2);
            sb.AppendLine();
            sb.AppendLine("  },");
            sb.AppendLine("  \"unitSettings\": [");
            if (setting != null && setting.unitSettings != null)
            {
                for (int i = 0; i < setting.unitSettings.Length; i++)
                {
                    UnitSetting unit = setting.unitSettings[i];
                    sb.AppendLine("    {");
                    AppendJsonProperty(sb, "logicalUserIndex", i.ToString(CultureInfo.InvariantCulture), false, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "liveVRUserSource", ResolveLiveVRUserSourceLabel(i), true, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "redirectType", unit != null ? unit.redirectType.ToString() : "UNKNOWN", true, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "resetType", unit != null ? unit.resetType.ToString() : "UNKNOWN", true, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "episodeType", unit != null ? unit.episodeType.ToString() : "UNKNOWN", true, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "episodeLength", unit != null ? unit.episodeLength.ToString(CultureInfo.InvariantCulture) : "0", false, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "episodeFileName", unit != null ? unit.episodeFileName : string.Empty, true, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "useRandomStartReal", unit != null && unit.useRandomStartReal ? "true" : "false", false, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "useRandomStartVirtual", unit != null && unit.useRandomStartVirtual ? "true" : "false", false, 3);
                    sb.AppendLine(",");
                    AppendVector2Property(sb, "realInitialPosition", unit != null ? unit.realStartPosition : Vector2.zero, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "realInitialRotationDegrees", unit != null ? unit.realStartRotation.ToString("F6", CultureInfo.InvariantCulture) : "0", false, 3);
                    sb.AppendLine(",");
                    AppendVector2Property(sb, "virtualInitialPosition", unit != null ? unit.virtualStartPosition : Vector2.zero, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "virtualInitialRotationDegrees", unit != null ? unit.virtualStartRotation.ToString("F6", CultureInfo.InvariantCulture) : "0", false, 3);
                    sb.AppendLine();
                    sb.Append("    }");
                    if (i < setting.unitSettings.Length - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendSpacesJson(StringBuilder sb, SimulationSetting setting, RDWSimulationManager manager, int indentLevel)
        {
            sb.Append(new string(' ', indentLevel * 2));
            sb.AppendLine("\"spaces\": {");
            AppendSpaceJson(sb, "real", setting != null ? setting.realSpaceSetting : null, manager, true, indentLevel + 1);
            sb.AppendLine(",");
            AppendSpaceJson(sb, "virtual", setting != null ? setting.virtualSpaceSetting : null, manager, false, indentLevel + 1);
            sb.AppendLine();
            sb.Append(new string(' ', indentLevel * 2));
            sb.Append("}");
        }

        private static void AppendSpaceJson(StringBuilder sb, string key, SpaceSetting spaceSetting, RDWSimulationManager manager, bool realSpace, int indentLevel)
        {
            sb.Append(new string(' ', indentLevel * 2));
            sb.Append('"').Append(EscapeJson(key)).AppendLine("\": {");
            AppendJsonProperty(sb, "usePredefinedSpace", spaceSetting != null && spaceSetting.usePredefinedSpace ? "true" : "false", false, indentLevel + 1);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "name", ResolveSpaceName(spaceSetting), true, indentLevel + 1);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "predefinedSpace", ResolvePredefinedSpaceName(spaceSetting), true, indentLevel + 1);
            sb.AppendLine(",");
            AppendVector2Property(sb, "configuredPosition", spaceSetting != null ? spaceSetting.position : Vector2.zero, indentLevel + 1);
            sb.AppendLine(",");
            AppendJsonProperty(sb, "configuredRotationDegrees", spaceSetting != null ? spaceSetting.rotation.ToString("F6", CultureInfo.InvariantCulture) : "0", false, indentLevel + 1);
            sb.AppendLine(",");

            Vector2 min;
            Vector2 max;
            string boundsSource;
            if (TryGetRuntimeSpaceBounds(manager, realSpace, out min, out max))
            {
                boundsSource = "runtime";
            }
            else if (TryGetConfiguredSpaceBounds(spaceSetting, out min, out max))
            {
                boundsSource = "configuration";
            }
            else
            {
                boundsSource = "unknown";
            }

            AppendJsonProperty(sb, "boundsSource", boundsSource, true, indentLevel + 1);
            sb.AppendLine(",");
            if (string.Equals(boundsSource, "unknown", StringComparison.Ordinal))
                AppendJsonProperty(sb, "bounds", null, false, indentLevel + 1);
            else
                AppendBoundsProperty(sb, "bounds", min, max, indentLevel + 1);
            sb.AppendLine();
            sb.Append(new string(' ', indentLevel * 2));
            sb.Append("}");
        }

        private static bool TryGetRuntimeSpaceBounds(RDWSimulationManager manager, bool realSpace, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;

            if (manager == null || manager.GetRedirectedUnits == null)
                return false;

            RedirectedUnit[] units = manager.GetRedirectedUnits;
            for (int i = 0; i < units.Length; i++)
            {
                RedirectedUnit unit = units[i];
                if (unit == null)
                    continue;

                Space2D space = realSpace ? unit.GetRealSpace() : unit.GetVirtualSpace();
                if (TryGetSpaceObjectBounds(space, out min, out max))
                    return true;
            }

            return false;
        }

        private static bool TryGetSpaceObjectBounds(Space2D space, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;

            if (space == null || space.spaceObject == null)
                return false;

            try
            {
                Polygon2D polygon = space.spaceObject as Polygon2D;
                if (polygon != null && polygon.GetVertices() != null && polygon.GetVertices().Count > 0)
                {
                    min = polygon.GetVertex(0, UnityEngine.Space.World);
                    max = min;
                    for (int i = 1; i < polygon.GetVertices().Count; i++)
                    {
                        Vector2 vertex = polygon.GetVertex(i, UnityEngine.Space.World);
                        min = Vector2.Min(min, vertex);
                        max = Vector2.Max(max, vertex);
                    }

                    return true;
                }

                Bounds2D bounds = space.spaceObject.bound;
                min = bounds.min;
                max = bounds.max;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryGetConfiguredSpaceBounds(SpaceSetting spaceSetting, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;

            if (spaceSetting == null || spaceSetting.spaceObjectSetting == null)
                return false;

            return TryGetObjectSettingBounds(spaceSetting.spaceObjectSetting, out min, out max);
        }

        private static bool TryGetObjectSettingBounds(ObjectSetting objectSetting, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;

            if (objectSetting == null)
                return false;

            if ((objectSetting.type == OBJECT_TYPE.POLYGON || objectSetting.type == OBJECT_TYPE.AUTO) &&
                objectSetting.vertices != null &&
                objectSetting.vertices.Count > 0)
            {
                Vector2 first = TransformConfiguredPoint(objectSetting.vertices[0], objectSetting.position, objectSetting.rotation);
                min = first;
                max = first;
                for (int i = 1; i < objectSetting.vertices.Count; i++)
                {
                    Vector2 point = TransformConfiguredPoint(objectSetting.vertices[i], objectSetting.position, objectSetting.rotation);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

                return true;
            }

            if ((objectSetting.type == OBJECT_TYPE.CIRCLE || objectSetting.type == OBJECT_TYPE.AUTO) &&
                objectSetting.radius > 0.0f)
            {
                Vector2 radius = new Vector2(objectSetting.radius, objectSetting.radius);
                min = objectSetting.position - radius;
                max = objectSetting.position + radius;
                return true;
            }

            if (objectSetting.type == OBJECT_TYPE.LINESEGMENT)
            {
                Vector2 p1 = TransformConfiguredPoint(objectSetting.p1, objectSetting.position, objectSetting.rotation);
                Vector2 p2 = TransformConfiguredPoint(objectSetting.p2, objectSetting.position, objectSetting.rotation);
                min = Vector2.Min(p1, p2);
                max = Vector2.Max(p1, p2);
                return true;
            }

            return false;
        }

        private static Vector2 TransformConfiguredPoint(Vector2 localPoint, Vector2 position, float rotationDegrees)
        {
            float radians = rotationDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(
                localPoint.x * cos - localPoint.y * sin,
                localPoint.x * sin + localPoint.y * cos) + position;
        }

        private static string ResolveSpaceName(SpaceSetting spaceSetting)
        {
            if (spaceSetting == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(spaceSetting.name))
                return spaceSetting.name;

            if (spaceSetting.spaceObjectSetting != null)
                return spaceSetting.spaceObjectSetting.name;

            return string.Empty;
        }

        private static string ResolvePredefinedSpaceName(SpaceSetting spaceSetting)
        {
            if (spaceSetting == null || spaceSetting.predefinedSpace == null)
                return string.Empty;

            return spaceSetting.predefinedSpace.name;
        }

        private static void AppendVector2Property(StringBuilder sb, string key, Vector2 value, int indentLevel)
        {
            AppendJsonProperty(sb, key, FormatVector2(value), false, indentLevel);
        }

        private static void AppendBoundsProperty(StringBuilder sb, string key, Vector2 min, Vector2 max, int indentLevel)
        {
            Vector2 size = max - min;
            Vector2 center = (min + max) * 0.5f;
            Vector2 halfExtents = size * 0.5f;
            string value = string.Format(
                CultureInfo.InvariantCulture,
                "{{ \"min\": {0}, \"max\": {1}, \"center\": {2}, \"size\": {3}, \"halfExtents\": {4}, \"widthMeters\": {5:F6}, \"heightMeters\": {6:F6} }}",
                FormatVector2(min),
                FormatVector2(max),
                FormatVector2(center),
                FormatVector2(size),
                FormatVector2(halfExtents),
                Mathf.Abs(size.x),
                Mathf.Abs(size.y));
            AppendJsonProperty(sb, key, value, false, indentLevel);
        }

        private static string FormatVector2(Vector2 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{{ \"x\": {0:F6}, \"y\": {1:F6} }}",
                value.x,
                value.y);
        }

        public void LogEpisodeInitialState(int episodeIndex, int episodeSeed, bool seedReplayOnly)
        {
            try
            {
                EnsureRunSession();
                RDWSimulationManager manager = RDWSimulationManager.instance;
                if (manager == null || manager.GetRedirectedUnits == null)
                    return;

                RedirectedUnit[] units = manager.GetRedirectedUnits;
                if (units == null || units.Length == 0)
                    return;

                Vector2 realMin;
                Vector2 realMax;
                Vector2 virtualMin;
                Vector2 virtualMax;
                bool hasRealBounds = TryGetRuntimeSpaceBounds(manager, true, out realMin, out realMax);
                bool hasVirtualBounds = TryGetRuntimeSpaceBounds(manager, false, out virtualMin, out virtualMax);
                List<string> rows = new List<string>();
                string sceneName = SceneManager.GetActiveScene().name;
                string seedLabel = episodeSeed == int.MinValue ? "NA" : episodeSeed.ToString(CultureInfo.InvariantCulture);

                for (int i = 0; i < units.Length; i++)
                {
                    RedirectedUnit unit = units[i];
                    if (unit == null)
                        continue;

                    Object2D realUser = unit.GetRealUser();
                    Object2D virtualUser = unit.GetVirtualUser();
                    Vector2 realPosition = realUser != null ? realUser.transform2D.localPosition : Vector2.zero;
                    Vector2 virtualPosition = virtualUser != null ? virtualUser.transform2D.localPosition : Vector2.zero;
                    float realRotation = realUser != null ? realUser.transform2D.localRotation : 0.0f;
                    float virtualRotation = virtualUser != null ? virtualUser.transform2D.localRotation : 0.0f;

                    StringBuilder row = new StringBuilder();
                    row.Append(EscapeCsv(DateTime.Now.ToString("yyyyMMddHHmmss.fff", CultureInfo.InvariantCulture))).Append(',');
                    row.Append(EscapeCsv(runId)).Append(',');
                    row.Append(EscapeCsv(sceneName)).Append(',');
                    row.Append(episodeIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
                    row.Append(seedLabel).Append(',');
                    row.Append(seedReplayOnly ? "true" : "false").Append(',');
                    row.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',');
                    row.Append(FormatCsvFloat(realPosition.x)).Append(',');
                    row.Append(FormatCsvFloat(realPosition.y)).Append(',');
                    row.Append(FormatCsvFloat(realRotation)).Append(',');
                    row.Append(FormatCsvFloat(virtualPosition.x)).Append(',');
                    row.Append(FormatCsvFloat(virtualPosition.y)).Append(',');
                    row.Append(FormatCsvFloat(virtualRotation)).Append(',');
                    AppendBoundsCsv(row, hasRealBounds, realMin, realMax);
                    row.Append(',');
                    AppendBoundsCsv(row, hasVirtualBounds, virtualMin, virtualMax);
                    rows.Add(row.ToString());
                }

                AppendRowsWithHeader(GetRunEpisodeInitialStateFilePath(), BuildEpisodeInitialStateHeader(), rows);
            }
            catch (Exception e)
            {
                Debug.LogWarning("GM_DataRecord LogEpisodeInitialState ERROR : " + e);
            }
        }

        private static string BuildEpisodeInitialStateHeader()
        {
            return "Date,runId,sceneName,episodeIndex,episodeSeed,seedReplayOnly,userId," +
                   "realStartX,realStartY,realStartYawDeg,virtualStartX,virtualStartY,virtualStartYawDeg," +
                   "realSpaceMinX,realSpaceMinY,realSpaceMaxX,realSpaceMaxY,realSpaceWidth,realSpaceHeight," +
                   "virtualSpaceMinX,virtualSpaceMinY,virtualSpaceMaxX,virtualSpaceMaxY,virtualSpaceWidth,virtualSpaceHeight";
        }

        private static void AppendBoundsCsv(StringBuilder row, bool hasBounds, Vector2 min, Vector2 max)
        {
            if (!hasBounds)
            {
                row.Append("NA,NA,NA,NA,NA,NA");
                return;
            }

            Vector2 size = max - min;
            row.Append(FormatCsvFloat(min.x)).Append(',');
            row.Append(FormatCsvFloat(min.y)).Append(',');
            row.Append(FormatCsvFloat(max.x)).Append(',');
            row.Append(FormatCsvFloat(max.y)).Append(',');
            row.Append(FormatCsvFloat(Mathf.Abs(size.x))).Append(',');
            row.Append(FormatCsvFloat(Mathf.Abs(size.y)));
        }

        private static string FormatCsvFloat(float value)
        {
            return value.ToString("F6", CultureInfo.InvariantCulture);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            string escaped = value.Replace("\"", "\"\"");
            return mustQuote ? "\"" + escaped + "\"" : escaped;
        }

        private static void AppendJsonProperty(StringBuilder sb, string key, string value, bool quoteValue, int indentLevel)
        {
            sb.Append(new string(' ', indentLevel * 2));
            sb.Append('"').Append(EscapeJson(key)).Append("\": ");
            if (quoteValue)
            {
                sb.Append('"').Append(EscapeJson(value)).Append('"');
            }
            else
            {
                sb.Append(string.IsNullOrEmpty(value) ? "null" : value);
            }
        }

        private static void AppendJsonArrayProperty(StringBuilder sb, string key, string[] values, int indentLevel)
        {
            sb.Append(new string(' ', indentLevel * 2));
            sb.Append('"').Append(EscapeJson(key)).Append("\": [");
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append('"').Append(EscapeJson(values[i])).Append('"');
            }
            sb.Append("]");
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "unknown";

            StringBuilder sb = new StringBuilder(value.Length);
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool invalid = false;
                for (int j = 0; j < invalidChars.Length; j++)
                {
                    if (c == invalidChars[j])
                    {
                        invalid = true;
                        break;
                    }
                }

                sb.Append(invalid || char.IsWhiteSpace(c) ? '_' : c);
            }

            return sb.ToString();
        }

        private static string ResolveUserCountLabel()
        {
            RDWSimulationManager manager = RDWSimulationManager.instance;
            if (manager == null || manager.simulationSetting == null || manager.simulationSetting.unitSettings == null)
                return "unknown";

            return manager.simulationSetting.unitSettings.Length.ToString(CultureInfo.InvariantCulture);
        }

        private static string ResolveMethodLabel()
        {
            RDWSimulationManager manager = RDWSimulationManager.instance;
            if (manager == null || manager.simulationSetting == null ||
                manager.simulationSetting.unitSettings == null ||
                manager.simulationSetting.unitSettings.Length == 0 ||
                manager.simulationSetting.unitSettings[0] == null)
            {
                return "unknown";
            }

            UnitSetting firstUnit = manager.simulationSetting.unitSettings[0];
            return SanitizePathSegment(firstUnit.redirectType + "-" + firstUnit.resetType);
        }

        private static string ResolveLiveVRUserSourceLabel(int userId)
        {
            LiveVRNetworkManager liveVR = LiveVRNetworkManager.Instance;
            if (liveVR == null)
                return "UNKNOWN";

            return liveVR.GetUserSourceLabel(userId, 0.75f, true);
        }

        public void Enequeue_Data(string _data)
        {
            currentTime = Time.time;
            string stringRelativeTime = string.Format("{0:F4}", currentTime - startTime);

            string refined_Data = DateTime.Now.ToString("yyyyMMddHHmmss.fff") + "," + stringRelativeTime + "," + _data;

            Queue_EX_DATA.Enqueue(refined_Data);

            // Auto save if queue gets too large
            if (Queue_EX_DATA.Count >= AUTO_SAVE_THRESHOLD)
            {
                Save_SteamingData_Batch();
            }
        }

        public void Save_SteamingData_Batch()
        {
            WriteSteamingData_Batch(ref Queue_EX_DATA);
        }

        public void Save_InterResetDistance_Batch()
        {
            WriteInterResetDistanceData_Batch(ref Queue_INTER_RESET_DIST);
        }

        public void Clear_Queue_EX_DATA()
        {
            Queue_EX_DATA.Clear();
        }

        public void Clear_Queue_INTER_RESET_DIST()
        {
            Queue_INTER_RESET_DIST.Clear();
        }

        public bool WriteSteamingData_Batch(ref Queue<string> _Queue_ex)
        {
            bool tempb = false;

            try
            {
                int totalCountoftheQueue = _Queue_ex.Count;

                List<string> copyDataQueue = new List<string>(_Queue_ex);

                Debug.Log("Saving Data Starts. Queue Count : " + totalCountoftheQueue);

                str_DataCategory = BuildDataCategory();
                AppendRowsWithHeader(GetRunEpisodeSummaryFilePath(), str_DataCategory, copyDataQueue);
                
                _Queue_ex.Clear(); // Clear buffer to prevent memory leak and double-writing
                
                tempb = true;
                isCategoryPrinted = false;
                //StartCoroutine(CheckSavingDataCompleted());
            }
            catch (Exception e)
            {
                Debug.Log("WriteSteamingData_BatchProcessing ERROR : " + e);
            }

            return tempb;
        }

        private string BuildDataCategory()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Date,Timestamp,");
            sb.Append("episodeSeed,");
            sb.Append("totalResetCountPerEpisode,");
            sb.Append("userResetCountVariancePerEpisode,");
            sb.Append("boundaryCollisionCountPerEpisode,");
            sb.Append("proactiveUserResetActionCountPerEpisode,");
            sb.Append("userInterResetSingleActionCountPerEpisode,");
            sb.Append("userInterResetBothActionCountPerEpisode");

            int userCount = 0;
            if (RDWSimulationManager.instance != null &&
                RDWSimulationManager.instance.simulationSetting != null &&
                RDWSimulationManager.instance.simulationSetting.unitSettings != null)
            {
                userCount = RDWSimulationManager.instance.simulationSetting.unitSettings.Length;
            }

            for (int i = 0; i < userCount; i++)
            {
                sb.Append(",user").Append(i.ToString("D2")).Append("ResetCountPerEpisode");
            }

            return sb.ToString();
        }

        private string BuildInterResetDistDataCategory()
        {
            return "Date,Timestamp,episodeObjectId,frame,simTime,userId,userSource,isLiveUser,isFallbackSim,otherUserId,pairMinUserId,pairMaxUserId,resetEventType,isBidirectionalUserPair,interResetDistance,cumulativeDistance,triggerId,candidateId,decisionId,executionId,originTriggerId,originCandidateId,accepted,executed,rejectReason";
        }

        public void ResetInterResetDistanceTracking()
        {
            prevPhysicalPosByUnitId.Clear();
            cumulativeDistByUnitId.Clear();
            lastResetCumulativeByUnitId.Clear();
        }

        public void LogInterResetDistance(
            int userId,
            int episodeObjectId,
            string resetEventType,
            bool isBidirectionalUserPair,
            Vector2 currentPhysicalPosition,
            int otherUserId = -1,
            int triggerId = -1,
            int candidateId = -1,
            int decisionId = -1,
            int executionId = -1,
            int originTriggerId = -1,
            int originCandidateId = -1,
            bool accepted = false,
            bool executed = true,
            string rejectReason = "NONE")
        {
            EnsureTrackingInitialized(userId, currentPhysicalPosition);
            AccumulateDistanceForUnit(userId, currentPhysicalPosition, false);

            float cumulativeDistance = cumulativeDistByUnitId[userId];
            if (!lastResetCumulativeByUnitId.ContainsKey(userId))
            {
                lastResetCumulativeByUnitId[userId] = 0.0f;
            }

            float interResetDistance = Mathf.Max(0.0f, cumulativeDistance - lastResetCumulativeByUnitId[userId]);
            lastResetCumulativeByUnitId[userId] = cumulativeDistance;
            int pairMinUserId = otherUserId >= 0 ? Mathf.Min(userId, otherUserId) : -1;
            int pairMaxUserId = otherUserId >= 0 ? Mathf.Max(userId, otherUserId) : -1;

            string refinedResetEventType = string.IsNullOrEmpty(resetEventType) ? "UNKNOWN" : resetEventType.Replace(",", "_");
            string refinedRejectReason = string.IsNullOrEmpty(rejectReason) ? "NONE" : rejectReason.Replace(",", "_");
            string userSource = ResolveLiveVRUserSourceLabel(userId);
            bool isLiveUser = userSource == "LIVE_REQUIRED" || userSource == "LIVE_OPTIONAL";
            bool isFallbackSim = userSource == "SIM_FALLBACK";
            string payload = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2:F6},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12:F6},{13:F6},{14},{15},{16},{17},{18},{19},{20},{21},{22}",
                episodeObjectId,
                Time.frameCount,
                Time.time,
                userId,
                userSource,
                isLiveUser ? 1 : 0,
                isFallbackSim ? 1 : 0,
                otherUserId,
                pairMinUserId,
                pairMaxUserId,
                refinedResetEventType,
                isBidirectionalUserPair ? 1 : 0,
                interResetDistance,
                cumulativeDistance,
                triggerId,
                candidateId,
                decisionId,
                executionId,
                originTriggerId,
                originCandidateId,
                accepted ? 1 : 0,
                executed ? 1 : 0,
                refinedRejectReason);

            Enqueue_InterResetDistanceData(payload);
        }

        private void Enqueue_InterResetDistanceData(string data)
        {
            currentTime = Time.time;
            string stringRelativeTime = string.Format(CultureInfo.InvariantCulture, "{0:F4}", currentTime - startTime);

            string refinedData = DateTime.Now.ToString("yyyyMMddHHmmss.fff") + "," + stringRelativeTime + "," + data;
            Queue_INTER_RESET_DIST.Enqueue(refinedData);

            if (Queue_INTER_RESET_DIST.Count >= AUTO_SAVE_THRESHOLD)
            {
                Save_InterResetDistance_Batch();
            }
        }

        private bool WriteInterResetDistanceData_Batch(ref Queue<string> dataQueue)
        {
            bool success = false;

            try
            {
                if (dataQueue.Count == 0)
                    return true;

                int totalCount = dataQueue.Count;
                List<string> copyDataQueue = new List<string>(dataQueue);

                Debug.Log("Saving Inter-Reset Distance Data Starts. Queue Count : " + totalCount);

                str_InterResetDistDataCategory = BuildInterResetDistDataCategory();
                AppendRowsWithHeader(GetRunInterResetDistanceFilePath(), str_InterResetDistDataCategory, copyDataQueue);

                dataQueue.Clear();
                success = true;
                isInterResetDistCategoryPrinted = false;
            }
            catch (Exception e)
            {
                Debug.Log("WriteInterResetDistanceData_Batch ERROR : " + e);
            }

            return success;
        }

        private string GetRunEpisodeSummaryFilePath()
        {
            EnsureRunSession();
            return runEpisodeSummaryFilePath;
        }

        private string GetRunInterResetDistanceFilePath()
        {
            EnsureRunSession();
            return runInterResetDistanceFilePath;
        }

        private string GetRunEpisodeInitialStateFilePath()
        {
            EnsureRunSession();
            return runEpisodeInitialStateFilePath;
        }

        private static void AppendRowsWithHeader(string filePath, string header, List<string> rows)
        {
            if (string.IsNullOrEmpty(filePath) || rows == null || rows.Count == 0)
                return;

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            bool shouldWriteHeader = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;
            using (StreamWriter streamWriter = File.AppendText(filePath))
            {
                if (shouldWriteHeader && !string.IsNullOrEmpty(header))
                {
                    streamWriter.WriteLine(header);
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    string row = rows[i];
                    if (!string.IsNullOrEmpty(row))
                    {
                        streamWriter.WriteLine(row);
                    }
                }
            }
        }

        private void UpdateInterResetDistanceTracking()
        {
            if (RDWSimulationManager.instance == null)
                return;

            RedirectedUnit[] units = RDWSimulationManager.instance.GetRedirectedUnits;
            if (units == null)
                return;

            for (int i = 0; i < units.Length; i++)
            {
                RedirectedUnit unit = units[i];
                if (unit == null || unit.GetRealUser() == null)
                    continue;

                int unitId = i;
                Vector2 currentPosition = unit.GetRealUser().transform2D.localPosition;

                EnsureTrackingInitialized(unitId, currentPosition);
                AccumulateDistanceForUnit(unitId, currentPosition, unit.IsResetting);
            }
        }

        private void EnsureTrackingInitialized(int unitId, Vector2 currentPosition)
        {
            if (!prevPhysicalPosByUnitId.ContainsKey(unitId))
            {
                prevPhysicalPosByUnitId[unitId] = currentPosition;
            }

            if (!cumulativeDistByUnitId.ContainsKey(unitId))
            {
                cumulativeDistByUnitId[unitId] = 0.0f;
            }

            if (!lastResetCumulativeByUnitId.ContainsKey(unitId))
            {
                lastResetCumulativeByUnitId[unitId] = 0.0f;
            }
        }

        private void AccumulateDistanceForUnit(int unitId, Vector2 currentPosition, bool isResetting)
        {
            Vector2 previousPosition = prevPhysicalPosByUnitId[unitId];
            if (isResetting)
            {
                prevPhysicalPosByUnitId[unitId] = currentPosition;
                return;
            }

            float dist = Vector2.Distance(currentPosition, previousPosition);
            if (dist < INTER_RESET_MAX_ACCEPTED_STEP_DISTANCE)
            {
                cumulativeDistByUnitId[unitId] += dist;
            }

            prevPhysicalPosByUnitId[unitId] = currentPosition;
        }



        public void Write_Warning(Warning_Type _warning, string _additionalMessage)
        {
            string warning_Message = string.Empty;

            switch (_warning)
            {
                case Warning_Type.Ex_Start:
                    warning_Message = "[The Experiment Main Process Starts Now. " + _additionalMessage + "]";
                    break;

                case Warning_Type.Ex_End:
                    warning_Message = "[The Experiment Main Process Ends Now.]";
                    break;

                case Warning_Type.TotalResetMean:
                    warning_Message = "TotalRsetMean" + _additionalMessage + "]";
                    break;
                    
                case Warning_Type.User00ResetMean:
                    warning_Message = "User00ResetMean" + _additionalMessage + "]";
                    break;

                case Warning_Type.User01ResetMean:
                    warning_Message = "User01ResetMean" + _additionalMessage + "]";
                    break;

                case Warning_Type.UserbetResetMean:
                    warning_Message = "UserbetResetMean" + _additionalMessage + "]";
                    break;

                case Warning_Type.UsershutterResetMean:
                    warning_Message = "UsershutterResetMean" + _additionalMessage + "]";
                    break;

                case Warning_Type.MeanDistBetResets:
                    warning_Message = "MeanDistBetResets" + _additionalMessage + "]";
                    break;
            }

            Debug.Log(warning_Message);
            Enequeue_Data(warning_Message + "," + (int)_warning);
        }

      





















    }
}
