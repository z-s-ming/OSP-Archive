using UnityEngine;
using System.Collections.Generic;
using csDelaunay;

/// <summary>
/// 对比标准维诺图 vs 加权维诺图
/// 左侧：标准Delaunay维诺图（各向同性）
/// 右侧：加权维诺图（各向异性，受方向和速度影响）
/// </summary>
[ExecuteInEditMode]
public class VoronoiComparison : MonoBehaviour
{
    [Header("物理空间设置")]
    public Vector2 spaceSize = new Vector2(10f, 10f);
    public float gridCellSize = 0.1f;
    
    [Header("种子点设置")]
    public List<Vector2> seedPositions = new List<Vector2>();
    
    [Header("显示设置")]
    public bool showVoronoi = true;
    public bool showSeedPoints = true;
    public bool autoGenerate = false;  // 勾选此项自动生成
    
    // 内部引用
    private Voronoi voronoi;
    private List<Edge> edges;
    
    void Awake()
    {
        Debug.Log("VoronoiComparison 脚本已加载", this);
    }
    
    void Start()
    {
        Debug.Log("VoronoiComparison Start 被调用", this);
        // 自动生成维诺图
        if (seedPositions.Count > 0)
        {
            GenerateComparison();
        }
    }
    
    void OnValidate()
    {
        // 当在 Inspector 中勾选 autoGenerate 时，自动生成维诺图
        if (autoGenerate)
        {
            GenerateComparison();
            autoGenerate = false;  // 执行一次后自动取消勾选
        }
    }
    
    void OnEnable()
    {
        #if UNITY_EDITOR
        UnityEditor.SceneView.duringSceneGui += OnSceneGUI;
        #endif
    }
    
    void OnDisable()
    {
        #if UNITY_EDITOR
        UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
        #endif
    }
    
    void OnSceneGUI(object context)
    {
        // Scene视图渲染回调
    }
    
    [ContextMenu("生成标准维诺图")]
    public void GenerateComparison()
    {
        Debug.Log("GenerateComparison 被调用了", this);
        
        // 验证种子点
        if (seedPositions.Count == 0)
        {
            Debug.LogError("没有种子点，请先添加种子点");
            return;
        }
        
        Debug.Log($"开始生成标准维诺图：{seedPositions.Count} 个种子点");
        
        // 生成维诺图
        try
        {
            GenerateStandardVoronoi();
            Debug.Log($"标准维诺图生成完成，edges 数量：{(edges != null ? edges.Count : 0)}", this);
            
            #if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
            #endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"生成标准维诺图失败：{e.Message}\n{e.StackTrace}");
        }
    }
    
    // 测试方法
    public void TestGenerate()
    {
        Debug.Log("TestGenerate 被调用", this);
        GenerateComparison();
    }
    
    void GenerateStandardVoronoi()
    {
        if (seedPositions.Count == 0)
        {
            Debug.LogWarning("没有种子点，无法生成标准维诺图");
            return;
        }

        // 转换坐标系：从[-spaceSize/2, spaceSize/2]转换为[0, spaceSize]
        List<Vector2f> points = new List<Vector2f>();
        foreach (var pos in seedPositions)
        {
            Vector2f p = new Vector2f(
                pos.x + spaceSize.x / 2f,
                pos.y + spaceSize.y / 2f
            );
            points.Add(p);
        }
        
        // 创建Delaunay维诺图
        Rectf bounds = new Rectf(0, 0, spaceSize.x, spaceSize.y);
        voronoi = new Voronoi(points, bounds);
        edges = voronoi.Edges;
        
        Debug.Log($"标准维诺图生成完成：{points.Count} 个种子点，{edges.Count} 条边");
    }
    
    void OnDrawGizmos()
    {
        // 绘制物理边界
        Gizmos.color = Color.white;
        float halfX = spaceSize.x / 2f;
        float halfZ = spaceSize.y / 2f;
        
        Vector3 p1 = transform.position + new Vector3(-halfX, 0, -halfZ);
        Vector3 p2 = transform.position + new Vector3(halfX, 0, -halfZ);
        Vector3 p3 = transform.position + new Vector3(halfX, 0, halfZ);
        Vector3 p4 = transform.position + new Vector3(-halfX, 0, halfZ);
        
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);
        
        // 绘制种子点
        if (showSeedPoints && seedPositions != null)
        {
            for (int i = 0; i < seedPositions.Count; i++)
            {
                Color seedColor = new Color(1f, 0.5f, 0f);
                Vector3 seedPos = transform.position + new Vector3(seedPositions[i].x, 0.1f, seedPositions[i].y);
                
                Gizmos.color = seedColor;
                Gizmos.DrawSphere(seedPos, 0.15f);
            }
        }
        
        // 如果没有生成维诺图，返回
        if (!showVoronoi || edges == null || edges.Count == 0)
        {
            return;
        }
        
        // 绘制维诺图的边
        Gizmos.color = Color.red;
        
        foreach (Edge edge in edges)
        {
            if (edge.ClippedEnds == null)
                continue;
                
            Vector2f p1_v = edge.ClippedEnds[LR.LEFT];
            Vector2f p2_v = edge.ClippedEnds[LR.RIGHT];
            
            // 转换回原始坐标系，加上物体的位置偏移
            Vector3 start = transform.position + new Vector3(p1_v.x - spaceSize.x / 2f, 0, p1_v.y - spaceSize.y / 2f);
            Vector3 end = transform.position + new Vector3(p2_v.x - spaceSize.x / 2f, 0, p2_v.y - spaceSize.y / 2f);
            
            Gizmos.DrawLine(start, end);
        }
    }
}
