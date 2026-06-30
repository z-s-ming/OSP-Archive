using System;
using UnityEngine;

[Serializable]
public struct LiveHmdPoseSample
{
    public int UserId;
    public uint Sequence;
    public long ClientUnixMilliseconds;
    public long HostReceiveUnixMilliseconds;
    public Vector2 ExperimentPosition;
    public float YawDegrees;
    public float HeightMeters;
    public bool IsCalibrated;
    public string ClientSessionId;
    public int CalibrationVersion;

    public float AgeSeconds
    {
        get
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Mathf.Max(0.0f, (now - HostReceiveUnixMilliseconds) / 1000.0f);
        }
    }

    public LiveHmdPoseSample(LiveVRPoseSample sample)
    {
        UserId = sample.UserId;
        Sequence = sample.Sequence;
        ClientUnixMilliseconds = sample.ClientUnixMilliseconds;
        HostReceiveUnixMilliseconds = sample.HostReceiveUnixMilliseconds;
        ExperimentPosition = sample.ExperimentPosition;
        YawDegrees = sample.YawDegrees;
        HeightMeters = sample.HeightMeters;
        IsCalibrated = sample.IsCalibrated;
        ClientSessionId = sample.ClientSessionId;
        CalibrationVersion = sample.CalibrationVersion;
    }

    public LiveVRPoseSample ToLiveVRPoseSample()
    {
        return new LiveVRPoseSample
        {
            UserId = UserId,
            Sequence = Sequence,
            ClientUnixMilliseconds = ClientUnixMilliseconds,
            HostReceiveUnixMilliseconds = HostReceiveUnixMilliseconds,
            ExperimentPosition = ExperimentPosition,
            YawDegrees = YawDegrees,
            HeightMeters = HeightMeters,
            IsCalibrated = IsCalibrated,
            ClientSessionId = ClientSessionId,
            CalibrationVersion = CalibrationVersion
        };
    }

    public static implicit operator LiveHmdPoseSample(LiveVRPoseSample sample)
    {
        return new LiveHmdPoseSample(sample);
    }

    public static implicit operator LiveVRPoseSample(LiveHmdPoseSample sample)
    {
        return sample.ToLiveVRPoseSample();
    }
}
