using System;
using System.Globalization;
using UnityEngine;

[Serializable]
public struct LiveVRPoseSample
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

    public string ToNetworkMessage()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return string.Format(
            c,
            "POSE|{0}|{1}|{2}|{3:R}|{4:R}|{5:R}|{6:R}|{7}|{8}|{9}",
            UserId,
            Sequence,
            ClientUnixMilliseconds,
            ExperimentPosition.x,
            ExperimentPosition.y,
            YawDegrees,
            HeightMeters,
            IsCalibrated ? 1 : 0,
            Escape(ClientSessionId),
            CalibrationVersion);
    }

    public static bool TryParse(string message, out LiveVRPoseSample sample)
    {
        sample = default(LiveVRPoseSample);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if ((parts.Length != 9 && parts.Length != 11) || parts[0] != "POSE")
            return false;

        CultureInfo c = CultureInfo.InvariantCulture;
        int userId;
        uint sequence;
        long clientTime;
        float x;
        float y;
        float yaw;
        float height;
        int calibrated;

        if (!int.TryParse(parts[1], NumberStyles.Integer, c, out userId) ||
            !uint.TryParse(parts[2], NumberStyles.Integer, c, out sequence) ||
            !long.TryParse(parts[3], NumberStyles.Integer, c, out clientTime) ||
            !float.TryParse(parts[4], NumberStyles.Float, c, out x) ||
            !float.TryParse(parts[5], NumberStyles.Float, c, out y) ||
            !float.TryParse(parts[6], NumberStyles.Float, c, out yaw) ||
            !float.TryParse(parts[7], NumberStyles.Float, c, out height) ||
            !int.TryParse(parts[8], NumberStyles.Integer, c, out calibrated))
        {
            return false;
        }

        sample.UserId = userId;
        sample.Sequence = sequence;
        sample.ClientUnixMilliseconds = clientTime;
        sample.HostReceiveUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        sample.ExperimentPosition = new Vector2(x, y);
        sample.YawDegrees = yaw;
        sample.HeightMeters = height;
        sample.IsCalibrated = calibrated != 0;
        sample.ClientSessionId = parts.Length >= 10 ? Unescape(parts[9]) : string.Empty;
        if (parts.Length >= 11)
        {
            int calibrationVersion;
            if (int.TryParse(parts[10], NumberStyles.Integer, c, out calibrationVersion))
                sample.CalibrationVersion = calibrationVersion;
        }
        return true;
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("%", "%25").Replace("|", "%7C");
    }

    private static string Unescape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("%7C", "|").Replace("%25", "%");
    }
}
