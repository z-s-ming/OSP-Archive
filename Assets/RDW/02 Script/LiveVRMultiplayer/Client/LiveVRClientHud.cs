using UnityEngine;

public class LiveVRClientHud : MonoBehaviour
{
    [HideInInspector]
    [SerializeField] private LiveVRNetworkManager networkManager;
    [HideInInspector]
    [SerializeField] private LiveVRClientTargetGuide targetGuide;
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

    public void ClearResetPrompt()
    {
        lastResetEventId = -1;
        lastResetPromptHostUnixMilliseconds = -1;
        resetPromptUntilTime = 0.0f;
        resetPromptText = string.Empty;
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
            if (IsResetPromptComplete(prompt))
            {
                ClearResetPrompt();
                manager.ClearLatestResetPrompt();
                return;
            }

            lastResetEventId = prompt.EventId;
            lastResetPromptHostUnixMilliseconds = prompt.HostUnixMilliseconds;
            resetPromptUntilTime = Time.unscaledTime + resetPromptVisibleSeconds;
            resetPromptText = BuildResetGuideText(manager, prompt);
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
            !hasVisibleResetPrompt &&
            !IsLocalRunComplete())
        {
            return;
        }

        EnsureStyles();

        if (hasVisibleResetPrompt)
        {
            DrawResetGuide(resetPromptText);
            return;
        }

        if (manager.IsConnectedToHost &&
            manager.HasCalibration &&
            manager.ExperimentState == LiveVRExperimentState.Running &&
            IsLocalRunComplete())
        {
            DrawCenterGuide("\u4f60\u5df2\u5b8c\u6210\n\u8bf7\u539f\u5730\u7b49\u5f85");
            return;
        }

        if (manager.IsConnectedToHost &&
            manager.HasCalibration &&
            manager.ExperimentState != LiveVRExperimentState.Running)
        {
            DrawCenterGuide(manager.ExperimentState == LiveVRExperimentState.Completed
                ? "\u5b9e\u9a8c\u5df2\u7ed3\u675f\n\u8bf7\u7b49\u5f85\u5b9e\u9a8c\u5458"
                : "\u6821\u51c6\u5b8c\u6210\n\u7b49\u5f85\u4e3b\u673a\u5f00\u59cb");
            return;
        }

        float y = screenPosition.y;
        if (!manager.IsConnectedToHost)
        {
            DrawLine(ref y, string.Format("\u7528\u6237 {0}\uff1a\u6b63\u5728\u8fde\u63a5\u4e3b\u673a {1}:{2}", manager.LocalUserId, manager.HostAddress, manager.HostPosePort), warningStyle);
            DrawLine(ref y, "\u8bf7\u4fdd\u6301\u5934\u663e\u7a0b\u5e8f\u5f00\u542f", normalStyle);
        }
        else if (!manager.HasHostAssignment)
        {
            DrawLine(ref y, "\u5df2\u8fde\u63a5\u4e3b\u673a\uff0c\u7b49\u5f85\u5206\u914d\u7528\u6237", warningStyle);
        }
        else if (!manager.HasCalibration)
        {
            DrawLine(ref y, string.Format("\u7528\u6237 {0}\uff1a\u5df2\u8fde\u63a5", manager.LocalUserId), normalStyle);
            DrawLine(ref y, "\u8bf7\u7ad9\u5230\u4e2d\u5fc3\u70b9\uff0c\u9762\u5411\u524d\u65b9\uff0c\u7b49\u5f85\u6821\u51c6", warningStyle);
        }

    }

    private LiveVRNetworkManager ResolveNetworkManager()
    {
        if (networkManager == null)
            networkManager = LiveVRNetworkManager.Instance;

        return networkManager;
    }

    private bool IsLocalRunComplete()
    {
        LiveVRClientTargetGuide guide = ResolveTargetGuide();
        return guide != null && guide.IsLocalRunComplete;
    }

    private LiveVRClientTargetGuide ResolveTargetGuide()
    {
        if (targetGuide == null)
            targetGuide = GetComponent<LiveVRClientTargetGuide>();

        return targetGuide;
    }

    private string BuildResetGuideText(LiveVRNetworkManager manager, LiveVRResetPromptMessage prompt)
    {
        int directionSign = prompt.HasTurnInstruction
            ? prompt.TurnDirectionSign
            : ResolveDirectionSign(manager, prompt.DirectionHint);

        bool turnLeft = directionSign >= 0;
        return turnLeft ? "<--\n\u5411\u5de6\u8f6c" : "-->\n\u5411\u53f3\u8f6c";
    }

    private static bool IsResetPromptComplete(LiveVRResetPromptMessage prompt)
    {
        return prompt.HasTurnInstruction &&
               (prompt.Progress01 >= 0.995f || prompt.RemainingTurnDegrees <= 5.0f);
    }

    private int ResolveDirectionSign(LiveVRNetworkManager manager, Vector2 targetDirection)
    {
        if (targetDirection.sqrMagnitude <= Mathf.Epsilon)
            return 1;

        Vector2 currentForward = ResolveCurrentForward(manager);
        float signedAngle = Vector2.SignedAngle(currentForward, targetDirection.normalized);
        return signedAngle >= 0.0f ? 1 : -1;
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
        resetStyle.alignment = TextAnchor.MiddleCenter;
        resetStyle.fontSize = 48;
        resetStyle.normal.textColor = new Color(1.0f, 0.92f, 0.2f);
    }

    private void DrawResetGuide(string text)
    {
        float width = Mathf.Max(panelSize.x, 520.0f);
        float height = Mathf.Max(panelSize.y * 3.0f, 128.0f);
        GUI.Box(GetCenteredRect(width, height), text, resetStyle);
    }

    private void DrawCenterGuide(string text)
    {
        float width = Mathf.Max(panelSize.x, 560.0f);
        float height = Mathf.Max(panelSize.y * 3.0f, 128.0f);
        GUI.Box(GetCenteredRect(width, height), text, resetStyle);
    }

    private static Rect GetCenteredRect(float width, float height)
    {
        return new Rect(
            Mathf.Max(0.0f, (Screen.width - width) * 0.5f),
            Mathf.Max(0.0f, (Screen.height - height) * 0.5f),
            width,
            height);
    }

    private void DrawLine(ref float y, string text, GUIStyle style)
    {
        GUI.Box(new Rect(screenPosition.x, y, panelSize.x, panelSize.y), text, style);
        y += panelSize.y + 4f;
    }
}
