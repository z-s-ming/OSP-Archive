using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-12000)]
public class LiveSpaceProfileProvider : MonoBehaviour
{
    public static LiveSpaceProfileProvider Instance { get; private set; }

    [SerializeField] private bool enableLiveSpaceProfile = true;
    [SerializeField] private LiveSpaceProfileSource source = LiveSpaceProfileSource.Rectangle;
    [SerializeField] private float rectangleWidthMeters = 10.0f;
    [SerializeField] private float rectangleDepthMeters = 10.0f;
    [SerializeField] private List<Vector2> manualBoundaryPolygon = new List<Vector2>();
    [SerializeField] private string spaceName = "Live Physical Space";

    private LiveSpaceProfile activeProfile;

    public LiveSpaceProfile ActiveProfile { get { return activeProfile; } }
    public bool HasActiveProfile { get { return activeProfile != null && activeProfile.IsValid; } }
    public bool IsEnabled { get { return enableLiveSpaceProfile; } }

    public void Configure(
        bool newEnableLiveSpaceProfile,
        LiveSpaceProfileSource newSource,
        float newRectangleWidthMeters,
        float newRectangleDepthMeters,
        List<Vector2> newManualBoundaryPolygon,
        string newSpaceName)
    {
        enableLiveSpaceProfile = newEnableLiveSpaceProfile;
        source = newSource;
        rectangleWidthMeters = newRectangleWidthMeters;
        rectangleDepthMeters = newRectangleDepthMeters;
        manualBoundaryPolygon = newManualBoundaryPolygon != null
            ? new List<Vector2>(newManualBoundaryPolygon)
            : new List<Vector2>();
        spaceName = string.IsNullOrEmpty(newSpaceName) ? "Live Physical Space" : newSpaceName;
        activeProfile = null;
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool TryBuildActiveSpace(out Space2D space, out LiveSpaceProfile profile)
    {
        space = null;
        profile = null;

        if (!enableLiveSpaceProfile)
            return false;

        profile = EnsureProfile();
        if (profile == null || !profile.IsValid)
            return false;

        space = profile.BuildSpace2D(spaceName);
        return space != null;
    }

    public bool TryGetActiveBounds(out Vector2 min, out Vector2 max)
    {
        min = Vector2.zero;
        max = Vector2.zero;

        LiveSpaceProfile profile = EnsureProfile();
        return profile != null && profile.TryGetBounds(out min, out max);
    }

    public bool TryContainsPoint(Vector2 point, float safetyMarginMeters, out bool contains)
    {
        contains = false;

        LiveSpaceProfile profile = EnsureProfile();
        if (profile == null || !profile.IsValid)
            return false;

        contains = IsPointInsidePolygon(point, profile.BoundaryPolygon);
        if (contains && safetyMarginMeters > 0.0f)
            contains = DistanceToPolygonBoundary(point, profile.BoundaryPolygon) >= safetyMarginMeters;

        return true;
    }

    public string GetStatusText()
    {
        if (!enableLiveSpaceProfile)
            return "LiveSpace disabled";

        LiveSpaceProfile profile = EnsureProfile();
        if (profile == null || !profile.IsValid)
            return "LiveSpace invalid";

        Vector2 min;
        Vector2 max;
        if (profile.TryGetBounds(out min, out max))
        {
            Vector2 size = max - min;
            return string.Format(
                "LiveSpace {0} vertices={1} size=({2:F1},{3:F1})",
                profile.Source,
                profile.BoundaryPolygon.Count,
                size.x,
                size.y);
        }

        return string.Format("LiveSpace {0} vertices={1}", profile.Source, profile.BoundaryPolygon.Count);
    }

    private LiveSpaceProfile EnsureProfile()
    {
        if (activeProfile != null && activeProfile.IsValid)
            return activeProfile;

        if (source == LiveSpaceProfileSource.ManualPolygon && manualBoundaryPolygon != null && manualBoundaryPolygon.Count >= 3)
            activeProfile = LiveSpaceProfile.CreateManualPolygon(manualBoundaryPolygon);
        else
            activeProfile = LiveSpaceProfile.CreateRectangle(rectangleWidthMeters, rectangleDepthMeters);

        return activeProfile;
    }

    private static bool IsPointInsidePolygon(Vector2 point, IList<Vector2> polygon)
    {
        bool inside = false;
        int count = polygon != null ? polygon.Count : 0;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            Vector2 pi = polygon[i];
            Vector2 pj = polygon[j];
            bool crosses = (pi.y > point.y) != (pj.y > point.y);
            if (crosses)
            {
                float xAtY = (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x;
                if (point.x < xAtY)
                    inside = !inside;
            }
        }

        return inside;
    }

    private static float DistanceToPolygonBoundary(Vector2 point, IList<Vector2> polygon)
    {
        float minDistance = float.PositiveInfinity;
        int count = polygon != null ? polygon.Count : 0;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % count];
            minDistance = Mathf.Min(minDistance, DistancePointToSegment(point, a, b));
        }

        return minDistance;
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
            return Vector2.Distance(point, a);

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
        Vector2 closest = a + ab * t;
        return Vector2.Distance(point, closest);
    }
}
