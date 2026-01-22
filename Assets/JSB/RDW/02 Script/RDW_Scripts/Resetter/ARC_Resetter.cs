using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Text;

public class ARC_Resetter : RotationResetter
{
    private List<float> List_ActualDistance3Way = new List<float>();
    private List<float> List_VirtualDistance3Way = new List<float>();

    public List<float> List_ActualDistance20Way = new List<float>();
    public List<float> List_VirtualDistance20Way = new List<float>();

    public ARC_Resetter() : base()
    {
    }

    public ARC_Resetter(float translationSpeed, float rotationSpeed) : base(translationSpeed, rotationSpeed)
    {
    }

    public override string ApplyWallReset(Object2D realUser, Object2D virtualUser, Space2D realSpace)
    {
        if (isFirst)
        {
            Vector3 targetCenterPoint = Vector3.zero;
            int realUserIndex = GetUserIndex(realUser?.transform2D?.transform);
            int virtualUserIndex = GetUserIndex(virtualUser?.transform2D?.transform);

            Calc_NWay_Distances(realUser.transform2D.transform, true, 20);
            //Calc_NWay_Distances(virtualUser.transform2D.transform, false, 20);

            // Find obstacle normal in physical squared env
            Vector3 wallnormalvec = Vector3.right;
            List<Vector3> dir4way = new List<Vector3>();
            List<float> dist4way = new List<float>();
            dir4way.Add(Vector3.forward);
            dir4way.Add(Vector3.right);
            dir4way.Add(-Vector3.forward);
            dir4way.Add(-Vector3.right);

            for (int i = 0; i < 4; i++)
            {
                dist4way.Add(GetBoundaryDistance(realUser.transform2D.transform.position, dir4way[i], true, 1000.0f, realUserIndex));
            }
            int tempindex = dist4way.IndexOf(dist4way.Min());
            wallnormalvec = -dir4way[tempindex];



            // Find values that satisfy condi 1
            List<int> list_dir_FirstConditionSatisfied = new List<int>();
            for (int i = 0; i < 20; i++)
            {
                float value = 0.0f;
                Vector3 dir1 = Quaternion.AngleAxis(18 * i, Vector3.up) * Vector3.forward;
                //value = Vector3.Dot(wallnormalvec, dir1);
                value = Mathf.Abs(Vector3.Angle(wallnormalvec, dir1));

                //if (value > 0.0f)
                if (value < 80.0f)
                {
                    list_dir_FirstConditionSatisfied.Add(i);
                }
            }

            // Find values that satisfy condi 1 && condi 2
            float virtualDist = 0.0f;
            virtualDist = GetBoundaryDistance(virtualUser.transform2D.transform.position, virtualUser.transform2D.transform.forward, false, 1000.0f, virtualUserIndex);

            List<int> list_dir_SecondConditionSatisfied = new List<int>();
            List<float> list_dir_SecondConditionSatisfied_distance = new List<float>();
            for (int i = 0; i < list_dir_FirstConditionSatisfied.Count; i++)
            {
                float diff = List_ActualDistance20Way[list_dir_FirstConditionSatisfied[i]] - virtualDist;
                
                if (diff >= 0.0f)
                {
                    list_dir_SecondConditionSatisfied.Add(list_dir_FirstConditionSatisfied[i]);
                    list_dir_SecondConditionSatisfied_distance.Add(diff);
                }
            }


            // condi 1 && condi 2
            int dirIndex = 0;

            if (list_dir_SecondConditionSatisfied.Count > 0)
            {
                dirIndex = list_dir_SecondConditionSatisfied[list_dir_SecondConditionSatisfied_distance.IndexOf(list_dir_SecondConditionSatisfied_distance.Min())];
            }
            else
            {
                List<float> list_dist_ABS_FirstConditionSatisfied = new List<float>();
                for (int i = 0; i < list_dir_FirstConditionSatisfied.Count; i++)
                {
                    list_dist_ABS_FirstConditionSatisfied.Add(Mathf.Abs(List_ActualDistance20Way[list_dir_FirstConditionSatisfied[i]] - virtualDist));
                }

                dirIndex = list_dir_FirstConditionSatisfied[list_dist_ABS_FirstConditionSatisfied.IndexOf(list_dist_ABS_FirstConditionSatisfied.Min())];

            }
            

            Vector3 temp = Quaternion.AngleAxis(18 * dirIndex, Vector3.up) * Vector3.forward;

            targetAngle = Vector2.SignedAngle(realUser.transform2D.forward, new Vector2(temp.x, temp.z));

            realTargetRotation = Utility.RotateVector2(realUser.transform2D.forward, targetAngle);
            virtualTargetRotation = Utility.RotateVector2(virtualUser.transform2D.forward, 360);

            //realTargetRotation = Matrix3x3.CreateRotation(targetAngle) * realUser.transform2D.forward;
            //virtualTargetRotation = Matrix3x3.CreateRotation(360) * virtualUser.transform2D.forward;
            isFirst = false;

            maxRotTime = Mathf.Abs(targetAngle) / rotationSpeed;
            remainRotTime = 0;
        }
      
        if (remainRotTime < maxRotTime)
        {
            realUser.transform2D.Rotate(Mathf.Sign(targetAngle) * rotationSpeed * Time.deltaTime);
            virtualUser.transform2D.Rotate((360 / maxRotTime) * Time.deltaTime);
            remainRotTime += Time.fixedDeltaTime;
        }
        else
        {
            Utility.SyncDirection(virtualUser, realUser, virtualTargetRotation, realTargetRotation);
            //realUser.transform2D.localPosition = realUser.transform2D.localPosition + realUser.transform2D.forward * Random.Range(0.1f, translationSpeed) * Time.fixedDeltaTime;
            realUser.transform2D.localPosition = realUser.transform2D.localPosition + realUser.transform2D.forward * translationSpeed * Time.fixedDeltaTime;

            isFirst = true;
            return "WALL_RESET_DONE";
        }

        return "IDLE";
    }


    public void Calc_NWay_Distances(Transform _transform, bool bActual, int N_waycount)
    {
        float distance = 0.0f;
        List<Vector3> direction = new List<Vector3>();

        int totalUserCount = RDWSimulationManager.instance.GetRedirectedUnits.Length;
        int userIndex = GetUserIndex(_transform);



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
        else if (N_waycount == 20)
        {
            for (int i = 0; i < 20; i++)
            {
                Vector3 result = Quaternion.AngleAxis(18 * i, Vector3.up) * Vector3.forward;
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

        //if (!bActual)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    foreach (var item in List_VirtualDistance20Way)
        //    {
        //        sb.Append(item.ToString() + ",");
        //    }

        //    Debug.Log(sb.ToString());
        //}

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
