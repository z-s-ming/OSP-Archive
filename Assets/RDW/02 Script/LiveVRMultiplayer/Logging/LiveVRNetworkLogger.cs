using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class LiveVRNetworkLogger : MonoBehaviour
{
    [HideInInspector]
    [SerializeField] private LiveVRNetworkManager networkManager;
    [HideInInspector]
    [SerializeField] private bool enableLogging = true;
    [HideInInspector]
    [SerializeField] private int expectedUserCount = 2;
    [HideInInspector]
    [SerializeField] private int logEveryNFrames = 5;
    [HideInInspector]
    [SerializeField] private float connectedTimeoutSeconds = 0.5f;

    private readonly Dictionary<int, uint> previousSequenceByUserId = new Dictionary<int, uint>();
    private readonly Queue<string> pendingRows = new Queue<string>();
    private string logPath;
    private int frameCounter;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        bool newEnableLogging,
        int newExpectedUserCount,
        int newLogEveryNFrames,
        float newConnectedTimeoutSeconds)
    {
        networkManager = newNetworkManager;
        enableLogging = newEnableLogging;
        expectedUserCount = newExpectedUserCount;
        logEveryNFrames = Mathf.Max(1, newLogEveryNFrames);
        connectedTimeoutSeconds = newConnectedTimeoutSeconds;
    }

    private void LateUpdate()
    {
        if (!enableLogging)
            return;

        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || !manager.IsHost)
            return;

        frameCounter++;
        if (frameCounter % Mathf.Max(1, logEveryNFrames) != 0)
            return;

        CaptureRows(manager);

        if (pendingRows.Count >= 64)
            Flush();
    }

    private void OnDestroy()
    {
        Flush();
    }

    public void FlushAndResetSession()
    {
        Flush();
        logPath = string.Empty;
        previousSequenceByUserId.Clear();
        pendingRows.Clear();
        frameCounter = 0;
    }

    private void CaptureRows(LiveVRNetworkManager manager)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        float simTime = Time.time;
        for (int userId = 0; userId < expectedUserCount; userId++)
        {
            LiveVRPoseSample sample;
            bool hasPose = manager.TryGetPose(userId, out sample);
            bool connected = hasPose && sample.AgeSeconds <= connectedTimeoutSeconds;
            uint packetGap = 0;
            if (hasPose)
            {
                uint previousSequence;
                if (previousSequenceByUserId.TryGetValue(userId, out previousSequence) && sample.Sequence > previousSequence + 1)
                    packetGap = sample.Sequence - previousSequence - 1;

                previousSequenceByUserId[userId] = sample.Sequence;
            }

            string userSource = manager.GetUserSourceLabel(userId, connectedTimeoutSeconds, true);
            pendingRows.Enqueue(BuildRow(now, simTime, userId, userSource, hasPose, connected, packetGap, sample));
        }
    }

    private string BuildRow(long now, float simTime, int userId, string userSource, bool hasPose, bool connected, uint packetGap, LiveVRPoseSample sample)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        bool isLiveUser = userSource == "LIVE_REQUIRED" || userSource == "LIVE_OPTIONAL";
        bool isFallbackSim = userSource == "SIM_FALLBACK";
        if (!hasPose)
        {
            return string.Format(
                c,
                "{0},{1},{2:R},{3},unknown,{4},{5},{6},0,0,0,0,0,0,0,0,false,{7},{8}",
                now,
                Time.frameCount,
                simTime,
                userId,
                userSource,
                isLiveUser ? "true" : "false",
                isFallbackSim ? "true" : "false",
                connected ? "true" : "false",
                packetGap);
        }

        long poseAgeMs = Math.Max(0, now - sample.HostReceiveUnixMilliseconds);
        return string.Format(
            c,
            "{0},{1},{2:R},{3},unknown,{4},{5},{6},{7},{8},{9},{10},{11:R},{12:R},{13:R},{14:R},{15},{16},{17}",
            now,
            Time.frameCount,
            simTime,
            userId,
            userSource,
            isLiveUser ? "true" : "false",
            isFallbackSim ? "true" : "false",
            sample.Sequence,
            sample.ClientUnixMilliseconds,
            sample.HostReceiveUnixMilliseconds,
            poseAgeMs,
            sample.ExperimentPosition.x,
            sample.ExperimentPosition.y,
            sample.YawDegrees,
            sample.HeightMeters,
            sample.IsCalibrated ? "true" : "false",
            connected ? "true" : "false",
            packetGap);
    }

    private void Flush()
    {
        if (pendingRows.Count == 0)
            return;

        string path = ResolveLogPath();
        StringBuilder sb = new StringBuilder();
        bool writeHeader = !File.Exists(path);
        if (writeHeader)
            sb.AppendLine("timestamp,frame,simTime,userId,deviceType,userSource,isLiveUser,isFallbackSim,sequence,clientUnixMs,hostReceiveUnixMs,poseAgeMs,x,y,yaw,height,calibrated,connected,packetGap");

        while (pendingRows.Count > 0)
            sb.AppendLine(pendingRows.Dequeue());

        try
        {
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LiveVR] Failed to write network log: " + e.Message);
        }
    }

    private string ResolveLogPath()
    {
        if (!string.IsNullOrEmpty(logPath))
            return logPath;

        if (_GCM.GM_DataRecord.instance != null)
        {
            logPath = _GCM.GM_DataRecord.instance.GetRunRawLogPath("live_vr_network.csv");
            return logPath;
        }

        string fallback = Path.Combine(Application.persistentDataPath, "live_vr_network.csv");
        logPath = fallback;
        return logPath;
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }
}
