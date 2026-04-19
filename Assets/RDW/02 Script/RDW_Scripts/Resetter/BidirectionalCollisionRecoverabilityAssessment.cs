using UnityEngine;

public struct BidirectionalCollisionRecoverabilityAssessment
{
    public bool Recoverable;
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
