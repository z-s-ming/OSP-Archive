using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ARCRedirector_OSP : GainRedirector
{
    private const float MOVEMENT_THRESHOLD = 0.2f; // meters per second. For 2. A linear movement rotation
    private const float MAXIMUM_LINEAR_MOVEMENT_ROTATION_RATE = 15f;
    private const float ROTATION_THRESHOLD = 1.5f; // degrees per second. For 3. An angular rotation
    private const float MAXIMUM_ANGULAR_ROTATION_RATE = 30f;
    private const float ANGLE_THRESHOLD_FOR_DAMPENING = 1f; // Angle threshold to apply dampening (degrees)
    private const float DISTANCE_THRESHOLD_FOR_DAMPENING = 1.25f; // Distance threshold to apply dampening (meters)
    private const float SMOOTHING_FACTOR = 0.125f; // Smoothing factor for redirection rotations

    private const float TRANSLATIONALGAIN_MIN = 0.86f;
    private const float TRANSLATIONALGAIN_MAX = 1.26f;
    private const float SAFE_DIVISOR_EPSILON = 0.001f;
    private const float RAY_EPSILON = 0.00001f;
    private const float BOUNDARY_AVOID_START = 0.8f;
    private const float BOUNDARY_AVOID_TG_MIN = 0.92f;

    private float previousMagnitude = 0f;

    protected Vector2 userPosition; // user localPosition
    protected Vector2 userDirection; // user local direction (localforward)

    private List<float> List_ActualDistance3Way = new List<float>();
    private List<float> List_VirtualDistance3Way = new List<float>();

    public List<float> List_ActualDistance20Way = new List<float>();
    public List<float> List_VirtualDistance20Way = new List<float>();

    private float dist_qq_sum_pre = 0.0f;

    public (GainType, List<float>) ApplyRedirection_ARC_OSP(RedirectedUnit unit, Vector2 deltaPosition, float deltaRotation)
    {
        List<float> returnValue = new List<float>();

        if (deltaPosition == Vector2.zero && deltaRotation == 0.0f)
        {
            returnValue.Add(0.0f);
            return (GainType.Undefined, returnValue);
        }

        // define some variables for redirection
        Transform2D realUserTransform = unit.GetRealUser().transform2D;
        Transform2D virtualUserTransform = unit.GetVirtualUser().transform2D;
        userPosition = realUserTransform.localPosition;
        userDirection = realUserTransform.forward;

        // Calc alignment difference:
        //   physical side uses user cell boundaries (OSP partition),
        //   virtual side uses virtual wall raycasts.
        Transform userTR_V = virtualUserTransform.transform;
        Calc_Cell3Way_Distances(unit, realUserTransform);
        Calc_NWay_Distances(userTR_V, false, 3);

        float dist_qq_sum = 0.0f;

        if (List_ActualDistance3Way.Count == 3 && List_VirtualDistance3Way.Count == 3)
        {
            for (int i = 0; i < 3; i++)
            {
                dist_qq_sum += Mathf.Abs(List_ActualDistance3Way[i] - List_VirtualDistance3Way[i]);
            }
        }
        else
        {
            Debug.LogError("ERROR : List_Distance3Way");
        }

        if (dist_qq_sum == 0.0f)
        {
            Debug.LogWarning("Noredirect");
            returnValue.Add(1.0f);
            return (GainType.Translation, returnValue); // no gain
        }

        float virtualForwardDistance = Mathf.Abs(List_VirtualDistance3Way[0]);
        float safeVirtualForwardDistance = Mathf.Max(virtualForwardDistance, SAFE_DIVISOR_EPSILON);
        float translationGainMagnitude = Mathf.Clamp(Mathf.Abs(List_ActualDistance3Way[0]) / safeVirtualForwardDistance, TRANSLATIONALGAIN_MIN, TRANSLATIONALGAIN_MAX);

        float misalignLeft = List_ActualDistance3Way[2] - List_VirtualDistance3Way[2];
        float misalignRight = List_ActualDistance3Way[1] - List_VirtualDistance3Way[1];
        float directionRotation = Mathf.Sign(deltaRotation); // If user is rotating to the left, directionRotation > 0.

        // Boundary-aware correction:
        // if user is close to cell boundary, reduce translation gain and bias steering to the side
        // with larger physical clearance to avoid corner attraction.
        float minPhysicalClearance = Mathf.Min(List_ActualDistance3Way[0], Mathf.Min(List_ActualDistance3Way[1], List_ActualDistance3Way[2]));
        float boundaryWeight = Mathf.Clamp01((BOUNDARY_AVOID_START - minPhysicalClearance) / BOUNDARY_AVOID_START);
        float sideClearanceBias = List_ActualDistance3Way[2] - List_ActualDistance3Way[1]; // + => left side safer
        if (sideClearanceBias > 0.0f)
        {
            misalignLeft += boundaryWeight * Mathf.Abs(sideClearanceBias);
        }
        else
        {
            misalignRight += boundaryWeight * Mathf.Abs(sideClearanceBias);
        }

        float boundaryCappedTG = Mathf.Lerp(TRANSLATIONALGAIN_MAX, BOUNDARY_AVOID_TG_MIN, boundaryWeight);
        translationGainMagnitude = Mathf.Min(translationGainMagnitude, boundaryCappedTG);

        if (misalignLeft > misalignRight) // If the target is to the left of the user
        {
            curvatureGain = Mathf.Min(1.0f, Mathf.Min(1.0f, Mathf.Abs(misalignLeft)) * HODGSON_MAX_CURVATURE_GAIN);
        }
        else
        {
            curvatureGain = Mathf.Min(1.0f, Mathf.Min(1.0f, Mathf.Abs(misalignRight)) * HODGSON_MIN_CURVATURE_GAIN);
        }

        float frameDiff = dist_qq_sum - dist_qq_sum_pre;
        dist_qq_sum_pre = dist_qq_sum;

        if (frameDiff > 0)
        {
            rotationGain = MIN_ROTATION_GAIN;
        }
        else if (frameDiff < 0)
        {
            rotationGain = 1.24f; // MAX_ROTATION_GAIN
        }
        else
        {
            rotationGain = 1.0f;
        }

        float rotationMagnitude = 0f;
        float curvatureMagnitude = 0f;
        bool isCurvatureSelected = true;

        if (deltaPosition.magnitude > MOVEMENT_THRESHOLD)
        {
            curvatureMagnitude = Mathf.Rad2Deg * curvatureGain * deltaPosition.magnitude;
        }
        else if (Mathf.Abs(deltaRotation) >= ROTATION_THRESHOLD)
        {
            rotationMagnitude = rotationGain * deltaRotation;
            isCurvatureSelected = false;
        }
        else
        {
            returnValue.Add(0.0f);
            return (GainType.Undefined, returnValue);
        }

        // smoothing (kept same behavior as ARC)
        float finalRotation = (1.0f - SMOOTHING_FACTOR) * previousMagnitude + SMOOTHING_FACTOR * Mathf.Abs(rotationMagnitude);
        previousMagnitude = finalRotation;

        if (!isCurvatureSelected)
        {
            float direction = directionRotation;
            returnValue.Add(finalRotation * direction);
            returnValue.Add(translationGainMagnitude);
            return (GainType.Rotation, returnValue);
        }
        else
        {
            returnValue.Add(curvatureMagnitude);
            returnValue.Add(translationGainMagnitude);
            return (GainType.Curvature, returnValue);
        }
    }

    private void Calc_Cell3Way_Distances(RedirectedUnit unit, Transform2D realUserTransform)
    {
        List_ActualDistance3Way.Clear();

        List<Vector2> userCellVertices;
        if (!TryGetUserCellVertices(unit, out userCellVertices))
        {
            // Fallback to legacy ARC behavior when partition data is unavailable.
            Calc_NWay_Distances(realUserTransform.transform, true, 3);
            return;
        }

        Vector2 origin = realUserTransform.position;
        Vector2[] directions = new Vector2[3]
        {
            realUserTransform.forward,
            Utility.CastVector3Dto2D(realUserTransform.transform.right),
            -Utility.CastVector3Dto2D(realUserTransform.transform.right)
        };

        for (int i = 0; i < directions.Length; i++)
        {
            Vector2 rayDir = directions[i].normalized;
            float distance = RayDistanceToPolygon(origin, rayDir, userCellVertices);
            if (float.IsInfinity(distance) || float.IsNaN(distance))
            {
                distance = 0.0f;
            }

            List_ActualDistance3Way.Add(distance);
        }
    }

    private bool TryGetUserCellVertices(RedirectedUnit unit, out List<Vector2> userCellVertices)
    {
        userCellVertices = null;

        if (_GCM.GlobalCoordinationManager.instance == null || RDWSimulationManager.instance == null)
        {
            return false;
        }

        RedirectedUnit[] units = RDWSimulationManager.instance.GetRedirectedUnits;
        if (units == null)
        {
            return false;
        }

        int redirectedUnitIndex = -1;
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] == unit)
            {
                redirectedUnitIndex = i;
                break;
            }
        }

        if (redirectedUnitIndex < 0)
        {
            return false;
        }

        if (!_GCM.GlobalCoordinationManager.instance.dic_AreaSegmentsVertex.TryGetValue(redirectedUnitIndex, out userCellVertices))
        {
            return false;
        }

        return userCellVertices != null && userCellVertices.Count >= 3;
    }

    private float RayDistanceToPolygon(Vector2 rayOrigin, Vector2 rayDirection, List<Vector2> vertices)
    {
        if (vertices == null || vertices.Count < 2)
        {
            return float.PositiveInfinity;
        }

        float minDistance = float.PositiveInfinity;
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector2 a = vertices[i];
            Vector2 b = vertices[(i + 1) % vertices.Count];
            float hitDistance;
            if (TryIntersectRaySegment(rayOrigin, rayDirection, a, b, out hitDistance))
            {
                if (hitDistance < minDistance)
                {
                    minDistance = hitDistance;
                }
            }
        }

        return minDistance;
    }

    private bool TryIntersectRaySegment(Vector2 rayOrigin, Vector2 rayDirection, Vector2 a, Vector2 b, out float rayDistance)
    {
        rayDistance = 0.0f;
        Vector2 segment = b - a;
        float det = Cross2D(rayDirection, segment);

        if (Mathf.Abs(det) <= RAY_EPSILON)
        {
            return false;
        }

        Vector2 diff = a - rayOrigin;
        float t = Cross2D(diff, segment) / det;
        float u = Cross2D(diff, rayDirection) / det;

        if (t >= 0.0f && u >= 0.0f && u <= 1.0f)
        {
            rayDistance = t;
            return true;
        }

        return false;
    }

    private float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    public void Calc_NWay_Distances(Transform _transform, bool bActual, int N_waycount)
    {
        float distance = 0.0f;
        RaycastHit hit;
        List<Vector3> direction = new List<Vector3>();

        if (N_waycount == 3)
        {
            direction.Add(_transform.forward);
            direction.Add(_transform.right);
            direction.Add(-_transform.right);

            if (bActual)
            {
                List_ActualDistance3Way.Clear();
            }
            else
            {
                List_VirtualDistance3Way.Clear();
            }
        }
        else if (N_waycount == 20)
        {
            for (int i = 0; i < 20; i++)
            {
                Vector3 result = Quaternion.AngleAxis(18 * i, Vector3.up) * _transform.forward;
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
                    if (hit.collider.gameObject.layer == LayerMask.NameToLayer("PhysicalWall"))
                    {
                        distance = hit.distance;
                    }
                    else if (hit.collider.gameObject.layer == LayerMask.NameToLayer("PhysicalUser"))
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
            else if (N_waycount == 20)
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
