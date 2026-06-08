using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    public class StateCollector
    {
        public List<GameObject> PhysicalUsers { get; } = new List<GameObject>();
        public List<GameObject> VirtualUsers { get; } = new List<GameObject>();
        public List<Vector2> UsersPrePhysicalPos { get; } = new List<Vector2>();
        public List<Vector2> UsersCurrentPhysicalPos { get; } = new List<Vector2>();
        public List<Vector2> UsersPreVirtualPos { get; } = new List<Vector2>();
        public List<Vector2> UsersCurrentVirtualPos { get; } = new List<Vector2>();
        // Episode completion uses virtual walking distance so RDW methods do not change task length.
        public List<float> UsersCumulativeDist { get; } = new List<float>();

        public float CurrentEpisodeTotalDistance { get; private set; }

        public void ResetTracking(int totalUserCount, Vector2 initPos)
        {
            UsersCumulativeDist.Clear();
            UsersCurrentPhysicalPos.Clear();
            UsersPrePhysicalPos.Clear();
            UsersCurrentVirtualPos.Clear();
            UsersPreVirtualPos.Clear();

            for (int i = 0; i < totalUserCount; i++)
            {
                UsersCumulativeDist.Add(0.0f);
                UsersCurrentPhysicalPos.Add(initPos);
                UsersPrePhysicalPos.Add(initPos);
                UsersCurrentVirtualPos.Add(initPos);
                UsersPreVirtualPos.Add(initPos);
            }
        }

        public void RefreshUsersFromSimulation(int totalUserCount)
        {
            PhysicalUsers.Clear();
            VirtualUsers.Clear();

            var manager = RDWSimulationManager.instance;
            if (manager == null || manager.GetRedirectedUnits == null)
                return;

            var units = manager.GetRedirectedUnits;
            for (int i = 0; i < totalUserCount; i++)
            {
                if (i >= units.Length || units[i] == null)
                    continue;

                if (units[i].realUser != null)
                {
                    GameObject physicalUser = units[i].realUser.gameObject;
                    if (physicalUser != null)
                        PhysicalUsers.Add(physicalUser);
                }

                if (units[i].virtualUser != null)
                {
                    GameObject virtualUser = units[i].virtualUser.gameObject;
                    if (virtualUser == null)
                        continue;

                    CapsuleCollider collider = virtualUser.GetComponent<CapsuleCollider>();
                    if (collider != null)
                    {
                        collider.enabled = false;
                    }

                    VirtualUsers.Add(virtualUser);
                }
            }
        }

        public void SyncPreAndCurrentToUsers(int totalUserCount)
        {
            for (int i = 0; i < totalUserCount; i++)
            {
                if (i < PhysicalUsers.Count && PhysicalUsers[i] != null && i < UsersPrePhysicalPos.Count)
                {
                    Vector2 currentPhysicalPos = ToPlanarPosition(PhysicalUsers[i]);
                    UsersPrePhysicalPos[i] = currentPhysicalPos;
                    UsersCurrentPhysicalPos[i] = currentPhysicalPos;
                }

                if (i < VirtualUsers.Count && VirtualUsers[i] != null && i < UsersPreVirtualPos.Count)
                {
                    Vector2 currentVirtualPos = ToPlanarPosition(VirtualUsers[i]);
                    UsersPreVirtualPos[i] = currentVirtualPos;
                    UsersCurrentVirtualPos[i] = currentVirtualPos;
                }
            }
        }

        public void SyncPreAndCurrentToPhysicalUsers(int totalUserCount)
        {
            SyncPreAndCurrentToUsers(totalUserCount);
        }

        public bool HasReadyUsers(int totalUserCount)
        {
            if (PhysicalUsers.Count < totalUserCount)
                return false;

            for (int i = 0; i < totalUserCount; i++)
            {
                if (PhysicalUsers[i] == null)
                    return false;
            }

            return true;
        }

        public FrameState CaptureFrameState(int totalUserCount)
        {
            if (!HasReadyUsers(totalUserCount))
            {
                return new FrameState(new List<GameObject>());
            }

            List<GameObject> snapshot = new List<GameObject>(totalUserCount);
            for (int i = 0; i < totalUserCount; i++)
            {
                if (PhysicalUsers[i] != null)
                    snapshot.Add(PhysicalUsers[i]);
            }

            return new FrameState(snapshot);
        }

        public void ResetEpisodeDistance()
        {
            CurrentEpisodeTotalDistance = 0.0f;

            for (int i = 0; i < UsersCumulativeDist.Count; i++)
            {
                UsersCumulativeDist[i] = 0.0f;
            }
        }

        public void AccumulateDistanceStep(int totalUserCount, float maxAcceptedStepDistance)
        {
            AccumulateVirtualDistanceStep(totalUserCount, maxAcceptedStepDistance);
        }

        public void AccumulateVirtualDistanceStep(int totalUserCount, float maxAcceptedStepDistance)
        {
            if (!HasReadyUsers(totalUserCount))
                return;

            if (VirtualUsers.Count < totalUserCount)
                return;

            for (int i = 0; i < totalUserCount; i++)
            {
                if (VirtualUsers[i] == null)
                    continue;

                Vector2 currentPos = ToPlanarPosition(VirtualUsers[i]);
                float dist = Vector2.Distance(currentPos, UsersPreVirtualPos[i]);

                if (dist < maxAcceptedStepDistance)
                {
                    CurrentEpisodeTotalDistance += dist;
                    if (i < UsersCumulativeDist.Count)
                    {
                        UsersCumulativeDist[i] += dist;
                    }
                }

                UsersPreVirtualPos[i] = currentPos;
                if (i < UsersCurrentVirtualPos.Count)
                {
                    UsersCurrentVirtualPos[i] = currentPos;
                }
            }
        }

        private static Vector2 ToPlanarPosition(GameObject user)
        {
            if (user == null)
                return Vector2.zero;

            Vector3 position = user.transform.position;
            return new Vector2(position.x, position.z);
        }
    }
}
