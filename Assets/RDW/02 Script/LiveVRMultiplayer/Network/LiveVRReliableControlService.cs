using System;
using System.Collections.Generic;
using UnityEngine;

public class LiveVRReliableControlService
{
    private class PendingMessage
    {
        public string MessageId;
        public int TargetUserId;
        public string MessageType;
        public string EnvelopeMessage;
        public float NextSendTime;
        public float CreatedTime;
        public int RetryCount;
        public bool TimedOut;
    }

    private readonly Dictionary<string, PendingMessage> pendingByMessageId = new Dictionary<string, PendingMessage>();
    private readonly HashSet<string> timedOutMessageTypes = new HashSet<string>();
    private readonly List<string> scratchIds = new List<string>();
    private readonly object pendingLock = new object();
    private Action<int, string> sendToUser;
    private Action<string> logWarning;
    private float retryIntervalSeconds = 0.25f;
    private float timeoutSeconds = 3.0f;
    private int nextMessageId;

    public int PendingCount
    {
        get
        {
            lock (pendingLock)
                return pendingByMessageId.Count;
        }
    }
    public int RetryCountTotal { get; private set; }
    public int TimeoutCountTotal { get; private set; }

    public void Configure(Action<int, string> sendToUserAction, Action<string> logWarningAction, float retryInterval, float timeout)
    {
        sendToUser = sendToUserAction;
        logWarning = logWarningAction;
        retryIntervalSeconds = Mathf.Max(0.05f, retryInterval);
        timeoutSeconds = Mathf.Max(retryIntervalSeconds, timeout);
    }

    public string SendReliableToUser(
        int targetUserId,
        string messageType,
        string payload,
        LiveVRProtocolVersionContext context)
    {
        if (sendToUser == null)
            return string.Empty;

        string messageId = GenerateMessageId(messageType, targetUserId);
        LiveVRControlEnvelope envelope = LiveVRControlEnvelope.Create(messageId, messageType, targetUserId, context, payload);
        PendingMessage pending = new PendingMessage
        {
            MessageId = messageId,
            TargetUserId = targetUserId,
            MessageType = messageType,
            EnvelopeMessage = envelope.ToNetworkMessage(),
            CreatedTime = Time.unscaledTime,
            NextSendTime = 0.0f
        };
        lock (pendingLock)
        {
            timedOutMessageTypes.Remove(messageType);
            pendingByMessageId[messageId] = pending;
        }
        SendPending(pending);
        return messageId;
    }

    public void SendReliableToUsers(IEnumerable<int> targetUserIds, string messageType, string payload, LiveVRProtocolVersionContext context)
    {
        if (targetUserIds == null)
            return;

        foreach (int userId in targetUserIds)
            SendReliableToUser(userId, messageType, payload, context);
    }

    public void Acknowledge(string messageId)
    {
        if (string.IsNullOrEmpty(messageId))
            return;

        lock (pendingLock)
            pendingByMessageId.Remove(messageId);
    }

    public bool HasPendingType(string messageType)
    {
        lock (pendingLock)
        {
            foreach (PendingMessage pending in pendingByMessageId.Values)
            {
                if (string.Equals(pending.MessageType, messageType, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    public bool HasTimedOutType(string messageType)
    {
        lock (pendingLock)
            return timedOutMessageTypes.Contains(messageType);
    }

    public void Tick()
    {
        if (sendToUser == null)
            return;

        lock (pendingLock)
        {
            if (pendingByMessageId.Count == 0)
                return;

            scratchIds.Clear();
            foreach (KeyValuePair<string, PendingMessage> pair in pendingByMessageId)
            {
                PendingMessage pending = pair.Value;
                if (pending.TimedOut)
                {
                    scratchIds.Add(pair.Key);
                    continue;
                }

                if (Time.unscaledTime - pending.CreatedTime > timeoutSeconds)
                {
                    pending.TimedOut = true;
                    TimeoutCountTotal++;
                    timedOutMessageTypes.Add(pending.MessageType);
                    if (logWarning != null)
                        logWarning(string.Format("[LiveVR] Reliable control timed out: type={0} target={1} messageId={2}", pending.MessageType, pending.TargetUserId, pending.MessageId));
                    scratchIds.Add(pair.Key);
                    continue;
                }

                if (Time.unscaledTime >= pending.NextSendTime)
                    SendPending(pending);
            }

            for (int i = 0; i < scratchIds.Count; i++)
                pendingByMessageId.Remove(scratchIds[i]);
        }
    }

    public void Clear()
    {
        lock (pendingLock)
        {
            pendingByMessageId.Clear();
            timedOutMessageTypes.Clear();
        }
    }

    private void SendPending(PendingMessage pending)
    {
        sendToUser(pending.TargetUserId, pending.EnvelopeMessage);
        pending.RetryCount++;
        if (pending.RetryCount > 1)
            RetryCountTotal++;
        pending.NextSendTime = Time.unscaledTime + retryIntervalSeconds;
    }

    private string GenerateMessageId(string messageType, int targetUserId)
    {
        nextMessageId++;
        return string.Format("{0}_{1}_{2}_{3}", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), targetUserId, messageType, nextMessageId);
    }
}
