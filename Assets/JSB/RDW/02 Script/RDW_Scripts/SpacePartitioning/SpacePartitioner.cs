using System.Collections.Generic;
using UnityEngine;

namespace JSB.RDW.SpacePartitioning
{
    /// <summary>
    /// 空间分区算法的抽象基类
    /// 定义所有空间分区算法必须遵守的契约
    /// 继承 MonoBehaviour，可直接挂载到场景物体上进行调试
    /// </summary>
    public abstract class SpacePartitioner : MonoBehaviour
    {
        #region 用户数据结构

        /// <summary>
        /// 用户信息结构体
        /// 包含用户的位置、朝向等基本信息
        /// </summary>
        [System.Serializable]
        public class UserInfo
        {
            /// <summary>
            /// 用户在物理空间中的位置（世界坐标）
            /// </summary>
            public Vector2 position;

            /// <summary>
            /// 用户的朝向角度（度数，0表示向右，90表示向上）
            /// </summary>
            public float orientation;

            /// <summary>
            /// 用户的权重（影响分配的空间大小）
            /// 默认为 1.0，可根据用户需求调整
            /// </summary>
            public float weight = 1.0f;

            /// <summary>
            /// 用户的唯一标识
            /// </summary>
            public int userId;

            /// <summary>
            /// 用户对应的 GameObject（可选）
            /// </summary>
            public GameObject userObject;

            public UserInfo(Vector2 pos, float orient, int id, float w = 1.0f)
            {
                position = pos;
                orientation = orient;
                userId = id;
                weight = w;
                userObject = null;
            }

            public UserInfo(GameObject obj, int id, float w = 1.0f)
            {
                if (obj != null)
                {
                    position = new Vector2(obj.transform.position.x, obj.transform.position.z);
                    orientation = obj.transform.eulerAngles.y;
                    userObject = obj;
                }
                else
                {
                    position = Vector2.zero;
                    orientation = 0f;
                    userObject = null;
                }
                userId = id;
                weight = w;
            }
        }

        #endregion

        #region 空间分区结果

        /// <summary>
        /// 空间分区结果结构体
        /// 存储单个用户的安全空间信息
        /// </summary>
        [System.Serializable]
        public class PartitionResult
        {
            /// <summary>
            /// 用户 ID
            /// </summary>
            public int userId;

            /// <summary>
            /// 分配给该用户的安全空间多边形顶点（按逆时针顺序）
            /// 采用世界坐标系
            /// </summary>
            public List<Vector2> safeSpacePolygon;

            /// <summary>
            /// 该安全空间的质心
            /// </summary>
            public Vector2 centroid;

            /// <summary>
            /// 该安全空间的面积
            /// </summary>
            public float area;

            /// <summary>
            /// 额外的调试信息（可选）
            /// </summary>
            public Dictionary<string, object> debugInfo;

            public PartitionResult(int id)
            {
                userId = id;
                safeSpacePolygon = new List<Vector2>();
                centroid = Vector2.zero;
                area = 0f;
                debugInfo = new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// 每一帧分区输出（用于运行时更新）
        /// </summary>
        [System.Serializable]
        public class PartitionFrameResult
        {
            public List<Vector2> centroids = new List<Vector2>();
            public List<float> areas = new List<float>();
        }

        #endregion

        #region 场地边界

        /// <summary>
        /// 场地边界（矩形）
        /// </summary>
        [System.Serializable]
        public class SpaceBounds
        {
            /// <summary>
            /// 场地的中心点
            /// </summary>
            public Vector2 center;

            /// <summary>
            /// 场地的尺寸（宽度和高度）
            /// </summary>
            public Vector2 size;

            /// <summary>
            /// 最小边界点（左下角）
            /// </summary>
            public Vector2 min => center - size * 0.5f;

            /// <summary>
            /// 最大边界点（右上角）
            /// </summary>
            public Vector2 max => center + size * 0.5f;

            public SpaceBounds(Vector2 c, Vector2 s)
            {
                center = c;
                size = s;
            }

            public SpaceBounds(float width, float height)
            {
                center = Vector2.zero;
                size = new Vector2(width, height);
            }

            public SpaceBounds(Vector2 s)
            {
                center = Vector2.zero;
                size = s;
            }

            /// <summary>
            /// 检查一个点是否在边界内
            /// </summary>
            public bool Contains(Vector2 point)
            {
                return point.x >= min.x && point.x <= max.x &&
                       point.y >= min.y && point.y <= max.y;
            }
        }

        #endregion

        #region 公共参数（Inspector 可见）

        [Header("可视化设置")]
        [Tooltip("是否显示分区边界")]
        public bool showPartitionBoundaries = true;

        [Tooltip("是否显示用户位置")]
        public bool showUserPositions = true;

        [Tooltip("边界线颜色")]
        public Color boundaryColor = Color.green;

        [Tooltip("用户标记大小")]
        public float userMarkerSize = 0.2f;

        #endregion

        #region 核心抽象方法

        /// <summary>
        /// 计算空间分区的核心方法（抽象方法，由子类实现）
        /// </summary>
        /// <param name="users">用户列表</param>
        /// <param name="bounds">场地边界</param>
        /// <returns>每个用户的安全空间分区结果</returns>
        public abstract List<PartitionResult> CalculatePartition(List<UserInfo> users, SpaceBounds bounds);

        /// <summary>
        /// 初始化空间相关的可视化/对象池（可选）
        /// </summary>
        public virtual void InitializeSpatialObjects(int totalUserCount, int edgeMaxCount, float shutterWidth)
        {
        }

        /// <summary>
        /// 更新种子点可视化（可选）
        /// </summary>
        public virtual void UpdateSeedVisuals(IReadOnlyList<Vector2> seedPoints)
        {
        }

        /// <summary>
        /// 更新分区、可视化与障碍（可选）
        /// </summary>
        public virtual PartitionFrameResult UpdatePartitioning(
            IReadOnlyList<Vector2> seedPoints,
            IReadOnlyList<GameObject> physicalUsers,
            SpaceBounds bounds,
            int totalUserCount,
            bool useVecObservation,
            IReadOnlyList<GameObject> s2cCenterPointers,
            Dictionary<int, List<Vector2>> areaSegments,
            float shutterWidth)
        {
            return new PartitionFrameResult();
        }

        /// <summary>
        /// 初始化障碍信息（可选）
        /// </summary>
        public virtual void InitObstacleInfo()
        {
        }

        /// <summary>
        /// 生成统一分布的初始种子点（可选）
        /// </summary>
        public virtual List<Vector2> GenerateUniformSeedPoints(int totalUserCount, SpaceBounds bounds, float epsilon)
        {
            return null;
        }

        /// <summary>
        /// 检查位置是否超出用户的安全区域（虚方法，子类可重写实现特定碰撞检测）
        /// </summary>
        /// <param name="position">待检测的位置</param>
        /// <param name="userId">用户ID</param>
        /// <param name="needReset">是否需要重置</param>
        /// <param name="isShutterReset">是否是挡板型重置</param>
        /// <returns>是否成功检测到碰撞信息</returns>
        public virtual bool CheckPositionCollision(Vector2 position, int userId, out bool needReset, out bool isShutterReset)
        {
            needReset = false;
            isShutterReset = false;
            return false; // 默认实现：返回false表示未实现碰撞检测
        }

        #endregion

        #region 辅助工具方法

        /// <summary>
        /// 计算多边形的质心
        /// </summary>
        protected Vector2 CalculateCentroid(List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count == 0)
                return Vector2.zero;

            Vector2 centroid = Vector2.zero;
            foreach (var vertex in polygon)
            {
                centroid += vertex;
            }
            return centroid / polygon.Count;
        }

        /// <summary>
        /// 计算多边形的面积（使用鞋带公式）
        /// </summary>
        protected float CalculateArea(List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return 0f;

            float area = 0f;
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += polygon[i].x * polygon[j].y;
                area -= polygon[j].x * polygon[i].y;
            }

            return Mathf.Abs(area) * 0.5f;
        }

        /// <summary>
        /// 裁剪多边形到边界内（使用 Sutherland-Hodgman 算法）
        /// </summary>
        protected List<Vector2> ClipPolygonToBounds(List<Vector2> polygon, SpaceBounds bounds)
        {
            if (polygon == null || polygon.Count == 0)
                return new List<Vector2>();

            List<Vector2> output = new List<Vector2>(polygon);

            // 依次用四条边界线裁剪
            output = ClipByEdge(output, new Vector2(bounds.min.x, bounds.min.y), new Vector2(bounds.min.x, bounds.max.y)); // 左边界
            output = ClipByEdge(output, new Vector2(bounds.min.x, bounds.max.y), new Vector2(bounds.max.x, bounds.max.y)); // 上边界
            output = ClipByEdge(output, new Vector2(bounds.max.x, bounds.max.y), new Vector2(bounds.max.x, bounds.min.y)); // 右边界
            output = ClipByEdge(output, new Vector2(bounds.max.x, bounds.min.y), new Vector2(bounds.min.x, bounds.min.y)); // 下边界

            return output;
        }

        /// <summary>
        /// 使用单条边裁剪多边形
        /// </summary>
        private List<Vector2> ClipByEdge(List<Vector2> polygon, Vector2 edgeStart, Vector2 edgeEnd)
        {
            if (polygon.Count == 0)
                return new List<Vector2>();

            List<Vector2> output = new List<Vector2>();
            Vector2 edgeDir = edgeEnd - edgeStart;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];

                bool currentInside = IsPointOnLeftSide(current, edgeStart, edgeDir);
                bool nextInside = IsPointOnLeftSide(next, edgeStart, edgeDir);

                if (currentInside && nextInside)
                {
                    // 两点都在内侧，添加下一个点
                    output.Add(next);
                }
                else if (currentInside && !nextInside)
                {
                    // 从内到外，添加交点
                    Vector2 intersection = LineIntersection(current, next - current, edgeStart, edgeDir);
                    output.Add(intersection);
                }
                else if (!currentInside && nextInside)
                {
                    // 从外到内，添加交点和下一个点
                    Vector2 intersection = LineIntersection(current, next - current, edgeStart, edgeDir);
                    output.Add(intersection);
                    output.Add(next);
                }
                // 两点都在外侧，不添加任何点
            }

            return output;
        }

        /// <summary>
        /// 判断点是否在边的左侧
        /// </summary>
        private bool IsPointOnLeftSide(Vector2 point, Vector2 edgeStart, Vector2 edgeDir)
        {
            Vector2 toPoint = point - edgeStart;
            float cross = edgeDir.x * toPoint.y - edgeDir.y * toPoint.x;
            return cross >= 0;
        }

        /// <summary>
        /// 计算两条直线的交点
        /// </summary>
        private Vector2 LineIntersection(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2)
        {
            float cross = d1.x * d2.y - d1.y * d2.x;
            if (Mathf.Abs(cross) < 1e-6f)
                return p1; // 平行线，返回第一个点

            Vector2 diff = p2 - p1;
            float t = (diff.x * d2.y - diff.y * d2.x) / cross;
            return p1 + d1 * t;
        }

        #endregion

        #region 可视化（Gizmos）

        /// <summary>
        /// 在 Scene 视图中绘制调试信息
        /// </summary>
        protected virtual void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;
        }

        /// <summary>
        /// 绘制分区结果
        /// </summary>
        protected void DrawPartitionResult(PartitionResult result, Color color)
        {
            if (result == null || result.safeSpacePolygon == null || result.safeSpacePolygon.Count < 3)
                return;

            Gizmos.color = color;

            // 绘制多边形边界
            for (int i = 0; i < result.safeSpacePolygon.Count; i++)
            {
                Vector2 current = result.safeSpacePolygon[i];
                Vector2 next = result.safeSpacePolygon[(i + 1) % result.safeSpacePolygon.Count];

                Vector3 p1 = new Vector3(current.x, 0.1f, current.y);
                Vector3 p2 = new Vector3(next.x, 0.1f, next.y);

                Gizmos.DrawLine(p1, p2);
            }

            // 绘制质心
            if (showUserPositions)
            {
                Gizmos.color = Color.yellow;
                Vector3 centroidPos = new Vector3(result.centroid.x, 0.2f, result.centroid.y);
                Gizmos.DrawSphere(centroidPos, userMarkerSize * 0.5f);
            }
        }

        #endregion

        #region Unity 生命周期

        protected virtual void Awake()
        {
            // 子类可重写进行初始化
        }

        protected virtual void Start()
        {
            // 子类可重写
        }

        protected virtual void Update()
        {
            // 子类可重写进行实时更新
        }

        #endregion
    }
}
