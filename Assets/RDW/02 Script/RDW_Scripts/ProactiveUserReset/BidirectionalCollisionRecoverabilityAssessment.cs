using UnityEngine;

public struct BidirectionalCollisionRecoverabilityAssessment
{
    public bool IsAdjacentCellCandidate;
    public bool IsApproachingCandidate;
    public bool Recoverable;
    public bool IsIrrecoverable;
    public bool IsApproaching;
    public bool IsPersistent;
    public bool RiskConfirmed;
    public int IrrecoverableStreak;
    public int PersistentStreak;
    public float CurrentDistance;
    public float PredictedMinDistance;
    public float ClosingSpeedNow;
    public float MaxSeparationMargin;
    public float MarginLL;
    public float MarginLR;
    public float MarginRL;
    public float MarginRR;
    public int BestSigmaA;
    public int BestSigmaB;
    public float WorstTimeOnBestPair;
    public Vector2 PositionA;
    public Vector2 PositionB;
}
