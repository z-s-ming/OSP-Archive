using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SpaceSetting
{
    [Header("Common Setting")]
    public bool usePredefinedSpace;

    [Header("Predefined Setting")]
    public string name = null;
    public GameObject predefinedSpace;
    public Vector2 position;
    public float rotation;

    [Header("Procedural Setting")]
    public ObjectSetting spaceObjectSetting;
    public List<ObjectSetting> obstacleObjectSettings;

    public Space2D GetSpace()
    {
        if (usePredefinedSpace)
        {
            if (predefinedSpace == null)
            {
                Debug.LogWarning("[SpaceSetting] usePredefinedSpace enabled, but predefinedSpace is null. Fallback to procedural setting.");
                return BuildProceduralSpace();
            }

            // Legacy path: predefinedSpace itself is a single mesh object.
            MeshFilter meshFilter = predefinedSpace.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return new Space2DBuilder()
                    .SetName(name)
                    .SetPrefab(predefinedSpace)
                    .SetLocalPosition(position)
                    .SetLocalRotation(rotation)
                    .Build();
            }

            // Compatible path: predefinedSpace is a composite hierarchy prefab (e.g., MuseumScene).
            return BuildSpaceFromCompositePredefined();
        }

        return BuildProceduralSpace();
    }

    private Space2D BuildProceduralSpace()
    {
        Object2D spaceObject = spaceObjectSetting.GetObject();

        List<Object2D> obstacles = new List<Object2D>();
        if (obstacleObjectSettings != null)
        {
            foreach (ObjectSetting obstacleObjectSetting in obstacleObjectSettings)
                obstacles.Add(obstacleObjectSetting.GetObject());
        }

        return new Space2DBuilder()
            .SetName(spaceObjectSetting.name)
            .SetSpaceObject(spaceObject)
            .SetObstacles(obstacles)
            .Build();
    }

    private Space2D BuildSpaceFromCompositePredefined()
    {
        GameObject tempRoot = GameObject.Instantiate(predefinedSpace);
        tempRoot.name = "[TempCompositeSpace]";

        try
        {
            Transform root = tempRoot.transform;

            List<BoxCollider> wallColliders = GetGroupColliders(root, "Walls");
            if (wallColliders.Count == 0)
            {
                wallColliders.AddRange(tempRoot.GetComponentsInChildren<BoxCollider>(true));
            }

            if (wallColliders.Count == 0)
            {
                Debug.LogWarning("[SpaceSetting] Composite predefined space has no BoxCollider. Fallback to procedural setting.");
                return BuildProceduralSpace();
            }

            Rect roomRect = BuildEnclosingRect(root, wallColliders);
            Object2D spaceObject = new Polygon2DBuilder()
                .SetName(string.IsNullOrEmpty(name) ? "Virtual Space" : name)
                .SetPrefab(null)
                .SetLocalPosition(position)
                .SetLocalRotation(rotation)
                .SetMode(false)
                .SetVertices(RectToVertices(roomRect))
                .Build();

            List<Object2D> obstacles = BuildObstaclesForComposite(root, tempRoot);

            if (obstacles.Count == 0)
            {
                Debug.LogWarning("[SpaceSetting] Composite predefined space parsed with 0 obstacle colliders (Panels/Exhibits).");
            }

            return new Space2DBuilder()
                .SetName(string.IsNullOrEmpty(name) ? "Virtual Space" : name)
                .SetSpaceObject(spaceObject)
                .SetObstacles(obstacles)
                .Build();
        }
        finally
        {
            GameObject.Destroy(tempRoot);
        }
    }

    private List<Object2D> BuildObstaclesForComposite(Transform root, GameObject tempRoot)
    {
        List<Object2D> obstacles = new List<Object2D>();

        // If inspector obstacle list is configured, use it first.
        bool hasManualObstacleSettings = obstacleObjectSettings != null && obstacleObjectSettings.Count > 0;
        if (hasManualObstacleSettings)
        {
            for (int i = 0; i < obstacleObjectSettings.Count; i++)
            {
                ObjectSetting os = obstacleObjectSettings[i];
                if (os == null)
                    continue;

                bool validPolygon = os.type == OBJECT_TYPE.POLYGON && os.vertices != null && os.vertices.Count >= 3;
                bool validCircle = os.type == OBJECT_TYPE.CIRCLE && os.radius > 0.01f;
                bool validLine = os.type == OBJECT_TYPE.LINESEGMENT && os.p1 != os.p2;
                bool validAutoPrefab = os.type == OBJECT_TYPE.AUTO && os.prefab != null;

                if (!validPolygon && !validCircle && !validLine && !validAutoPrefab)
                    continue;

                obstacles.Add(os.GetObject());
            }
        }

        if (obstacles.Count > 0)
            return obstacles;

        // Fallback: auto parse Panels/Exhibits colliders from predefined hierarchy.
        List<BoxCollider> obstacleColliders = new List<BoxCollider>();
        obstacleColliders.AddRange(GetGroupColliders(root, "Panels"));
        obstacleColliders.AddRange(GetGroupColliders(root, "Exhibits"));

        if (obstacleColliders.Count == 0)
        {
            foreach (var c in tempRoot.GetComponentsInChildren<BoxCollider>(true))
            {
                string n = c.transform.name.ToLowerInvariant();
                if (n.Contains("panel") || n.Contains("exhibit"))
                {
                    obstacleColliders.Add(c);
                }
            }
        }

        foreach (var box in obstacleColliders)
        {
            Rect r = BuildColliderRect(root, box);
            if (r.width < 0.02f || r.height < 0.02f)
                continue;

            obstacles.Add(
                new Polygon2DBuilder()
                    .SetName(box.transform.name)
                    .SetPrefab(null)
                    .SetLocalPosition(Vector2.zero)
                    .SetLocalRotation(0f)
                    .SetMode(false)
                    .SetVertices(RectToVertices(r))
                    .Build()
            );
        }

        return obstacles;
    }

    private static List<BoxCollider> GetGroupColliders(Transform root, string childGroupName)
    {
        List<BoxCollider> list = new List<BoxCollider>();
        Transform group = root.Find(childGroupName);
        if (group == null)
            return list;

        list.AddRange(group.GetComponentsInChildren<BoxCollider>(true));
        return list;
    }

    private static Rect BuildEnclosingRect(Transform root, List<BoxCollider> colliders)
    {
        bool initialized = false;
        float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;

        foreach (var c in colliders)
        {
            Rect r = BuildColliderRect(root, c);
            if (!initialized)
            {
                minX = r.xMin;
                maxX = r.xMax;
                minY = r.yMin;
                maxY = r.yMax;
                initialized = true;
            }
            else
            {
                minX = Mathf.Min(minX, r.xMin);
                maxX = Mathf.Max(maxX, r.xMax);
                minY = Mathf.Min(minY, r.yMin);
                maxY = Mathf.Max(maxY, r.yMax);
            }
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static Rect BuildColliderRect(Transform root, BoxCollider box)
    {
        Vector3 c = box.center;
        Vector3 e = box.size * 0.5f;

        Vector3[] localCorners =
        {
            c + new Vector3( e.x,  e.y,  e.z),
            c + new Vector3( e.x,  e.y, -e.z),
            c + new Vector3( e.x, -e.y,  e.z),
            c + new Vector3( e.x, -e.y, -e.z),
            c + new Vector3(-e.x,  e.y,  e.z),
            c + new Vector3(-e.x,  e.y, -e.z),
            c + new Vector3(-e.x, -e.y,  e.z),
            c + new Vector3(-e.x, -e.y, -e.z),
        };

        bool initialized = false;
        float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;

        for (int i = 0; i < localCorners.Length; i++)
        {
            Vector3 world = box.transform.TransformPoint(localCorners[i]);
            Vector3 inRoot = root.InverseTransformPoint(world);
            float x = inRoot.x;
            float y = inRoot.z;

            if (!initialized)
            {
                minX = x;
                maxX = x;
                minY = y;
                maxY = y;
                initialized = true;
            }
            else
            {
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static List<Vector2> RectToVertices(Rect rect)
    {
        return new List<Vector2>
        {
            new Vector2(rect.xMin, rect.yMax),
            new Vector2(rect.xMax, rect.yMax),
            new Vector2(rect.xMax, rect.yMin),
            new Vector2(rect.xMin, rect.yMin)
        };
    }
}
