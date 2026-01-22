using UnityEngine;
using System.Collections.Generic;
using JSB.RDW.Voronoi;

[ExecuteInEditMode]
public class WeightedVoronoiGrid : MonoBehaviour
{
    [Header("Grid Settings")]
    [Tooltip("物理场景的大小（单位：米）")]
    public Vector2 physicalSpaceSize = new Vector2(10f, 10f);
    
    [Tooltip("网格单元大小（单位：米）")]
    public float gridCellSize = 0.1f;
    
    [Header("Seed Points")]
    public List<SeedPoint> seedPoints = new List<SeedPoint>();
    
    [Header("Visualization")]
    public bool showGrid = true;
    public bool showSeedPoints = true;
    public float seedPointGizmoSize = 0.2f;
    
    [Header("Voronoi Parameters")]
    [Tooltip("主朝向扩散速度")]
    public float forwardSpeed = 1.0f;
    [Tooltip("两侧扩散速度")]
    public float sideSpeed = 0.75f;
    [Tooltip("反方向扩散速度")]
    public float backwardSpeed = 0.5f;
    
    [Header("Angle Thresholds (degrees)")]
    [Tooltip("前方区域的角度阈值")]
    [Range(0, 90)] public float forwardAngle = 45f;
    [Tooltip("两侧区域的角度阈值")]
    [Range(0, 90)] public float sideAngle = 135f;
    
    // 内部变量
    private Texture2D voronoiTexture;
    private MeshRenderer meshRenderer;
    private int gridWidth, gridHeight;
    private int[,] spacePartition; // 空间划分二维数组，存储每个格子所属的种子点索引
    
    // 预定义颜色池（四色定理 - 4种颜色足以区分相邻区域）
    private static readonly Color[] colorPalette = new Color[]
    {
        new Color(1f, 0.2f, 0.2f),      // 红色
        new Color(0.2f, 0.8f, 0.2f),    // 绿色
        new Color(0.2f, 0.5f, 1f),      // 蓝色
        new Color(1f, 0.8f, 0.2f),      // 黄色
    };
    
    [System.Serializable]
    public class SeedPoint
    {
        public Vector2 position;  // 在物理空间中的位置
        public float direction;   // 朝向角度（度数，0表示右，90表示上）
        public int colorIndex = -1; // 分配的颜色索引（-1表示未分配）
        public string label = "Seed";
        
        public SeedPoint(Vector2 pos, float dir)
        {
            position = pos;
            direction = dir;
            label = "Seed";
            colorIndex = -1;
        }
    }
    
    void Start()
    {
        InitializeVoronoiGrid();
        GenerateVoronoiTexture();
        ApplyTextureToObject();
    }
    
    void Update()
    {
        // Update 方法保留，以备将来需要
    }
    
    void OnValidate()
    {
        #if UNITY_EDITOR
        // 在编辑器中验证和初始化种子点
        ValidateAndInitializeSeedPoints();
        #endif
    }
    
    void ValidateAndInitializeSeedPoints()
    {
        if (seedPoints == null) return;
        
        // 检查并初始化未设置的种子点
        for (int i = 0; i < seedPoints.Count; i++)
        {
            if (seedPoints[i] == null)
            {
                seedPoints[i] = CreateRandomSeedPoint(i);
                continue;
            }
            
            // 检查是否是默认值（新添加的种子点）
            bool isDefault = seedPoints[i].position == Vector2.zero && 
                           seedPoints[i].direction == 0f &&
                           seedPoints[i].label == "Seed";
            
            // 检查是否与其他种子点位置重复
            bool isDuplicate = false;
            for (int j = 0; j < i; j++)
            {
                if (seedPoints[j] != null && 
                    Vector2.Distance(seedPoints[i].position, seedPoints[j].position) < 0.01f)
                {
                    isDuplicate = true;
                    break;
                }
            }
            
            // 如果是默认值或位置重复，重新生成随机值
            if (isDefault || isDuplicate)
            {
                Vector2 newPos = GenerateNonOverlappingPosition();
                float newDir = Random.Range(0f, 360f);
                
                seedPoints[i].position = newPos;
                seedPoints[i].direction = newDir;
                seedPoints[i].label = $"Seed {i + 1}";
                
                Debug.Log($"自动生成种子点 {i + 1}: 位置({newPos.x:F2}, {newPos.y:F2}), 方向{newDir:F0}°");
            }
            else
            {
                // 检查现有种子点是否与其他种子点位置过近
                for (int j = 0; j < i; j++)
                {
                    if (seedPoints[j] != null && 
                        Vector2.Distance(seedPoints[i].position, seedPoints[j].position) < 0.1f)
                    {
                        Debug.LogWarning($"种子点 {i + 1} 和种子点 {j + 1} 位置过近（<0.1米）！建议调整位置。");
                    }
                }
            }
        }
    }
    
    Vector2 GenerateNonOverlappingPosition()
    {
        int maxAttempts = 50;
        float minDistance = 0.5f; // 最小距离
        
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector2 candidate = new Vector2(
                Random.Range(-physicalSpaceSize.x / 2f + 0.5f, physicalSpaceSize.x / 2f - 0.5f),
                Random.Range(-physicalSpaceSize.y / 2f + 0.5f, physicalSpaceSize.y / 2f - 0.5f)
            );
            
            bool tooClose = false;
            foreach (var seed in seedPoints)
            {
                if (seed != null && Vector2.Distance(candidate, seed.position) < minDistance)
                {
                    tooClose = true;
                    break;
                }
            }
            
            if (!tooClose)
            {
                return candidate;
            }
        }
        
        // 如果无法找到不重叠的位置，返回一个随机位置
        return new Vector2(
            Random.Range(-physicalSpaceSize.x / 2f + 0.5f, physicalSpaceSize.x / 2f - 0.5f),
            Random.Range(-physicalSpaceSize.y / 2f + 0.5f, physicalSpaceSize.y / 2f - 0.5f)
        );
    }
    
    SeedPoint CreateRandomSeedPoint(int index)
    {
        Vector2 pos = GenerateNonOverlappingPosition();
        float dir = Random.Range(0f, 360f);
        
        return new SeedPoint(pos, dir)
        {
            label = $"Seed {index + 1}"
        };
    }
    
    void InitializeVoronoiGrid()
    {
        // 计算网格尺寸
        gridWidth = Mathf.FloorToInt(physicalSpaceSize.x / gridCellSize);
        gridHeight = Mathf.FloorToInt(physicalSpaceSize.y / gridCellSize);
        
        Debug.Log($"初始化Voronoi网格: {gridWidth}x{gridHeight}, 种子点数量: {seedPoints.Count}");
        
        // 创建纹理
        voronoiTexture = new Texture2D(gridWidth, gridHeight)
        {
            filterMode = FilterMode.Point,  // 保持清晰的像素边界
            wrapMode = TextureWrapMode.Clamp
        };
        
        // 获取或添加MeshFilter
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }
        
        // 如果没有mesh，创建一个quad
        if (meshFilter.sharedMesh == null)
        {
            meshFilter.sharedMesh = CreateQuadMesh();
        }
        
        // 获取或添加MeshRenderer
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }
        
        // 创建材质
        if (meshRenderer.sharedMaterial == null)
        {
            Material mat = new Material(Shader.Find("Unlit/Texture"));
            meshRenderer.sharedMaterial = mat;
        }
        
        // 设置对象大小
        transform.localScale = new Vector3(physicalSpaceSize.x, 1f, physicalSpaceSize.y);
    }
    
    Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "VoronoiQuad";
        
        // 顶点 (平面在XZ平面上)
        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-0.5f, 0, -0.5f),
            new Vector3(0.5f, 0, -0.5f),
            new Vector3(-0.5f, 0, 0.5f),
            new Vector3(0.5f, 0, 0.5f)
        };
        
        // UV坐标
        Vector2[] uv = new Vector2[4]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };
        
        // 三角形索引
        int[] triangles = new int[6]
        {
            0, 2, 1,
            2, 3, 1
        };
        
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        
        return mesh;
    }
    
    void GenerateVoronoiTexture()
    {
        if (gridWidth <= 0 || gridHeight <= 0)
        {
            Debug.LogWarning("Voronoi网格未初始化，无法生成纹理");
            return;
        }

        if (seedPoints == null || seedPoints.Count == 0)
        {
            Debug.LogWarning("没有种子点，无法生成Voronoi图");
            spacePartition = new int[gridWidth, gridHeight];
            voronoiTexture = WeightedVoronoiUtility.RenderPartitionTexture(
                spacePartition,
                gridWidth,
                gridHeight,
                seedPoints,
                colorPalette,
                Color.gray);
            return;
        }

        Debug.Log($"开始生成Voronoi图（扩散生长法），种子点数量: {seedPoints.Count}");

        // 构建物理空间矩形（以原点为中心）
        Rect physicalRect = new Rect(
            -physicalSpaceSize.x / 2f,
            -physicalSpaceSize.y / 2f,
            physicalSpaceSize.x,
            physicalSpaceSize.y);

        spacePartition = WeightedVoronoiUtility.GeneratePartition(
            seedPoints,
            physicalRect,
            gridCellSize);

        WeightedVoronoiUtility.AssignColorsToSeeds(
            seedPoints,
            spacePartition,
            gridWidth,
            gridHeight,
            colorPalette);

        voronoiTexture = WeightedVoronoiUtility.RenderPartitionTexture(
            spacePartition,
            gridWidth,
            gridHeight,
            seedPoints,
            colorPalette,
            Color.gray);

        Debug.Log("Voronoi图生成完成");
    }
    
    // 剩余功能通过WeightedVoronoiUtility完成
    
    void ApplyTextureToObject()
    {
        if (voronoiTexture != null && meshRenderer != null && meshRenderer.sharedMaterial != null)
        {
            meshRenderer.sharedMaterial.mainTexture = voronoiTexture;
            meshRenderer.sharedMaterial.color = Color.white;
        }
    }
    
    void OnDrawGizmos()
    {
        if (!showSeedPoints) return;
        
        // 绘制物理空间边界
        Gizmos.color = Color.cyan;
        Vector3 center = transform.position;
        Vector3 size = new Vector3(physicalSpaceSize.x, 0.1f, physicalSpaceSize.y);
        Gizmos.DrawWireCube(center, size);
        
        // 绘制种子点
        for (int i = 0; i < seedPoints.Count; i++)
        {
            var seed = seedPoints[i];
            if (seed == null) continue;
            
            // 种子点位置（转换为3D空间）
            Vector3 seedPos3D = new Vector3(
                transform.position.x + seed.position.x,
                transform.position.y + 0.1f,
                transform.position.z + seed.position.y
            );
            
            // 获取分配的颜色
            Color seedColor = (seed.colorIndex >= 0) 
                ? colorPalette[seed.colorIndex % colorPalette.Length] 
                : Color.white;
            
            // 绘制种子点
            Gizmos.color = seedColor;
            Gizmos.DrawSphere(seedPos3D, seedPointGizmoSize);
            
            // 绘制朝向箭头
            Vector3 direction = Quaternion.Euler(0, seed.direction, 0) * Vector3.forward;
            Gizmos.DrawRay(seedPos3D, direction * seedPointGizmoSize * 2f);
            
            // 绘制朝向区域
            DrawDirectionalArc(seedPos3D, seed.direction, seedColor);
        }
        
        // 绘制调试标签（仅在编辑器中）
        #if UNITY_EDITOR
        DrawDebugVisualizations();
        #endif
        
        // 绘制网格（如果启用）
        if (showGrid && gridWidth > 0 && gridHeight > 0)
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.1f);
            for (int x = 0; x <= gridWidth; x++)
            {
                float xPos = transform.position.x + (x * gridCellSize - physicalSpaceSize.x / 2f);
                Vector3 start = new Vector3(xPos, transform.position.y + 0.05f, 
                    transform.position.z - physicalSpaceSize.y / 2f);
                Vector3 end = new Vector3(xPos, transform.position.y + 0.05f, 
                    transform.position.z + physicalSpaceSize.y / 2f);
                Gizmos.DrawLine(start, end);
            }
            
            for (int y = 0; y <= gridHeight; y++)
            {
                float yPos = transform.position.z + (y * gridCellSize - physicalSpaceSize.y / 2f);
                Vector3 start = new Vector3(transform.position.x - physicalSpaceSize.x / 2f, 
                    transform.position.y + 0.05f, yPos);
                Vector3 end = new Vector3(transform.position.x + physicalSpaceSize.x / 2f, 
                    transform.position.y + 0.05f, yPos);
                Gizmos.DrawLine(start, end);
            }
        }
    }
    
    void DrawDirectionalArc(Vector3 position, float direction, Color color)
    {
        Gizmos.color = new Color(color.r, color.g, color.b, 0.3f);
        
        // 绘制前方区域
        DrawArcSegment(position, direction, forwardAngle, forwardSpeed * 0.5f);
        
        // 绘制两侧区域
        Gizmos.color = new Color(color.r, color.g, color.b, 0.2f);
        DrawArcSegment(position, direction + forwardAngle, sideAngle - forwardAngle, sideSpeed * 0.5f);
        DrawArcSegment(position, direction - forwardAngle, sideAngle - forwardAngle, sideSpeed * 0.5f);
        
        // 绘制后方区域
        Gizmos.color = new Color(color.r, color.g, color.b, 0.1f);
        DrawArcSegment(position, direction + sideAngle, 180f - sideAngle, backwardSpeed * 0.5f);
        DrawArcSegment(position, direction - sideAngle, 180f - sideAngle, backwardSpeed * 0.5f);
    }
    
    void DrawArcSegment(Vector3 center, float startAngle, float arcAngle, float radius)
    {
        int segments = 20;
        float angleStep = arcAngle / segments * Mathf.Deg2Rad;
        float startRad = (startAngle - arcAngle / 2f) * Mathf.Deg2Rad;
        
        Vector3 prevPoint = center + new Vector3(
            Mathf.Cos(startRad) * radius, 
            0, 
            Mathf.Sin(startRad) * radius
        );
        
        for (int i = 1; i <= segments; i++)
        {
            float angle = startRad + angleStep * i;
            Vector3 nextPoint = center + new Vector3(
                Mathf.Cos(angle) * radius, 
                0, 
                Mathf.Sin(angle) * radius
            );
            
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }
    
    void DrawDebugVisualizations()
    {
        #if UNITY_EDITOR
        // 在Scene视图中绘制调试信息
        if (Camera.current == null) return;
        
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.fontSize = 12;
        
        UnityEditor.Handles.BeginGUI();
        foreach (var seed in seedPoints)
        {
            Vector3 worldPos = new Vector3(
                transform.position.x + seed.position.x, 
                transform.position.y + 0.5f, 
                transform.position.z + seed.position.y
            );
            
            Vector3 screenPos = Camera.current.WorldToScreenPoint(worldPos);
            
            if (screenPos.z > 0)
            {
                Vector2 guiPos = new Vector2(screenPos.x, Camera.current.pixelHeight - screenPos.y);
                GUI.Label(new Rect(guiPos.x, guiPos.y, 200, 50), 
                    $"{seed.label}\nDir: {seed.direction:F0}°", style);
            }
        }
        UnityEditor.Handles.EndGUI();
        #endif
    }
    
    // 公共方法，用于从其他脚本添加种子点
    public void AddSeedPoint(Vector2 position, float direction, string label = "")
    {
        seedPoints.Add(new SeedPoint(position, direction)
        {
            label = string.IsNullOrEmpty(label) ? $"Seed {seedPoints.Count + 1}" : label
        });
        
        if (Application.isPlaying)
        {
            RegenerateVoronoi();
        }
    }
    
    public void RemoveSeedPoint(int index)
    {
        if (index >= 0 && index < seedPoints.Count)
        {
            seedPoints.RemoveAt(index);
            
            if (Application.isPlaying)
            {
                RegenerateVoronoi();
            }
        }
    }
    
    public void ClearAllSeedPoints()
    {
        seedPoints.Clear();
        
        if (Application.isPlaying)
        {
            RegenerateVoronoi();
        }
    }
    
    public void RegenerateVoronoi()
    {
        if (!Application.isPlaying) return;
        
        GenerateVoronoiTexture();
        ApplyTextureToObject();
    }
    
    // 在Inspector中提供按钮
    [ContextMenu("Generate Voronoi")]
    void GenerateVoronoiFromMenu()
    {
        if (Application.isPlaying)
        {
            RegenerateVoronoi();
        }
        else
        {
            Debug.Log("请在播放模式下生成Voronoi图");
        }
    }
    
    [ContextMenu("Add Random Seed")]
    void AddRandomSeed()
    {
        // 在物理空间范围内随机位置
        Vector2 randomPos = new Vector2(
            Random.Range(-physicalSpaceSize.x / 2f + 0.5f, physicalSpaceSize.x / 2f - 0.5f),
            Random.Range(-physicalSpaceSize.y / 2f + 0.5f, physicalSpaceSize.y / 2f - 0.5f)
        );
        
        // 随机方向（0-360度）
        float randomDirection = Random.Range(0f, 360f);
        
        // 生成标签
        string label = $"Seed {seedPoints.Count + 1}";
        
        AddSeedPoint(randomPos, randomDirection, label);
    }
     
    
    /// <summary>
    /// 获取空间划分数组（公共接口）
    /// </summary>
    public int[,] GetSpacePartition()
    {
        return spacePartition;
    }
    
    
}