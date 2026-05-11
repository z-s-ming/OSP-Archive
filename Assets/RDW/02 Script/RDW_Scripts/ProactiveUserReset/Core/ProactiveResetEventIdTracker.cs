public static class ProactiveResetEventIdTracker
{
    private static int nextTriggerId = 1;
    private static int nextCandidateId = 1;
    private static int nextDecisionId = 1;
    private static int nextExecutionId = 1;
    private static int acceptedDecisionCount;
    private static int rejectedDecisionCount;
    private static int executedCount;

    public static int AcceptedDecisionCount { get { return acceptedDecisionCount; } }
    public static int RejectedDecisionCount { get { return rejectedDecisionCount; } }
    public static int ExecutedCount { get { return executedCount; } }

    public static void Initialize()
    {
        ResetSession();
    }

    public static void ResetSession()
    {
        nextTriggerId = 1;
        nextCandidateId = 1;
        nextDecisionId = 1;
        nextExecutionId = 1;
        acceptedDecisionCount = 0;
        rejectedDecisionCount = 0;
        executedCount = 0;
    }

    public static int NextTriggerId()
    {
        return nextTriggerId++;
    }

    public static int NextCandidateId()
    {
        return nextCandidateId++;
    }

    public static int NextDecisionId()
    {
        return nextDecisionId++;
    }

    public static int NextExecutionId()
    {
        return nextExecutionId++;
    }

    public static void RecordDecision(bool accepted)
    {
        if (accepted)
            acceptedDecisionCount++;
        else
            rejectedDecisionCount++;
    }

    public static void RecordExecution(bool executed)
    {
        if (executed)
            executedCount++;
    }
}
