using System;
using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    /// <summary>
    /// Read-only snapshot of one user's runtime state for module-level queries.
    /// </summary>
    public class UserRuntimeState
    {
        public int UserId;
        public bool HasPhysicalUser;
        public Vector2 PhysicalPosition;
        public Vector2 PhysicalHeading;
        public bool HasVirtualUser;
        public Vector2 VirtualPosition;
        public float VirtualHeadingDegrees;
        public List<Vector2> CellVertices = new List<Vector2>();
        public Vector2 CellCentroid;
    }

    /// <summary>
    /// Centralized provider for extracting per-user runtime state from live simulation data.
    /// </summary>
    public class UserRuntimeStateProvider
    {
        private readonly StateCollector _stateCollector;
        private readonly Dictionary<int, List<Vector2>> _cellVerticesByUserId;
        private readonly Func<RedirectedUnit[]> _redirectedUnitsAccessor;

        public UserRuntimeStateProvider(
            StateCollector stateCollector,
            Dictionary<int, List<Vector2>> cellVerticesByUserId,
            Func<RedirectedUnit[]> redirectedUnitsAccessor)
        {
            _stateCollector = stateCollector;
            _cellVerticesByUserId = cellVerticesByUserId;
            _redirectedUnitsAccessor = redirectedUnitsAccessor;
        }

        public List<Vector2> GetAllPhysicalPositions(FrameState frameState)
        {
            List<Vector2> positions = new List<Vector2>();
            if (frameState == null || frameState.PhysicalUsers == null)
                return positions;

            for (int i = 0; i < frameState.PhysicalUsers.Count; i++)
            {
                GameObject user = frameState.PhysicalUsers[i];
                if (user == null)
                    continue;

                Vector3 p = user.transform.position;
                positions.Add(new Vector2(p.x, p.z));
            }

            return positions;
        }

        public bool TryGetUserRuntimeState(FrameState frameState, int userId, out UserRuntimeState runtimeState)
        {
            runtimeState = null;

            if (frameState == null || frameState.PhysicalUsers == null)
                return false;

            if (userId < 0 || userId >= frameState.PhysicalUsers.Count)
                return false;

            GameObject physicalUser = frameState.PhysicalUsers[userId];
            if (physicalUser == null)
                return false;

            runtimeState = new UserRuntimeState
            {
                UserId = userId,
                HasPhysicalUser = true
            };

            Vector3 physicalPos3 = physicalUser.transform.position;
            runtimeState.PhysicalPosition = new Vector2(physicalPos3.x, physicalPos3.z);

            Vector3 heading3 = physicalUser.transform.forward;
            Vector2 heading2 = new Vector2(heading3.x, heading3.z);
            runtimeState.PhysicalHeading = heading2.sqrMagnitude > 0.000001f ? heading2.normalized : Vector2.up;

            ResolveVirtualState(userId, runtimeState);
            ResolveCellState(userId, runtimeState);
            return true;
        }

        public bool TryGetLiveUserRuntimeState(int userId, int totalUserCount, out UserRuntimeState runtimeState)
        {
            runtimeState = null;

            if (_stateCollector == null || !_stateCollector.HasReadyUsers(totalUserCount))
                return false;

            FrameState frameState = _stateCollector.CaptureFrameState(totalUserCount);
            return TryGetUserRuntimeState(frameState, userId, out runtimeState);
        }

        private void ResolveVirtualState(int userId, UserRuntimeState runtimeState)
        {
            if (runtimeState == null)
                return;

            RedirectedUnit[] units = _redirectedUnitsAccessor != null ? _redirectedUnitsAccessor() : null;
            if (units == null || userId < 0 || userId >= units.Length || units[userId] == null || units[userId].virtualUser == null)
                return;

            Object2D virtualUser = units[userId].virtualUser;
            runtimeState.HasVirtualUser = true;
            runtimeState.VirtualPosition = virtualUser.transform2D.localPosition;
            runtimeState.VirtualHeadingDegrees = virtualUser.transform2D.localRotation;
        }

        private void ResolveCellState(int userId, UserRuntimeState runtimeState)
        {
            if (runtimeState == null || _cellVerticesByUserId == null)
                return;

            if (!_cellVerticesByUserId.TryGetValue(userId, out List<Vector2> cellVertices) || cellVertices == null)
                return;

            runtimeState.CellVertices.AddRange(cellVertices);
            if (runtimeState.CellVertices.Count == 0)
                return;

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < runtimeState.CellVertices.Count; i++)
            {
                centroid += runtimeState.CellVertices[i];
            }

            runtimeState.CellCentroid = centroid / runtimeState.CellVertices.Count;
        }
    }
}
