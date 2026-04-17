using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ARC_Resetter_OSP : RotationResetter
{
    private const int RESET_SAMPLE_COUNT = 72; // denser sampling than ARC(20)
    private const float FIRST_CONDITION_MAX_ANGLE = 85.0f;
    private const float RESET_CLEARANCE_BOUND = 0.5f; // align with Resetter.NeedWallReset()
    private const int MAX_REPOSITION_STEPS = 256;
    private const float EPSILON = 0.0001f;

    private List<float> List_ActualDistance3Way = new List<float>();
    private List<float> List_VirtualDistance3Way = new List<float>();

    // Kept field names for compatibility/logging with existing ARC style.
    public List<float> List_ActualDistance20Way = new List<float>();
    public List<float> List_VirtualDistance20Way = new List<float>();

    private Vector2 lastInwardDirection = Vector2.up;

    public ARC_Resetter_OSP() : base()
    {
    }

    public ARC_Resetter_OSP(float translationSpeed, float rotationSpeed) : base(translationSpeed, rotationSpeed)
    {
    }

    public override string ApplyWallReset(Object2D realUser, Object2D virtualUser, Space2D realSpace)
    {
        if (isFirst)
        {
            Calc_NWay_Distances(realUser.transform2D.transform, true, RESET_SAMPLE_COUNT);

            Vector3 wallnormalvec = EstimateInwardDirection(realUser.transform2D.transform);
            Vector2 inward2D = new Vector2(wallnormalvec.x, wallnormalvec.z);
            if (inward2D.sqrMagnitude > EPSILON)
            {
                lastInwardDirection = inward2D.normalized;
            }

            float virtualDist = GetForwardVirtualDistance(virtualUser.transform2D.transform);

            int selectedIndex = SelectResetDirectionIndex(wallnormalvec, virtualDist);
            float angleStep = 360.0f / RESET_SAMPLE_COUNT;
            Vector3 selectedDir3 = Quaternion.AngleAxis(angleStep * selectedIndex, Vector3.up) * Vector3.forward;
            Vector2 selectedDir2 = new Vector2(selectedDir3.x, selectedDir3.z).normalized;

            targetAngle = Vector2.SignedAngle(realUser.transform2D.forward, selectedDir2);
            realTargetRotation = Utility.RotateVector2(realUser.transform2D.forward, targetAngle);
            virtualTargetRotation = Utility.RotateVector2(virtualUser.transform2D.forward, 360.0f);

            isFirst = false;
            maxRotTime = (Mathf.Abs(targetAngle) < EPSILON || rotationSpeed <= 0.0f) ? 0.0f : Mathf.Abs(targetAngle) / rotationSpeed;
            remainRotTime = 0.0f;
        }

        if (remainRotTime < maxRotTime)
        {
            realUser.transform2D.Rotate(Mathf.Sign(targetAngle) * rotationSpeed * Time.deltaTime);
            float safeMaxRotTime = Mathf.Max(maxRotTime, Time.fixedDeltaTime);
            virtualUser.transform2D.Rotate((360.0f / safeMaxRotTime) * Time.deltaTime);
            remainRotTime += Time.fixedDeltaTime;
        }
        else
        {
            Utility.SyncDirection(virtualUser, realUser, virtualTargetRotation, realTargetRotation);

            // Keep ARC's "step-forward after reset".
            realUser.transform2D.localPosition += realUser.transform2D.forward * translationSpeed * Time.fixedDeltaTime;

            // Corner/edge anti-loop: nudge to guaranteed safe bound used by NeedWallReset().
            int repositionSteps = 0;
            while (!realSpace.spaceObject.IsInside(realUser.transform2D.position, Space.World, RESET_CLEARANCE_BOUND) &&
                   repositionSteps < MAX_REPOSITION_STEPS)
            {
                Vector2 correctionDir = lastInwardDirection;
                if (correctionDir.sqrMagnitude <= EPSILON)
                {
                    Vector2 toCenter = realSpace.spaceObject.transform2D.position - realUser.transform2D.position;
                    correctionDir = (toCenter.sqrMagnitude > EPSILON) ? toCenter.normalized : realUser.transform2D.forward;
                }

                realUser.transform2D.localPosition += correctionDir.normalized * translationSpeed * Time.fixedDeltaTime;
                repositionSteps++;
            }

            isFirst = true;
            return "WALL_RESET_DONE";
        }

        return "IDLE";
    }

    private float GetForwardVirtualDistance(Transform virtualTransform)
    {
        RaycastHit hit;
        if (Physics.Raycast(virtualTransform.position, virtualTransform.forward, out hit, 1000.0f))
        {
            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("VirtualWall"))
            {
                return hit.distance;
            }
        }

        return 0.0f;
    }

    private int SelectResetDirectionIndex(Vector3 inwardNormal, float virtualDist)
    {
        int count = List_ActualDistance20Way.Count;
        if (count == 0)
        {
            return 0;
        }

        float step = 360.0f / count;
        int bestIndex = 0;
        float bestScore = float.PositiveInfinity;
        bool foundCandidate = false;

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(step * i, Vector3.up) * Vector3.forward;
            float angle = Mathf.Abs(Vector3.Angle(inwardNormal, dir));
            if (angle > FIRST_CONDITION_MAX_ANGLE)
            {
                continue;
            }

            float realDist = List_ActualDistance20Way[i];
            float diff = realDist - virtualDist;
            bool feasible = diff >= 0.0f;

            int left = (i - 1 + count) % count;
            int right = (i + 1) % count;
            float sideClearance = Mathf.Min(List_ActualDistance20Way[left], List_ActualDistance20Way[right]);
            float inwardAlignment = Vector3.Dot(dir.normalized, inwardNormal.normalized);

            // Lower score is better:
            // 1) keep ARC-like virtual distance matching (|diff|),
            // 2) strongly prefer inward direction in corners,
            // 3) prefer directions with better neighboring clearance.
            float score = Mathf.Abs(diff);
            if (!feasible)
            {
                score += 1000.0f;
            }
            score -= 1.5f * inwardAlignment;
            score -= 0.25f * sideClearance;

            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
                foundCandidate = true;
            }
        }

        if (foundCandidate)
        {
            return bestIndex;
        }

        // Hard fallback: choose largest clearance direction.
        float maxDist = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            if (List_ActualDistance20Way[i] > maxDist)
            {
                maxDist = List_ActualDistance20Way[i];
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private Vector3 EstimateInwardDirection(Transform realTransform)
    {
        RaycastHit hit;
        Vector3[] dir4way = new Vector3[4]
        {
            Vector3.forward,
            Vector3.right,
            -Vector3.forward,
            -Vector3.right
        };

        float[] dist4way = new float[4]
        {
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity
        };

        for (int i = 0; i < 4; i++)
        {
            if (Physics.Raycast(realTransform.position, dir4way[i], out hit, 1000.0f))
            {
                if (hit.collider.gameObject.layer == LayerMask.NameToLayer("PhysicalWall") ||
                    hit.collider.gameObject.layer == LayerMask.NameToLayer("PhysicalUser") ||
                    hit.collider.gameObject.name == "obstacle_" + i)
                {
                    dist4way[i] = hit.distance;
                }
            }
        }

        Vector3 weightedInward = Vector3.zero;
        for (int i = 0; i < 4; i++)
        {
            if (!float.IsInfinity(dist4way[i]))
            {
                float w = 1.0f / Mathf.Max(dist4way[i], 0.05f);
                weightedInward += (-dir4way[i]) * w;
            }
        }

        if (weightedInward.sqrMagnitude > EPSILON)
        {
            return weightedInward.normalized;
        }

        int nearestIndex = 0;
        float nearestDist = dist4way[0];
        for (int i = 1; i < 4; i++)
        {
            if (dist4way[i] < nearestDist)
            {
                nearestDist = dist4way[i];
                nearestIndex = i;
            }
        }

        if (float.IsInfinity(nearestDist))
        {
            return -realTransform.forward.normalized;
        }

        return (-dir4way[nearestIndex]).normalized;
    }

    public void Calc_NWay_Distances(Transform _transform, bool bActual, int N_waycount)
    {
        float distance = 0.0f;
        RaycastHit hit;
        List<Vector3> direction = new List<Vector3>();

        if (N_waycount == 3)
        {
            direction.Add(Vector3.forward);
            direction.Add(Vector3.right);
            direction.Add(-Vector3.right);

            if (bActual)
            {
                List_ActualDistance3Way.Clear();
            }
            else
            {
                List_VirtualDistance3Way.Clear();
            }
        }
        else
        {
            float angleStep = 360.0f / Mathf.Max(1, N_waycount);
            for (int i = 0; i < N_waycount; i++)
            {
                Vector3 result = Quaternion.AngleAxis(angleStep * i, Vector3.up) * Vector3.forward;
                direction.Add(result);
            }

            if (bActual)
            {
                List_ActualDistance20Way.Clear();
            }
            else
            {
                List_VirtualDistance20Way.Clear();
            }
        }

        for (int i = 0; i < direction.Count; i++)
        {
            if (Physics.Raycast(_transform.position, direction[i], out hit, 1000.0f))
            {
                if (bActual)
                {
                    if (hit.collider.gameObject.layer == LayerMask.NameToLayer("PhysicalWall") ||
                        hit.collider.gameObject.layer == LayerMask.NameToLayer("PhysicalUser"))
                    {
                        distance = hit.distance;
                    }
                }
                else
                {
                    if (hit.collider.gameObject.layer == LayerMask.NameToLayer("VirtualWall"))
                    {
                        distance = hit.distance;
                    }
                }
            }

            if (N_waycount == 3)
            {
                if (bActual)
                {
                    List_ActualDistance3Way.Add(distance);
                }
                else
                {
                    List_VirtualDistance3Way.Add(distance);
                }
            }
            else
            {
                if (bActual)
                {
                    List_ActualDistance20Way.Add(distance);
                }
                else
                {
                    List_VirtualDistance20Way.Add(distance);
                }
            }

            distance = 0.0f;
        }
    }
}
