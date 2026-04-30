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
        public List<float> UsersCumulativeDist { get; } = new List<float>();

        public float CurrentEpisodeTotalDistance { get; private set; }

        public void ResetTracking(int totalUserCount, Vector2 initPos)
        {
            UsersCumulativeDist.Clear();
            UsersCurrentPhysicalPos.Clear();
            UsersPrePhysicalPos.Clear();

            for (int i = 0; i < totalUserCount; i++)
            {
                UsersCumulativeDist.Add(0.0f);
                UsersCurrentPhysicalPos.Add(initPos);
                UsersPrePhysicalPos.Add(initPos);
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
                    PhysicalUsers.Add(units[i].realUser.gameObject);
                }

                if (units[i].virtualUser != null)
                {
                    GameObject virtualUser = units[i].virtualUser.gameObject;
                    CapsuleCollider collider = virtualUser.GetComponent<CapsuleCollider>();
                    if (collider != null)
                    {
                        collider.enabled = false;
                    }

                    VirtualUsers.Add(virtualUser);
                }
            }
        }

        public void SyncPreAndCurrentToPhysicalUsers(int totalUserCount)
        {
            for (int i = 0; i < totalUserCount; i++)
            {
                if (i >= PhysicalUsers.Count || PhysicalUsers[i] == null)
                    continue;

                Vector2 currentPos = new Vector2(PhysicalUsers[i].transform.position.x, PhysicalUsers[i].transform.position.z);
                if (i < UsersPrePhysicalPos.Count)
                {
                    UsersPrePhysicalPos[i] = currentPos;
                    UsersCurrentPhysicalPos[i] = currentPos;
                }
            }
        }

        public bool HasReadyUsers(int totalUserCount)
        {
            return PhysicalUsers.Count >= totalUserCount;
        }

        public FrameState CaptureFrameState(int totalUserCount)
        {
            if (!HasReadyUsers(totalUserCount))
            {
                return new FrameState(new List<GameObject>());
            }

            return new FrameState(PhysicalUsers);
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
            if (!HasReadyUsers(totalUserCount))
                return;

            var units = RDWSimulationManager.instance.GetRedirectedUnits;

            for (int i = 0; i < totalUserCount; i++)
            {
                if (PhysicalUsers[i] == null)
                    continue;

                if (units != null && i < units.Length && units[i] != null && units[i].IsResetting)
                {
                    UsersPrePhysicalPos[i] = new Vector2(PhysicalUsers[i].transform.position.x, PhysicalUsers[i].transform.position.z);
                    continue;
                }

                Vector2 currentPos = new Vector2(PhysicalUsers[i].transform.position.x, PhysicalUsers[i].transform.position.z);
                float dist = Vector2.Distance(currentPos, UsersPrePhysicalPos[i]);

                if (dist < maxAcceptedStepDistance)
                {
                    CurrentEpisodeTotalDistance += dist;
                    if (i < UsersCumulativeDist.Count)
                    {
                        UsersCumulativeDist[i] += dist;
                    }
                }

                UsersPrePhysicalPos[i] = currentPos;
                if (i < UsersCurrentPhysicalPos.Count)
                {
                    UsersCurrentPhysicalPos[i] = currentPos;
                }
            }
        }
    }
}
