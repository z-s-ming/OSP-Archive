using System.Collections.Generic;
using UnityEngine;

public struct LiveVRGainCommand
{
    public int UserId;
    public uint CommandSequence;
    public uint SourcePoseSequence;
    public GainType GainType;
    public float GainRateDegreesPerSecond;
    public float HostTime;
    public float AgeSeconds;
    public float ClientValidSeconds;
}

public static class LiveVRGainCommandService
{
    private const float CommandHoldDurationSeconds = 0.20f;
    private const float ClientValidDurationSeconds = 0.25f;
    private const float StoppedSpeedMetersPerSecond = 0.08f;
    private const float StoppedYawRateDegreesPerSecond = 12.0f;
    private const float MinWalkingSpeedForCurvatureMetersPerSecond = 0.08f;
    private const float MinYawRateForRotationDegreesPerSecond = 12.0f;

    private struct StoredCommand
    {
        public uint CommandSequence;
        public uint SourcePoseSequence;
        public GainType GainType;
        public float GainRateDegreesPerSecond;
        public float HostTime;
        public float HoldUntilTime;
    }

    private static readonly Dictionary<int, StoredCommand> CommandsByUserId = new Dictionary<int, StoredCommand>();
    private static uint nextCommandSequence;

    public static void UpdateFromRdwSample(int userId, LiveVRGainDebugSample sample)
    {
        if (!sample.IsValid)
            return;

        if (sample.ResetActive)
        {
            Clear(userId);
            return;
        }

        if (sample.DuplicatePoseSequence)
            return;

        bool userStopped =
            sample.PhysicalSpeedMetersPerSecond < StoppedSpeedMetersPerSecond &&
            Mathf.Abs(sample.PhysicalYawRateDegreesPerSecond) < StoppedYawRateDegreesPerSecond;
        if (userStopped)
        {
            Clear(userId);
            return;
        }

        if (!sample.HasRedirection ||
            sample.GainType == GainType.Undefined ||
            Mathf.Abs(sample.PrimaryRateDegreesPerSecond) <= Mathf.Epsilon)
        {
            Clear(userId);
            return;
        }

        if (sample.GainType == GainType.Curvature &&
            sample.PhysicalSpeedMetersPerSecond < MinWalkingSpeedForCurvatureMetersPerSecond)
        {
            Clear(userId);
            return;
        }

        if (sample.GainType == GainType.Rotation &&
            Mathf.Abs(sample.PhysicalYawRateDegreesPerSecond) < MinYawRateForRotationDegreesPerSecond)
        {
            Clear(userId);
            return;
        }

        CommandsByUserId[userId] = new StoredCommand
        {
            CommandSequence = ++nextCommandSequence,
            SourcePoseSequence = sample.CurrentPoseSequence,
            GainType = sample.GainType,
            GainRateDegreesPerSecond = sample.PrimaryRateDegreesPerSecond,
            HostTime = Time.unscaledTime,
            HoldUntilTime = Time.unscaledTime + CommandHoldDurationSeconds
        };
    }

    public static bool TryGetCommand(int userId, out LiveVRGainCommand command)
    {
        command = default(LiveVRGainCommand);

        StoredCommand stored;
        if (!CommandsByUserId.TryGetValue(userId, out stored))
            return false;

        if (Time.unscaledTime > stored.HoldUntilTime)
        {
            CommandsByUserId.Remove(userId);
            return false;
        }

        command = new LiveVRGainCommand
        {
            UserId = userId,
            CommandSequence = stored.CommandSequence,
            SourcePoseSequence = stored.SourcePoseSequence,
            GainType = stored.GainType,
            GainRateDegreesPerSecond = stored.GainRateDegreesPerSecond,
            HostTime = stored.HostTime,
            AgeSeconds = Mathf.Max(0.0f, Time.unscaledTime - stored.HostTime),
            ClientValidSeconds = ClientValidDurationSeconds
        };

        return command.GainType != GainType.Undefined &&
               Mathf.Abs(command.GainRateDegreesPerSecond) > Mathf.Epsilon;
    }

    public static void Clear(int userId)
    {
        CommandsByUserId.Remove(userId);
    }

    public static void ClearAll()
    {
        CommandsByUserId.Clear();
    }
}
