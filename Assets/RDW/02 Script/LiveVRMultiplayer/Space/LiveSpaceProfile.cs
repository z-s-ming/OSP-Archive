using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public enum LiveSpaceProfileSource
{
    Rectangle = 0,
    ManualPolygon = 1,
    QuestBoundary = 2
}

[Serializable]
public class LiveSpaceProfile
{
    public string ProfileId;
    public LiveSpaceProfileSource Source = LiveSpaceProfileSource.Rectangle;
    public List<Vector2> BoundaryPolygon = new List<Vector2>();
    public Vector2 OriginWorldPosition = Vector2.zero;
    public float OriginYawDegrees = 0.0f;
    public long CreatedUnixMilliseconds;

    public bool IsValid
    {
        get { return BoundaryPolygon != null && BoundaryPolygon.Count >= 3; }
    }

    public static LiveSpaceProfile CreateRectangle(float widthMeters, float depthMeters)
    {
        float halfWidth = Mathf.Max(0.1f, widthMeters) * 0.5f;
        float halfDepth = Mathf.Max(0.1f, depthMeters) * 0.5f;

        LiveSpaceProfile profile = new LiveSpaceProfile();
        profile.ProfileId = "rectangle_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        profile.Source = LiveSpaceProfileSource.Rectangle;
        profile.CreatedUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        profile.BoundaryPolygon = new List<Vector2>
        {
            new Vector2(-halfWidth, halfDepth),
            new Vector2(halfWidth, halfDepth),
            new Vector2(halfWidth, -halfDepth),
            new Vector2(-halfWidth, -halfDepth)
        };
        return profile;
    }

    public static LiveSpaceProfile CreateManualPolygon(IList<Vector2> vertices)
    {
        LiveSpaceProfile profile = new LiveSpaceProfile();
        profile.ProfileId = "polygon_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        profile.Source = LiveSpaceProfileSource.ManualPolygon;
        profile.CreatedUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        profile.BoundaryPolygon = vertices != null ? new List<Vector2>(vertices) : new List<Vector2>();
        return profile;
    }

    public Space2D BuildSpace2D(string name)
    {
        if (!IsValid)
            return null;

        Polygon2D polygon = new Polygon2DBuilder()
            .SetName(string.IsNullOrEmpty(name) ? "Live Physical Space" : name)
            .SetPrefab(null)
            .SetLocalPosition(OriginWorldPosition)
            .SetLocalRotation(OriginYawDegrees)
            .SetMode(false)
            .SetVertices(new List<Vector2>(BoundaryPolygon))
            .Build();

        return new Space2DBuilder()
            .SetName(string.IsNullOrEmpty(name) ? "Live Physical Space" : name)
            .SetSpaceObject(polygon)
            .SetObstacles(new List<Object2D>())
            .Build();
    }

    public bool TryGetBounds(out Vector2 min, out Vector2 max)
    {
        min = Vector2.zero;
        max = Vector2.zero;

        if (!IsValid)
            return false;

        min = BoundaryPolygon[0];
        max = BoundaryPolygon[0];
        for (int i = 1; i < BoundaryPolygon.Count; i++)
        {
            Vector2 point = BoundaryPolygon[i];
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return true;
    }
}
