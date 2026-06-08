using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;

public class LiveVRNetworkStatusOverlay : MonoBehaviour
{
    [HideInInspector]
    [SerializeField] private LiveVRNetworkManager networkManager;
    [HideInInspector]
    [SerializeField] private bool showOverlay = true;
    [HideInInspector]
    [SerializeField] private int expectedUserCount = 2;
    [HideInInspector]
    [SerializeField] private Vector2 screenPosition = new Vector2(12f, 12f);
    [HideInInspector]
    [SerializeField] private Vector2 panelSize = new Vector2(420f, 28f);
    [HideInInspector]
    [SerializeField] private float staleWarningSeconds = 0.5f;

    private GUIStyle normalStyle;
    private GUIStyle warningStyle;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        bool newShowOverlay,
        int newExpectedUserCount,
        Vector2 newScreenPosition,
        Vector2 newPanelSize,
        float newStaleWarningSeconds)
    {
        networkManager = newNetworkManager;
        showOverlay = newShowOverlay;
        expectedUserCount = newExpectedUserCount;
        screenPosition = newScreenPosition;
        panelSize = newPanelSize;
        staleWarningSeconds = newStaleWarningSeconds;
    }

    private void OnGUI()
    {
        if (!showOverlay)
            return;

        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || manager.Mode == LiveVRExperimentMode.Disabled)
            return;

        EnsureStyles();

        float y = screenPosition.y;
        if (manager.IsHost)
        {
            DrawLine(ref y, string.Format(
                "LiveVR mode={0} listening UDP:{1} localIPs={2} state={3}",
                manager.Mode,
                manager.HostPosePort,
                GetLocalIPv4Summary(),
                manager.ExperimentState), normalStyle);
        }
        else
        {
            DrawLine(ref y, string.Format(
                "LiveVR mode={0} localUser={1} host={2}:{3} state={4}",
                manager.Mode,
                manager.LocalUserId,
                manager.HostAddress,
                manager.HostPosePort,
                manager.ExperimentState), normalStyle);
        }

        if (manager.IsHost)
        {
            DrawLine(ref y, "ROLE: HOST AUTHORITY | RDW/redirection/reset/logging", normalStyle);

            LiveSpaceProfileProvider provider = LiveSpaceProfileProvider.Instance;
            string liveSpaceStatus = provider != null ? provider.GetStatusText() : "LiveSpace provider missing";
            GUIStyle liveSpaceStyle = provider != null && provider.HasActiveProfile ? normalStyle : warningStyle;
            DrawLine(ref y, liveSpaceStatus, liveSpaceStyle);
            DrawLine(ref y, string.Format(
                "UDP raw={0} pose={1} hello={2} parseFail={3} from={4}",
                manager.HostRawPacketCount,
                manager.HostPosePacketCount,
                manager.HostHelloPacketCount,
                manager.HostParseFailCount,
                string.IsNullOrEmpty(manager.HostLastRemoteEndpoint) ? "-" : manager.HostLastRemoteEndpoint), normalStyle);
            DrawLine(ref y, "Controls: Calibrate at CENTER/FORWARD; Check Ready, Start Run, Stop Run; Recovery uses Soft/Recalibrate Restart.", normalStyle);

            if (!string.IsNullOrEmpty(manager.HostLastRawPacketPreview))
                DrawLine(ref y, "Last UDP: " + manager.HostLastRawPacketPreview, normalStyle);
        }
        else
        {
            DrawLine(ref y, "ROLE: CLIENT SENSOR/VIEW ONLY | receives virtual pose/reset UI", normalStyle);
        }

        if (!manager.IsHost)
        {
            GUIStyle ackStyle = manager.IsConnectedToHost ? normalStyle : warningStyle;
            DrawLine(ref y, string.Format(
                "Client link: connected={0} lastAckAge={1:F3}s lastAckSeq={2} calibrated={3}",
                manager.IsConnectedToHost,
                manager.LastAckAgeSeconds,
                manager.LastAckSequence,
                manager.HasCalibration), ackStyle);
        }

        for (int userId = 0; userId < expectedUserCount; userId++)
        {
            string userSource = manager.GetUserSourceLabel(userId, staleWarningSeconds, true);
            LiveVRPoseSample sample;
            if (!manager.TryGetPose(userId, out sample))
            {
                if (manager.ShouldUseSimulatedUser(userId, staleWarningSeconds, true))
                {
                    DrawLine(ref y, string.Format("User {0}: {1} active", userId, userSource), normalStyle);
                    continue;
                }

                string online = manager.IsClientHelloRecent(userId, staleWarningSeconds * 4.0f)
                    ? "connected, waiting pose"
                    : "no client";
                DrawLine(ref y, string.Format("User {0}: {1} no pose ({2})", userId, userSource, online), warningStyle);
                continue;
            }

            bool stale = sample.AgeSeconds > staleWarningSeconds;
            GUIStyle style = stale || !sample.IsCalibrated ? warningStyle : normalStyle;
            if (manager.ShouldUseSimulatedUser(userId, staleWarningSeconds, true))
                style = normalStyle;
            string spaceStatus = GetLiveSpaceStatus(sample);
            string stage = stale
                ? "STALE, waiting reconnect"
                : sample.IsCalibrated
                ? "calibrated, ready for host start"
                : "connected, waiting host calibration";
            DrawLine(ref y, string.Format(
                "User {0}: {1} {2} pos=({3:F2},{4:F2}) yaw={5:F1} age={6:F3}s {7}",
                userId,
                userSource,
                stage,
                sample.ExperimentPosition.x,
                sample.ExperimentPosition.y,
                sample.YawDegrees,
                sample.AgeSeconds,
                spaceStatus), style);

            if (manager.IsHost)
                DrawGainDebugLine(ref y, userId);
        }
    }

    private void DrawGainDebugLine(ref float y, int userId)
    {
        LiveVRGainDebugSample gain;
        if (!LiveVRGainDebugState.TryGetLatest(userId, out gain))
            return;

        GUIStyle style = gain.ResetActive || !gain.HasRedirection ? warningStyle : normalStyle;
        DrawLine(ref y, string.Format(
            "  Gain u={0}: type={1} redir={2} pΔ={3:F3}m pYawΔ={4:F2} vΔ={5:F3}m vYawΔ={6:F2} injYaw={7:F2} rate={8:F1}/s T/R/C={9:F2}/{10:F2}/{11:F3} yawDiff={12:F1}",
            userId,
            gain.ResetActive ? "RESET" : gain.HasRedirection ? gain.GainType.ToString() : "None",
            string.IsNullOrEmpty(gain.RedirectorName) ? "-" : gain.RedirectorName,
            gain.PhysicalDeltaMeters,
            gain.PhysicalYawDeltaDegrees,
            gain.VirtualDeltaMeters,
            gain.VirtualYawDeltaDegrees,
            gain.InjectedYawDeltaDegrees,
            gain.PrimaryRateDegreesPerSecond,
            gain.TranslationGain,
            gain.RotationGain,
            gain.CurvatureGain,
            gain.VirtualPhysicalYawDiffDegrees), style);
    }

    private string GetLiveSpaceStatus(LiveVRPoseSample sample)
    {
        LiveSpaceProfileProvider provider = LiveSpaceProfileProvider.Instance;
        if (provider == null || !provider.IsEnabled)
            return string.Empty;

        bool contains;
        if (!provider.TryContainsPoint(sample.ExperimentPosition, 0.0f, out contains))
            return "space=?";

        return contains ? "inside" : "outside";
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }

    private void EnsureStyles()
    {
        if (normalStyle != null && warningStyle != null)
            return;

        normalStyle = new GUIStyle(GUI.skin.box);
        normalStyle.alignment = TextAnchor.MiddleLeft;
        normalStyle.fontSize = 14;
        normalStyle.normal.textColor = Color.white;

        warningStyle = new GUIStyle(normalStyle);
        warningStyle.normal.textColor = new Color(1.0f, 0.75f, 0.25f);
    }

    private void DrawLine(ref float y, string text, GUIStyle style)
    {
        GUI.Box(new Rect(screenPosition.x, y, panelSize.x, panelSize.y), text, style);
        y += panelSize.y + 2f;
    }

    private static string GetLocalIPv4Summary()
    {
        try
        {
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < host.AddressList.Length; i++)
            {
                IPAddress address = host.AddressList[i];
                if (address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                string value = address.ToString();
                if (value.StartsWith("127."))
                    continue;

                if (sb.Length > 0)
                    sb.Append(",");
                sb.Append(value);
            }

            return sb.Length > 0 ? sb.ToString() : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
