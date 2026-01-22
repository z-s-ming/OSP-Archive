using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ARCRedirector : GainRedirector
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

    private float previousMagnitude = 0f;

    protected Vector2 userPosition; // user localPosition
    protected Vector2 userDirection; // user local direction (localforward)


    private List<float> List_ActualDistance3Way = new List<float>();
    private List<float> List_VirtualDistance3Way = new List<float>();
    //private List<float> List_ActualDistance3Way_Pre = new List<float>();
    //private List<float> List_VirtualDistance3Way_Pre = new List<float>();

    public List<float> List_ActualDistance20Way = new List<float>();
    public List<float> List_VirtualDistance20Way = new List<float>();

    private float dist_qq_sum_pre = 0.0f;

    public (GainType, List<float>) ApplyRedirection_ARC(RedirectedUnit unit, Vector2 deltaPosition, float deltaRotation)
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

        // Calc alignment difference
        Transform userTR_R = realUserTransform.transform;
        Transform userTR_V = virtualUserTransform.transform;

        Calc_NWay_Distances(userTR_R, true, 3);
        Calc_NWay_Distances(userTR_V, false, 3);

        float dist_qq_sum = 0.0f;

        if (List_ActualDistance3Way.Count == 3 && List_VirtualDistance3Way.Count == 3)
        {
            //dist(prox(p_phys), prox(p_virt))
            for (int i = 0; i < 3; i++)
            {
                dist_qq_sum += Mathf.Abs(List_ActualDistance3Way[i] - List_VirtualDistance3Way[i]);
            }

            //Debug.Log(dist_qq_sum);

        }
        else
        {
            Debug.LogError("ERROR : List_Distance3Way");
        }


        if (dist_qq_sum == 0.0f)
        {
            Debug.LogWarning("Noredirect");
            returnValue.Add(1.0f);
            return (GainType.Translation, returnValue); //no gain
        }

        float translationGainMagnitude = Mathf.Clamp(Mathf.Abs(List_ActualDistance3Way[0]) / Mathf.Abs(List_VirtualDistance3Way[0]), TRANSLATIONALGAIN_MIN, TRANSLATIONALGAIN_MAX);
        //Debug.Log("translationGainMagnitude " + translationGainMagnitude);
        //returnValue.Add(translationGainMagnitude);
        //return (GainType.Translation, returnValue);


        float misalignLeft = List_ActualDistance3Way[2] - List_VirtualDistance3Way[2];
        float misalignRight = List_ActualDistance3Way[1] - List_VirtualDistance3Way[1];
        float directionRotation = Mathf.Sign(deltaRotation); // If user is rotating to the left, directionRotation > 0. 그냥 부호임. 왼쪽: 1 or 오른쪽: -1.

        //Debug.Log(misalignLeft + " " + misalignRight);

        if (misalignLeft > misalignRight) // If the target is to the left of the user,
        {
            //curvatureGain = HODGSON_MIN_CURVATURE_GAIN;
            //curvatureGain = Mathf.Min(1.0f, Mathf.Min(1.0f, Mathf.Abs(misalignLeft)) * HODGSON_MIN_CURVATURE_GAIN);
            curvatureGain = Mathf.Min(1.0f, Mathf.Min(1.0f, Mathf.Abs(misalignLeft)) * HODGSON_MAX_CURVATURE_GAIN);

        }
        else
        {
            //curvatureGain = HODGSON_MAX_CURVATURE_GAIN; //ARC 대부분 max값을 적용한다고하니, 그리고 우선 시뮬이니까 scalinFactor는 제외함
            //curvatureGain = Mathf.Min(1.0f, Mathf.Min(1.0f, Mathf.Abs(misalignRight)) * HODGSON_MAX_CURVATURE_GAIN);
            curvatureGain = Mathf.Min(1.0f, Mathf.Min(1.0f, Mathf.Abs(misalignRight)) * HODGSON_MIN_CURVATURE_GAIN);

        }

        float frameDiff = dist_qq_sum - dist_qq_sum_pre;
        dist_qq_sum_pre = dist_qq_sum;

        if (frameDiff > 0) // if user rotates away from the target (if their direction are opposite),
        {
            rotationGain = MIN_ROTATION_GAIN;
        }
        else if(frameDiff < 0)
        {
            rotationGain = 1.24f; //MAX_ROTATION_GAIN
        }
        else
        {
            rotationGain = 1.0f;
        }

        // select the largest magnitude
        float rotationMagnitude = 0, curvatureMagnitude = 0;

        bool isCurvatureSelected = true;

        if (deltaPosition.magnitude > MOVEMENT_THRESHOLD)
        {
            curvatureMagnitude = Mathf.Rad2Deg * curvatureGain * deltaPosition.magnitude; // 2. A linear movement rotation rate. 여기에 delta T를 곱해야 Rotation이 됨.
            //if (curvatureMagnitude > 0)
            //{
            //    curvatureMagnitude = Mathf.Clamp(curvatureMagnitude, 0, MAXIMUM_LINEAR_MOVEMENT_ROTATION_RATE);
            //}
            //else
            //{
            //    curvatureMagnitude = Mathf.Clamp(curvatureMagnitude, -MAXIMUM_LINEAR_MOVEMENT_ROTATION_RATE, 0);
            //}
        }
        else if (Mathf.Abs(deltaRotation) >= ROTATION_THRESHOLD)
        {
            rotationMagnitude = rotationGain * deltaRotation; // 3. An angular rotation rate. 여기에 delta T를 곱해야 Rotation이 됨.
            //if (rotationMagnitude > 0)
            //{
            //    rotationMagnitude = Mathf.Clamp(rotationMagnitude, 0, MAXIMUM_ANGULAR_ROTATION_RATE);
            //}
            //else
            //{
            //    rotationMagnitude = Mathf.Clamp(rotationMagnitude, -MAXIMUM_ANGULAR_ROTATION_RATE, 0);
            //}

            isCurvatureSelected = false;
        }
        else
        {
            returnValue.Add(0.0f);
            return (GainType.Undefined, returnValue);
        }

        //float selectedMagnitude = Mathf.Max(Mathf.Abs(rotationMagnitude), Mathf.Abs(curvatureMagnitude)); // selectedMagnitude is ABS(절대값)
        //bool isCurvatureSelected = Mathf.Abs(curvatureMagnitude) > Mathf.Abs(rotationMagnitude);

        //smoothing
        float finalRotation = (1.0f - SMOOTHING_FACTOR) * previousMagnitude + SMOOTHING_FACTOR * Mathf.Abs(rotationMagnitude);
        previousMagnitude = finalRotation;

        // apply final redirection
        if (!isCurvatureSelected)
        {
            //Debug.Log("AA");

            float direction = directionRotation;
            returnValue.Add(finalRotation * direction);
            returnValue.Add(translationGainMagnitude);
            return (GainType.Rotation, returnValue);
        }
        else
        {
            //Debug.Log("BB");

            //float direction = -Mathf.Sign(curvatureGain);
            //returnValue.Add(curvatureMagnitude * direction);
            returnValue.Add(curvatureMagnitude);
            returnValue.Add(translationGainMagnitude);


            //Debug.Log("translationGainMagnitude " + translationGainMagnitude);
            return (GainType.Curvature, returnValue);
        }
    }

    public void Calc_NWay_Distances(Transform _transform, bool bActual, int N_waycount)
    {
        float distance = 0.0f;
        List<Vector3> direction = new List<Vector3>();

        int totalUserCount = RDWSimulationManager.instance.GetRedirectedUnits.Length;
        int userIndex = GetUserIndex(_transform);



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
            distance = GetBoundaryDistance(_transform.position, direction[i], bActual, 1000.0f, userIndex);

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

    private float GetBoundaryDistance(Vector3 origin, Vector3 direction, bool isPhysical, float maxDistance, int userIndex)
    {
        if (TryGetPartitionDistance(origin, direction, userIndex, maxDistance, out float partitionDistance))
            return partitionDistance;

        var settings = RDWSimulationManager.instance.simulationSetting;
        Vector2 halfSize = isPhysical
            ? new Vector2(Mathf.Abs(settings.realSpaceSetting.spaceObjectSetting.vertices[0].x), Mathf.Abs(settings.realSpaceSetting.spaceObjectSetting.vertices[0].y))
            : new Vector2(Mathf.Abs(settings.virtualSpaceSetting.spaceObjectSetting.vertices[0].x), Mathf.Abs(settings.virtualSpaceSetting.spaceObjectSetting.vertices[0].y));

        Vector2 dir2 = new Vector2(direction.x, direction.z);
        if (dir2.sqrMagnitude < Mathf.Epsilon)
            return maxDistance;

        dir2.Normalize();

        float tMin = float.PositiveInfinity;

        if (Mathf.Abs(dir2.x) > Mathf.Epsilon)
        {
            float tx1 = (-halfSize.x - origin.x) / dir2.x;
            float tx2 = (halfSize.x - origin.x) / dir2.x;
            if (tx1 > 0) tMin = Mathf.Min(tMin, tx1);
            if (tx2 > 0) tMin = Mathf.Min(tMin, tx2);
        }

        if (Mathf.Abs(dir2.y) > Mathf.Epsilon)
        {
            float tz1 = (-halfSize.y - origin.z) / dir2.y;
            float tz2 = (halfSize.y - origin.z) / dir2.y;
            if (tz1 > 0) tMin = Mathf.Min(tMin, tz1);
            if (tz2 > 0) tMin = Mathf.Min(tMin, tz2);
        }

        if (float.IsInfinity(tMin) || tMin <= 0)
            return maxDistance;

        return Mathf.Min(tMin, maxDistance);
    }

    private int GetUserIndex(Transform userTransform)
    {
        if (userTransform == null || userTransform.gameObject == null)
            return -1;

        string tag = userTransform.gameObject.tag;
        if (string.IsNullOrEmpty(tag))
            return -1;

        if (tag.StartsWith("RealUser"))
        {
            string indexText = tag.Substring("RealUser".Length);
            if (int.TryParse(indexText, out int index))
                return index;
        }
        else if (tag.StartsWith("VirtualUser"))
        {
            string indexText = tag.Substring("VirtualUser".Length);
            if (int.TryParse(indexText, out int index))
                return index;
        }

        return -1;
    }

    private bool TryGetPartitionDistance(Vector3 origin, Vector3 direction, int userIndex, float maxDistance, out float distance)
    {
        distance = maxDistance;

        if (userIndex < 0 || _OSP.OSP_Agent.instance == null)
            return false;

        if (!_OSP.OSP_Agent.instance.dic_AreaSegmentsVertex.TryGetValue(userIndex, out List<Vector2> polygon)
            || polygon == null || polygon.Count < 3)
            return false;

        Vector2 dir2 = new Vector2(direction.x, direction.z);
        if (dir2.sqrMagnitude < Mathf.Epsilon)
            return false;

        dir2.Normalize();
        Vector2 origin2 = new Vector2(origin.x, origin.z);

        float tMin = float.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            if (TryRaySegmentIntersection(origin2, dir2, a, b, out float t) && t > 0)
                tMin = Mathf.Min(tMin, t);
        }

        if (float.IsInfinity(tMin) || tMin <= 0)
            return false;

        distance = Mathf.Min(tMin, maxDistance);
        return true;
    }

    private bool TryRaySegmentIntersection(Vector2 origin, Vector2 dir, Vector2 a, Vector2 b, out float t)
    {
        t = 0f;
        Vector2 s = b - a;
        float denom = Cross(dir, s);
        if (Mathf.Abs(denom) < 1e-6f)
            return false;

        Vector2 diff = a - origin;
        float tRay = Cross(diff, s) / denom;
        float u = Cross(diff, dir) / denom;

        if (tRay >= 0f && u >= 0f && u <= 1f)
        {
            t = tRay;
            return true;
        }

        return false;
    }

    private float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }
}
