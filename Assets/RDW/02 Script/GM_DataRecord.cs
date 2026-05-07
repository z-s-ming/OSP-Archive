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
        private string fileName = string.Empty;
        private string interResetDistanceFileName = string.Empty;
        private string runId = string.Empty;
        private string runFolderPath = string.Empty;
        private string runRawFolderPath = string.Empty;
        private string runDerivedFolderPath = string.Empty;
        private string runPlotsFolderPath = string.Empty;
        private string runManifestPath = string.Empty;
        private string runEpisodeSummaryFilePath = string.Empty;
        private string runInterResetDistanceFilePath = string.Empty;

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
                "raw/inter_reset_distance.csv",
                "raw/proactive_trigger_frame.csv"
            }, 1);

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
                sb.AppendLine();
            }
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
                    AppendJsonProperty(sb, "redirectType", unit != null ? unit.redirectType.ToString() : "UNKNOWN", true, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "resetType", unit != null ? unit.resetType.ToString() : "UNKNOWN", true, 3);
                    sb.AppendLine(",");
                    AppendJsonProperty(sb, "episodeType", unit != null ? unit.episodeType.ToString() : "UNKNOWN", true, 3);
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

        private void SetFileName()
        {
            string fileNameFormat = string.Empty;

            switch (curren_EX_Type)
            {
                case Experiment_Type.VS_Line:
                    fileName = "Experiment_01_DataLog_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    break;

                
            }
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
                //string tempFileName = fileName + ".txt";
                SetFileName();
                string tempFileName = fileName + ".txt";
                string file_Location = System.IO.Path.Combine(folder_Path, tempFileName);

                int totalCountoftheQueue = _Queue_ex.Count;

                List<string> copyDataQueue = new List<string>(_Queue_ex);

                Debug.Log("Saving Data Starts. Queue Count : " + totalCountoftheQueue);

                str_DataCategory = BuildDataCategory();
                AppendRowsWithHeader(file_Location, str_DataCategory, copyDataQueue);
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
            return "Date,Timestamp,episodeObjectId,frame,simTime,runtimeUnitId,otherRuntimeUnitId,runtimePairMinId,runtimePairMaxId,resetEventType,isBidirectionalUserPair,interResetDistance,cumulativeDistance";
        }

        public void ResetInterResetDistanceTracking()
        {
            prevPhysicalPosByUnitId.Clear();
            cumulativeDistByUnitId.Clear();
            lastResetCumulativeByUnitId.Clear();
        }

        public void LogInterResetDistance(int runtimeUnitId, int episodeObjectId, string resetEventType, bool isBidirectionalUserPair, Vector2 currentPhysicalPosition, int otherRuntimeUnitId = -1)
        {
            EnsureTrackingInitialized(runtimeUnitId, currentPhysicalPosition);
            AccumulateDistanceForUnit(runtimeUnitId, currentPhysicalPosition, false);

            float cumulativeDistance = cumulativeDistByUnitId[runtimeUnitId];
            if (!lastResetCumulativeByUnitId.ContainsKey(runtimeUnitId))
            {
                lastResetCumulativeByUnitId[runtimeUnitId] = 0.0f;
            }

            float interResetDistance = Mathf.Max(0.0f, cumulativeDistance - lastResetCumulativeByUnitId[runtimeUnitId]);
            lastResetCumulativeByUnitId[runtimeUnitId] = cumulativeDistance;
            int runtimePairMinId = otherRuntimeUnitId >= 0 ? Mathf.Min(runtimeUnitId, otherRuntimeUnitId) : -1;
            int runtimePairMaxId = otherRuntimeUnitId >= 0 ? Mathf.Max(runtimeUnitId, otherRuntimeUnitId) : -1;

            string refinedResetEventType = string.IsNullOrEmpty(resetEventType) ? "UNKNOWN" : resetEventType.Replace(",", "_");
            string payload = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2:F6},{3},{4},{5},{6},{7},{8},{9:F6},{10:F6}",
                episodeObjectId,
                Time.frameCount,
                Time.time,
                runtimeUnitId,
                otherRuntimeUnitId,
                runtimePairMinId,
                runtimePairMaxId,
                refinedResetEventType,
                isBidirectionalUserPair ? 1 : 0,
                interResetDistance,
                cumulativeDistance);

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

                if (string.IsNullOrEmpty(interResetDistanceFileName))
                {
                    interResetDistanceFileName = "Experiment_01_InterResetDistance_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".csv";
                }

                string fileLocation = System.IO.Path.Combine(folder_Path, interResetDistanceFileName);
                int totalCount = dataQueue.Count;
                List<string> copyDataQueue = new List<string>(dataQueue);

                Debug.Log("Saving Inter-Reset Distance Data Starts. Queue Count : " + totalCount);

                str_InterResetDistDataCategory = BuildInterResetDistDataCategory();
                AppendRowsWithHeader(fileLocation, str_InterResetDistDataCategory, copyDataQueue);
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

                int unitId = unit.GetID();
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
