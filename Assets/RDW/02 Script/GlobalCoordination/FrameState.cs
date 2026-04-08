using System.Collections.Generic;
using UnityEngine;

namespace _GCM
{
    public class FrameState
    {
        public FrameState(IReadOnlyList<GameObject> physicalUsers)
        {
            PhysicalUsers = physicalUsers;
        }

        public IReadOnlyList<GameObject> PhysicalUsers { get; }
    }
}
