using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class Resetter
{
    protected float translationSpeed;
    protected float rotationSpeed;
    public bool isFirst;

    //protected float epsilonRotation, epsilonTranslation;

    float initialAngle;
    float maxRotTime, remainRotTime;

    public Resetter() { // 기본 생성자
        isFirst = true;
        //epsilonRotation = (rotationSpeed * Time.fixedDeltaTime / 2) + 0.001f;
        //epsilonTranslation = (translationSpeed * Time.fixedDeltaTime / 2) + 0.001f;
    }

    public Resetter(float translationSpeed, float rotationSpeed)
    {
        this.translationSpeed = translationSpeed;
        this.rotationSpeed = rotationSpeed;
        isFirst = true;
        //epsilonRotation = (rotationSpeed * Time.fixedDeltaTime / 2) + 0.001f;
        //epsilonTranslation = (translationSpeed * Time.fixedDeltaTime / 2) + 0.001f;
    }

    public void SyncDirection(Object2D realUser, Vector2 realTargetDirection)
    {
        if (realTargetDirection.magnitude > 1)
            realTargetDirection = realTargetDirection.normalized;

        realUser.transform2D.forward = realTargetDirection;
        if (realUser.gameObject != null) realUser.gameObject.transform.forward = Utility.CastVector2Dto3D(realTargetDirection);
    }

    public virtual string ApplyWallReset(Object2D realUser, Object2D virtualUser, Space2D realSpace) {
        realUser.transform2D.localPosition = Vector2.zero;
        return "WALL_RESET_DONE";
    }

    public virtual string ApplyUserReset(Object2D realUser, Vector2 resetDirection, ref int truc)
    {
        float rotationSpeed = 60.0f;

        if (isFirst)
        {
            initialAngle = Vector2.SignedAngle(realUser.transform2D.forward, resetDirection);
            isFirst = false;
            maxRotTime = Mathf.Abs(initialAngle) / rotationSpeed;
            remainRotTime = 0;
        }

        if (remainRotTime < maxRotTime)
        {
            realUser.transform2D.Rotate(Mathf.Sign(initialAngle) * rotationSpeed * Time.fixedDeltaTime);
            remainRotTime += Time.fixedDeltaTime;
        }
        else
        {
            SyncDirection(realUser, resetDirection);
            isFirst = true;
            truc++;

            //RDWSimulationManager.instance.userResetTotalCount++;
            RDWSimulationManager.instance.Enqueue_UserResetFilter(DateTime.Now);
            return "USER_RESET_DONE";
        }

        return "IDLE";
    }

    public (bool,bool) NeedWallReset(Object2D realUser, Space2D realSpace)
    {
        // Use world coordinates so checks are robust even when user and space have different parents.
        Vector2 realUserPosition = realUser.transform2D.position;
        bool isInsideOuterBoundary = realSpace.spaceObject != null &&
                                     realSpace.spaceObject.IsInside(realUserPosition, Space.World, 0.5f);

        // Ignore obstacle-based shutter reset in OSP flow; only outer-boundary reset is kept.
        return (!isInsideOuterBoundary, false);
        //return !realSpace.IsInside(realUser, 0);
    }

    public virtual bool NeedUserReset(
        Object2D realUser,
        List<Object2D> otherUsers,
        out Object2D intersectedUser,
        out int truc,
        out bool isBidirectionalResetEvent)
    {
        bool flag = false;
        float translationSpeed = 4;
        float epsilon = translationSpeed * Time.fixedDeltaTime;
        Object2D targetUser = null;
        truc = 0;
        bool resetflag = false;
        isBidirectionalResetEvent = false;

        for (int i = 0; i < otherUsers.Count; i++)
        {
            flag = false;
            if (realUser.IsIntersect(otherUsers[i]))
            {
                Transform2D b = realUser.transform2D; // (=this)
                Transform2D a = otherUsers[i].transform2D; // (=unitList[i])

                if (Vector2.Dot(a.forward, b.forward) >= 0) // 한쪽만 reset 해야 되는 경우
                {
                    Vector2 dir = a.localPosition - b.localPosition;

                    if (Vector2.Dot(b.forward, dir) >= 0) // b의 바라보는 방향 기준으로 a가 b보다 앞쪽에 있는 경우 b를 reset 시킨다
                    {
                        flag = true;
                        resetflag = true;
                        targetUser = otherUsers[i];
                    }
                    else // b의 바라보는 방향 기준으로 a가 b보다 뒤쪽에 있는 경우 a를 reset 시킨다
                    {
                        flag = false;
                    }
                }
                else // 둘다 reset 해야 되는 경우
                {
                    flag = true;
                    resetflag = true;
                    targetUser = otherUsers[i];
                    isBidirectionalResetEvent = true;
                }
            }

            if (flag)
                truc++;
        }

        intersectedUser = targetUser;
        return resetflag;
    }

    public virtual bool NeedUserReset(
        RedirectedUnit currentUnit,
        RedirectedUnit[] otherUnits,
        out Object2D intersectedUser,
        out int truc,
        out bool isBidirectionalResetEvent)
    {
        bool resetCurrentUser = false;
        Object2D targetUser = null;
        truc = 0;
        isBidirectionalResetEvent = false;

        if (currentUnit == null || currentUnit.GetRealUser() == null || otherUnits == null)
        {
            intersectedUser = null;
            return false;
        }

        Object2D currentUser = currentUnit.GetRealUser();
        for (int i = 0; i < otherUnits.Length; i++)
        {
            RedirectedUnit otherUnit = otherUnits[i];
            if (otherUnit == null || otherUnit.GetRealUser() == null || otherUnit.GetID() == currentUnit.GetID())
                continue;

            Object2D otherUser = otherUnit.GetRealUser();
            if (!currentUser.IsIntersect(otherUser))
                continue;

            RedirectedUnit unitA = currentUnit.GetID() <= otherUnit.GetID() ? currentUnit : otherUnit;
            RedirectedUnit unitB = currentUnit.GetID() <= otherUnit.GetID() ? otherUnit : currentUnit;
            float closingVelocity = ComputeClosingVelocity(unitA, unitB);
            float forwardDot = ComputeForwardDot(unitA, unitB);

            if (closingVelocity > 0.0f && forwardDot < 0.0f)
            {
                resetCurrentUser = true;
                targetUser = otherUser;
                isBidirectionalResetEvent = true;
                truc++;
                continue;
            }

            if (currentUnit.GetID() == unitB.GetID())
            {
                resetCurrentUser = true;
                targetUser = otherUser;
                truc++;
            }
        }

        intersectedUser = targetUser;
        return resetCurrentUser;
    }

    private static float ComputeClosingVelocity(RedirectedUnit unitA, RedirectedUnit unitB)
    {
        Vector2 positionA = unitA.GetRealUser().transform2D.localPosition;
        Vector2 positionB = unitB.GetRealUser().transform2D.localPosition;
        Vector2 directionAB = positionB - positionA;
        if (directionAB.sqrMagnitude <= Mathf.Epsilon)
            return 0.0f;

        Vector2 velocityA = unitA.GetLastMovementDirection() * Mathf.Max(0.0f, unitA.GetLastInstantaneousSpeed());
        Vector2 velocityB = unitB.GetLastMovementDirection() * Mathf.Max(0.0f, unitB.GetLastInstantaneousSpeed());
        return Vector2.Dot(velocityA - velocityB, directionAB.normalized);
    }

    private static float ComputeForwardDot(RedirectedUnit unitA, RedirectedUnit unitB)
    {
        Vector2 forwardA = unitA.GetRealUser().transform2D.forward;
        Vector2 forwardB = unitB.GetRealUser().transform2D.forward;
        if (forwardA.sqrMagnitude <= Mathf.Epsilon || forwardB.sqrMagnitude <= Mathf.Epsilon)
            return 1.0f;

        return Vector2.Dot(forwardA.normalized, forwardB.normalized);
    }

    public float GetTranslationSpeed()
    {
        return translationSpeed;
    }

    public float GetRotationSpeed()
    {
        return rotationSpeed;
    }
}
