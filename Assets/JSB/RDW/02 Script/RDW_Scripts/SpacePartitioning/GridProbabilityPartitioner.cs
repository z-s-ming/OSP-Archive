using System.Collections.Generic;
using UnityEngine;

namespace JSB.RDW.SpacePartitioning
{
    /// <summary>
    /// 基于网格概率的空间分区算法
    /// 使用方向加权的扩散机制来分配空间，考虑用户的朝向和移动倾向
    /// </summary>
    public class GridProbabilityPartitioner : SpacePartitioner
    {
        /// <summary>
        /// 射线碰撞检测结果
        /// </summary>
        public struct RayHitResult
        {
            public bool hit;              // 是否发生碰撞
            public float distance;        // 碰撞点距离
            public Vector2 hitPoint;      // 碰撞点世界坐标
            public Vector2Int hitGridPos; // 碰撞格子的网格坐标
            public int hitOwnerId;        // 碰撞格子的归属用户ID（-1表示无主）
        }

        public class GridPartitionSnapshot
        {
            public int[,] ownerMap;
            public SpaceBounds bounds;
            public float cellSize;
            public int width;
            public int height;
        }
        #region 算法参数

        [Header("网格参数")]
        [Tooltip("网格单元大小（单位：米）")]
        [Range(0.01f, 1f)]
        public float gridCellSize = 0.1f;

        [Header("扩散速度参数")]
        [Tooltip("主朝向扩散速度（前方）")]
        [Range(0.1f, 2f)]
        public float forwardSpeed = 1.0f;

        [Tooltip("两侧扩散速度")]
        [Range(0.1f, 2f)]
        public float sideSpeed = 0.75f;

        [Tooltip("反方向扩散速度（后方）")]
        [Range(0.1f, 2f)]
        public float backwardSpeed = 0.5f;

        [Header("角度阈值（度）")]
        [Tooltip("前方区域的角度阈值")]
        [Range(0, 90)]
        public float forwardAngle = 45f;

        [Tooltip("两侧区域的角度阈值")]
        [Range(0, 90)]
        public float sideAngle = 135f;

        [Header("碰撞检测参数")]
        [Tooltip("射线步进大小（0 表示自动使用 gridCellSize / 2）")]
        [Range(0f, 0.5f)]
        public float rayMarchStepSize = 0f;

        [Tooltip("射线检测最大距离")]
        [Range(1f, 20f)]
        public float maxRayDistance = 10f;

        [Tooltip("物理边界安全边距（米）")]
        [Range(0f, 1f)]
        public float boundaryMargin = 0.5f;

        [Tooltip("用户间安全距离（米）- 距离其他用户区域小于此值触发碰撞")]
        [Range(0f, 2f)]
        public float userSafetyDistance = 0.5f;

        [Header("调试选项")]
        [Tooltip("是否显示网格")]
        public bool showGrid = false;

        #endregion

        #region 内部数据结构

        private int gridWidth;
        private int gridHeight;
        private int[,] spacePartition; // 存储每个格子所属的用户ID（-1 表示未分配）
        private float[,] distanceField; // 存储每个格子到最近种子点的距离
        private List<UserInfo> currentUsers;
        private SpaceBounds lastBounds;
        private bool hasBounds;

        // 可复用的集合，避免每帧分配
        private Dictionary<int, UserInfo> userDict = new Dictionary<int, UserInfo>();
        private MinHeap frontier = new MinHeap();
        private List<Vector2Int> userCells = new List<Vector2Int>();
        private List<Vector2> boundaryPoints = new List<Vector2>();
        private HashSet<Vector2Int> cellSet = new HashSet<Vector2Int>();
        private List<Vector2Int> boundaryCells = new List<Vector2Int>();

        #endregion

        #region 核心方法实现

        /// <summary>
        /// 实现基于网格概率的空间分区计算
        /// </summary>
        public override List<PartitionResult> CalculatePartition(List<UserInfo> users, SpaceBounds bounds)
        {
            List<PartitionResult> results = new List<PartitionResult>();

            if (users == null || users.Count == 0)
            {
                Debug.LogWarning("[GridProbabilityPartitioner] 用户列表为空，无法计算分区");
                return results;
            }

            currentUsers = users;
            if (!hasBounds)
            {
                lastBounds = bounds;
                hasBounds = true;
            }

            // 1. 初始化网格
            InitializeGrid(lastBounds);

            // 2. 放置种子点
            PlaceSeedPoints(users, lastBounds);

            // 3. 执行扩散算法
            PerformDiffusion(users, lastBounds);

            // 4. 提取每个用户的多边形边界
            results = ExtractPartitionPolygons(users, lastBounds);

            return results;
        }

        /// <summary>
        /// 判断位置是否在当前网格边界内
        /// </summary>
        public bool IsPositionInsideBounds(Vector2 position)
        {
            if (!hasBounds)
                return false;

            return lastBounds.Contains(position);
        }

        /// <summary>
        /// 判断位置是否在物理边界的安全区域内（考虑边距）
        /// </summary>
        public bool IsPositionInsideSafeBounds(Vector2 position)
        {
            if (!hasBounds)
                return false;

            Vector2 safeMin = lastBounds.min + new Vector2(boundaryMargin, boundaryMargin);
            Vector2 safeMax = lastBounds.max - new Vector2(boundaryMargin, boundaryMargin);

            return position.x >= safeMin.x && position.x <= safeMax.x &&
                   position.y >= safeMin.y && position.y <= safeMax.y;
        }

        /// <summary>
        /// 获取指定位置的网格归属用户ID
        /// </summary>
        public bool TryGetGridOwner(Vector2 position, out int ownerId)
        {
            ownerId = -1;

            if (!hasBounds || spacePartition == null)
                return false;

            Vector2Int gridPos = WorldToGrid(position, lastBounds);
            if (!IsValidGridPosition(gridPos))
                return false;

            ownerId = spacePartition[gridPos.x, gridPos.y];
            return ownerId >= 0;
        }

        /// <summary>
        /// 判断位置是否归属于指定用户
        /// </summary>
        public bool IsPositionOwnedByUser(Vector2 position, int userId)
        {
            return TryGetGridOwner(position, out int ownerId) && ownerId == userId;
        }

        /// <summary>
        /// 检查位置到其他用户区域的最短距离（用于安全距离判定）
        /// </summary>
        private float GetDistanceToOtherUserArea(Vector2 position, int userId)
        {
            if (!hasBounds || spacePartition == null)
                return float.MaxValue;

            Vector2Int centerGrid = WorldToGrid(position, lastBounds);
            if (!IsValidGridPosition(centerGrid))
                return 0f;

            int currentOwner = spacePartition[centerGrid.x, centerGrid.y];
            if (currentOwner != userId)
                return 0f; // 已经在其他用户区域内

            // 使用BFS查找最近的其他用户格子
            float minDistance = float.MaxValue;
            int searchRadius = Mathf.CeilToInt(userSafetyDistance / gridCellSize) + 2;

            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                {
                    Vector2Int checkGrid = new Vector2Int(centerGrid.x + dx, centerGrid.y + dy);
                    if (!IsValidGridPosition(checkGrid))
                        continue;

                    int checkOwner = spacePartition[checkGrid.x, checkGrid.y];
                    if (checkOwner >= 0 && checkOwner != userId)
                    {
                        Vector2 checkWorldPos = GridToWorld(checkGrid, lastBounds);
                        float dist = Vector2.Distance(position, checkWorldPos);
                        minDistance = Mathf.Min(minDistance, dist);
                    }
                }
            }

            return minDistance;
        }

        /// <summary>
        /// 获取当前网格分区快照（供碰撞检测/调试）
        /// </summary>
        public GridPartitionSnapshot GetGridSnapshot()
        {
            if (!hasBounds || spacePartition == null)
                return null;

            return new GridPartitionSnapshot
            {
                ownerMap = spacePartition,
                bounds = lastBounds,
                cellSize = gridCellSize,
                width = gridWidth,
                height = gridHeight
            };
        }

        /// <summary>
        /// 网格射线步进法碰撞检测
        /// 沿射线方向逐格检测，判断是否离开用户的安全区域
        /// </summary>
        /// <param name="startPos">射线起点（世界坐标）</param>
        /// <param name="direction">射线方向（归一化向量）</param>
        /// <param name="userId">用户ID</param>
        /// <param name="maxDistance">最大检测距离（可选，默认使用 maxRayDistance）</param>
        /// <returns>碰撞检测结果</returns>
        public RayHitResult RayMarchCollisionCheck(Vector2 startPos, Vector2 direction, int userId, float maxDistance = -1f)
        {
            RayHitResult result = new RayHitResult
            {
                hit = false,
                distance = 0f,
                hitPoint = startPos,
                hitGridPos = new Vector2Int(-1, -1),
                hitOwnerId = -1
            };

            // 检查是否已初始化
            if (!hasBounds || spacePartition == null)
            {
                result.hit = true; // 未初始化视为碰撞
                return result;
            }

            // 归一化方向向量
            direction = direction.normalized;
            if (direction.magnitude < 0.001f)
            {
                // 方向向量为零，无法检测
                return result;
            }

            // 确定步进大小
            float stepSize = rayMarchStepSize > 0f ? rayMarchStepSize : gridCellSize * 0.5f;

            // 确定最大距离
            if (maxDistance < 0f)
                maxDistance = this.maxRayDistance;

            // 射线步进
            float currentDistance = 0f;
            Vector2 currentPos = startPos;

            while (currentDistance <= maxDistance)
            {
                // 转换为网格坐标
                Vector2Int gridPos = WorldToGrid(currentPos, lastBounds);

                // 检查是否越界
                if (!IsValidGridPosition(gridPos))
                {
                    // 越界即碰撞
                    result.hit = true;
                    result.distance = currentDistance;
                    result.hitPoint = currentPos;
                    result.hitGridPos = gridPos;
                    result.hitOwnerId = -1;
                    return result;
                }

                // 检查当前格子的归属
                int ownerId = spacePartition[gridPos.x, gridPos.y];

                // 如果格子不属于该用户，发生碰撞
                if (ownerId != userId)
                {
                    result.hit = true;
                    result.distance = currentDistance;
                    result.hitPoint = currentPos;
                    result.hitGridPos = gridPos;
                    result.hitOwnerId = ownerId;
                    return result;
                }

                // 继续前进
                currentDistance += stepSize;
                currentPos = startPos + direction * currentDistance;
            }

            // 未发生碰撞
            result.distance = maxDistance;
            result.hitPoint = startPos + direction * maxDistance;
            return result;
        }

        /// <summary>
        /// 重写基类碰撞检测方法，使用网格直接查询（用于当前位置判定）
        /// </summary>
        public override bool CheckPositionCollision(Vector2 position, int userId, out bool needReset, out bool isShutterReset)
        {
            needReset = false;
            isShutterReset = false;

            // 优先检查是否超出安全边界（带边距）
            if (!IsPositionInsideSafeBounds(position))
            {
                needReset = true;
                isShutterReset = false;
                return true;
            }

            // 检查是否完全越界
            if (!IsPositionInsideBounds(position))
            {
                needReset = true;
                isShutterReset = false;
                return true;
            }

            // 检查网格归属
            if (TryGetGridOwner(position, out int ownerId))
            {
                if (ownerId != userId)
                {
                    needReset = true;
                    isShutterReset = true;
                    return true;
                }

                // 属于当前用户，但需要检查是否距离其他用户区域太近
                if (userSafetyDistance > 0f)
                {
                    float distanceToOthers = GetDistanceToOtherUserArea(position, userId);
                    if (distanceToOthers < userSafetyDistance)
                    {
                        needReset = true;
                        isShutterReset = true;
                        return true;
                    }
                }

                return true;
            }

            // 在边界内但没有网格归属（未分配的格子），视为异常，触发重置
            needReset = true;
            isShutterReset = true;
            return true;
        }

        #endregion

        #region 网格初始化

        /// <summary>
        /// 初始化网格
        /// </summary>
        private void InitializeGrid(SpaceBounds bounds)
        {
            gridWidth = Mathf.CeilToInt(bounds.size.x / gridCellSize);
            gridHeight = Mathf.CeilToInt(bounds.size.y / gridCellSize);

            spacePartition = new int[gridWidth, gridHeight];
            distanceField = new float[gridWidth, gridHeight];

            // 初始化为未分配状态
            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    spacePartition[x, y] = -1;
                    distanceField[x, y] = float.MaxValue;
                }
            }
        }

        /// <summary>
        /// 放置种子点（用户的初始位置）
        /// </summary>
        private void PlaceSeedPoints(List<UserInfo> users, SpaceBounds bounds)
        {
            foreach (var user in users)
            {
                Vector2Int gridPos = WorldToGrid(user.position, bounds);

                if (IsValidGridPosition(gridPos))
                {
                    spacePartition[gridPos.x, gridPos.y] = user.userId;
                    distanceField[gridPos.x, gridPos.y] = 0f;
                }
                else
                {
                    Debug.LogWarning($"[GridProbabilityPartitioner] 用户 {user.userId} 的位置 {user.position} 超出边界");
                }
            }
        }

        #endregion

        #region 扩散算法

        /// <summary>
        /// 执行概率扩散算法
        /// </summary>
        private void PerformDiffusion(List<UserInfo> users, SpaceBounds bounds)
        {
            // 复用用户信息字典，避免每帧分配
            userDict.Clear();
            foreach (var user in users)
            {
                userDict[user.userId] = user;
            }

            frontier.Clear();
            // 采用标准 Dijkstra：无需 visited 数组，依赖“过期节点”检查

            // 将所有种子点加入队列
            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    if (spacePartition[x, y] >= 0)
                    {
                        frontier.Push(new FrontierNode(new Vector2Int(x, y), distanceField[x, y]));
                    }
                }
            }

            // 加权扩散（Dijkstra）
            while (frontier.Count > 0)
            {
                FrontierNode node = frontier.Pop();
                Vector2Int current = node.position;
                float currentDistance = node.cost;

                if (currentDistance > distanceField[current.x, current.y] + 1e-6f)
                    continue;

                int currentOwnerId = spacePartition[current.x, current.y];

                if (currentOwnerId < 0)
                    continue;

                UserInfo currentUser = userDict[currentOwnerId];
                Vector2 currentWorldPos = GridToWorld(current, bounds);

                // 检查四邻域
                Vector2Int[] neighbors = new Vector2Int[]
                {
                    new Vector2Int(current.x + 1, current.y),
                    new Vector2Int(current.x - 1, current.y),
                    new Vector2Int(current.x, current.y + 1),
                    new Vector2Int(current.x, current.y - 1)
                };

                foreach (var neighbor in neighbors)
                {
                    if (!IsValidGridPosition(neighbor))
                        continue;

                    // 计算扩散速度（考虑方向）
                    Vector2 neighborWorldPos = GridToWorld(neighbor, bounds);
                    float speed = CalculateDiffusionSpeed(currentUser, currentWorldPos, neighborWorldPos);

                    // 计算新距离
                    float newDistance = currentDistance + gridCellSize / speed;

                    // 如果新距离更短，更新归属
                    if (newDistance < distanceField[neighbor.x, neighbor.y])
                    {
                        distanceField[neighbor.x, neighbor.y] = newDistance;
                        spacePartition[neighbor.x, neighbor.y] = currentOwnerId;
                        frontier.Push(new FrontierNode(neighbor, newDistance));
                    }
                }
            }
        }

        private readonly struct FrontierNode
        {
            public readonly Vector2Int position;
            public readonly float cost;

            public FrontierNode(Vector2Int position, float cost)
            {
                this.position = position;
                this.cost = cost;
            }
        }

        private class MinHeap
        {
            private readonly List<FrontierNode> heap = new List<FrontierNode>();

            public int Count => heap.Count;

            public void Clear()
            {
                heap.Clear();
            }

            public void Push(FrontierNode node)
            {
                heap.Add(node);
                SiftUp(heap.Count - 1);
            }

            public FrontierNode Pop()
            {
                FrontierNode root = heap[0];
                int lastIndex = heap.Count - 1;
                FrontierNode last = heap[lastIndex];
                heap.RemoveAt(lastIndex);
                if (heap.Count > 0)
                {
                    heap[0] = last;
                    SiftDown(0);
                }

                return root;
            }

            private void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (heap[parent].cost <= heap[index].cost)
                        break;

                    Swap(parent, index);
                    index = parent;
                }
            }

            private void SiftDown(int index)
            {
                int count = heap.Count;
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;

                    if (left < count && heap[left].cost < heap[smallest].cost)
                        smallest = left;

                    if (right < count && heap[right].cost < heap[smallest].cost)
                        smallest = right;

                    if (smallest == index)
                        break;

                    Swap(index, smallest);
                    index = smallest;
                }
            }

            private void Swap(int a, int b)
            {
                FrontierNode temp = heap[a];
                heap[a] = heap[b];
                heap[b] = temp;
            }
        }

        /// <summary>
        /// 计算扩散速度（基于方向）
        /// </summary>
        private float CalculateDiffusionSpeed(UserInfo user, Vector2 from, Vector2 to)
        {
            // 计算从当前位置到目标位置的方向
            Vector2 direction = (to - from).normalized;

            // 用户的朝向向量（将角度转换为向量）
            float orientationRad = user.orientation * Mathf.Deg2Rad;
            Vector2 userForward = new Vector2(Mathf.Cos(orientationRad), Mathf.Sin(orientationRad));

            // 计算夹角
            float angle = Vector2.Angle(userForward, direction);

            // 根据角度选择速度
            float speed;
            if (angle <= forwardAngle)
            {
                // 前方区域
                speed = forwardSpeed;
            }
            else if (angle <= sideAngle)
            {
                // 两侧区域
                speed = sideSpeed;
            }
            else
            {
                // 后方区域
                speed = backwardSpeed;
            }

            // 考虑用户权重
            speed *= user.weight;

            // 防止速度为 0
            speed = Mathf.Max(0.001f, speed);

            return speed;
        }

        #endregion

        #region 多边形提取

        /// <summary>
        /// 提取每个用户的分区多边形
        /// </summary>
        private List<PartitionResult> ExtractPartitionPolygons(List<UserInfo> users, SpaceBounds bounds)
        {
            List<PartitionResult> results = new List<PartitionResult>();

            foreach (var user in users)
            {
                PartitionResult result = new PartitionResult(user.userId);

                // 提取该用户的所有网格单元（复用列表）
                userCells.Clear();
                for (int x = 0; x < gridWidth; x++)
                {
                    for (int y = 0; y < gridHeight; y++)
                    {
                        if (spacePartition[x, y] == user.userId)
                        {
                            userCells.Add(new Vector2Int(x, y));
                        }
                    }
                }

                if (userCells.Count == 0)
                {
                    Debug.LogWarning($"[GridProbabilityPartitioner] 用户 {user.userId} 未分配到任何网格单元");
                    results.Add(result);
                    continue;
                }

                // 提取边界多边形（使用 Marching Squares 算法）
                List<Vector2> polygon = ExtractBoundaryPolygon(userCells, user.userId, bounds);

                if (polygon.Count >= 3)
                {
                    result.safeSpacePolygon = polygon;
                    result.centroid = CalculateCentroid(polygon);
                    result.area = CalculateArea(polygon);

                    result.debugInfo["gridCellCount"] = userCells.Count;
                    result.debugInfo["approximateArea"] = userCells.Count * gridCellSize * gridCellSize;
                    result.debugInfo["gridOwnerMap"] = spacePartition;
                    result.debugInfo["gridBounds"] = bounds;
                    result.debugInfo["gridCellSize"] = gridCellSize;
                    result.debugInfo["gridWidth"] = gridWidth;
                    result.debugInfo["gridHeight"] = gridHeight;
                }
                else
                {
                    Debug.LogWarning($"[GridProbabilityPartitioner] 用户 {user.userId} 的多边形提取失败");
                }

                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// 提取边界多边形（简化版 Marching Squares）
        /// </summary>
        private List<Vector2> ExtractBoundaryPolygon(List<Vector2Int> cells, int userId, SpaceBounds bounds)
        {
            boundaryPoints.Clear();

            // 查找边界单元（至少有一个邻居不属于该用户）
            cellSet.Clear();
            foreach (var cell in cells)
            {
                cellSet.Add(cell);
            }
            boundaryCells.Clear();

            foreach (var cell in cells)
            {
                bool isBoundary = false;
                Vector2Int[] neighbors = new Vector2Int[]
                {
                    new Vector2Int(cell.x + 1, cell.y),
                    new Vector2Int(cell.x - 1, cell.y),
                    new Vector2Int(cell.x, cell.y + 1),
                    new Vector2Int(cell.x, cell.y - 1)
                };

                foreach (var neighbor in neighbors)
                {
                    if (!cellSet.Contains(neighbor))
                    {
                        isBoundary = true;
                        break;
                    }
                }

                if (isBoundary)
                {
                    boundaryCells.Add(cell);
                }
            }

            // 将边界单元的中心点作为多边形顶点（简化方法）
            // 更复杂的方法可以使用 Marching Squares 算法提取精确轮廓
            foreach (var cell in boundaryCells)
            {
                Vector2 worldPos = GridToWorld(cell, bounds);
                boundaryPoints.Add(worldPos);
            }

            // 对边界点进行排序（使用凸包算法或极角排序）
            if (boundaryPoints.Count > 0)
            {
                boundaryPoints = SortPointsByAngle(boundaryPoints);
            }

            return boundaryPoints;
        }

        /// <summary>
        /// 按极角对点进行排序（用于形成多边形）
        /// </summary>
        private List<Vector2> SortPointsByAngle(List<Vector2> points)
        {
            if (points.Count < 3)
                return points;

            // 计算中心点
            Vector2 center = Vector2.zero;
            foreach (var p in points)
            {
                center += p;
            }
            center /= points.Count;

            // 按相对于中心点的极角排序
            points.Sort((a, b) =>
            {
                float angleA = Mathf.Atan2(a.y - center.y, a.x - center.x);
                float angleB = Mathf.Atan2(b.y - center.y, b.x - center.x);
                return angleA.CompareTo(angleB);
            });

            return points;
        }

        #endregion

        #region 坐标转换

        /// <summary>
        /// 世界坐标转网格坐标
        /// </summary>
        private Vector2Int WorldToGrid(Vector2 worldPos, SpaceBounds bounds)
        {
            Vector2 localPos = worldPos - bounds.min;
            int x = Mathf.FloorToInt(localPos.x / gridCellSize);
            int y = Mathf.FloorToInt(localPos.y / gridCellSize);
            return new Vector2Int(x, y);
        }

        /// <summary>
        /// 网格坐标转世界坐标（单元中心）
        /// </summary>
        private Vector2 GridToWorld(Vector2Int gridPos, SpaceBounds bounds)
        {
            float x = bounds.min.x + (gridPos.x + 0.5f) * gridCellSize;
            float y = bounds.min.y + (gridPos.y + 0.5f) * gridCellSize;
            return new Vector2(x, y);
        }

        /// <summary>
        /// 检查网格位置是否有效
        /// </summary>
        private bool IsValidGridPosition(Vector2Int gridPos)
        {
            return gridPos.x >= 0 && gridPos.x < gridWidth &&
                   gridPos.y >= 0 && gridPos.y < gridHeight;
        }

        #endregion

        #region 可视化

        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();

            if (!Application.isPlaying || spacePartition == null)
                return;

            if (showGrid)
            {
                DrawGrid();
            }

            if (showPartitionBoundaries && currentUsers != null)
            {
                DrawPartitions();
            }
        }

        /// <summary>
        /// 绘制网格
        /// </summary>
        private void DrawGrid()
        {
            if (!hasBounds)
                return;

            SpaceBounds bounds = lastBounds;

            Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);

            for (int x = 0; x <= gridWidth; x++)
            {
                Vector2 start = bounds.min + new Vector2(x * gridCellSize, 0);
                Vector2 end = bounds.min + new Vector2(x * gridCellSize, bounds.size.y);
                Gizmos.DrawLine(new Vector3(start.x, 0, start.y), new Vector3(end.x, 0, end.y));
            }

            for (int y = 0; y <= gridHeight; y++)
            {
                Vector2 start = bounds.min + new Vector2(0, y * gridCellSize);
                Vector2 end = bounds.min + new Vector2(bounds.size.x, y * gridCellSize);
                Gizmos.DrawLine(new Vector3(start.x, 0, start.y), new Vector3(end.x, 0, end.y));
            }
        }

        /// <summary>
        /// 绘制分区结果
        /// </summary>
        private void DrawPartitions()
        {
            Color[] colors = new Color[]
            {
                Color.red,
                Color.green,
                Color.blue,
                Color.yellow,
                Color.cyan,
                Color.magenta
            };

            if (!hasBounds)
                return;

            SpaceBounds bounds = lastBounds;

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    int ownerId = spacePartition[x, y];
                    if (ownerId >= 0)
                    {
                        Gizmos.color = colors[ownerId % colors.Length];
                        Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.3f);

                        Vector2 worldPos = GridToWorld(new Vector2Int(x, y), bounds);
                        Vector3 center = new Vector3(worldPos.x, 0.01f, worldPos.y);
                        Vector3 size = new Vector3(gridCellSize, 0.01f, gridCellSize);

                        Gizmos.DrawCube(center, size);
                    }
                }
            }
        }

        #endregion
    }
}
