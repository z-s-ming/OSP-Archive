using System.Collections.Generic;
using UnityEngine;

public static class BidirectionalCollisionDebugVisualizer
{
    private const float SAMPLE_DURATION_SECONDS = 1.5f;
    private const float RENDER_RETENTION_SECONDS = 3.0f;

    private static readonly Dictionary<long, TraceSession> sessions = new Dictionary<long, TraceSession>();
    private static bool enabled = true;

    private sealed class TraceSession
    {
        public RedirectedUnit UnitA;
        public RedirectedUnit UnitB;
        public float SamplingEndTime;
        public float ExpireTime;
        public readonly List<Vector3> PointsA = new List<Vector3>(128);
        public readonly List<Vector3> PointsB = new List<Vector3>(128);
    }

    public static void SetEnabled(bool isEnabled)
    {
        enabled = isEnabled;
        if (!enabled)
            sessions.Clear();
    }

    public static bool IsEnabled()
    {
        return enabled;
    }

    public static void ResetSession()
    {
        sessions.Clear();
    }

    public static void TryStartTrace(
        RedirectedUnit unitA,
        RedirectedUnit unitB,
        BidirectionalCollisionRecoverabilityAssessment assessment)
    {
        if (!enabled || unitA == null || unitB == null || unitA.GetRealUser() == null || unitB.GetRealUser() == null)
            return;

        int idA = unitA.GetID();
        int idB = unitB.GetID();
        long pairKey = BuildPairKey(idA, idB);
        if (sessions.ContainsKey(pairKey))
            return;

        float now = Time.time;
        TraceSession session = new TraceSession
        {
            UnitA = unitA,
            UnitB = unitB,
            SamplingEndTime = now + SAMPLE_DURATION_SECONDS,
            ExpireTime = now + SAMPLE_DURATION_SECONDS + RENDER_RETENTION_SECONDS
        };

        Vector2 pA = unitA.GetRealUser().transform2D.localPosition;
        Vector2 pB = unitB.GetRealUser().transform2D.localPosition;
        session.PointsA.Add(new Vector3(pA.x, 0.03f, pA.y));
        session.PointsB.Add(new Vector3(pB.x, 0.03f, pB.y));
        sessions[pairKey] = session;

        Debug.Log($"[BiCollisionDebug] pair ({Mathf.Min(idA, idB)}, {Mathf.Max(idA, idB)}) tracked for {SAMPLE_DURATION_SECONDS:F1}s. Recoverable={assessment.Recoverable}, RiskConfirmed={assessment.RiskConfirmed}");
    }

    public static void Tick()
    {
        if (!enabled || sessions.Count == 0)
            return;

        float now = Time.time;
        List<long> expiredKeys = null;

        foreach (KeyValuePair<long, TraceSession> kvp in sessions)
        {
            TraceSession session = kvp.Value;
            if (session == null || session.UnitA == null || session.UnitB == null || session.UnitA.GetRealUser() == null || session.UnitB.GetRealUser() == null)
            {
                if (expiredKeys == null) expiredKeys = new List<long>();
                expiredKeys.Add(kvp.Key);
                continue;
            }

            if (now <= session.SamplingEndTime)
            {
                Vector2 pA = session.UnitA.GetRealUser().transform2D.localPosition;
                Vector2 pB = session.UnitB.GetRealUser().transform2D.localPosition;
                session.PointsA.Add(new Vector3(pA.x, 0.03f, pA.y));
                session.PointsB.Add(new Vector3(pB.x, 0.03f, pB.y));
            }

            if (now > session.ExpireTime)
            {
                if (expiredKeys == null) expiredKeys = new List<long>();
                expiredKeys.Add(kvp.Key);
            }

            DrawDebugLines(session);
        }

        if (expiredKeys == null)
            return;

        for (int i = 0; i < expiredKeys.Count; i++)
        {
            sessions.Remove(expiredKeys[i]);
        }
    }

    public static void DrawGizmos()
    {
        if (!enabled || sessions.Count == 0)
            return;

        foreach (TraceSession session in sessions.Values)
        {
            if (session == null)
                continue;

            DrawPolyline(session.PointsA, new Color(0.2f, 0.8f, 1.0f, 0.95f));
            DrawPolyline(session.PointsB, new Color(1.0f, 0.55f, 0.2f, 0.95f));

            DrawHeadMarker(session.PointsA, new Color(0.0f, 1.0f, 1.0f, 1.0f));
            DrawHeadMarker(session.PointsB, new Color(1.0f, 0.7f, 0.3f, 1.0f));

            if (session.PointsA.Count > 0 && session.PointsB.Count > 0)
            {
                Gizmos.color = new Color(1.0f, 0.2f, 0.2f, 0.8f);
                Gizmos.DrawLine(session.PointsA[session.PointsA.Count - 1], session.PointsB[session.PointsB.Count - 1]);
            }
        }
    }

    private static void DrawPolyline(List<Vector3> points, Color color)
    {
        if (points == null || points.Count < 2)
            return;

        Gizmos.color = color;
        for (int i = 1; i < points.Count; i++)
        {
            Gizmos.DrawLine(points[i - 1], points[i]);
        }
    }

    private static void DrawHeadMarker(List<Vector3> points, Color color)
    {
        if (points == null || points.Count == 0)
            return;

        Gizmos.color = color;
        Gizmos.DrawSphere(points[points.Count - 1], 0.045f);
    }

    private static void DrawDebugLines(TraceSession session)
    {
        DrawDebugPolyline(session.PointsA, new Color(0.2f, 0.8f, 1.0f, 0.95f));
        DrawDebugPolyline(session.PointsB, new Color(1.0f, 0.55f, 0.2f, 0.95f));
    }

    private static void DrawDebugPolyline(List<Vector3> points, Color color)
    {
        if (points == null || points.Count < 2)
            return;

        for (int i = 1; i < points.Count; i++)
        {
            Debug.DrawLine(points[i - 1], points[i], color, 0.0f, false);
        }
    }

    private static long BuildPairKey(int unitAId, int unitBId)
    {
        int minId = Mathf.Min(unitAId, unitBId);
        int maxId = Mathf.Max(unitAId, unitBId);
        return ((long)(uint)minId << 32) | (uint)maxId;
    }
}
