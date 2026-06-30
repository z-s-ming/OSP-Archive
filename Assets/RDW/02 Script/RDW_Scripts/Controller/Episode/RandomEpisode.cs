using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomEpisode : Episode
{
    private const float MinFallbackSegmentDistance = 0.5f;
    private const float FallbackDistanceStep = 0.25f;
    private const float FallbackAngleStep = 10.0f;
    private const float DefaultTargetDistancePerUser = 200.0f;
    private const float TargetInsideBound = 0.5f;
    private const int MaxSamplingAttempts = 10000;
    private readonly List<float> turnAngles = new List<float>();

    protected virtual float MinSegmentDistance { get { return 4.0f; } }
    protected virtual float MaxSegmentDistance { get { return 8.0f; } }
    protected virtual string EpisodeName { get { return "RandomEpisode"; } }

    public RandomEpisode() : base()
    {
        episodeLength = int.MaxValue;
        InitializeTurnAngles();
    }

    public RandomEpisode(int episodeLength) : base(episodeLength)
    {
        this.episodeLength = int.MaxValue;
        InitializeTurnAngles();
    }

    private float generatedPathDistance = 0.0f;

    protected override void GenerateEpisode(Transform2D virtualUserTransform, Space2D virtualSpace, Object2D virtualUser)
    {
        ResetGeneratedDistanceIfNeeded();

        Vector2 userPosition = virtualUserTransform.localPosition;
        float targetDistance = GetTargetDistancePerUser();
        bool useGlobalEpisodeTermination = _GCM.GlobalCoordinationManager.instance != null;

        if (!useGlobalEpisodeTermination && generatedPathDistance >= targetDistance)
        {
            episodeLength = currentEpisodeIndex;
            currentTargetPosition = userPosition;
            return;
        }

        for (int attempt = 0; attempt < MaxSamplingAttempts; attempt++)
        {
            float angle = turnAngles[Random.Range(0, turnAngles.Count)];
            float distance = Utility.sampleUniform(MinSegmentDistance, MaxSegmentDistance);
            Vector2 sampleForward = Utility.RotateVector2(virtualUserTransform.forward, angle);
            Vector2 samplingPosition = userPosition + sampleForward * distance;

            if (!IsValidTargetCandidate(virtualSpace, userPosition, samplingPosition, TargetInsideBound))
                continue;

            generatedPathDistance += distance;
            currentTargetPosition = samplingPosition;

            if (!useGlobalEpisodeTermination && generatedPathDistance >= targetDistance)
                episodeLength = currentEpisodeIndex + 1;

            return;
        }

        if (TryFindDeterministicTarget(
            virtualSpace,
            userPosition,
            virtualUserTransform.forward,
            MinSegmentDistance,
            MaxSegmentDistance,
            out Vector2 deterministicTarget,
            out float deterministicDistance))
        {
            generatedPathDistance += deterministicDistance;
            currentTargetPosition = deterministicTarget;

            if (!useGlobalEpisodeTermination && generatedPathDistance >= targetDistance)
                episodeLength = currentEpisodeIndex + 1;

            return;
        }

        if (TryFindDeterministicTarget(
            virtualSpace,
            userPosition,
            virtualUserTransform.forward,
            MinFallbackSegmentDistance,
            MinSegmentDistance - FallbackDistanceStep,
            out Vector2 fallbackTarget,
            out float fallbackDistance))
        {
            Debug.LogWarning(string.Format(
                "{0} failed to sample a valid {1:F0}-{2:F0}m target. Using a shorter reachable target instead.",
                EpisodeName,
                MinSegmentDistance,
                MaxSegmentDistance));
            generatedPathDistance += fallbackDistance;
            currentTargetPosition = fallbackTarget;

            if (!useGlobalEpisodeTermination && generatedPathDistance >= targetDistance)
                episodeLength = currentEpisodeIndex + 1;

            return;
        }

        Debug.LogWarning(EpisodeName + " failed to find any reachable target. Ending this user's episode path.");
        episodeLength = currentEpisodeIndex;
        currentTargetPosition = userPosition;
    }

    private bool TryFindDeterministicTarget(
        Space2D virtualSpace,
        Vector2 userPosition,
        Vector2 forward,
        float minDistance,
        float maxDistance,
        out Vector2 targetPosition,
        out float targetDistance)
    {
        for (float distance = maxDistance; distance >= minDistance; distance -= FallbackDistanceStep)
        {
            for (float angle = 0.0f; angle < 360.0f; angle += FallbackAngleStep)
            {
                Vector2 sampleForward = Utility.RotateVector2(forward, angle);
                Vector2 candidate = userPosition + sampleForward * distance;

                if (!IsValidTargetCandidate(virtualSpace, userPosition, candidate, TargetInsideBound))
                    continue;

                targetPosition = candidate;
                targetDistance = distance;
                return true;
            }
        }

        targetPosition = userPosition;
        targetDistance = 0.0f;
        return false;
    }

    private void ResetGeneratedDistanceIfNeeded()
    {
        if (currentEpisodeIndex == 0)
        {
            generatedPathDistance = 0.0f;
            episodeLength = int.MaxValue;
        }
    }

    private float GetTargetDistancePerUser()
    {
        if (_GCM.GlobalCoordinationManager.instance != null)
            return Mathf.Max(0.0f, _GCM.GlobalCoordinationManager.instance.TargetDistancePerUser);

        return DefaultTargetDistancePerUser;
    }

    private void InitializeTurnAngles()
    {
        turnAngles.Clear();

        for (int angle = -90; angle <= 90; angle += 10)
        {
            turnAngles.Add(angle);
        }
    }

    public static float RandomGaussian(float minValue = 0.0f, float maxValue = 1.0f)
    {
        float u, v, S;

        do
        {
            u = 2.0f * UnityEngine.Random.value - 1.0f;
            v = 2.0f * UnityEngine.Random.value - 1.0f;
            S = u * u + v * v;
        }
        while (S >= 1.0f);

        // Standard Normal Distribution
        float std = u * Mathf.Sqrt(-2.0f * Mathf.Log(S) / S);

        // Normal Distribution centered between the min and max value
        // and clamped following the "three-sigma rule"
        float mean = (minValue + maxValue) / 2.0f;
        float sigma = (maxValue - mean) / 3.0f;
        return Mathf.Clamp(std * sigma + mean, minValue, maxValue);
    }
}
