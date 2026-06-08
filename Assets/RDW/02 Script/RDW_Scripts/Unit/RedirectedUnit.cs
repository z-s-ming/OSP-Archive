using System.Collections.Generic;
using UnityEngine;
using System;

public class RedirectedUnit
{
    protected Redirector redirector;
    protected Resetter resetter;
    public SimulationController controller;
    public Object2D realUser, virtualUser;
    protected Space2D realSpace, virtualSpace;
    public ResultData resultData;
    static int totalID = 0;
    protected int id;
    private int currentTimeStep = 0;

    private bool showResetLocator = false;
    private GameObject resetLocPrefab = null;
    private List<GameObject> resetLocObjects;

    private bool showRealWall = false;
    private GameObject realWallPrefab = null;
    private List<GameObject> realWallObjects;

    private string status, previousStatus;
    private Object2D intersectedUser;

    private bool initialStep;
    private int step;
    private int nextStep;

    private int idleComboCnt;


    int truc;
    private bool isBidirectionalResetEvent;
    private Vector2 cachedUserResetDirection = Vector2.zero;
    private bool hasCachedUserResetDirection = false;
    private Vector2 lastMovementDirection = Vector2.zero;
    private float lastInstantaneousSpeed = 0.0f;
    private bool usedRotationGainInLastMove = false;
    private bool hasSpeedAnchor = false;
    private bool hasPendingProactiveUserResetIntent = false;
    private Object2D pendingProactiveOtherUser = null;
    private Vector2 pendingProactiveResetDirection = Vector2.zero;
    private bool pendingProactiveIsBidirectionalResetEvent = true;
    private int pendingProactiveUserId = -1;
    private int pendingProactiveOtherUserId = -1;
    private int pendingProactiveOriginTriggerId = -1;
    private int pendingProactiveOriginCandidateId = -1;
    private int pendingProactiveDecisionId = -1;

    private bool isResetting = false;
    public bool IsResetting { get { return isResetting; } }
    private bool isExternalResetActive = false;
    private ResetPlan externalResetPlan;
    private static int nextResetPlanId = 1;

    public RedirectedUnit() // 기본 생성자
    {
        redirector = new Redirector();
        resetter = new Resetter();
        controller = new SimulationController();
        resultData = new ResultData();
        resetLocObjects = new List<GameObject>();
        realWallObjects = new List<GameObject>();
        id = -1;

        status = "UNDEFINED"; // TODO: 이래도 되나?     

    }

    public RedirectedUnit(Redirector redirector, Resetter resetter, SimulationController controller, Space2D realSpace, Space2D virtualSpace, Object2D realUser, Object2D virtualUser) // 생성자
    {
        this.redirector = redirector;
        this.resetter = resetter;
        this.controller = controller;
        this.realSpace = realSpace;
        this.virtualSpace = virtualSpace;
        this.realUser = realUser;
        this.virtualUser = virtualUser;
        this.status = "IDLE";
        this.lastMovementDirection = realUser != null ? realUser.transform2D.forward : Vector2.zero;
        this.hasSpeedAnchor = realUser != null;

        resetLocObjects = new List<GameObject>();
        realWallObjects = new List<GameObject>();
        resultData = new ResultData();
        resultData.setUnitID(totalID++);
        id = totalID;
        resultData.setEpisodeID(controller.GetEpisodeID());
    }

    public void Destroy()
    {
        this.redirector = null;
        this.resetter = null;
        this.controller = null;
        this.resultData = null;
        // if (virtualSpace != null) this.virtualSpace.Destroy();
        // if (realSpace != null)  this.realSpace.Destroy();
        if (realUser != null)  this.realUser.Destroy();
        if (virtualUser != null) this.virtualUser.Destroy();
    }

    public List<Object2D> GetUsers(RedirectedUnit[] otherUnits)
    {
        List<Object2D> otherUsers = new List<Object2D>();

        for (int i = 0; i < otherUnits.Length; i++)
        {
            if (this.id == otherUnits[i].GetID())
                continue;

            otherUsers.Add(otherUnits[i].GetRealUser());
        }

        return otherUsers;
    }

    public string CheckCurrentStatus(RedirectedUnit[] otherUnits)
    {
        if( 
                ( (status == "WALL_RESET" && previousStatus == "WALL_RESET_DONE") ||
                  (status == "USER_RESET" && previousStatus == "USER_RESET_DONE")    )
          )
        {
            isExternalResetActive = false;
            if(showResetLocator)
            {
                resetLocObjects.Add(GameObject.Instantiate(resetLocPrefab, Vector3.zero, Quaternion.identity, GameObject.Find("Virtual Space").transform));
                resetLocObjects[resetLocObjects.Count - 1].transform.localPosition = virtualUser.gameObject.transform.localPosition + new Vector3(0, 0, 0);
            }
            
            if(showRealWall)
            {
                realWallObjects.Add(GameObject.Instantiate(realWallPrefab, Vector3.zero, Quaternion.identity, GameObject.Find("Virtual Space").transform));

                Vector3 realCenterPosition =  Utility.CastVector2Dto3D( Utility.RotateVector2(-realUser.transform2D.localPosition, virtualUser.transform2D.localRotation - realUser.transform2D.localRotation));
                realWallObjects[realWallObjects.Count - 1].transform.localPosition =
                    virtualUser.gameObject.transform.localPosition
                    + realCenterPosition + Utility.CastVector2Dto3D(Utility.CastVector3Dto2D(realCenterPosition).normalized * -0.6f) // Translation Speed 4 -> -0.7f
                     //+ virtualUser.gameObject.transform.forward * realUser.gameObject.transform.localPosition.magnitude
                     //+ virtualUser.gameObject.transform.forward * - 0.7f
                    + new Vector3(0, 2, 0);

                realWallObjects[realWallObjects.Count - 1].transform.localRotation =
                    Utility.CastRotation2Dto3D(
                        virtualUser.transform2D.localRotation - realUser.transform2D.localRotation + 90f
                        );
                // if(realUser.transform2D.localPosition.y >= 0)
                // {
                //     realWallObjects[realWallObjects.Count - 1].transform.localRotation =
                //         Utility.CastRotation2Dto3D(
                //             Utility.CastRotation3Dto2D(virtualUser.gameObject.transform.localRotation)
                //             - Vector2.Angle(Utility.CastVector3Dto2D(realUser.gameObject.transform.localPosition), Vector2.right) + 90f
                //             );

                // }
                // else
                // {
                //     realWallObjects[realWallObjects.Count - 1].transform.localRotation =
                //         Utility.CastRotation2Dto3D(
                //             Utility.CastRotation3Dto2D(virtualUser.gameObject.transform.localRotation)
                //             + Vector2.Angle(Utility.CastVector3Dto2D(realUser.gameObject.transform.localPosition), Vector2.right) - 90f
                //             );
                // }

            }
            
        }

        if (status == "WALL_RESET")
        {
            if (previousStatus == "WALL_RESET_DONE")
            {
                status = "IDLE";
                previousStatus = "IDLE";
            }

        }
        else if (status == "USER_RESET" && RDWSimulationManager.instance.simulationSetting.bAllowUserReset)
        {
            if (previousStatus == "USER_RESET_DONE")
            {
                status = "IDLE";
                previousStatus = "IDLE";
                hasCachedUserResetDirection = false;
            }

        }
        else if (status == "IDLE")
        {
            (bool,bool) item  = resetter.NeedWallReset(realUser, realSpace);

            if (item.Item1)
            {
                // Debug.LogError($"[Wall Reset Triggered] User ID: {id} | 触发墙壁重置！暂停中...");
                // Debug.Break(); // 暂停 Unity 编辑器，方便观察碰撞现场

                int userId = ResolveUserIdByRealUser(realUser);
                ResetPlanType resetPlanType = item.Item2 ? ResetPlanType.ShutterReset : ResetPlanType.WallReset;
                Vector2 resetTargetDirection = ResolveWallResetTargetDirection();
                if (item.Item2)
                {
                    resultData.AddShutterReset();
                    _GCM.GM_DataRecord.instance?.LogInterResetDistance(
                        userId,
                        controller != null ? controller.GetEpisodeID() : -1,
                        "SHUTTER_RESET",
                        false,
                        realUser.transform2D.localPosition);
                    //Debug.Log("bb");
                }
                else
                {
                    resultData.AddWallReset();
                    _GCM.GM_DataRecord.instance?.LogInterResetDistance(
                        userId,
                        controller != null ? controller.GetEpisodeID() : -1,
                        "WALL_RESET",
                        false,
                        realUser.transform2D.localPosition);
                    //Debug.Log("aa");
                }

                status = "WALL_RESET";
                TryBeginExternalReset(BuildResetPlan(
                    resetPlanType,
                    userId,
                    -1,
                    resetTargetDirection,
                    "WALL_RESET",
                    "WALL_RESET_DONE",
                    false,
                    false));
                //Debug.LogError(realUser.gameObject.tag.ToString() + " AddWallReset");
            }
            else if (RDWSimulationManager.instance.simulationSetting.bAllowUserReset &&
                     previousStatus != "USER_RESET_DONE" &&
                     TryConsumeProactiveUserResetIntent(out Object2D proactiveOtherUser,
                                                        out Vector2 proactiveDirection,
                                                        out bool proactiveBidirectionalResetEvent,
                                                        out int proactiveUserId,
                                                        out int proactiveOtherUserId,
                                                        out int originTriggerId,
                                                        out int originCandidateId,
                                                        out int decisionId))
            {
                status = "USER_RESET";
                intersectedUser = proactiveOtherUser;
                isBidirectionalResetEvent = proactiveBidirectionalResetEvent;
                cachedUserResetDirection = proactiveDirection;
                hasCachedUserResetDirection = true;
                float proactiveResetSignedAngle = Vector2.SignedAngle(realUser.transform2D.forward, cachedUserResetDirection);
                float proactiveResetAbsAngle = Mathf.Abs(proactiveResetSignedAngle);

                resultData.AddUserReset();
                bool countedUserResetEvent = RDWSimulationManager.instance.RegisterUserResetEvent(id, intersectedUser, isBidirectionalResetEvent, true);
                int userId = proactiveUserId >= 0 ? proactiveUserId : ResolveUserIdByRealUser(realUser);
                int otherUserId = proactiveOtherUserId >= 0 ? proactiveOtherUserId : ResolveUserIdByRealUser(intersectedUser);
                int executionId = ProactiveResetEventIdTracker.NextExecutionId();
                ProactiveResetEventIdTracker.RecordExecution(true);
                _GCM.GlobalCoordinationManager.instance?.RegisterProactiveUserResetExecution(userId, otherUserId);
                _GCM.GM_DataRecord.instance?.LogInterResetDistance(
                    userId,
                    controller != null ? controller.GetEpisodeID() : -1,
                    "PROACTIVE_USER_RESET",
                    isBidirectionalResetEvent,
                    realUser.transform2D.localPosition,
                    otherUserId,
                    originTriggerId,
                    originCandidateId,
                    decisionId,
                    executionId,
                    originTriggerId,
                    originCandidateId,
                    true,
                    true,
                    "NONE");
                TryBeginExternalReset(BuildResetPlan(
                    ResetPlanType.ProactiveUserReset,
                    userId,
                    otherUserId,
                    cachedUserResetDirection,
                    "USER_RESET",
                    "USER_RESET_DONE",
                    isBidirectionalResetEvent,
                    true));
                Debug.Log(
                    $"[主动重置] triggerId={originTriggerId}, candidateId={originCandidateId}, decisionId={decisionId}, executionId={executionId}, dangerPair=({userId},{otherUserId}), userId={userId} 执行主动USER_RESET, other={(intersectedUser != null ? intersectedUser.gameObject.name : "null")}, " +
                    $"resetSignedAngle={proactiveResetSignedAngle:F2}deg, resetAbsAngle={proactiveResetAbsAngle:F2}deg");

                if (countedUserResetEvent &&
                    isBidirectionalResetEvent &&
                    RDWSimulationManager.instance != null &&
                    RDWSimulationManager.instance.simulationSetting != null &&
                    RDWSimulationManager.instance.simulationSetting.useDebugMode)
                {
                    Debug.Break();
                }
            }
            else if (RDWSimulationManager.instance.simulationSetting.bAllowUserReset &&
                     resetter.NeedUserReset(this, otherUnits, out intersectedUser, out truc, out isBidirectionalResetEvent) &&
                     previousStatus != "USER_RESET_DONE" )
            {
                status = "USER_RESET";
                resultData.AddUserReset();
                bool countedUserResetEvent = RDWSimulationManager.instance.RegisterUserResetEvent(id, intersectedUser, isBidirectionalResetEvent, false);
                int userId = ResolveUserIdByRealUser(realUser);
                int otherUserId = ResolveUserIdByRealUser(intersectedUser);
                Vector2 resetTargetDirection = UserResetDirectionResolver.ResolveDirection(this, intersectedUser);
                _GCM.GM_DataRecord.instance?.LogInterResetDistance(
                    userId,
                    controller != null ? controller.GetEpisodeID() : -1,
                    "USER_RESET",
                    isBidirectionalResetEvent,
                    realUser.transform2D.localPosition,
                    otherUserId);
                hasCachedUserResetDirection = false;
                TryBeginExternalReset(BuildResetPlan(
                    ResetPlanType.UserReset,
                    userId,
                    otherUserId,
                    resetTargetDirection,
                    "USER_RESET",
                    "USER_RESET_DONE",
                    isBidirectionalResetEvent,
                    false));
                if (isBidirectionalResetEvent)
                {
                    TrySynchronizeBidirectionalUserReset(intersectedUser);
                }
                //Debug.Log(realUser.gameObject.tag.ToString() + " AddUserReset");
                if (countedUserResetEvent &&
                    isBidirectionalResetEvent &&
                    RDWSimulationManager.instance != null &&
                    RDWSimulationManager.instance.simulationSetting != null &&
                    RDWSimulationManager.instance.simulationSetting.useDebugMode)
                {
                    Debug.Break(); // 暂停 Unity 编辑器，方便观察碰撞现场
                }
            }
            else if (!GetEpisode().IsNotEnd())
            {
                status = "END";
                Debug.Log("EndEpisode!");
            }
            else
            {
                status = "IDLE";
            }

            previousStatus = "IDLE";
        }

        return status;
    }

    private bool TryConsumeProactiveUserResetIntent(
        out Object2D otherUser,
        out Vector2 resetDirection,
        out bool bidirectionalResetEvent,
        out int userId,
        out int otherUserId,
        out int originTriggerId,
        out int originCandidateId,
        out int decisionId)
    {
        if (!hasPendingProactiveUserResetIntent)
        {
            otherUser = null;
            resetDirection = Vector2.zero;
            bidirectionalResetEvent = false;
            userId = -1;
            otherUserId = -1;
            originTriggerId = -1;
            originCandidateId = -1;
            decisionId = -1;
            return false;
        }

        otherUser = pendingProactiveOtherUser;
        resetDirection = pendingProactiveResetDirection;
        bidirectionalResetEvent = pendingProactiveIsBidirectionalResetEvent;
        userId = pendingProactiveUserId;
        otherUserId = pendingProactiveOtherUserId;
        originTriggerId = pendingProactiveOriginTriggerId;
        originCandidateId = pendingProactiveOriginCandidateId;
        decisionId = pendingProactiveDecisionId;

        hasPendingProactiveUserResetIntent = false;
        pendingProactiveOtherUser = null;
        pendingProactiveResetDirection = Vector2.zero;
        pendingProactiveIsBidirectionalResetEvent = true;
        pendingProactiveUserId = -1;
        pendingProactiveOtherUserId = -1;
        pendingProactiveOriginTriggerId = -1;
        pendingProactiveOriginCandidateId = -1;
        pendingProactiveDecisionId = -1;
        return true;
    }

    private void TrySynchronizeBidirectionalUserReset(Object2D otherUser)
    {
        RedirectedUnit otherUnit = ResolveUnitByRealUser(otherUser);
        if (otherUnit == null || otherUnit.GetID() == id)
            return;

        otherUnit.BeginSynchronizedBidirectionalUserReset(realUser);
    }

    private void BeginSynchronizedBidirectionalUserReset(Object2D otherUser)
    {
        if (status == "USER_RESET" || status == "WALL_RESET" || status == "END")
            return;

        status = "USER_RESET";
        previousStatus = "IDLE";
        intersectedUser = otherUser;
        isBidirectionalResetEvent = true;
        hasCachedUserResetDirection = false;

        resultData.AddUserReset();
        RDWSimulationManager.instance.RegisterUserResetEvent(id, intersectedUser, true, false);
        int userId = ResolveUserIdByRealUser(realUser);
        int otherUserId = ResolveUserIdByRealUser(intersectedUser);
        Vector2 resetTargetDirection = UserResetDirectionResolver.ResolveDirection(this, intersectedUser);
        _GCM.GM_DataRecord.instance?.LogInterResetDistance(
            userId,
            controller != null ? controller.GetEpisodeID() : -1,
            "USER_RESET",
            true,
            realUser.transform2D.localPosition,
            otherUserId);
        TryBeginExternalReset(BuildResetPlan(
            ResetPlanType.UserReset,
            userId,
            otherUserId,
            resetTargetDirection,
            "USER_RESET",
            "USER_RESET_DONE",
            true,
            false));
    }

    public void Simulate(RedirectedUnit[] otherUnits)
    {
        currentTimeStep += 1;
        string currentStatus = CheckCurrentStatus(otherUnits);

        switch (currentStatus)
        {
            case "IDLE":
                Move();
                isResetting = false;
                break;
            case "WALL_RESET":
                previousStatus = isExternalResetActive ? "IDLE" : ApplyWallReset();
                isResetting = true;
                break;
            case "USER_RESET":
                if (previousStatus != "USER_RESET_DONE")
                {
                    previousStatus = isExternalResetActive ? "IDLE" : ApplyUserReset(intersectedUser, ref truc);
                    isResetting = true;
                }
                break;
            default:
                break;
        }

        //Debug.Log(id + " isResetting " + isResetting);
    }

    public void ApplyExternalRealUserPose(Vector2 localPosition, float localRotation)
    {
        if (realUser == null || realUser.transform2D == null)
            return;

        Vector2 previousPosition = realUser.transform2D.localPosition;
        Vector2 displacement = localPosition - previousPosition;
        float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);

        lastInstantaneousSpeed = displacement.magnitude / deltaTime;
        if (displacement.sqrMagnitude > Mathf.Epsilon)
        {
            lastMovementDirection = displacement.normalized;
        }
        else
        {
            Vector2 nextForward = Utility.RotateVector2(Vector2.up, localRotation);
            if (nextForward.sqrMagnitude > Mathf.Epsilon)
                lastMovementDirection = nextForward.normalized;
        }

        hasSpeedAnchor = true;
        realUser.transform2D.localPosition = localPosition;
        realUser.transform2D.localRotation = localRotation;
    }

    public bool CompleteExternalReset(int planId)
    {
        if (!isExternalResetActive || externalResetPlan.PlanId != planId)
            return false;

        previousStatus = externalResetPlan.DoneStatus;
        isExternalResetActive = false;
        isResetting = false;

        if (string.Equals(externalResetPlan.DoneStatus, "USER_RESET_DONE", StringComparison.Ordinal))
        {
            hasCachedUserResetDirection = false;
            resetter.isFirst = true;
            RDWSimulationManager.instance.Enqueue_UserResetFilter(DateTime.Now);
        }

        return true;
    }

    public bool CommitLiveExternalReset(
        int planId,
        Vector2 finalPhysicalPosition,
        float finalPhysicalYawDegrees,
        Vector2 frozenVirtualPosition,
        float frozenVirtualYawDegrees)
    {
        if (!isExternalResetActive || externalResetPlan.PlanId != planId)
            return false;

        if (virtualUser != null && virtualUser.transform2D != null)
        {
            virtualUser.transform2D.localPosition = frozenVirtualPosition;
            virtualUser.transform2D.localRotation = frozenVirtualYawDegrees;
            controller.ResetCurrentState(virtualUser.transform2D);
        }

        ApplyExternalRealUserPose(finalPhysicalPosition, finalPhysicalYawDegrees);

        previousStatus = "IDLE";
        status = "IDLE";
        isExternalResetActive = false;
        isResetting = false;

        if (string.Equals(externalResetPlan.DoneStatus, "USER_RESET_DONE", StringComparison.Ordinal))
        {
            hasCachedUserResetDirection = false;
            resetter.isFirst = true;
            RDWSimulationManager.instance.Enqueue_UserResetFilter(DateTime.Now);
        }

        return true;
    }

    public bool TryGetExternalResetPlan(out ResetPlan plan)
    {
        plan = externalResetPlan;
        return isExternalResetActive;
    }

    public void CancelExternalResetForLiveRestart()
    {
        isExternalResetActive = false;
        isResetting = false;
        status = "IDLE";
        previousStatus = "IDLE";
        hasCachedUserResetDirection = false;
        ClearProactiveUserResetIntent();
        if (resetter != null)
            resetter.isFirst = true;
    }

    private ResetPlan BuildResetPlan(
        ResetPlanType type,
        int userId,
        int otherUserId,
        Vector2 targetDirection,
        string activeStatus,
        string doneStatus,
        bool bidirectional,
        bool proactive)
    {
        Vector2 fallbackDirection = realUser != null && realUser.transform2D != null
            ? realUser.transform2D.forward
            : Vector2.up;
        Vector2 normalizedDirection = NormalizeOrFallback(targetDirection, fallbackDirection);
        Vector2 targetPosition = realUser != null && realUser.transform2D != null
            ? realUser.transform2D.localPosition
            : Vector2.zero;

        return new ResetPlan
        {
            PlanId = nextResetPlanId++,
            UserId = userId,
            OtherUserId = otherUserId,
            Type = type,
            TargetDirection = normalizedDirection,
            TargetPosition = targetPosition,
            HasTargetPosition = true,
            IsBidirectional = bidirectional,
            IsProactive = proactive,
            ActiveStatus = activeStatus,
            DoneStatus = doneStatus
        };
    }

    private bool TryBeginExternalReset(ResetPlan plan)
    {
        if (!RdwResetExecutionRegistry.TryBeginReset(this, plan))
            return false;

        externalResetPlan = plan;
        isExternalResetActive = true;
        return true;
    }

    private Vector2 ResolveWallResetTargetDirection()
    {
        if (realUser == null || realUser.transform2D == null)
            return Vector2.up;

        Vector2 directionToCenter = -realUser.transform2D.localPosition;
        return NormalizeOrFallback(directionToCenter, realUser.transform2D.forward);
    }

    private static Vector2 NormalizeOrFallback(Vector2 value, Vector2 fallback)
    {
        if (value.sqrMagnitude > Mathf.Epsilon)
            return value.normalized;

        if (fallback.sqrMagnitude > Mathf.Epsilon)
            return fallback.normalized;

        return Vector2.up;
    }

    public string ApplyUserReset(Object2D otherUser, ref int truc)
    {
        if (!hasCachedUserResetDirection)
        {
            cachedUserResetDirection = UserResetDirectionResolver.ResolveDirection(this, otherUser);
            hasCachedUserResetDirection = true;
        }

        string result = resetter.ApplyUserReset(realUser, cachedUserResetDirection, ref truc); // 필요하면 User Reset과 Wall Reset의 방법을 다르게 만들 수 있도록 이런 식으로 구현
        if (result == "USER_RESET_DONE")
        {
            hasCachedUserResetDirection = false;
        }

        return result;
    }

    public string ApplyWallReset()
    {
        return resetter.ApplyWallReset(realUser, virtualUser, realSpace);
    }

    public void SetProactiveUserResetIntent(
        Object2D otherUser,
        Vector2 resetDirection,
        bool bidirectionalResetEvent = true,
        int userId = -1,
        int otherUserId = -1,
        int originTriggerId = -1,
        int originCandidateId = -1,
        int decisionId = -1)
    {
        pendingProactiveOtherUser = otherUser;
        pendingProactiveResetDirection = resetDirection.sqrMagnitude > Mathf.Epsilon
            ? resetDirection.normalized
            : GetLastMovementDirection();
        pendingProactiveIsBidirectionalResetEvent = bidirectionalResetEvent;
        pendingProactiveUserId = userId;
        pendingProactiveOtherUserId = otherUserId;
        pendingProactiveOriginTriggerId = originTriggerId;
        pendingProactiveOriginCandidateId = originCandidateId;
        pendingProactiveDecisionId = decisionId;
        hasPendingProactiveUserResetIntent = true;
    }

    public void ClearProactiveUserResetIntent()
    {
        hasPendingProactiveUserResetIntent = false;
        pendingProactiveOtherUser = null;
        pendingProactiveResetDirection = Vector2.zero;
        pendingProactiveIsBidirectionalResetEvent = true;
        pendingProactiveUserId = -1;
        pendingProactiveOtherUserId = -1;
        pendingProactiveOriginTriggerId = -1;
        pendingProactiveOriginCandidateId = -1;
        pendingProactiveDecisionId = -1;
    }

    private static int ResolveUserIdByRealUser(Object2D userObject)
    {
        if (userObject == null || RDWSimulationManager.instance == null)
            return -1;

        RedirectedUnit[] units = RDWSimulationManager.instance.GetRedirectedUnits;
        if (units == null)
            return -1;

        for (int i = 0; i < units.Length; i++)
        {
            RedirectedUnit unit = units[i];
            if (unit != null && unit.GetRealUser() == userObject)
                return i;
        }

        return -1;
    }

    private static RedirectedUnit ResolveUnitByRealUser(Object2D userObject)
    {
        if (userObject == null || RDWSimulationManager.instance == null)
            return null;

        RedirectedUnit[] units = RDWSimulationManager.instance.GetRedirectedUnits;
        if (units == null)
            return null;

        for (int i = 0; i < units.Length; i++)
        {
            RedirectedUnit unit = units[i];
            if (unit != null && unit.GetRealUser() == userObject)
                return unit;
        }

        return null;
    }

    private int i = 0;
    public void Move()
    {
        usedRotationGainInLastMove = false;
        Vector2 positionBeforeMove = realUser != null ? realUser.transform2D.localPosition : Vector2.zero;
        Vector2 deltaPosition = new Vector2(0,0);
        float deltaRotation = 0f;
        if(virtualSpace.tileMode)
        {
            (deltaPosition, deltaRotation) = controller.VirtualMove(virtualUser, virtualSpace, realUser, realSpace); // 가상 유저를 이동 (시뮬레이션)
        }
        else
        {
            (deltaPosition, deltaRotation) = controller.VirtualMove(virtualUser, virtualSpace); // 가상 유저를 이동 (시뮬레이션)
        }

        if(redirector is ARCRedirector)
        {
            (GainType type, List<float> degree) = ((ARCRedirector)redirector).ApplyRedirection_ARC(this, deltaPosition, deltaRotation);
            controller.RealMove(realUser, type, degree); // 실제 유저를 이동
            usedRotationGainInLastMove = (type == GainType.Rotation);
        }
        else if (redirector is ARCRedirector_OSP)
        {
            (GainType type, List<float> degree) = ((ARCRedirector_OSP)redirector).ApplyRedirection_ARC_OSP(this, deltaPosition, deltaRotation);
            controller.RealMove(realUser, type, degree); // 실제 유저를 이동
            usedRotationGainInLastMove = (type == GainType.Rotation);
        }
        else
        {
            (GainType type, float degree) = redirector.ApplyRedirection(this, deltaPosition, deltaRotation); // 왜곡시킬 값을 계산
            controller.RealMove(realUser, type, degree); // 실제 유저를 이동
            usedRotationGainInLastMove = (type == GainType.Rotation);

            if (redirector is GainRedirector)
            {
                resultData.setGains(type, ((GainRedirector)redirector).GetApplidedGain(type));
            }

            resultData.AddElapsedTime(Time.fixedDeltaTime);
        }

        if (realUser != null)
        {
            Vector2 positionAfterMove = realUser.transform2D.localPosition;
            if (hasSpeedAnchor)
            {
                Vector2 displacement = positionAfterMove - positionBeforeMove;
                float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
                lastInstantaneousSpeed = displacement.magnitude / deltaTime;

                if (displacement.sqrMagnitude > Mathf.Epsilon)
                {
                    lastMovementDirection = displacement.normalized;
                }
                else if (realUser.transform2D.forward.sqrMagnitude > Mathf.Epsilon)
                {
                    lastMovementDirection = realUser.transform2D.forward.normalized;
                }
            }
            else if (realUser.transform2D.forward.sqrMagnitude > Mathf.Epsilon)
            {
                lastMovementDirection = realUser.transform2D.forward.normalized;
            }

            hasSpeedAnchor = true;
        }

      
    }

    public void DebugDraws(Color userColor)
    {
        realUser.DebugDraw(userColor);
        virtualUser.DebugDraw(userColor);
    }
     
    public int GetCurrentTimeStep()
    {
        return currentTimeStep;
    }

    public int GetID()
    {
        return id;
    }

    public string GetStatus()
    {
        return status;
    }

    public Redirector GetRedirector()
    {
        return redirector;
    }

    public Resetter GetResetter()
    {
        return resetter;
    }

    public Space2D GetRealSpace()
    {
        return realSpace;
    }

    public Space2D GetVirtualSpace()
    {
        return virtualSpace;
    }

    public Object2D GetRealUser()
    {
        return realUser;
    }

    public Vector2 GetLastMovementDirection()
    {
        if (lastMovementDirection.sqrMagnitude > Mathf.Epsilon)
            return lastMovementDirection.normalized;

        if (realUser != null && realUser.transform2D.forward.sqrMagnitude > Mathf.Epsilon)
            return realUser.transform2D.forward.normalized;

        return Vector2.up;
    }

    public bool UsedRotationGainInLastMove()
    {
        return usedRotationGainInLastMove;
    }

    public float GetLastInstantaneousSpeed()
    {
        if (!string.Equals(status, "IDLE", StringComparison.Ordinal))
            return 0.0f;

        return Mathf.Max(0.0f, lastInstantaneousSpeed);
    }

    public Object2D GetVirtualUser()
    {
        return virtualUser;
    }

    public Episode GetEpisode()
    {
        return controller.GetEpisode();
    }

    public void SetShowResetLocator(bool showResetLocator)
    {
        this.showResetLocator = showResetLocator;
    }

    public void SetResetLocPrefab(GameObject resetLocPrefab)
    {
        this.resetLocPrefab = resetLocPrefab;
    }

    public void DeleteResetLocObjects()
    {
        for(int i = 0; i < resetLocObjects.Count ; i++)
        {
            GameObject.Destroy(resetLocObjects[i]);
        }
        resetLocObjects = null;
    }

    public void GenerateResetLocObjects()
    {
        resetLocObjects = new List<GameObject>();
    }

    public int GetNumOfResetLocObjects()
    {
        return resetLocObjects.Count;
    }

    public void SetInitialStep(bool initialStep)
    {
        this.initialStep = initialStep;
    }

    public void SetShowRealWall(bool showRealWall)
    {
        this.showRealWall = showRealWall;
    }

    public void SetRealWallPrefab(GameObject realWallPrefab)
    {
        this.realWallPrefab = realWallPrefab;
    }

    public void DeleteRealWallObjects()
    {
        for(int i = 0; i < realWallObjects.Count ; i++)
        {
            GameObject.Destroy(realWallObjects[i]);
        }
        realWallObjects = null;
    }

    public void GenerateRealWallObjects()
    {
        realWallObjects = new List<GameObject>();
    }
}
