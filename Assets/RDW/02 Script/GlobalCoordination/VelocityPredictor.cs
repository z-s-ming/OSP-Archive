using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    public class VelocityPredictor
    {
        private readonly int _totalUserCount;

        private readonly Vector3[] _userLastPos;
        private readonly Vector3[] _userSmoothV1;
        private readonly Vector3[] _userSmoothV2;
        private readonly Vector3[] _userFrozenOffset;
        private readonly Vector3[] _userCurrentOffset;
        private readonly bool[] _userIsStopped;
        private bool _velocityInit;

        public float AlphaMax { get; set; }
        public float VMax { get; set; }
        public float VelocityToOffsetFactor { get; set; }
        public float MaxVelocityOffsetDist { get; set; }
        public float StopThreshold { get; set; }

        public VelocityPredictor(int totalUserCount)
        {
            _totalUserCount = totalUserCount;

            _userLastPos = new Vector3[totalUserCount];
            _userSmoothV1 = new Vector3[totalUserCount];
            _userSmoothV2 = new Vector3[totalUserCount];
            _userFrozenOffset = new Vector3[totalUserCount];
            _userCurrentOffset = new Vector3[totalUserCount];
            _userIsStopped = new bool[totalUserCount];
            _velocityInit = false;
        }

        public Vector3[] CurrentOffsets => _userCurrentOffset;

        public IReadOnlyList<Vector3> GetOffsets()
        {
            return _userCurrentOffset;
        }

        public Vector3 GetLinearPredictedPosition(int userId, Vector3 currentPosition, float horizonSeconds)
        {
            if (userId < 0 || userId >= _totalUserCount)
                return currentPosition;

            Vector3 predictedVelocity = _userSmoothV2[userId];
            predictedVelocity.y = 0f;

            Vector3 predictedPosition = currentPosition + predictedVelocity * Mathf.Max(0f, horizonSeconds);
            predictedPosition.y = currentPosition.y;

            return predictedPosition;
        }

        public void ResetWithUsers(IReadOnlyList<GameObject> physicalUsers)
        {
            for (int i = 0; i < _totalUserCount; i++)
            {
                if (physicalUsers != null && i < physicalUsers.Count && physicalUsers[i] != null)
                {
                    _userLastPos[i] = physicalUsers[i].transform.position;
                }
                else
                {
                    _userLastPos[i] = Vector3.zero;
                }

                _userSmoothV1[i] = Vector3.zero;
                _userSmoothV2[i] = Vector3.zero;
                _userFrozenOffset[i] = Vector3.zero;
                _userCurrentOffset[i] = Vector3.zero;
                _userIsStopped[i] = false;
            }

            _velocityInit = true;
        }

        public void Update(IReadOnlyList<GameObject> physicalUsers, float dt, bool enableVelocityOffset)
        {
            if (!enableVelocityOffset)
            {
                for (int i = 0; i < _totalUserCount; i++)
                {
                    _userCurrentOffset[i] = Vector3.zero;
                }
                return;
            }

            if (!_velocityInit || dt <= 0.0001f)
                return;

            for (int i = 0; i < _totalUserCount; i++)
            {
                if (physicalUsers == null || i >= physicalUsers.Count || physicalUsers[i] == null)
                    continue;

                Vector3 currentPos = physicalUsers[i].transform.position;

                Vector3 rawVelocity = (currentPos - _userLastPos[i]) / dt;
                rawVelocity.y = 0;
                float rawSpeed = rawVelocity.magnitude;

                float speedRatio = Mathf.Clamp01(rawSpeed / VMax);
                float alpha = AlphaMax * (speedRatio * speedRatio);

                _userSmoothV1[i] = alpha * rawVelocity + (1f - alpha) * _userSmoothV1[i];
                _userSmoothV2[i] = alpha * _userSmoothV1[i] + (1f - alpha) * _userSmoothV2[i];

                Vector3 finalVelocity = _userSmoothV2[i];
                float finalSpeed = finalVelocity.magnitude;

                Vector3 offsetDir = finalVelocity.normalized;
                float targetOffsetDist = Mathf.Clamp(finalSpeed * VelocityToOffsetFactor, 0f, MaxVelocityOffsetDist);
                Vector3 computedOffset = offsetDir * targetOffsetDist;

                if (finalSpeed < StopThreshold)
                {
                    if (!_userIsStopped[i])
                    {
                        _userFrozenOffset[i] = computedOffset;
                        _userIsStopped[i] = true;
                    }

                    _userFrozenOffset[i] = Vector3.Lerp(_userFrozenOffset[i], Vector3.zero, dt * 0.5f);
                    computedOffset = _userFrozenOffset[i];
                }
                else
                {
                    _userIsStopped[i] = false;
                }

                _userLastPos[i] = currentPos;
                _userCurrentOffset[i] = computedOffset;
            }
        }
    }
}
