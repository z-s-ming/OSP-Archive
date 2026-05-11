using System;
using System.Collections.Generic;

public class ProactiveResetIntentDispatcher
{
    private readonly IProactiveResetCooldownPolicy cooldownPolicy;

    public ProactiveResetIntentDispatcher(IProactiveResetCooldownPolicy cooldownPolicy)
    {
        this.cooldownPolicy = cooldownPolicy;
    }

    public void Dispatch(List<ProactiveResetIntent> intents, RedirectedUnit[] units, int frameIndex)
    {
        if (intents == null || intents.Count == 0 || units == null)
            return;

        for (int i = 0; i < intents.Count; i++)
        {
            ProactiveResetIntent intent = intents[i];
            if (intent.SelectedUserId < 0 || intent.SelectedUserId >= units.Length)
                continue;
            if (intent.OtherUserId < 0 || intent.OtherUserId >= units.Length)
                continue;

            RedirectedUnit selectedUnit = units[intent.SelectedUserId];
            RedirectedUnit otherUnit = units[intent.OtherUserId];
            if (selectedUnit == null || selectedUnit.GetRealUser() == null || otherUnit == null || otherUnit.GetRealUser() == null)
                continue;

            if (!string.Equals(selectedUnit.GetStatus(), "IDLE", StringComparison.Ordinal))
                continue;

            if (cooldownPolicy != null && cooldownPolicy.IsCoolingDown(intent.SelectedUserId, frameIndex))
                continue;

            if (cooldownPolicy != null && cooldownPolicy.IsPairCoolingDown(intent.SelectedUserId, intent.OtherUserId, frameIndex))
                continue;

            selectedUnit.SetProactiveUserResetIntent(
                otherUnit.GetRealUser(),
                intent.ResetDirection,
                intent.IsBidirectionalUserResetEvent,
                intent.SelectedUserId,
                intent.OtherUserId,
                intent.OriginTriggerId,
                intent.OriginCandidateId,
                intent.DecisionId);
        }
    }
}
