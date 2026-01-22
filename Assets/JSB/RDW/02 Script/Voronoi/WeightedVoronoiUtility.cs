using System.Collections.Generic;
using UnityEngine;

namespace JSB.RDW.Voronoi
{
    /// <summary>
    /// Utility methods for weighted Voronoi partitioning and texture rendering.
    /// Handles grid-based growth, coloring, and texture generation.
    /// </summary>
    public static class WeightedVoronoiUtility
    {
        // 内置参数：方向性速度和角度阈值
        /// <summary>前方移动速度（相对值）</summary>
        private const float FORWARD_SPEED = 1.0f;  // 大幅增加前方速度
        /// <summary>侧方移动速度（相对值）</summary>
        private const float SIDE_SPEED = 0.8f;     // 侧方保持中等
        /// <summary>后方移动速度（相对值）</summary>
        private const float BACKWARD_SPEED = 0.5f;  // 后方极慢
        /// <summary>前方区域角度阈值（度）</summary>
        private const float FORWARD_ANGLE = 45f;
        /// <summary>侧方区域角度阈值（度）</summary>
        private const float SIDE_ANGLE = 135f;

        /// <summary>
        /// 存储Voronoi区域的几何度量信息。
        /// </summary>
        public class RegionMetrics
        {
            /// <summary>对应的种子点索引</summary>
            public int seedIndex;
            /// <summary>区域面积（平方米）</summary>
            public float area;
            /// <summary>区域质心（几何中心）</summary>
            public Vector2 centroid;
            /// <summary>区域边界线段列表</summary>
            public List<BoundarySegment> boundarySegments = new List<BoundarySegment>();

            internal Vector2 centroidAccum;  // 质心累加值（内部计算用）
            internal int cellCount;          // 网格单元数量（内部计算用）
        }

        /// <summary>
        /// 表示区域边界的一条线段（轴对齐）。
        /// </summary>
        public struct BoundarySegment
        {
            /// <summary>线段起点（世界坐标）</summary>
            public Vector2 start;
            /// <summary>线段终点（世界坐标）</summary>
            public Vector2 end;

            public BoundarySegment(Vector2 start, Vector2 end)
            {
                this.start = start;
                this.end = end;
            }
        }

        /// <summary>
        /// 生成加权Voronoi空间分区。
        /// 使用Dijkstra算法的变体，根据种子点的方向性速度计算每个网格单元的归属。
        /// </summary>
        /// <param name="seeds">种子点列表，包含位置和朝向信息</param>
        /// <param name="physicalSpace">物理空间的矩形范围</param>
        /// <param name="gridCellSize">网格单元大小（米），默认0.1m</param>
        /// <returns>二维数组，存储每个网格单元所属的种子点索引（-1表示未分配）</returns>
        public static int[,] GeneratePartition(
            List<WeightedVoronoiGrid.SeedPoint> seeds,
            Rect physicalSpace,
            float gridCellSize = 0.1f)
        {
            int gridWidth = Mathf.FloorToInt(physicalSpace.width / gridCellSize);
            int gridHeight = Mathf.FloorToInt(physicalSpace.height / gridCellSize);
            
            int[,] partition = new int[gridWidth, gridHeight];
            float[,] arrivalTime = new float[gridWidth, gridHeight];

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    partition[x, y] = -1;
                    arrivalTime[x, y] = float.MaxValue;
                }
            }

            if (seeds == null || seeds.Count == 0)
            {
                return partition;
            }

            var frontier = new SortedSet<GridCell>(
                Comparer<GridCell>.Create((a, b) =>
                {
                    int timeCompare = a.time.CompareTo(b.time);
                    if (timeCompare != 0) return timeCompare;
                    int xCompare = a.x.CompareTo(b.x);
                    if (xCompare != 0) return xCompare;
                    return a.y.CompareTo(b.y);
                }));

            for (int i = 0; i < seeds.Count; i++)
            {
                var seed = seeds[i];
                int gridX, gridY;
                if (WorldToGridPosition(seed.position, physicalSpace, gridCellSize, gridWidth, gridHeight, out gridX, out gridY))
                {
                    partition[gridX, gridY] = i;
                    arrivalTime[gridX, gridY] = 0f;
                    frontier.Add(new GridCell(gridX, gridY, 0f, i));
                }
            }

            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            while (frontier.Count > 0)
            {
                var current = frontier.Min;
                frontier.Remove(current);

                if (current.time > arrivalTime[current.x, current.y])
                    continue;

                for (int dir = 0; dir < 4; dir++)
                {
                    int nx = current.x + dx[dir];
                    int ny = current.y + dy[dir];

                    if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                        continue;

                    Vector2 neighborWorldPos = GridToWorldPosition(nx, ny, physicalSpace, gridCellSize);
                    float speed = GetDirectionalSpeedToPoint(
                        seeds[current.seedIndex],
                        neighborWorldPos);

                    float travelTime = gridCellSize / Mathf.Max(speed, 0.0001f);
                    float newArrivalTime = current.time + travelTime;

                    if (newArrivalTime < arrivalTime[nx, ny])
                    {
                        arrivalTime[nx, ny] = newArrivalTime;
                        partition[nx, ny] = current.seedIndex;
                        frontier.Add(new GridCell(nx, ny, newArrivalTime, current.seedIndex));
                    }
                }
            }

            return partition;
        }

        /// <summary>
        /// 使用贪心图着色算法为种子点分配颜色索引。
        /// 确保相邻区域使用不同颜色，便于可视化区分。
        /// </summary>
        /// <param name="seeds">种子点列表，会修改其colorIndex属性</param>
        /// <param name="spacePartition">空间分区数组</param>
        /// <param name="gridWidth">网格宽度</param>
        /// <param name="gridHeight">网格高度</param>
        /// <param name="colorPalette">颜色调色板</param>
        public static void AssignColorsToSeeds(
            List<WeightedVoronoiGrid.SeedPoint> seeds,
            int[,] spacePartition,
            int gridWidth,
            int gridHeight,
            Color[] colorPalette)
        {
            if (seeds == null || seeds.Count == 0)
                return;

            for (int i = 0; i < seeds.Count; i++)
            {
                seeds[i].colorIndex = -1;
            }

            HashSet<int>[] adjacencyList = new HashSet<int>[seeds.Count];
            for (int i = 0; i < adjacencyList.Length; i++)
            {
                adjacencyList[i] = new HashSet<int>();
            }

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    int currentSeed = spacePartition[x, y];
                    if (currentSeed < 0) continue;

                    if (x + 1 < gridWidth)
                    {
                        int neighbor = spacePartition[x + 1, y];
                        if (neighbor >= 0 && neighbor != currentSeed)
                        {
                            adjacencyList[currentSeed].Add(neighbor);
                            adjacencyList[neighbor].Add(currentSeed);
                        }
                    }

                    if (y + 1 < gridHeight)
                    {
                        int neighbor = spacePartition[x, y + 1];
                        if (neighbor >= 0 && neighbor != currentSeed)
                        {
                            adjacencyList[currentSeed].Add(neighbor);
                            adjacencyList[neighbor].Add(currentSeed);
                        }
                    }
                }
            }

            int paletteLength = colorPalette != null && colorPalette.Length > 0 ? colorPalette.Length : 1;

            for (int i = 0; i < seeds.Count; i++)
            {
                bool[] usedColors = new bool[paletteLength];

                foreach (int neighbor in adjacencyList[i])
                {
                    if (seeds[neighbor].colorIndex >= 0)
                    {
                        usedColors[seeds[neighbor].colorIndex % paletteLength] = true;
                    }
                }

                for (int c = 0; c < paletteLength; c++)
                {
                    if (!usedColors[c])
                    {
                        seeds[i].colorIndex = c;
                        break;
                    }
                }

                if (seeds[i].colorIndex < 0)
                {
                    seeds[i].colorIndex = i % paletteLength;
                }
            }
        }

        /// <summary>
        /// 将空间分区渲染为2D纹理，用于可视化Voronoi图。
        /// 每个种子点的区域使用其分配的颜色填充。
        /// </summary>
        /// <param name="partition">空间分区数组</param>
        /// <param name="gridWidth">网格宽度</param>
        /// <param name="gridHeight">网格高度</param>
        /// <param name="seeds">种子点列表</param>
        /// <param name="colorPalette">颜色调色板</param>
        /// <param name="fallbackColor">未分配区域的默认颜色</param>
        /// <returns>生成的2D纹理</returns>
        public static Texture2D RenderPartitionTexture(
            int[,] partition,
            int gridWidth,
            int gridHeight,
            List<WeightedVoronoiGrid.SeedPoint> seeds,
            Color[] colorPalette,
            Color fallbackColor)
        {
            Texture2D texture = new Texture2D(gridWidth, gridHeight)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[gridWidth * gridHeight];
            int seedCount = seeds != null ? seeds.Count : 0;

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    int seedIndex = partition[x, y];
                    Color cellColor = fallbackColor;

                    if (seedIndex >= 0 && seedIndex < seedCount && colorPalette != null && colorPalette.Length > 0)
                    {
                        int paletteIndex = seeds[seedIndex].colorIndex % colorPalette.Length;
                        cellColor = colorPalette[paletteIndex];
                    }

                    pixels[y * gridWidth + x] = cellColor;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// 计算每个子空间的面积、质心和边界线段（基于网格划分结果）。
        /// 边界线段按世界坐标输出，轴对齐，便于可视化或生成碰撞体。
        /// </summary>
        public static List<RegionMetrics> ComputeRegionMetrics(
            int[,] partition,
            List<WeightedVoronoiGrid.SeedPoint> seeds,
            Rect physicalSpace,
            float gridCellSize = 0.1f)
        {
            int seedCount = seeds != null ? seeds.Count : 0;
            int gridWidth = partition.GetLength(0);
            int gridHeight = partition.GetLength(1);

            var regions = new List<RegionMetrics>(seedCount);
            for (int i = 0; i < seedCount; i++)
            {
                regions.Add(new RegionMetrics { seedIndex = i });
            }

            float cellArea = gridCellSize * gridCellSize;
            float halfWidth = physicalSpace.width * 0.5f;
            float halfHeight = physicalSpace.height * 0.5f;

            // 用于去重线段（相同起终点的边不重复记录）
            var segmentSets = new List<HashSet<string>>(seedCount);
            for (int i = 0; i < seedCount; i++)
            {
                segmentSets.Add(new HashSet<string>());
            }

            for (int x = 0; x < gridWidth; x++)
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    int s = partition[x, y];
                    if (s < 0 || s >= seedCount)
                        continue;

                    // 面积与质心累积
                    regions[s].cellCount++;
                    regions[s].area += cellArea;
                    regions[s].centroidAccum += GridToWorldPosition(x, y, physicalSpace, gridCellSize);

                    // 检查四个方向的边界
                    int nx, ny;

                    // 左边界
                    nx = x - 1; ny = y;
                    if (nx < 0 || partition[nx, ny] != s)
                    {
                        AddBoundarySegment(regions[s], segmentSets[s],
                            new Vector2(x * gridCellSize - halfWidth, y * gridCellSize - halfHeight),
                            new Vector2(x * gridCellSize - halfWidth, (y + 1) * gridCellSize - halfHeight));
                    }

                    // 右边界
                    nx = x + 1; ny = y;
                    if (nx >= gridWidth || partition[nx, ny] != s)
                    {
                        AddBoundarySegment(regions[s], segmentSets[s],
                            new Vector2((x + 1) * gridCellSize - halfWidth, y * gridCellSize - halfHeight),
                            new Vector2((x + 1) * gridCellSize - halfWidth, (y + 1) * gridCellSize - halfHeight));
                    }

                    // 下边界
                    nx = x; ny = y - 1;
                    if (ny < 0 || partition[nx, ny] != s)
                    {
                        AddBoundarySegment(regions[s], segmentSets[s],
                            new Vector2(x * gridCellSize - halfWidth, y * gridCellSize - halfHeight),
                            new Vector2((x + 1) * gridCellSize - halfWidth, y * gridCellSize - halfHeight));
                    }

                    // 上边界
                    nx = x; ny = y + 1;
                    if (ny >= gridHeight || partition[nx, ny] != s)
                    {
                        AddBoundarySegment(regions[s], segmentSets[s],
                            new Vector2(x * gridCellSize - halfWidth, (y + 1) * gridCellSize - halfHeight),
                            new Vector2((x + 1) * gridCellSize - halfWidth, (y + 1) * gridCellSize - halfHeight));
                    }
                }
            }

            // 完成质心计算
            for (int i = 0; i < regions.Count; i++)
            {
                if (regions[i].cellCount > 0)
                {
                    regions[i].centroid = regions[i].centroidAccum / regions[i].cellCount;
                }
                else
                {
                    regions[i].centroid = Vector2.zero;
                    regions[i].area = 0f;
                }
            }

            return regions;
        }

        /// <summary>
        /// 计算从种子点到目标点的方向性速度。
        /// 根据目标点相对于种子朝向的角度，返回对应的移动速度。
        /// </summary>
        /// <param name="seed">种子点（包含位置和朝向）</param>
        /// <param name="targetPoint">目标点位置</param>
        /// <returns>该方向的移动速度</returns>
        private static float GetDirectionalSpeedToPoint(
            WeightedVoronoiGrid.SeedPoint seed,
            Vector2 targetPoint)
        {
            Vector2 toTarget = targetPoint - seed.position;
            if (toTarget.sqrMagnitude < 0.000001f)
                return FORWARD_SPEED;

            // 计算从种子点到目标点的2D角度（数学角度：0°=东，90°=北）
            float targetAngle2D = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            
            // 将2D数学角度转换为Unity Y轴旋转角度（0°=北，90°=东）
            // Unity Y轴角度 = 90° - 数学角度
            float targetAngleUnity = 90f - targetAngle2D;
            
            // 计算相对角度差
            float relativeAngle = Mathf.DeltaAngle(seed.direction, targetAngleUnity);

            return GetDirectionalSpeed(Mathf.Abs(relativeAngle));
        }

        /// <summary>
        /// 根据偏离角度返回对应的移动速度。
        /// 角度范围：[0-45°]=前方速度, [45-135°]=侧方速度, [135-180°]=后方速度
        /// </summary>
        /// <param name="angleDegrees">相对于朝向的偏离角度（度）</param>
        /// <returns>对应方向的移动速度</returns>
        private static float GetDirectionalSpeed(float angleDegrees)
        {
            angleDegrees = Mathf.Abs(angleDegrees) % 360f;
            if (angleDegrees > 180f) angleDegrees = 360f - angleDegrees;

            if (angleDegrees <= FORWARD_ANGLE)
                return FORWARD_SPEED;
            else if (angleDegrees <= SIDE_ANGLE)
                return SIDE_SPEED;
            else
                return BACKWARD_SPEED;
        }

        /// <summary>
        /// 将世界坐标转换为网格坐标。
        /// </summary>
        /// <param name="worldPos">世界空间中的位置</param>
        /// <param name="physicalSpace">物理空间范围</param>
        /// <param name="gridCellSize">网格单元大小</param>
        /// <param name="gridWidth">网格宽度</param>
        /// <param name="gridHeight">网格高度</param>
        /// <param name="gridX">输出：网格X坐标</param>
        /// <param name="gridY">输出：网格Y坐标</param>
        /// <returns>如果坐标在有效范围内返回true，否则返回false</returns>
        private static bool WorldToGridPosition(
            Vector2 worldPos,
            Rect physicalSpace,
            float gridCellSize,
            int gridWidth,
            int gridHeight,
            out int gridX,
            out int gridY)
        {
            float localX = worldPos.x - physicalSpace.xMin;
            float localY = worldPos.y - physicalSpace.yMin;

            gridX = Mathf.FloorToInt(localX / gridCellSize);
            gridY = Mathf.FloorToInt(localY / gridCellSize);

            return gridX >= 0 && gridX < gridWidth && gridY >= 0 && gridY < gridHeight;
        }

        /// <summary>
        /// 将网格坐标转换为世界坐标（返回网格单元的中心点）。
        /// </summary>
        /// <param name="gridX">网格X坐标</param>
        /// <param name="gridY">网格Y坐标</param>
        /// <param name="physicalSpace">物理空间范围</param>
        /// <param name="gridCellSize">网格单元大小</param>
        /// <returns>世界空间中的位置（网格单元中心）</returns>
        private static Vector2 GridToWorldPosition(int gridX, int gridY, Rect physicalSpace, float gridCellSize)
        {
            float x = physicalSpace.xMin + (gridX + 0.5f) * gridCellSize;
            float y = physicalSpace.yMin + (gridY + 0.5f) * gridCellSize;
            return new Vector2(x, y);
        }

        /// <summary>
        /// 添加边界线段到区域度量中。
        /// 自动统一线段方向并去重，避免重复记录同一条边。
        /// </summary>
        /// <param name="region">区域度量对象</param>
        /// <param name="segmentSet">用于去重的线段集合</param>
        /// <param name="start">线段起点</param>
        /// <param name="end">线段终点</param>
        private static void AddBoundarySegment(RegionMetrics region, HashSet<string> segmentSet, Vector2 start, Vector2 end)
        {
            // 统一方向，避免重复记录同一条边
            if (start.x > end.x || (Mathf.Approximately(start.x, end.x) && start.y > end.y))
            {
                var temp = start;
                start = end;
                end = temp;
            }

            string key = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F4},{1:F4}->{2:F4},{3:F4}", start.x, start.y, end.x, end.y);

            if (segmentSet.Add(key))
            {
                region.boundarySegments.Add(new BoundarySegment(start, end));
            }
        }

        /// <summary>
        /// 优先队列中的网格单元节点（用于Dijkstra算法）。
        /// </summary>
        private struct GridCell
        {
            public int x;           // 网格X坐标
            public int y;           // 网格Y坐标
            public float time;      // 从种子点到达该单元的累积时间
            public int seedIndex;   // 对应的种子点索引

            public GridCell(int x, int y, float time, int seedIndex)
            {
                this.x = x;
                this.y = y;
                this.time = time;
                this.seedIndex = seedIndex;
            }
        }
    }
}
