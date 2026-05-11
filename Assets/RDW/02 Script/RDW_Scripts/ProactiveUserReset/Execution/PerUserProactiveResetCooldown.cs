using System.Collections.Generic;
using UnityEngine;

public class PerUserProactiveResetCooldown : IProactiveResetCooldownPolicy
{
    private readonly Dictionary<int, int> cooldownUntilFrameByUser = new Dictionary<int, int>();
    private readonly Dictionary<long, int> cooldownUntilFrameByPair = new Dictionary<long, int>();

    public void Clear()
    {
        cooldownUntilFrameByUser.Clear();
        cooldownUntilFrameByPair.Clear();
    }

    public bool IsCoolingDown(int userId, int currentFrame)
    {
        if (userId < 0)
            return false;

        if (!cooldownUntilFrameByUser.TryGetValue(userId, out int cooldownUntilFrame))
            return false;

        if (currentFrame < cooldownUntilFrame)
            return true;

        cooldownUntilFrameByUser.Remove(userId);
        return false;
    }

    public bool IsPairCoolingDown(int userAId, int userBId, int currentFrame)
    {
        if (userAId < 0 || userBId < 0 || userAId == userBId)
            return false;

        long pairKey = BuildPairKey(userAId, userBId);
        if (!cooldownUntilFrameByPair.TryGetValue(pairKey, out int cooldownUntilFrame))
            return false;

        if (currentFrame < cooldownUntilFrame)
            return true;

        cooldownUntilFrameByPair.Remove(pairKey);
        return false;
    }

    public void RegisterExecution(int userId, int currentFrame, float fixedDeltaTime, float cooldownSeconds)
    {
        if (userId < 0)
            return;

        float safeCooldownSeconds = Mathf.Max(0.0f, cooldownSeconds);
        if (safeCooldownSeconds <= 0.0f)
            return;

        int cooldownFrames = Mathf.CeilToInt(safeCooldownSeconds / Mathf.Max(fixedDeltaTime, 0.0001f));
        cooldownUntilFrameByUser[userId] = currentFrame + cooldownFrames;
    }

    public void RegisterPairExecution(int userAId, int userBId, int currentFrame, float fixedDeltaTime, float cooldownSeconds)
    {
        if (userAId < 0 || userBId < 0 || userAId == userBId)
            return;

        float safeCooldownSeconds = Mathf.Max(0.0f, cooldownSeconds);
        if (safeCooldownSeconds <= 0.0f)
            return;

        int cooldownFrames = Mathf.CeilToInt(safeCooldownSeconds / Mathf.Max(fixedDeltaTime, 0.0001f));
        cooldownUntilFrameByPair[BuildPairKey(userAId, userBId)] = currentFrame + cooldownFrames;
    }

    private static long BuildPairKey(int userAId, int userBId)
    {
        int min = Mathf.Min(userAId, userBId);
        int max = Mathf.Max(userAId, userBId);
        return ((long)min << 32) ^ (uint)max;
    }
}
