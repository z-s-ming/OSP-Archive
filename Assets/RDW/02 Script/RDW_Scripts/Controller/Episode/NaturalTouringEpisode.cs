using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    public class NaturalTouringEpisode : Episode
    {
        public NaturalTouringEpisode() : base() { }

        public NaturalTouringEpisode(int episodeLength) : base(episodeLength) { }

        [Header("=== 真人游览行为参数 ===")]
        [Tooltip("前向偏置角度：正常行走时的左右摆动范围 (默认 +/- 45度)")]
        [Range(0f, 90f)]
        public float forwardBiasAngle = 45.0f;

        [Tooltip("转向概率：行人在游览中改变目标的概率 (0.0 ~ 1.0)")]
        [Range(0f, 1f)]
        public float directionChangeProbability = 0.2f;

        [Tooltip("转向角度范围：当发生转向时，允许的最大偏转角度 (默认 +/- 120度)")]
        [Range(45f, 180f)]
        public float turnBiasAngle = 120.0f;

        [Tooltip("死锁重试阈值：前方多少次尝试失败后，强制进行全向搜索(掉头)")]
        public int maxForwardRetry = 5;

        protected override void GenerateEpisode(Transform2D virtualUserTransform, Space2D virtualSpace, Object2D virtualUser)
        {
            Vector2 samplingPosition = Vector2.zero;
            Vector2 userPosition = virtualUserTransform.localPosition;
            Vector2 currentForward = virtualUserTransform.forward;

            bool validPointFound = false;
            int retryCount = 0;

            // 1. 决定本轮意图：是继续走(Forward)还是切目标(Turn)?
            // 我们在循环外决定意图，避免在一个死胡同里反复切换意图
            bool isIntentionalTurn = Random.value < directionChangeProbability;
            
            do
            {
                float distance = 0.0f;

                // --- 步骤 A: 距离生成 (保持原有课程逻辑，确保实验变量受控) ---
                if (_GCM.GlobalCoordinationManager.instance.bMixedExploration)
                {
                     // 这里为了简化代码，直接复用之前的逻辑分支
                     // 你也可以根据需要直接调用 GlobalCoordinationManager 的参数
                     // 游览通常包含走走停停，所以这里我们倾向于使用 LE (长距离) 和 SE (短距离)
                     if(Random.value > 0.3f) 
                        distance = Utility.sampleUniform(1.0f, 3.0f); // 正常行走
                     else 
                        distance = Utility.sampleUniform(0.5f, 1.5f); // 慢速观赏
                }
                else
                {
                    distance = Utility.sampleUniform(1.0f, 3.0f);
                }

                // --- 步骤 B: 角度生成 (核心修改) ---
                Vector2 sampleForward;
                
                if (retryCount < maxForwardRetry)
                {
                    float selectedAngleRange;

                    if (isIntentionalTurn)
                    {
                        // 【模式2：兴趣转向】
                        // 模拟用户被侧后方的物体吸引，或者走到路口转弯
                        // 范围较大，比如 -120 到 +120 度
                        selectedAngleRange = turnBiasAngle;
                    }
                    else
                    {
                        // 【模式1：惯性前行】
                        // 模拟用户看着前方走路，只有微小的左右调整
                        // 范围较小，比如 -45 到 +45 度
                        selectedAngleRange = forwardBiasAngle;
                    }

                    float randomDelta = Random.Range(-selectedAngleRange, selectedAngleRange);
                    sampleForward = Utility.RotateVector2(currentForward, randomDelta);
                }
                else
                {
                    // 【模式3：碰壁逃逸/掉头】
                    // 如果前 5 次尝试都失败了（说明前面是墙或者死胡同），
                    // 模拟真人“停下来，向后转”的行为。
                    // 此时允许 360 度随机，或者优先向后方搜索。
                    float fullRandomAngle = Random.Range(0f, 360f);
                    sampleForward = Utility.RotateVector2(currentForward, fullRandomAngle);
                }

                // --- 步骤 C: 计算并校验 ---
                samplingPosition = userPosition + sampleForward * distance;
                retryCount++;

                // 检查点是否在房间内 (使用 0.5f 的边缘缓冲)
                validPointFound = IsValidTargetCandidate(virtualSpace, userPosition, samplingPosition, 0.5f);

            } while (!validPointFound);

            currentTargetPosition = samplingPosition;
        }
    }
}
