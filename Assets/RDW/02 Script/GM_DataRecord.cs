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

                string m_str_DataCategory = string.Empty;

                int totalCountoftheQueue = _Queue_ex.Count;

                List<string> copyDataQueue = new List<string>(_Queue_ex);

                //int markerCount = 1;

                string catestr = string.Empty;

                Debug.Log("Saving Data Starts. Queue Count : " + totalCountoftheQueue);

                using (StreamWriter streamWriter = File.AppendText(file_Location))
                {
                    //while (_Queue_ex.Count != 0)
                    {
                        for (int i = 0; i < totalCountoftheQueue; i++)
                        {
                            //string stringData = _Queue_ex.Dequeue();
                            string stringData = copyDataQueue[i];

                            if (stringData.Length > 0)
                            {
                                if (!isCategoryPrinted)
                                {
                                    switch (curren_EX_Type)
                                    {
                                        case Experiment_Type.VS_Line:
                                            str_DataCategory = BuildDataCategory();


                                            break;

                                    }
                                    streamWriter.WriteLine(str_DataCategory);
                                    isCategoryPrinted = true;
                                }

                                streamWriter.WriteLine(stringData);
                            }
                        }
                    }
                }
                
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
            sb.Append("proactiveUserResetCountPerEpisode,");
            sb.Append("userInterResetSingleCountPerEpisode,");
            sb.Append("userInterResetBothCountPerEpisode");

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
            return "Date,Timestamp,episodeId,frame,simTime,unitId,resetType,isBidirectional,interResetDistance,cumulativeDistance";
        }

        public void ResetInterResetDistanceTracking()
        {
            prevPhysicalPosByUnitId.Clear();
            cumulativeDistByUnitId.Clear();
            lastResetCumulativeByUnitId.Clear();
        }

        public void LogInterResetDistance(int unitId, int episodeId, string resetType, bool isBidirectionalEvent, Vector2 currentPhysicalPosition)
        {
            EnsureTrackingInitialized(unitId, currentPhysicalPosition);
            AccumulateDistanceForUnit(unitId, currentPhysicalPosition, false);

            float cumulativeDistance = cumulativeDistByUnitId[unitId];
            if (!lastResetCumulativeByUnitId.ContainsKey(unitId))
            {
                lastResetCumulativeByUnitId[unitId] = 0.0f;
            }

            float interResetDistance = Mathf.Max(0.0f, cumulativeDistance - lastResetCumulativeByUnitId[unitId]);
            lastResetCumulativeByUnitId[unitId] = cumulativeDistance;

            string refinedResetType = string.IsNullOrEmpty(resetType) ? "UNKNOWN" : resetType.Replace(",", "_");
            string payload = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2:F6},{3},{4},{5},{6:F6},{7:F6}",
                episodeId,
                Time.frameCount,
                Time.time,
                unitId,
                refinedResetType,
                isBidirectionalEvent ? 1 : 0,
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

                using (StreamWriter streamWriter = File.AppendText(fileLocation))
                {
                    for (int i = 0; i < totalCount; i++)
                    {
                        string stringData = copyDataQueue[i];
                        if (string.IsNullOrEmpty(stringData))
                            continue;

                        if (!isInterResetDistCategoryPrinted)
                        {
                            str_InterResetDistDataCategory = BuildInterResetDistDataCategory();
                            streamWriter.WriteLine(str_InterResetDistDataCategory);
                            isInterResetDistCategoryPrinted = true;
                        }

                        streamWriter.WriteLine(stringData);
                    }
                }

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
