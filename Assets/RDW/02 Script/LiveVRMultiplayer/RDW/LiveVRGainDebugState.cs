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
    public float SampleDeltaSeconds;
    public float PhysicalSpeedMetersPerSecond;
    public float PhysicalYawDeltaDegrees;
    public float PhysicalYawRateDegreesPerSecond;
    public uint CurrentPoseSequence;
    public uint PreviousPoseSequence;
    public bool DuplicatePoseSequence;
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
    private const float GainStateValidSeconds = 0.12f;
    private struct ActiveGainState
    {
        public GainType GainType;
        public float GainRateDegreesPerSecond;
        public float ExpiresAtTime;
    }

    public struct IntervalGainStats
    {
        public float StartedAtTime;
        public float LastLogDistanceMeters;
        public int FreshPoseSamples;
        public int DuplicatePoseSamples;
        public float PhysicalDistanceMeters;
        public float VirtualDistanceMeters;
        public float TotalInjectedYawDegrees;
        public float CurvatureInjectedYawDegrees;
        public float RotationInjectedYawDegrees;
        public int CurvatureFrames;
        public int RotationFrames;
        public int TranslationFrames;
        public int NoneFrames;
    }

    private const float IntervalDistanceLogStepMeters = 2.0f;
    private static readonly Dictionary<int, LiveVRGainDebugSample> LatestByUserId = new Dictionary<int, LiveVRGainDebugSample>();
    private static readonly Dictionary<int, float> NextLogTimeByUserId = new Dictionary<int, float>();
    private static readonly Dictionary<int, float> PendingInjectedYawByUserId = new Dictionary<int, float>();
    private static readonly Dictionary<int, ActiveGainState> ActiveGainByUserId = new Dictionary<int, ActiveGainState>();
    private static readonly Dictionary<int, IntervalGainStats> IntervalStatsByUserId = new Dictionary<int, IntervalGainStats>();

    public static void Record(int userId, LiveVRGainDebugSample sample)
    {
        sample.UserId = userId;
        sample.HostTime = Time.unscaledTime;
        LatestByUserId[userId] = sample;
        UpdateIntervalStats(userId, sample);
        if (sample.ResetActive)
        {
            PendingInjectedYawByUserId.Remove(userId);
            ActiveGainByUserId.Remove(userId);
        }
        else if (sample.HasRedirection && !sample.DuplicatePoseSequence)
        {
            ActiveGainByUserId[userId] = new ActiveGainState
            {
                GainType = sample.GainType,
                GainRateDegreesPerSecond = sample.PrimaryRateDegreesPerSecond,
                ExpiresAtTime = Time.unscaledTime + GainStateValidSeconds
            };
        }

        float nextLogTime;
        if (!NextLogTimeByUserId.TryGetValue(userId, out nextLogTime))
            nextLogTime = 0.0f;

        if (Time.unscaledTime < nextLogTime)
            return;

        NextLogTimeByUserId[userId] = Time.unscaledTime + LogIntervalSeconds;
        Debug.Log(string.Format(
            "[LiveVR] Gain user={0} type={1} redir={2} seq={3} prevSeq={4} duplicatePose={5} sampleDt={6:F3} physicalDelta={7:F3} speed={8:F3} physicalYawDelta={9:F2} yawRate={10:F2} virtualDelta={11:F3} virtualYawDelta={12:F2} injectedYaw={13:F2} rate={14:F2} gains T/R/C={15:F3}/{16:F3}/{17:F3} yawDiff={18:F2}",
            userId,
            sample.HasRedirection ? sample.GainType.ToString() : "None",
            string.IsNullOrEmpty(sample.RedirectorName) ? "-" : sample.RedirectorName,
            sample.CurrentPoseSequence,
            sample.PreviousPoseSequence,
            sample.DuplicatePoseSequence ? 1 : 0,
            sample.SampleDeltaSeconds,
            sample.PhysicalDeltaMeters,
            sample.PhysicalSpeedMetersPerSecond,
            sample.PhysicalYawDeltaDegrees,
            sample.PhysicalYawRateDegreesPerSecond,
            sample.VirtualDeltaMeters,
            sample.VirtualYawDeltaDegrees,
            sample.InjectedYawDeltaDegrees,
            sample.PrimaryRateDegreesPerSecond,
            sample.TranslationGain,
            sample.RotationGain,
            sample.CurvatureGain,
            sample.VirtualPhysicalYawDiffDegrees));
    }

    private static void UpdateIntervalStats(int userId, LiveVRGainDebugSample sample)
    {
        IntervalGainStats stats;
        if (!IntervalStatsByUserId.TryGetValue(userId, out stats))
        {
            stats = new IntervalGainStats
            {
                StartedAtTime = Time.unscaledTime,
                LastLogDistanceMeters = 0.0f
            };
        }

        if (sample.ResetActive)
        {
            IntervalStatsByUserId[userId] = stats;
            return;
        }

        if (sample.DuplicatePoseSequence)
        {
            stats.DuplicatePoseSamples++;
            IntervalStatsByUserId[userId] = stats;
            return;
        }

        stats.FreshPoseSamples++;
        stats.PhysicalDistanceMeters += sample.PhysicalDeltaMeters;
        stats.VirtualDistanceMeters += sample.VirtualDeltaMeters;
        stats.TotalInjectedYawDegrees += sample.InjectedYawDeltaDegrees;

        if (sample.HasRedirection)
        {
            if (sample.GainType == GainType.Curvature)
            {
                stats.CurvatureFrames++;
                stats.CurvatureInjectedYawDegrees += sample.InjectedYawDeltaDegrees;
            }
            else if (sample.GainType == GainType.Rotation)
            {
                stats.RotationFrames++;
                stats.RotationInjectedYawDegrees += sample.InjectedYawDeltaDegrees;
            }
            else if (sample.GainType == GainType.Translation)
            {
                stats.TranslationFrames++;
            }
        }
        else
        {
            stats.NoneFrames++;
        }

        if (stats.PhysicalDistanceMeters - stats.LastLogDistanceMeters >= IntervalDistanceLogStepMeters)
        {
            stats.LastLogDistanceMeters = stats.PhysicalDistanceMeters;
            LogIntervalStats(userId, -1, "distance_step", stats);
        }

        IntervalStatsByUserId[userId] = stats;
    }

    public static void LogAndResetInterval(int userId, int resetEventId, string reason)
    {
        IntervalGainStats stats;
        if (!IntervalStatsByUserId.TryGetValue(userId, out stats))
        {
            stats = new IntervalGainStats
            {
                StartedAtTime = Time.unscaledTime,
                LastLogDistanceMeters = 0.0f
            };
        }

        LogIntervalStats(userId, resetEventId, reason, stats);
        IntervalStatsByUserId[userId] = new IntervalGainStats
        {
            StartedAtTime = Time.unscaledTime,
            LastLogDistanceMeters = 0.0f
        };
    }

    private static void LogIntervalStats(int userId, int resetEventId, string reason, IntervalGainStats stats)
    {
        float duration = Mathf.Max(0.0f, Time.unscaledTime - stats.StartedAtTime);
        Debug.Log(string.Format(
            "[LiveVR] GainInterval user={0} reason={1} event={2} duration={3:F2}s physicalDist={4:F3}m virtualDist={5:F3}m totalInjectedYaw={6:F2} curvatureYaw={7:F2} rotationYaw={8:F2} frames fresh/dup/none/curv/rot/trans={9}/{10}/{11}/{12}/{13}/{14}",
            userId,
            string.IsNullOrEmpty(reason) ? "-" : reason,
            resetEventId,
            duration,
            stats.PhysicalDistanceMeters,
            stats.VirtualDistanceMeters,
            stats.TotalInjectedYawDegrees,
            stats.CurvatureInjectedYawDegrees,
            stats.RotationInjectedYawDegrees,
            stats.FreshPoseSamples,
            stats.DuplicatePoseSamples,
            stats.NoneFrames,
            stats.CurvatureFrames,
            stats.RotationFrames,
            stats.TranslationFrames));
    }

    public static bool TryGetLatest(int userId, out LiveVRGainDebugSample sample)
    {
        return LatestByUserId.TryGetValue(userId, out sample);
    }

    public static bool TryConsumePendingInjectedYaw(int userId, out float injectedYawDeltaDegrees, out GainType gainType)
    {
        injectedYawDeltaDegrees = 0.0f;
        gainType = GainType.Undefined;

        float pendingYaw;
        if (!PendingInjectedYawByUserId.TryGetValue(userId, out pendingYaw))
            return false;

        PendingInjectedYawByUserId.Remove(userId);
        injectedYawDeltaDegrees = pendingYaw;

        LiveVRGainDebugSample sample;
        if (LatestByUserId.TryGetValue(userId, out sample) && sample.HasRedirection)
            gainType = sample.GainType;

        return Mathf.Abs(injectedYawDeltaDegrees) > Mathf.Epsilon;
    }

    public static bool TryGetActiveGainState(
        int userId,
        out float gainRateDegreesPerSecond,
        out GainType gainType,
        out float gainValidSeconds)
    {
        gainRateDegreesPerSecond = 0.0f;
        gainType = GainType.Undefined;
        gainValidSeconds = 0.0f;

        ActiveGainState state;
        if (!ActiveGainByUserId.TryGetValue(userId, out state))
            return false;

        gainValidSeconds = state.ExpiresAtTime - Time.unscaledTime;
        if (gainValidSeconds <= 0.0f)
        {
            ActiveGainByUserId.Remove(userId);
            return false;
        }

        gainType = state.GainType;
        gainRateDegreesPerSecond = state.GainRateDegreesPerSecond;
        return Mathf.Abs(gainRateDegreesPerSecond) > Mathf.Epsilon && gainType != GainType.Undefined;
    }

    public static void Clear()
    {
        LatestByUserId.Clear();
        NextLogTimeByUserId.Clear();
        PendingInjectedYawByUserId.Clear();
        ActiveGainByUserId.Clear();
        IntervalStatsByUserId.Clear();
    }
}
