using System.Collections.Generic;
using UnityEngine;

public struct LiveVRGainDebugSample
{
    public int UserId;
    public bool IsValid;
    public bool ResetActive;
    public bool HasRedirection;
    public GainType GainType;
    public string RedirectorName;
    public float HostTime;
    public float PhysicalDeltaMeters;
    public float PhysicalYawDeltaDegrees;
    public float PhysicalYawRateDegreesPerSecond;
    public float VirtualDeltaMeters;
    public float VirtualYawDeltaDegrees;
    public float InjectedYawDeltaDegrees;
    public float PrimaryRateDegreesPerSecond;
    public float TranslationScale;
    public float TranslationGain;
    public float RotationGain;
    public float CurvatureGain;
    public float PreviousVirtualYawDegrees;
    public float NewVirtualYawDegrees;
    public float CurrentPhysicalYawDegrees;
    public float VirtualPhysicalYawDiffDegrees;
}

public static class LiveVRGainDebugState
{
    private const float LogIntervalSeconds = 0.5f;
    private static readonly Dictionary<int, LiveVRGainDebugSample> LatestByUserId = new Dictionary<int, LiveVRGainDebugSample>();
    private static readonly Dictionary<int, float> NextLogTimeByUserId = new Dictionary<int, float>();

    public static void Record(int userId, LiveVRGainDebugSample sample)
    {
        sample.UserId = userId;
        sample.HostTime = Time.unscaledTime;
        LatestByUserId[userId] = sample;

        float nextLogTime;
        if (!NextLogTimeByUserId.TryGetValue(userId, out nextLogTime))
            nextLogTime = 0.0f;

        if (Time.unscaledTime < nextLogTime)
            return;

        NextLogTimeByUserId[userId] = Time.unscaledTime + LogIntervalSeconds;
        Debug.Log(string.Format(
            "[LiveVR] Gain user={0} type={1} redir={2} physicalDelta={3:F3} physicalYawDelta={4:F2} virtualDelta={5:F3} virtualYawDelta={6:F2} injectedYaw={7:F2} rate={8:F2} gains T/R/C={9:F3}/{10:F3}/{11:F3} yawDiff={12:F2}",
            userId,
            sample.HasRedirection ? sample.GainType.ToString() : "None",
            string.IsNullOrEmpty(sample.RedirectorName) ? "-" : sample.RedirectorName,
            sample.PhysicalDeltaMeters,
            sample.PhysicalYawDeltaDegrees,
            sample.VirtualDeltaMeters,
            sample.VirtualYawDeltaDegrees,
            sample.InjectedYawDeltaDegrees,
            sample.PrimaryRateDegreesPerSecond,
            sample.TranslationGain,
            sample.RotationGain,
            sample.CurvatureGain,
            sample.VirtualPhysicalYawDiffDegrees));
    }

    public static bool TryGetLatest(int userId, out LiveVRGainDebugSample sample)
    {
        return LatestByUserId.TryGetValue(userId, out sample);
    }

    public static void Clear()
    {
        LatestByUserId.Clear();
        NextLogTimeByUserId.Clear();
    }
}
