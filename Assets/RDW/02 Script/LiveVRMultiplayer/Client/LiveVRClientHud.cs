using UnityEngine;

public class LiveVRClientHud : MonoBehaviour
{
    [HideInInspector]
    [SerializeField] private LiveVRNetworkManager networkManager;
    [HideInInspector]
    [SerializeField] private bool showHud = true;
    [HideInInspector]
    [SerializeField] private Vector2 screenPosition = new Vector2(12f, 160f);
    [HideInInspector]
    [SerializeField] private Vector2 panelSize = new Vector2(520f, 34f);
    [HideInInspector]
    [SerializeField] private float resetPromptVisibleSeconds = 4.0f;

    private GUIStyle normalStyle;
    private GUIStyle warningStyle;
    private GUIStyle resetStyle;
    private int lastResetEventId = -1;
    private long lastResetPromptHostUnixMilliseconds = -1;
    private float resetPromptUntilTime;
    private string resetPromptText = string.Empty;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        bool newShowHud,
        Vector2 newScreenPosition,
        Vector2 newPanelSize,
        float newResetPromptVisibleSeconds)
    {
        networkManager = newNetworkManager;
        showHud = newShowHud;
        screenPosition = newScreenPosition;
        panelSize = newPanelSize;
        resetPromptVisibleSeconds = newResetPromptVisibleSeconds;
    }

    private void Update()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null)
            return;

        LiveVRResetPromptMessage prompt;
        if (manager.TryGetLatestResetPrompt(out prompt) &&
            (prompt.EventId != lastResetEventId || prompt.HostUnixMilliseconds != lastResetPromptHostUnixMilliseconds))
        {
            lastResetEventId = prompt.EventId;
            lastResetPromptHostUnixMilliseconds = prompt.HostUnixMilliseconds;
            resetPromptUntilTime = Time.unscaledTime + resetPromptVisibleSeconds;
            string turnInstruction = BuildTurnInstruction(manager, prompt);
            resetPromptText = prompt.HasTargetPosition
                ? string.Format(
                    "RESET: {0}  {1}  target=({2:F2},{3:F2})",
                    prompt.ResetType,
                    turnInstruction,
                    prompt.TargetPosition.x,
                    prompt.TargetPosition.y)
                : string.Format("RESET: {0}  {1}", prompt.ResetType, turnInstruction);
        }
        else if (!manager.TryGetLatestResetPrompt(out prompt))
        {
            resetPromptUntilTime = 0.0f;
            resetPromptText = string.Empty;
        }
    }

    private void OnGUI()
    {
        if (!showHud)
            return;

        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (manager == null || manager.Mode == LiveVRExperimentMode.Disabled || manager.IsHost)
            return;

        if (manager.IsWaitingForClientStartupConfirmation)
            return;

        bool hasVisibleResetPrompt = Time.unscaledTime < resetPromptUntilTime;
        if (manager.IsConnectedToHost &&
            manager.HasCalibration &&
            manager.ExperimentState == LiveVRExperimentState.Running &&
            !hasVisibleResetPrompt)
        {
            return;
        }

        EnsureStyles();

        float y = screenPosition.y;
        if (!manager.IsConnectedToHost)
        {
            DrawLine(ref y, string.Format("User {0}: connecting to Host {1}:{2}", manager.LocalUserId, manager.HostAddress, manager.HostPosePort), warningStyle);
            DrawLine(ref y, manager.HostDiscoveryStatus, normalStyle);
            DrawLine(ref y, "Keep headset app open.", normalStyle);
        }
        else if (!manager.HasHostAssignment)
        {
            DrawLine(ref y, "Connected to Host. Waiting for Host user assignment.", warningStyle);
            DrawLine(ref y, manager.ClientAssignmentStatus, normalStyle);
        }
        else if (!manager.HasCalibration)
        {
            DrawLine(ref y, string.Format("User {0}: connected", manager.LocalUserId), normalStyle);
            DrawLine(ref y, "Stand on CENTER, face FORWARD, wait for operator calibration.", warningStyle);
        }
        else
        {
            DrawLine(ref y, "Calibration complete. Walk to start position and wait.", normalStyle);
        }

        if (hasVisibleResetPrompt)
            DrawLine(ref y, resetPromptText, resetStyle);
    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }

    private string BuildTurnInstruction(LiveVRNetworkManager manager, LiveVRResetPromptMessage prompt)
    {
        if (prompt.HasTurnInstruction)
        {
            float remaining = Mathf.Max(0.0f, prompt.RemainingTurnDegrees);
            if (remaining < 5.0f)
                return "Hold still";

            string direction = prompt.TurnDirectionSign >= 0 ? "left" : "right";
            return string.Format(
                "Keep turning {0} {1:F0} deg ({2:P0})",
                direction,
                remaining,
                Mathf.Clamp01(prompt.Progress01));
        }

        return BuildTurnInstruction(manager, prompt.DirectionHint);
    }

    private string BuildTurnInstruction(LiveVRNetworkManager manager, Vector2 targetDirection)
    {
        if (targetDirection.sqrMagnitude <= Mathf.Epsilon)
            return "Stop and turn in place";

        Vector2 currentForward = ResolveCurrentForward(manager);
        float signedAngle = Vector2.SignedAngle(currentForward, targetDirection.normalized);
        float absAngle = Mathf.Abs(signedAngle);

        if (absAngle < 3.0f)
            return "Hold still";

        return signedAngle > 0.0f
            ? string.Format("Turn left {0:F0} deg", absAngle)
            : string.Format("Turn right {0:F0} deg", absAngle);
    }

    private Vector2 ResolveCurrentForward(LiveVRNetworkManager manager)
    {
        LiveVRPoseSample localPose;
        if (manager != null && manager.TryGetLocalPoseSample(out localPose))
            return NormalizeOrFallback(Utility.RotateVector2(Vector2.up, localPose.YawDegrees), Vector2.up);

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            return NormalizeOrFallback(new Vector2(mainCamera.transform.forward.x, mainCamera.transform.forward.z), Vector2.up);

        return Vector2.up;
    }

    private static Vector2 NormalizeOrFallback(Vector2 value, Vector2 fallback)
    {
        if (value.sqrMagnitude > Mathf.Epsilon)
            return value.normalized;

        if (fallback.sqrMagnitude > Mathf.Epsilon)
            return fallback.normalized;

        return Vector2.up;
    }

    private void EnsureStyles()
    {
        if (normalStyle != null && warningStyle != null && resetStyle != null)
            return;

        normalStyle = new GUIStyle(GUI.skin.box);
        normalStyle.alignment = TextAnchor.MiddleLeft;
        normalStyle.fontSize = 16;
        normalStyle.normal.textColor = Color.white;

        warningStyle = new GUIStyle(normalStyle);
        warningStyle.normal.textColor = new Color(1.0f, 0.75f, 0.25f);

        resetStyle = new GUIStyle(normalStyle);
        resetStyle.fontSize = 24;
        resetStyle.normal.textColor = Color.red;
    }

    private void DrawLine(ref float y, string text, GUIStyle style)
    {
        GUI.Box(new Rect(screenPosition.x, y, panelSize.x, panelSize.y), text, style);
        y += panelSize.y + 4f;
    }
}
