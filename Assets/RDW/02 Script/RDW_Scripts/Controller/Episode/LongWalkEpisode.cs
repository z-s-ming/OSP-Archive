using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LongWalkEpisode : Episode
{
    public LongWalkEpisode() : base() { }

    public LongWalkEpisode(int episodeLength) : base(episodeLength) { }

    protected override void GenerateEpisode(Transform2D virtualUserTransform, Space2D virtualSpace, Object2D virtualUser)
    {
        Vector2 userPosition = virtualUserTransform.localPosition;
        float distance = 12.0f;

        // Prefer a long forward target, but keep straight-line visibility to avoid crossing obstacles.
        for (int i = 0; i < 36; i++)
        {
            float angle = -180f + (i * 10f);
            Vector2 sampleForward = Utility.RotateVector2(virtualUserTransform.forward, angle);
            Vector2 candidate = userPosition + sampleForward * distance;
            if (IsValidTargetCandidate(virtualSpace, userPosition, candidate, 0.5f))
            {
                currentTargetPosition = candidate;
                return;
            }
        }

        // Fallback: keep user in place when no valid long-walk target is available.
        currentTargetPosition = userPosition;
    }
}
