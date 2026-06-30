using System;
using System.Globalization;
using UnityEngine;

public enum LiveVRExperimentState
{
    Idle = 0,
    WaitingForUsers = 1,
    Ready = 2,
    Running = 3,
    Paused = 4,
    Completed = 5,
    Invalid = 6
}

public enum LiveVRTrialEndState
{
    Normal = 0,
    ManualStop = 1,
    InvalidTrackingLost = 2,
    InvalidNetworkLost = 3,
    InvalidResetTimeout = 4,
    InvalidSafetyBoundary = 5,
    InvalidUserAbort = 6,
    InvalidHostError = 7
}

public enum LiveVRUserConnectionState
{
    ConnectedFresh = 0,
    ConnectedStale = 1,
    Disconnected = 2,
    Reconnecting = 3,
    NeedsPoseReanchor = 4
}

public struct LiveVRControlEnvelope
{
    public string MessageId;
    public string MessageType;
    public int TargetUserId;
    public string HostRunId;
    public string RunId;
    public int TrialId;
    public int ConfigVersion;
    public int RestartEpoch;
    public int CalibrationVersion;
    public string Payload;

    public static LiveVRControlEnvelope Create(
        string messageId,
        string messageType,
        int targetUserId,
        LiveVRProtocolVersionContext context,
        string payload)
    {
        LiveVRControlEnvelope envelope = new LiveVRControlEnvelope
        {
            MessageId = messageId,
            MessageType = messageType,
            TargetUserId = targetUserId,
            Payload = payload ?? string.Empty
        };

        if (context != null)
        {
            envelope.HostRunId = context.HostRunId;
            envelope.RunId = context.RunId;
            envelope.TrialId = context.TrialId;
            envelope.ConfigVersion = context.ConfigVersion;
            envelope.RestartEpoch = context.RestartEpoch;
            envelope.CalibrationVersion = context.CalibrationVersion;
        }

        return envelope;
    }

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "CONTROL|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}",
            Escape(MessageId),
            Escape(MessageType),
            TargetUserId,
            Escape(HostRunId),
            Escape(RunId),
            TrialId,
            ConfigVersion,
            RestartEpoch,
            CalibrationVersion,
            Escape(Payload));
    }

    public static bool TryParse(string message, out LiveVRControlEnvelope envelope)
    {
        envelope = default(LiveVRControlEnvelope);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 11 || parts[0] != "CONTROL")
            return false;

        int targetUserId;
        int trialId;
        int configVersion;
        int restartEpoch;
        int calibrationVersion;
        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetUserId) ||
            !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out trialId) ||
            !int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out configVersion) ||
            !int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out restartEpoch) ||
            !int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out calibrationVersion))
        {
            return false;
        }

        envelope.MessageId = Unescape(parts[1]);
        envelope.MessageType = Unescape(parts[2]);
        envelope.TargetUserId = targetUserId;
        envelope.HostRunId = Unescape(parts[4]);
        envelope.RunId = Unescape(parts[5]);
        envelope.TrialId = trialId;
        envelope.ConfigVersion = configVersion;
        envelope.RestartEpoch = restartEpoch;
        envelope.CalibrationVersion = calibrationVersion;
        envelope.Payload = Unescape(parts[10]);
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

public struct LiveVRControlAckMessage
{
    public string MessageId;
    public string MessageType;
    public int UserId;
    public bool Accepted;
    public string Reason;
    public long UnixMilliseconds;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "CONTROL_ACK|{0}|{1}|{2}|{3}|{4}|{5}",
            Escape(MessageId),
            Escape(MessageType),
            UserId,
            Accepted ? 1 : 0,
            Escape(Reason),
            UnixMilliseconds);
    }

    public static bool TryParse(string message, out LiveVRControlAckMessage ack)
    {
        ack = default(LiveVRControlAckMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 7 || parts[0] != "CONTROL_ACK")
            return false;

        int userId;
        int acceptedFlag;
        long unixMs;
        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out acceptedFlag) ||
            !long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out unixMs))
        {
            return false;
        }

        ack.MessageId = Unescape(parts[1]);
        ack.MessageType = Unescape(parts[2]);
        ack.UserId = userId;
        ack.Accepted = acceptedFlag != 0;
        ack.Reason = Unescape(parts[5]);
        ack.UnixMilliseconds = unixMs;
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

public struct LiveVRClientResetClearMessage
{
    public int UserId;
    public int RestartEpoch;
    public string Reason;
    public long HostUnixMilliseconds;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "CLIENT_RESET_CLEAR|{0}|{1}|{2}|{3}",
            UserId,
            RestartEpoch,
            Escape(Reason),
            HostUnixMilliseconds);
    }

    public static bool TryParse(string message, out LiveVRClientResetClearMessage clear)
    {
        clear = default(LiveVRClientResetClearMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 5 || parts[0] != "CLIENT_RESET_CLEAR")
            return false;

        int userId;
        int restartEpoch;
        long hostTime;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out restartEpoch) ||
            !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        clear.UserId = userId;
        clear.RestartEpoch = restartEpoch;
        clear.Reason = Unescape(parts[3]);
        clear.HostUnixMilliseconds = hostTime;
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

public struct LiveVRAckMessage
{
    public int UserId;
    public uint LastSequence;
    public long HostUnixMilliseconds;
    public LiveVRExperimentState ExperimentState;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "ACK|{0}|{1}|{2}|{3}",
            UserId,
            LastSequence,
            HostUnixMilliseconds,
            ExperimentState);
    }

    public static bool TryParse(string message, out LiveVRAckMessage ack)
    {
        ack = default(LiveVRAckMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 5 || parts[0] != "ACK")
            return false;

        int userId;
        uint sequence;
        long hostTime;
        LiveVRExperimentState state;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !uint.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime) ||
            !Enum.TryParse(parts[4], true, out state))
        {
            return false;
        }

        ack.UserId = userId;
        ack.LastSequence = sequence;
        ack.HostUnixMilliseconds = hostTime;
        ack.ExperimentState = state;
        return true;
    }
}

public struct LiveVRHostDiscoveryRequestMessage
{
    public string DeviceKey;
    public string DeviceName;
    public long ClientUnixMilliseconds;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "DISCOVER_HOST|{0}|{1}|{2}",
            Escape(DeviceKey),
            Escape(DeviceName),
            ClientUnixMilliseconds);
    }

    public static bool TryParse(string message, out LiveVRHostDiscoveryRequestMessage request)
    {
        request = default(LiveVRHostDiscoveryRequestMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 4 || parts[0] != "DISCOVER_HOST")
            return false;

        long clientTime;
        if (!long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out clientTime))
            return false;

        request.DeviceKey = Unescape(parts[1]);
        request.DeviceName = Unescape(parts[2]);
        request.ClientUnixMilliseconds = clientTime;
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

public struct LiveVRHostAdvertisementMessage
{
    public int HostPosePort;
    public int ExpectedUserCount;
    public string HostRunId;
    public long HostUnixMilliseconds;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "HOST_ADVERTISE|{0}|{1}|{2}|{3}",
            HostPosePort,
            ExpectedUserCount,
            Escape(HostRunId),
            HostUnixMilliseconds);
    }

    public static bool TryParse(string message, out LiveVRHostAdvertisementMessage advertisement)
    {
        advertisement = default(LiveVRHostAdvertisementMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 5 || parts[0] != "HOST_ADVERTISE")
            return false;

        int hostPort;
        int expectedUsers;
        long hostTime;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostPort) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedUsers) ||
            !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        advertisement.HostPosePort = hostPort;
        advertisement.ExpectedUserCount = expectedUsers;
        advertisement.HostRunId = Unescape(parts[3]);
        advertisement.HostUnixMilliseconds = hostTime;
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

public struct LiveVRHelloMessage
{
    public int UserId;
    public long ClientUnixMilliseconds;
    public bool ProactiveResetEnabled;
    public string DeviceKey;
    public string DeviceName;
    public string ClientSessionId;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "HELLO|{0}|{1}|{2}|{3}|{4}|{5}",
            UserId,
            ClientUnixMilliseconds,
            ProactiveResetEnabled ? 1 : 0,
            Escape(DeviceKey),
            Escape(DeviceName),
            Escape(ClientSessionId));
    }

    public static bool TryParse(string message, out LiveVRHelloMessage hello)
    {
        hello = default(LiveVRHelloMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length < 3 || parts.Length > 7 || parts[0] != "HELLO")
            return false;

        int userId;
        long clientTime;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out clientTime))
        {
            return false;
        }

        hello.UserId = userId;
        hello.ClientUnixMilliseconds = clientTime;
        hello.ProactiveResetEnabled = parts.Length < 4 || parts[3] != "0";
        hello.DeviceKey = parts.Length >= 5 ? Unescape(parts[4]) : string.Empty;
        hello.DeviceName = parts.Length >= 6 ? Unescape(parts[5]) : string.Empty;
        hello.ClientSessionId = parts.Length >= 7 ? Unescape(parts[6]) : string.Empty;
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

public struct LiveVRClientAssignmentMessage
{
    public int UserId;
    public int ExpectedUserCount;
    public bool ProactiveResetEnabled;
    public long HostUnixMilliseconds;
    public string HostRunId;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "ASSIGN|{0}|{1}|{2}|{3}|{4}",
            UserId,
            ExpectedUserCount,
            ProactiveResetEnabled ? 1 : 0,
            HostUnixMilliseconds,
            Escape(HostRunId));
    }

    public static bool TryParse(string message, out LiveVRClientAssignmentMessage assignment)
    {
        assignment = default(LiveVRClientAssignmentMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if ((parts.Length != 4 && parts.Length != 6) || parts[0] != "ASSIGN")
            return false;

        int userId;
        int expectedUserCount;
        int proactiveResetFlag;
        long hostTime;

        if (parts.Length == 4)
        {
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out proactiveResetFlag) ||
                !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
            {
                return false;
            }

            expectedUserCount = 0;
            assignment.HostRunId = string.Empty;
        }
        else if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
                 !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedUserCount) ||
                 !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out proactiveResetFlag) ||
                 !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        assignment.UserId = userId;
        assignment.ExpectedUserCount = expectedUserCount;
        assignment.ProactiveResetEnabled = proactiveResetFlag != 0;
        assignment.HostUnixMilliseconds = hostTime;
        if (parts.Length == 6)
            assignment.HostRunId = Unescape(parts[5]);
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

public struct LiveVRClientAssignmentRejectMessage
{
    public string Reason;
    public int ExpectedUserCount;
    public long HostUnixMilliseconds;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "ASSIGN_REJECT|{0}|{1}|{2}",
            Escape(Reason),
            ExpectedUserCount,
            HostUnixMilliseconds);
    }

    public static bool TryParse(string message, out LiveVRClientAssignmentRejectMessage rejection)
    {
        rejection = default(LiveVRClientAssignmentRejectMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 4 || parts[0] != "ASSIGN_REJECT")
            return false;

        int expectedUserCount;
        long hostTime;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedUserCount) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        rejection.Reason = Unescape(parts[1]);
        rejection.ExpectedUserCount = expectedUserCount;
        rejection.HostUnixMilliseconds = hostTime;
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

public struct LiveVRStateMessage
{
    public LiveVRExperimentState ExperimentState;
    public long HostUnixMilliseconds;
    public int TargetSeed;
    public int TargetSeedVersion;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "STATE|{0}|{1}|{2}|{3}",
            ExperimentState,
            HostUnixMilliseconds,
            TargetSeed,
            TargetSeedVersion);
    }

    public static bool TryParse(string message, out LiveVRStateMessage stateMessage)
    {
        stateMessage = default(LiveVRStateMessage);
        stateMessage.TargetSeed = int.MinValue;
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if ((parts.Length != 3 && parts.Length != 5) || parts[0] != "STATE")
            return false;

        LiveVRExperimentState state;
        long hostTime;
        if (!Enum.TryParse(parts[1], true, out state) ||
            !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        stateMessage.ExperimentState = state;
        stateMessage.HostUnixMilliseconds = hostTime;
        if (parts.Length >= 5)
        {
            int targetSeed;
            int targetSeedVersion;
            if (int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetSeed) &&
                int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetSeedVersion))
            {
                stateMessage.TargetSeed = targetSeed;
                stateMessage.TargetSeedVersion = targetSeedVersion;
            }
        }
        return true;
    }
}

public struct LiveVRCalibrateCenterMessage
{
    public int UserId;
    public int EventId;
    public long HostUnixMilliseconds;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "CALIBRATE_CENTER|{0}|{1}|{2}",
            UserId,
            EventId,
            HostUnixMilliseconds);
    }

    public static bool TryParse(string message, out LiveVRCalibrateCenterMessage calibrate)
    {
        calibrate = default(LiveVRCalibrateCenterMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 4 || parts[0] != "CALIBRATE_CENTER")
            return false;

        int userId;
        int eventId;
        long hostTime;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out eventId) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        calibrate.UserId = userId;
        calibrate.EventId = eventId;
        calibrate.HostUnixMilliseconds = hostTime;
        return true;
    }
}

public struct LiveVRClearCalibrationMessage
{
    public int UserId;
    public int EventId;
    public long HostUnixMilliseconds;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "CLEAR_CALIBRATION|{0}|{1}|{2}",
            UserId,
            EventId,
            HostUnixMilliseconds);
    }

    public static bool TryParse(string message, out LiveVRClearCalibrationMessage clear)
    {
        clear = default(LiveVRClearCalibrationMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 4 || parts[0] != "CLEAR_CALIBRATION")
            return false;

        int userId;
        int eventId;
        long hostTime;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out eventId) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        clear.UserId = userId;
        clear.EventId = eventId;
        clear.HostUnixMilliseconds = hostTime;
        return true;
    }
}

public struct LiveVRResetPromptMessage
{
    public int UserId;
    public string ResetType;
    public Vector2 DirectionHint;
    public bool HasTargetPosition;
    public Vector2 TargetPosition;
    public bool HasTurnInstruction;
    public int TurnDirectionSign;
    public float TotalTurnDegrees;
    public float RemainingTurnDegrees;
    public float Progress01;
    public int EventId;
    public long HostUnixMilliseconds;

    public string ToNetworkMessage()
    {
        if (HasTargetPosition)
        {
            if (HasTurnInstruction)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "RESET_PROMPT|{0}|{1}|{2:R}|{3:R}|{4}|{5}|{6:R}|{7:R}|{8}|{9:R}|{10:R}|{11:R}",
                    UserId,
                    SanitizeToken(ResetType),
                    DirectionHint.x,
                    DirectionHint.y,
                    EventId,
                    HostUnixMilliseconds,
                    TargetPosition.x,
                    TargetPosition.y,
                    TurnDirectionSign,
                    TotalTurnDegrees,
                    RemainingTurnDegrees,
                    Progress01);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "RESET_PROMPT|{0}|{1}|{2:R}|{3:R}|{4}|{5}|{6:R}|{7:R}",
                UserId,
                SanitizeToken(ResetType),
                DirectionHint.x,
                DirectionHint.y,
                EventId,
                HostUnixMilliseconds,
                TargetPosition.x,
                TargetPosition.y);
        }

        if (HasTurnInstruction)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "RESET_PROMPT|{0}|{1}|{2:R}|{3:R}|{4}|{5}|{6}|{7:R}|{8:R}|{9:R}",
                UserId,
                SanitizeToken(ResetType),
                DirectionHint.x,
                DirectionHint.y,
                EventId,
                HostUnixMilliseconds,
                TurnDirectionSign,
                TotalTurnDegrees,
                RemainingTurnDegrees,
                Progress01);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "RESET_PROMPT|{0}|{1}|{2:R}|{3:R}|{4}|{5}",
            UserId,
            SanitizeToken(ResetType),
            DirectionHint.x,
            DirectionHint.y,
            EventId,
            HostUnixMilliseconds);
    }

    public static bool TryParse(string message, out LiveVRResetPromptMessage resetPrompt)
    {
        resetPrompt = default(LiveVRResetPromptMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if ((parts.Length != 7 && parts.Length != 9 && parts.Length != 11 && parts.Length != 13) || parts[0] != "RESET_PROMPT")
            return false;

        int userId;
        float x;
        float y;
        float targetX = 0.0f;
        float targetY = 0.0f;
        int turnDirectionSign = 0;
        float totalTurnDegrees = 0.0f;
        float remainingTurnDegrees = 0.0f;
        float progress01 = 0.0f;
        int eventId;
        long hostTime;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
            !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out eventId) ||
            !long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        if (parts.Length == 9 &&
            (!float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out targetX) ||
             !float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out targetY)))
        {
            return false;
        }

        bool hasTargetPosition = parts.Length == 9 || parts.Length == 13;
        bool hasTurnInstruction = parts.Length == 11 || parts.Length == 13;
        int turnStartIndex = parts.Length == 13 ? 9 : 7;

        if (parts.Length == 13 &&
            (!float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out targetX) ||
             !float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out targetY)))
        {
            return false;
        }

        if (hasTurnInstruction &&
            (!int.TryParse(parts[turnStartIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out turnDirectionSign) ||
             !float.TryParse(parts[turnStartIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out totalTurnDegrees) ||
             !float.TryParse(parts[turnStartIndex + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out remainingTurnDegrees) ||
             !float.TryParse(parts[turnStartIndex + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out progress01)))
        {
            return false;
        }

        resetPrompt.UserId = userId;
        resetPrompt.ResetType = parts[2];
        resetPrompt.DirectionHint = new Vector2(x, y);
        resetPrompt.HasTargetPosition = hasTargetPosition;
        resetPrompt.TargetPosition = new Vector2(targetX, targetY);
        resetPrompt.HasTurnInstruction = hasTurnInstruction;
        resetPrompt.TurnDirectionSign = turnDirectionSign;
        resetPrompt.TotalTurnDegrees = totalTurnDegrees;
        resetPrompt.RemainingTurnDegrees = remainingTurnDegrees;
        resetPrompt.Progress01 = progress01;
        resetPrompt.EventId = eventId;
        resetPrompt.HostUnixMilliseconds = hostTime;
        return true;
    }

    private static string SanitizeToken(string value)
    {
        return string.IsNullOrEmpty(value) ? "UNKNOWN" : value.Replace("|", "_");
    }
}

public struct LiveVRResetStartMessage
{
    public int UserId;
    public string ResetType;
    public Vector2 DirectionHint;
    public bool HasTargetPosition;
    public Vector2 TargetPosition;
    public int TurnDirectionSign;
    public float PhysicalTurnDegrees;
    public float InjectedTurnDegrees;
    public int EventId;
    public long HostUnixMilliseconds;

    public string ToNetworkMessage()
    {
        if (HasTargetPosition)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "RESET_START|{0}|{1}|{2:R}|{3:R}|{4}|{5}|{6:R}|{7:R}|{8}|{9:R}|{10:R}",
                UserId,
                SanitizeToken(ResetType),
                DirectionHint.x,
                DirectionHint.y,
                EventId,
                HostUnixMilliseconds,
                TargetPosition.x,
                TargetPosition.y,
                TurnDirectionSign,
                PhysicalTurnDegrees,
                InjectedTurnDegrees);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "RESET_START|{0}|{1}|{2:R}|{3:R}|{4}|{5}|{6}|{7:R}|{8:R}",
            UserId,
            SanitizeToken(ResetType),
            DirectionHint.x,
            DirectionHint.y,
            EventId,
            HostUnixMilliseconds,
            TurnDirectionSign,
            PhysicalTurnDegrees,
            InjectedTurnDegrees);
    }

    public static bool TryParse(string message, out LiveVRResetStartMessage resetStart)
    {
        resetStart = default(LiveVRResetStartMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if ((parts.Length != 10 && parts.Length != 12) || parts[0] != "RESET_START")
            return false;

        int userId;
        float x;
        float y;
        int eventId;
        long hostTime;
        float targetX = 0.0f;
        float targetY = 0.0f;
        int turnDirectionSign;
        float physicalTurnDegrees;
        float injectedTurnDegrees;

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
            !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out eventId) ||
            !long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        bool hasTargetPosition = parts.Length == 12;
        int turnStartIndex = hasTargetPosition ? 9 : 7;
        if (hasTargetPosition &&
            (!float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out targetX) ||
             !float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out targetY)))
        {
            return false;
        }

        if (!int.TryParse(parts[turnStartIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out turnDirectionSign) ||
            !float.TryParse(parts[turnStartIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out physicalTurnDegrees) ||
            !float.TryParse(parts[turnStartIndex + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out injectedTurnDegrees))
        {
            return false;
        }

        resetStart.UserId = userId;
        resetStart.ResetType = parts[2];
        resetStart.DirectionHint = new Vector2(x, y);
        resetStart.HasTargetPosition = hasTargetPosition;
        resetStart.TargetPosition = new Vector2(targetX, targetY);
        resetStart.TurnDirectionSign = turnDirectionSign;
        resetStart.PhysicalTurnDegrees = physicalTurnDegrees;
        resetStart.InjectedTurnDegrees = injectedTurnDegrees;
        resetStart.EventId = eventId;
        resetStart.HostUnixMilliseconds = hostTime;
        return true;
    }

    private static string SanitizeToken(string value)
    {
        return string.IsNullOrEmpty(value) ? "UNKNOWN" : value.Replace("|", "_");
    }
}

public struct LiveVRResetDoneMessage
{
    public int UserId;
    public int EventId;
    public long ClientUnixMilliseconds;
    public float FinalPhysicalYawDegrees;
    public float FinalInjectedTurnDegrees;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "RESET_DONE|{0}|{1}|{2}|{3:R}|{4:R}",
            UserId,
            EventId,
            ClientUnixMilliseconds,
            FinalPhysicalYawDegrees,
            FinalInjectedTurnDegrees);
    }

    public static bool TryParse(string message, out LiveVRResetDoneMessage resetDone)
    {
        resetDone = default(LiveVRResetDoneMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 6 || parts[0] != "RESET_DONE")
            return false;

        int userId;
        int eventId;
        long clientTime;
        float finalYaw;
        float finalInjected;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out eventId) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out clientTime) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out finalYaw) ||
            !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out finalInjected))
        {
            return false;
        }

        resetDone.UserId = userId;
        resetDone.EventId = eventId;
        resetDone.ClientUnixMilliseconds = clientTime;
        resetDone.FinalPhysicalYawDegrees = finalYaw;
        resetDone.FinalInjectedTurnDegrees = finalInjected;
        return true;
    }
}

public struct LiveVRTargetReachedMessage
{
    public int UserId;
    public int TargetIndex;
    public long ClientUnixMilliseconds;
    public Vector2 TargetPosition;
    public Vector2 UserPosition;
    public float DistanceMeters;
    public float CumulativeDistanceMeters;
    public bool RunComplete;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "TARGET_REACHED|{0}|{1}|{2}|{3:R}|{4:R}|{5:R}|{6:R}|{7:R}|{8:R}|{9}",
            UserId,
            TargetIndex,
            ClientUnixMilliseconds,
            TargetPosition.x,
            TargetPosition.y,
            UserPosition.x,
            UserPosition.y,
            DistanceMeters,
            CumulativeDistanceMeters,
            RunComplete ? 1 : 0);
    }

    public static bool TryParse(string message, out LiveVRTargetReachedMessage reached)
    {
        reached = default(LiveVRTargetReachedMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if ((parts.Length != 9 && parts.Length != 11) || parts[0] != "TARGET_REACHED")
            return false;

        int userId;
        int targetIndex;
        long clientTime;
        float targetX;
        float targetY;
        float userX;
        float userY;
        float distance;
        float cumulativeDistance = 0.0f;
        int runComplete = 1;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetIndex) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out clientTime) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out targetX) ||
            !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out targetY) ||
            !float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out userX) ||
            !float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out userY) ||
            !float.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out distance))
        {
            return false;
        }

        if (parts.Length == 11 &&
            (!float.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out cumulativeDistance) ||
             !int.TryParse(parts[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out runComplete)))
        {
            return false;
        }

        reached.UserId = userId;
        reached.TargetIndex = targetIndex;
        reached.ClientUnixMilliseconds = clientTime;
        reached.TargetPosition = new Vector2(targetX, targetY);
        reached.UserPosition = new Vector2(userX, userY);
        reached.DistanceMeters = distance;
        reached.CumulativeDistanceMeters = parts.Length == 11 ? cumulativeDistance : distance;
        reached.RunComplete = runComplete != 0;
        return true;
    }
}

public struct LiveVRResetEndMessage
{
    public int UserId;
    public int EventId;
    public long HostUnixMilliseconds;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "RESET_END|{0}|{1}|{2}",
            UserId,
            EventId,
            HostUnixMilliseconds);
    }

    public static bool TryParse(string message, out LiveVRResetEndMessage resetEnd)
    {
        resetEnd = default(LiveVRResetEndMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if (parts.Length != 4 || parts[0] != "RESET_END")
            return false;

        int userId;
        int eventId;
        long hostTime;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out eventId) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime))
        {
            return false;
        }

        resetEnd.UserId = userId;
        resetEnd.EventId = eventId;
        resetEnd.HostUnixMilliseconds = hostTime;
        return true;
    }
}

public struct LiveVRVirtualPoseMessage
{
    public int UserId;
    public uint Sequence;
    public long HostUnixMilliseconds;
    public Vector2 VirtualPosition;
    public float VirtualYawDegrees;
    public float InjectedYawDeltaDegrees;
    public GainType GainType;
    public float GainRateDegreesPerSecond;
    public float GainValidSeconds;

    public string ToNetworkMessage()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "VIRTUAL_POSE|{0}|{1}|{2}|{3:R}|{4:R}|{5:R}|{6:R}|{7}|{8:R}|{9:R}",
            UserId,
            Sequence,
            HostUnixMilliseconds,
            VirtualPosition.x,
            VirtualPosition.y,
            VirtualYawDegrees,
            InjectedYawDeltaDegrees,
            GainType,
            GainRateDegreesPerSecond,
            GainValidSeconds);
    }

    public static bool TryParse(string message, out LiveVRVirtualPoseMessage virtualPose)
    {
        virtualPose = default(LiveVRVirtualPoseMessage);
        if (string.IsNullOrEmpty(message))
            return false;

        string[] parts = message.Split('|');
        if ((parts.Length != 7 && parts.Length != 9 && parts.Length != 11) || parts[0] != "VIRTUAL_POSE")
            return false;

        int userId;
        uint sequence;
        long hostTime;
        float x;
        float y;
        float yaw;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId) ||
            !uint.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out hostTime) ||
            !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
            !float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
            !float.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out yaw))
        {
            return false;
        }

        virtualPose.UserId = userId;
        virtualPose.Sequence = sequence;
        virtualPose.HostUnixMilliseconds = hostTime;
        virtualPose.VirtualPosition = new Vector2(x, y);
        virtualPose.VirtualYawDegrees = yaw;
        virtualPose.InjectedYawDeltaDegrees = 0.0f;
        virtualPose.GainType = GainType.Undefined;
        virtualPose.GainRateDegreesPerSecond = 0.0f;
        virtualPose.GainValidSeconds = 0.0f;
        if (parts.Length == 9)
        {
            float injectedYaw;
            GainType gainType;
            if (float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out injectedYaw))
                virtualPose.InjectedYawDeltaDegrees = injectedYaw;

            if (Enum.TryParse(parts[8], true, out gainType))
                virtualPose.GainType = gainType;
        }
        else if (parts.Length == 11)
        {
            float injectedYaw;
            GainType gainType;
            float gainRate;
            float gainValidSeconds;
            if (float.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out injectedYaw))
                virtualPose.InjectedYawDeltaDegrees = injectedYaw;

            if (Enum.TryParse(parts[8], true, out gainType))
                virtualPose.GainType = gainType;

            if (float.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out gainRate))
                virtualPose.GainRateDegreesPerSecond = gainRate;

            if (float.TryParse(parts[10], NumberStyles.Float, CultureInfo.InvariantCulture, out gainValidSeconds))
                virtualPose.GainValidSeconds = gainValidSeconds;
        }
        return true;
    }
}
