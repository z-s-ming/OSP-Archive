using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class BidirectionalCollisionRecoverabilityLogger
{
    private const float EPSILON = 0.0001f;

    private static string logFilePath = string.Empty;
    private static bool headerWritten;

    public static void ResetSession()
    {
        logFilePath = string.Empty;
        headerWritten = false;
    }

    public static void TryLog(
        RedirectedUnit unitA,
        RedirectedUnit unitB,
        float safeDistance,
        float horizonSeconds,
        int sampleCount,
        string triggerTag,
        BidirectionalCollisionRecoverabilityAssessment assessment)
    {
        if (!ShouldLogCurrentFrame(out int outputEveryNFrames))
            return;

        if (unitA == null || unitB == null || unitA.GetRealUser() == null || unitB.GetRealUser() == null)
            return;

        int frameIndex = Time.frameCount;
        if (frameIndex % outputEveryNFrames != 0)
            return;

        EnsureLogFile();
        if (string.IsNullOrEmpty(logFilePath))
            return;

        Object2D userA = unitA.GetRealUser();
        Object2D userB = unitB.GetRealUser();

        Vector2 positionA = userA.transform2D.localPosition;
        Vector2 positionB = userB.transform2D.localPosition;
        Vector2 offsetAB = positionB - positionA;
        float distanceNow = offsetAB.magnitude;
        bool isIntersectNow = userA.IsIntersect(userB);

        Vector2 forwardA = NormalizeOrZero(userA.transform2D.forward);
        Vector2 forwardB = NormalizeOrZero(userB.transform2D.forward);
        Vector2 movementA = NormalizeOrZero(unitA.GetLastMovementDirection());
        Vector2 movementB = NormalizeOrZero(unitB.GetLastMovementDirection());
        float forwardDot = Vector2.Dot(forwardA, forwardB);
        float movementDot = Vector2.Dot(movementA, movementB);
        bool unitAUsedRotationGain = unitA.UsedRotationGainInLastMove();
        bool unitBUsedRotationGain = unitB.UsedRotationGainInLastMove();

        float speedA = Mathf.Max(unitA.GetResetter().GetTranslationSpeed(), 0.0f);
        float speedB = Mathf.Max(unitB.GetResetter().GetTranslationSpeed(), 0.0f);
        float closingSpeed = ResolveClosingSpeed(offsetAB, movementA * speedA, movementB * speedB);
        string collisionType = ResolveCollisionType(unitA.GetStatus(), unitB.GetStatus());

        int unitAId = unitA.GetID();
        int unitBId = unitB.GetID();
        int pairMinId = Mathf.Min(unitAId, unitBId);
        int pairMaxId = Mathf.Max(unitAId, unitBId);

        StringBuilder sb = new StringBuilder();
        if (!headerWritten)
        {
            sb.AppendLine("frame,time,pairMinId,pairMaxId,unitAId,unitBId,triggerTag,unitAStatus,unitBStatus,collisionType,unitAUsedRotationGain,unitBUsedRotationGain,distanceNow,predictedMinDistance,currentDistance,safeDistance,isIntersectNow,forwardDot,movementDot,closingSpeed,closingSpeedNow,isAdjacentCellCandidate,isApproachingCandidate,irrecoverableStreak,persistentStreak,isIrrecoverable,isApproaching,isPersistent,riskConfirmed,recoverable,maxSeparationMargin,marginLL,marginLR,marginRL,marginRR,bestSigmaA,bestSigmaB,worstTimeOnBestPair,horizonSeconds,sampleCount");
            headerWritten = true;
        }

        sb.Append(frameIndex).Append(',');
        sb.Append(ToInvariant(Time.time)).Append(',');
        sb.Append(pairMinId).Append(',');
        sb.Append(pairMaxId).Append(',');
        sb.Append(unitAId).Append(',');
        sb.Append(unitBId).Append(',');
        sb.Append(SanitizeCsv(triggerTag)).Append(',');
        sb.Append(SanitizeCsv(unitA.GetStatus())).Append(',');
        sb.Append(SanitizeCsv(unitB.GetStatus())).Append(',');
        sb.Append(SanitizeCsv(collisionType)).Append(',');
        sb.Append(unitAUsedRotationGain ? 1 : 0).Append(',');
        sb.Append(unitBUsedRotationGain ? 1 : 0).Append(',');
        sb.Append(ToInvariant(distanceNow)).Append(',');
        sb.Append(ToInvariant(assessment.PredictedMinDistance)).Append(',');
        sb.Append(ToInvariant(assessment.CurrentDistance)).Append(',');
        sb.Append(ToInvariant(safeDistance)).Append(',');
        sb.Append(isIntersectNow ? 1 : 0).Append(',');
        sb.Append(ToInvariant(forwardDot)).Append(',');
        sb.Append(ToInvariant(movementDot)).Append(',');
        sb.Append(ToInvariant(closingSpeed)).Append(',');
        sb.Append(ToInvariant(assessment.ClosingSpeedNow)).Append(',');
        sb.Append(assessment.IsAdjacentCellCandidate ? 1 : 0).Append(',');
        sb.Append(assessment.IsApproachingCandidate ? 1 : 0).Append(',');
        sb.Append(assessment.IrrecoverableStreak).Append(',');
        sb.Append(assessment.PersistentStreak).Append(',');
        sb.Append(assessment.IsIrrecoverable ? 1 : 0).Append(',');
        sb.Append(assessment.IsApproaching ? 1 : 0).Append(',');
        sb.Append(assessment.IsPersistent ? 1 : 0).Append(',');
        sb.Append(assessment.RiskConfirmed ? 1 : 0).Append(',');
        sb.Append(assessment.Recoverable ? 1 : 0).Append(',');
        sb.Append(ToInvariant(assessment.MaxSeparationMargin)).Append(',');
        sb.Append(ToInvariant(assessment.MarginLL)).Append(',');
        sb.Append(ToInvariant(assessment.MarginLR)).Append(',');
        sb.Append(ToInvariant(assessment.MarginRL)).Append(',');
        sb.Append(ToInvariant(assessment.MarginRR)).Append(',');
        sb.Append(assessment.BestSigmaA).Append(',');
        sb.Append(assessment.BestSigmaB).Append(',');
        sb.Append(ToInvariant(assessment.WorstTimeOnBestPair)).Append(',');
        sb.Append(ToInvariant(horizonSeconds)).Append(',');
        sb.Append(sampleCount);
        sb.AppendLine();

        File.AppendAllText(logFilePath, sb.ToString());
    }

    private static bool ShouldLogCurrentFrame(out int outputEveryNFrames)
    {
        outputEveryNFrames = 1;
        RDWSimulationManager manager = RDWSimulationManager.instance;
        if (manager == null || manager.simulationSetting == null)
            return false;

        if (!manager.simulationSetting.enableBiRecoverabilityLogging)
            return false;

        outputEveryNFrames = Mathf.Max(1, manager.simulationSetting.biRecoverabilityLogEveryNFrames);
        return true;
    }

    private static void EnsureLogFile()
    {
        if (!string.IsNullOrEmpty(logFilePath))
            return;

        string root = Path.Combine(Directory.GetCurrentDirectory(), "CGnA_DataLog", "biRecoverability");
        Directory.CreateDirectory(root);

        string name = $"bi_recoverability_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        logFilePath = Path.Combine(root, name);
    }

    private static float ResolveClosingSpeed(Vector2 offsetAB, Vector2 velocityA, Vector2 velocityB)
    {
        if (offsetAB.sqrMagnitude <= EPSILON)
            return 0.0f;

        Vector2 towardB = offsetAB.normalized;
        Vector2 relativeVelocity = velocityB - velocityA;
        return -Vector2.Dot(relativeVelocity, towardB);
    }

    private static Vector2 NormalizeOrZero(Vector2 vector)
    {
        if (vector.sqrMagnitude <= EPSILON)
            return Vector2.zero;

        return vector.normalized;
    }

    private static string ToInvariant(float value)
    {
        if (float.IsNaN(value))
            return "NaN";
        if (float.IsPositiveInfinity(value))
            return "Inf";
        if (float.IsNegativeInfinity(value))
            return "-Inf";

        return value.ToString("F6", CultureInfo.InvariantCulture);
    }

    private static string SanitizeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace(",", "_");
    }

    private static string ResolveCollisionType(string unitAStatus, string unitBStatus)
    {
        bool aWall = string.Equals(unitAStatus, "WALL_RESET", StringComparison.Ordinal);
        bool bWall = string.Equals(unitBStatus, "WALL_RESET", StringComparison.Ordinal);
        if (aWall || bWall)
            return "BoundaryCollision";

        bool aUser = string.Equals(unitAStatus, "USER_RESET", StringComparison.Ordinal);
        bool bUser = string.Equals(unitBStatus, "USER_RESET", StringComparison.Ordinal);
        if (aUser && bUser)
            return "UserCollisionDoubleReset";
        if (aUser || bUser)
            return "UserCollisionSingleReset";

        return "None";
    }
}
