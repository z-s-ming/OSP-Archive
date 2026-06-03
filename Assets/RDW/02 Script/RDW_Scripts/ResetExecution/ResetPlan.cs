using UnityEngine;

public enum ResetPlanType
{
    WallReset = 0,
    ShutterReset = 1,
    UserReset = 2,
    ProactiveUserReset = 3
}

public struct ResetPlan
{
    public int PlanId;
    public int UserId;
    public int OtherUserId;
    public ResetPlanType Type;
    public Vector2 TargetDirection;
    public Vector2 TargetPosition;
    public bool HasTargetPosition;
    public bool IsBidirectional;
    public bool IsProactive;
    public string ActiveStatus;
    public string DoneStatus;

    public string ResetTypeName
    {
        get
        {
            switch (Type)
            {
                case ResetPlanType.WallReset:
                    return "WALL_RESET";
                case ResetPlanType.ShutterReset:
                    return "SHUTTER_RESET";
                case ResetPlanType.UserReset:
                    return "USER_RESET";
                case ResetPlanType.ProactiveUserReset:
                    return "PROACTIVE_USER_RESET";
                default:
                    return "RESET";
            }
        }
    }
}
