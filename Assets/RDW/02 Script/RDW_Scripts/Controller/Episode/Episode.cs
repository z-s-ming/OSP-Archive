using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Episode
{
    protected static int totalID = 0;
    protected int id;
    protected Vector2? currentTargetPosition;
    protected int currentEpisodeIndex;
    protected int episodeLength;
    public GameObject targetPrefab = null;
    protected GameObject targetObject = null;
    public bool showTarget = false;
    private bool wrongEpisode = false;

    protected Vector2 realAgentInitialPosition;
    protected Vector2 virtualAgentInitialPosition;

    public List<float> List_DiscreteAngle = new List<float>();

    public Vector2 GetRealAgentInitialPosition()
    {
        return realAgentInitialPosition;
    }
    
    public void SetRealAgentInitialPosition(Vector2 realAgentInitialPosition)
    {
        this.realAgentInitialPosition = realAgentInitialPosition;
    }

    public Vector2 GetVirtualAgentInitialPosition()
    {
        return virtualAgentInitialPosition;
    }

    public void SetVirtualAgentInitialPosition(Vector2 virtualAgentInitialPosition)
    {
        this.virtualAgentInitialPosition = virtualAgentInitialPosition;
    }

    public void setShowTarget(bool showTarget)
    {
        this.showTarget = showTarget;
    }

    public bool GetWrongEpisode()
    {
        return wrongEpisode;
    }

    public void SetWrongEpisode(bool wrongEpisode)
    {
        this.wrongEpisode = wrongEpisode;
    }

    public Episode() { // 기본 생성자
        id = totalID++;
        currentEpisodeIndex = 0;
        currentTargetPosition = null;
        this.episodeLength = 0;

        List_DiscreteAngle.Clear();
        for (int i = 0; i < 36; i++)
        {
            List_DiscreteAngle.Add(-180.0f + (i * 10));
        }
    }

    public Episode(int episodeLength) // 생성자
    {
        id = totalID++;
        currentEpisodeIndex = 0;
        currentTargetPosition = null;
        this.episodeLength = episodeLength;

        List_DiscreteAngle.Clear();
        for (int i = 0; i < 36; i++)
        {
            List_DiscreteAngle.Add(-180.0f + (i * 10));
        }

    }

    public void ResetEpisode()
    {
        id = totalID++;
        currentEpisodeIndex = 0;
        currentTargetPosition = null;

        List_DiscreteAngle.Clear();
        for (int i = 0; i < 36; i++)
        {
            List_DiscreteAngle.Add(-180.0f + (i * 10));
        }
    }

    public int GetCurrentEpisodeIndex()
    {
        return currentEpisodeIndex;
    }

    public void SetCurrentEpisodeIndex(int currentEpisodeIndex)
    {
        this.currentEpisodeIndex = currentEpisodeIndex;
    }

    public int GetEpisodeLength()
    {
        return episodeLength;
    }

    public int getID()
    {
        return id;
    }

    protected void InstaniateTarget()
    {
        EnsureTargetObject(currentTargetPosition.Value);
    }

    protected void InstaniateTarget(Vector2 manualTargetPosition)
    {
        EnsureTargetObject(manualTargetPosition);
    }

    private void EnsureTargetObject(Vector2 targetPosition)
    {
        if (targetPrefab == null)
            return;

        if (targetObject == null)
        {
            Transform parent = GetVirtualSpaceTransform();
            targetObject = parent != null
                ? GameObject.Instantiate(targetPrefab, Vector3.zero, Quaternion.identity, parent)
                : GameObject.Instantiate(targetPrefab, Vector3.zero, Quaternion.identity);
        }

        targetObject.SetActive(true);
        targetObject.transform.localPosition = Utility.CastVector2Dto3D(targetPosition) + new Vector3(0, 1.35f, 0);
    }

    private Transform GetVirtualSpaceTransform()
    {
        GameObject virtualSpaceObject = GameObject.Find("Virtual Space");
        return virtualSpaceObject != null ? virtualSpaceObject.transform : null;
    }

    public bool IsNotEnd()
    {
        if (currentEpisodeIndex < episodeLength)
            return true;
        else
            return false;
    }

    public void DeleteTarget()
    {
        if (targetObject != null)
            targetObject.SetActive(false);
        currentEpisodeIndex += 1;
        currentTargetPosition = null;
    }

    public void ReLocateTarget()
    {
        if (targetObject != null)
            targetObject.SetActive(false);
        currentTargetPosition = null;
    }

    public virtual Vector2 GetTarget(Transform2D virtualUserTransform, Space2D virtualSpace, Object2D virtualUser)
    {
        if (!currentTargetPosition.HasValue)
        {
            GenerateEpisode(virtualUserTransform, virtualSpace, virtualUser);
            if(targetPrefab != null && showTarget) InstaniateTarget();
        }

        return currentTargetPosition.Value;
    }

    protected bool IsValidTargetCandidate(
        Space2D virtualSpace,
        Vector2 userPosition,
        Vector2 candidatePosition,
        float insideBound = 0.5f)
    {
        if (virtualSpace == null)
            return false;

        // Target must be inside navigable space and directly reachable by a straight segment.
        return virtualSpace.IsInside(candidatePosition, Space.Self, insideBound).Item1
            && virtualSpace.IsPossiblePath(candidatePosition, userPosition, Space.Self);
    }

    protected virtual void GenerateEpisode(Transform2D virtualUserTransform, Space2D virtualSpace, Object2D virtualUser) { }
}
