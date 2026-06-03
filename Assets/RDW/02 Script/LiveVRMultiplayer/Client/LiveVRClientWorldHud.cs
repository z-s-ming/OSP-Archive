using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class LiveVRClientWorldHud : MonoBehaviour
{
    [SerializeField] private LiveVRNetworkManager networkManager;
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
            statusText.text = lastStatusText;
            if (fallbackText != null)
                fallbackText.text = lastStatusText;
            background.color = manager.IsConnectedToHost
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

        if (manager.ExperimentState == LiveVRExperimentState.Running)
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
            fallbackText.transform.localPosition = new Vector3(-0.53f, 0.24f + verticalOffsetMeters, Mathf.Max(0.25f, distanceMeters - 0.02f));
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
            lastResetEventId = prompt.EventId;
            lastResetPromptHostUnixMilliseconds = prompt.HostUnixMilliseconds;
            resetPromptUntilTime = Time.unscaledTime + 4.0f;
            resetPrompt = string.Format("RESET {0}\n{1}", prompt.ResetType, BuildTurnInstruction(manager, prompt));
        }
        else if (!manager.TryGetLatestResetPrompt(out prompt))
        {
            resetPromptUntilTime = 0.0f;
            resetPrompt = string.Empty;
        }
    }

    private string BuildStatusText(LiveVRNetworkManager manager)
    {
        StringBuilder sb = new StringBuilder(256);
        sb.AppendFormat("LIVE VR USER {0}\n\n", manager.LocalUserId);

        if (!manager.IsConnectedToHost)
        {
            sb.AppendLine("Connecting to Host...");
            sb.AppendFormat("{0}:{1}\n", manager.HostAddress, manager.HostPosePort);
            sb.AppendLine("Waiting for Host ACK.");
            sb.AppendLine(manager.HostDiscoveryStatus);
            sb.AppendLine("Keep the headset app open.");
        }
        else if (!manager.HasHostAssignment)
        {
            sb.AppendLine("Connected to Host.");
            sb.AppendLine("Waiting for Host user assignment.");
            sb.AppendLine(manager.ClientAssignmentStatus);
        }
        else if (!manager.HasCalibration)
        {
            sb.AppendLine("Connected.");
            sb.AppendFormat("Assigned User {0}.\n", manager.LocalUserId);
            sb.AppendLine("Stand on the CENTER mark.");
            sb.AppendLine("Face the marked FORWARD direction.");
            sb.AppendLine("Waiting for operator calibration.");
        }
        else if (manager.ExperimentState == LiveVRExperimentState.Running)
        {
            if (Time.unscaledTime < resetPromptUntilTime)
                sb.AppendLine("Reset required.");
        }
        else if (manager.ExperimentState == LiveVRExperimentState.Completed)
        {
            sb.AppendLine("Experiment complete.");
            sb.AppendLine("Please wait for the operator.");
        }
        else
        {
            sb.AppendLine("Calibration complete.");
            sb.AppendLine("Enter passthrough if needed.");
            sb.AppendLine("Walk to your start position.");
            sb.AppendLine("Wait for experiment start.");
        }

        if (Time.unscaledTime < resetPromptUntilTime)
        {
            sb.AppendLine();
            sb.AppendLine(resetPrompt);
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
