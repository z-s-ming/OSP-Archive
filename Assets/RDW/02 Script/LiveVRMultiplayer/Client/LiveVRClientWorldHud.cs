using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class LiveVRClientWorldHud : MonoBehaviour
{
    [SerializeField] private LiveVRNetworkManager networkManager;
    [SerializeField] private LiveVRClientTargetGuide targetGuide;
    [SerializeField] private Transform hmdCamera;
    [SerializeField] private bool showHud = true;
    [SerializeField] private float distanceMeters = 1.25f;
    [SerializeField] private Vector2 canvasSize = new Vector2(1150.0f, 550.0f);
    [SerializeField] private float verticalOffsetMeters = -0.08f;
    [SerializeField] private float updateIntervalSeconds = 0.1f;

    private Canvas canvas;
    private RectTransform canvasRect;
    private Text statusText;
    private Image background;
    private TextMesh fallbackText;
    private string lastStatusText = string.Empty;
    private float nextUpdateTime;
    private int lastResetEventId = -1;
    private long lastResetPromptHostUnixMilliseconds = -1;
    private string resetPrompt = string.Empty;
    private float resetPromptUntilTime;

    public void Configure(
        LiveVRNetworkManager newNetworkManager,
        Transform newHmdCamera,
        bool newShowHud,
        float newDistanceMeters,
        Vector2 newCanvasSize,
        float newVerticalOffsetMeters,
        float newUpdateIntervalSeconds)
    {
        networkManager = newNetworkManager;
        hmdCamera = newHmdCamera;
        showHud = newShowHud;
        distanceMeters = Mathf.Max(0.25f, newDistanceMeters);
        canvasSize = NormalizeCanvasSize(newCanvasSize);
        verticalOffsetMeters = newVerticalOffsetMeters;
        updateIntervalSeconds = Mathf.Max(0.02f, newUpdateIntervalSeconds);
    }

    public void ClearResetPrompt()
    {
        lastResetEventId = -1;
        lastResetPromptHostUnixMilliseconds = -1;
        resetPrompt = string.Empty;
        resetPromptUntilTime = 0.0f;
        lastStatusText = string.Empty;
        nextUpdateTime = 0.0f;
    }

    private void LateUpdate()
    {
        LiveVRNetworkManager manager = ResolveNetworkManager();
        if (!ShouldShow(manager))
        {
            if (canvas != null)
                canvas.enabled = false;
            return;
        }

        EnsureCanvas();
        if (canvas == null)
            return;

        FollowHead();
        PollResetPrompt(manager);

        bool visibleThisFrame = ShouldShowStatusPanel(manager);
        canvas.enabled = visibleThisFrame;
        if (fallbackText != null)
            fallbackText.gameObject.SetActive(visibleThisFrame);

        if (!visibleThisFrame)
            return;

        if (Time.unscaledTime >= nextUpdateTime)
        {
            nextUpdateTime = Time.unscaledTime + updateIntervalSeconds;
            lastStatusText = BuildStatusText(manager);
            bool resetActive = Time.unscaledTime < resetPromptUntilTime;
            bool localComplete = IsLocalRunComplete();
            bool waitingForStart = !resetActive &&
                                   manager.IsConnectedToHost &&
                                   manager.HasCalibration &&
                                   (manager.ExperimentState != LiveVRExperimentState.Running || localComplete);
            statusText.alignment = resetActive || waitingForStart ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            statusText.fontSize = resetActive ? 120 : waitingForStart ? 54 : 32;
            statusText.text = lastStatusText;
            if (fallbackText != null)
            {
                fallbackText.anchor = resetActive || waitingForStart ? TextAnchor.MiddleCenter : TextAnchor.UpperLeft;
                fallbackText.alignment = resetActive || waitingForStart ? TextAlignment.Center : TextAlignment.Left;
                fallbackText.fontSize = resetActive ? 120 : waitingForStart ? 92 : 64;
                fallbackText.text = lastStatusText;
            }
            background.color = resetActive
                ? new Color(0.02f, 0.02f, 0.02f, 0.86f)
                : manager.IsConnectedToHost
                ? new Color(0.02f, 0.08f, 0.06f, 0.78f)
                : new Color(0.16f, 0.04f, 0.02f, 0.82f);
        }
    }

    private bool ShouldShow(LiveVRNetworkManager manager)
    {
        return showHud &&
               manager != null &&
               manager.Mode != LiveVRExperimentMode.Disabled &&
               !manager.IsHost;
    }

    private bool ShouldShowStatusPanel(LiveVRNetworkManager manager)
    {
        if (manager == null)
            return false;

        if (manager.IsWaitingForClientStartupConfirmation)
            return false;

        if (Time.unscaledTime < resetPromptUntilTime)
            return true;

        if (!manager.IsConnectedToHost)
            return true;

        if (!manager.HasCalibration)
            return true;

        if (manager.ExperimentState == LiveVRExperimentState.Running && !IsLocalRunComplete())
            return false;

        return true;
    }

    private void EnsureCanvas()
    {
        if (canvas != null)
            return;

        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform == null)
            return;

        GameObject canvasObject = new GameObject("LiveVR Client World HUD");
        canvasObject.layer = ResolveLayer("UI");
        canvasObject.transform.SetParent(cameraTransform, false);

        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cameraTransform.GetComponent<Camera>();
        canvas.sortingOrder = 500;

        canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = canvasSize;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 1200.0f;

        background = canvasObject.AddComponent<Image>();
        background.color = new Color(0.02f, 0.04f, 0.06f, 0.78f);

        GameObject textObject = new GameObject("Status Text");
        textObject.layer = canvasObject.layer;
        textObject.transform.SetParent(canvasObject.transform, false);

        statusText = textObject.AddComponent<Text>();
        statusText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        statusText.fontSize = 32;
        statusText.alignment = TextAnchor.MiddleLeft;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.verticalOverflow = VerticalWrapMode.Truncate;
        statusText.color = Color.white;

        RectTransform textRect = statusText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(40.0f, 30.0f);
        textRect.offsetMax = new Vector2(-40.0f, -30.0f);

        GameObject fallbackObject = new GameObject("Fallback TextMesh");
        fallbackObject.layer = canvasObject.layer;
        fallbackObject.transform.SetParent(cameraTransform, false);
        fallbackText = fallbackObject.AddComponent<TextMesh>();
        fallbackText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        fallbackText.fontSize = 64;
        fallbackText.characterSize = 0.0085f;
        fallbackText.anchor = TextAnchor.UpperLeft;
        fallbackText.alignment = TextAlignment.Left;
        fallbackText.color = Color.white;
        fallbackText.text = "LIVE VR CLIENT";

        MeshRenderer renderer = fallbackObject.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.sortingOrder = 600;
    }

    private void FollowHead()
    {
        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform == null || canvasRect == null)
            return;

        canvasRect.localPosition = new Vector3(0.0f, verticalOffsetMeters, distanceMeters);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * 0.001f;

        if (fallbackText != null)
        {
            bool resetActive = Time.unscaledTime < resetPromptUntilTime;
            bool localComplete = IsLocalRunComplete();
            bool waitingForStart = !resetActive &&
                                   networkManager != null &&
                                   networkManager.IsConnectedToHost &&
                                   networkManager.HasCalibration &&
                                   (networkManager.ExperimentState != LiveVRExperimentState.Running || localComplete);
            fallbackText.transform.localPosition = resetActive || waitingForStart
                ? new Vector3(0.0f, verticalOffsetMeters, Mathf.Max(0.25f, distanceMeters - 0.02f))
                : new Vector3(-0.53f, 0.24f + verticalOffsetMeters, Mathf.Max(0.25f, distanceMeters - 0.02f));
            fallbackText.transform.localRotation = Quaternion.identity;
            fallbackText.transform.localScale = Vector3.one;
        }
    }

    private void PollResetPrompt(LiveVRNetworkManager manager)
    {
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
            resetPromptUntilTime = Time.unscaledTime + 4.0f;
            resetPrompt = BuildResetGuideText(manager, prompt);
        }
        else if (!manager.TryGetLatestResetPrompt(out prompt))
        {
            resetPromptUntilTime = 0.0f;
            resetPrompt = string.Empty;
        }
    }

    private string BuildStatusText(LiveVRNetworkManager manager)
    {
        if (Time.unscaledTime < resetPromptUntilTime)
            return resetPrompt;

        if (manager.IsConnectedToHost &&
            manager.HasCalibration &&
            manager.ExperimentState == LiveVRExperimentState.Completed)
        {
            return "\u5b9e\u9a8c\u5df2\u7ed3\u675f\n\u8bf7\u7b49\u5f85\u5b9e\u9a8c\u5458";
        }

        if (manager.IsConnectedToHost &&
            manager.HasCalibration &&
            manager.ExperimentState == LiveVRExperimentState.Running &&
            IsLocalRunComplete())
        {
            return "\u4f60\u5df2\u5b8c\u6210\n\u8bf7\u539f\u5730\u7b49\u5f85";
        }

        if (manager.IsConnectedToHost &&
            manager.HasCalibration &&
            manager.ExperimentState != LiveVRExperimentState.Running)
        {
            return "\u6821\u51c6\u5b8c\u6210\n\u7b49\u5f85\u4e3b\u673a\u5f00\u59cb";
        }

        StringBuilder sb = new StringBuilder(256);
        sb.AppendFormat("\u7528\u6237 {0}\n\n", manager.LocalUserId);

        if (!manager.IsConnectedToHost)
        {
            sb.AppendLine("\u6b63\u5728\u8fde\u63a5\u4e3b\u673a");
            sb.AppendFormat("{0}:{1}\n", manager.HostAddress, manager.HostPosePort);
            sb.AppendLine("\u7b49\u5f85\u4e3b\u673a\u54cd\u5e94");
            sb.AppendLine("\u8bf7\u4fdd\u6301\u5934\u663e\u7a0b\u5e8f\u5f00\u542f");
        }
        else if (!manager.HasHostAssignment)
        {
            sb.AppendLine("\u5df2\u8fde\u63a5\u4e3b\u673a");
            sb.AppendLine("\u7b49\u5f85\u4e3b\u673a\u5206\u914d\u7528\u6237");
        }
        else if (!manager.HasCalibration)
        {
            sb.AppendLine("\u5df2\u8fde\u63a5");
            sb.AppendFormat("\u5df2\u5206\u914d\u7528\u6237 {0}\n", manager.LocalUserId);
            sb.AppendLine("\u8bf7\u7ad9\u5230\u4e2d\u5fc3\u70b9");
            sb.AppendLine("\u9762\u5411\u6807\u8bb0\u7684\u524d\u65b9");
            sb.AppendLine("\u7b49\u5f85\u5b9e\u9a8c\u5458\u6821\u51c6");
        }
        else if (manager.ExperimentState == LiveVRExperimentState.Running)
        {
            if (Time.unscaledTime < resetPromptUntilTime)
                sb.AppendLine("\u9700\u8981\u91cd\u7f6e");
        }
        return sb.ToString();
    }

    private string FormatAge(float seconds)
    {
        if (float.IsInfinity(seconds))
            return "none";

        return string.Format("{0:F2}s", seconds);
    }

    private Transform ResolveHmdCamera()
    {
        if (hmdCamera != null)
            return hmdCamera;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            hmdCamera = mainCamera.transform;

        return hmdCamera;
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

        Transform cameraTransform = ResolveHmdCamera();
        if (cameraTransform != null)
            return NormalizeOrFallback(new Vector2(cameraTransform.forward.x, cameraTransform.forward.z), Vector2.up);

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

    private int ResolveLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? layer : 0;
    }

    private static Vector2 NormalizeCanvasSize(Vector2 size)
    {
        if (size.x <= 10.0f && size.y <= 10.0f)
            return new Vector2(Mathf.Max(100.0f, size.x * 1000.0f), Mathf.Max(100.0f, size.y * 1000.0f));

        return new Vector2(Mathf.Max(100.0f, size.x), Mathf.Max(100.0f, size.y));
    }
}
