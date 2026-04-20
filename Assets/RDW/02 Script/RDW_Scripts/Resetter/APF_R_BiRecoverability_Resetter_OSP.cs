using System.Collections.Generic;
using UnityEngine;

public class APF_R_BiRecoverability_Resetter_OSP : APF_R_Resetter_OSP
{
    private const float RECOVERABILITY_HORIZON_SECONDS = 1.5f;
    private const int RECOVERABILITY_SAMPLE_COUNT = 60;

    public APF_R_BiRecoverability_Resetter_OSP() : base()
    {
    }

    public APF_R_BiRecoverability_Resetter_OSP(float translationSpeed, float rotationSpeed)
        : base(translationSpeed, rotationSpeed)
    {
    }

    public override bool NeedUserReset(Object2D realUser, List<Object2D> otherUsers, out Object2D intersectedUser, out int truc)
    {
        bool flag = false;
        Object2D targetUser = null;
        truc = 0;
        bool resetflag = false;

        for (int i = 0; i < otherUsers.Count; i++)
        {
            flag = false;
            Object2D candidateUser = otherUsers[i];
            if (!realUser.IsIntersect(candidateUser))
                continue;

            Transform2D selfTransform = realUser.transform2D;
            Transform2D otherTransform = candidateUser.transform2D;

            if (Vector2.Dot(otherTransform.forward, selfTransform.forward) >= 0)
            {
                Vector2 dir = otherTransform.localPosition - selfTransform.localPosition;
                if (Vector2.Dot(selfTransform.forward, dir) >= 0)
                {
                    flag = true;
                    resetflag = true;
                    targetUser = candidateUser;
                }
            }
            else
            {
                if (CanAvoidBidirectionalCollision(realUser, candidateUser))
                    continue;

                flag = true;
                resetflag = true;
                targetUser = candidateUser;
            }

            if (flag)
                truc++;
        }

        intersectedUser = targetUser;
        return resetflag;
    }

    private bool CanAvoidBidirectionalCollision(Object2D selfUser, Object2D otherUser)
    {
        RedirectedUnit selfUnit = FindUnitByRealUser(selfUser);
        RedirectedUnit otherUnit = FindUnitByRealUser(otherUser);
        if (selfUnit == null || otherUnit == null)
            return false;

        BidirectionalCollisionRecoverabilityAssessment assessment =
            BidirectionalCollisionRecoverabilityEvaluator.Evaluate(
                selfUnit,
                otherUnit,
                RECOVERABILITY_HORIZON_SECONDS,
                RECOVERABILITY_SAMPLE_COUNT,
                "intersect_opposite");

        return assessment.Recoverable;
    }

    private RedirectedUnit FindUnitByRealUser(Object2D realUser)
    {
        if (RDWSimulationManager.instance == null || RDWSimulationManager.instance.GetRedirectedUnits == null)
            return null;

        RedirectedUnit[] units = RDWSimulationManager.instance.GetRedirectedUnits;
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] != null && units[i].GetRealUser() == realUser)
                return units[i];
        }

        return null;
    }
}
