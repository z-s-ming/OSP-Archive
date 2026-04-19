# Bidirectional Recoverability Reset

## Purpose

This reset module is designed for the bidirectional user-reset problem.
It is implemented as a new reset type instead of a risk module because its role is to decide whether a bilateral user collision should enter the reset pipeline.

The new reset type is:

- `ResetType.APF_R_Turn_OSP_BiRecoverability`

It keeps the existing `APF_R_Turn_OSP` wall-reset behavior and adds a recoverability gate in the bilateral `USER_RESET` trigger path.

## Core Idea

When two users collide while facing opposite directions, the system treats this as a bilateral-reset candidate.
Before triggering `USER_RESET`, the new resetter evaluates whether the collision is still avoidable by continuous maximum-curvature turning.

The evaluator checks the four extreme bilateral responses:

- `(+1, +1)`
- `(+1, -1)`
- `(-1, +1)`
- `(-1, -1)`

where:

- `+1` means maximum left turn
- `-1` means maximum right turn

For each pair, it computes the minimum short-horizon separation margin:

`m_sep^(sigmaA, sigmaB) = min_t ( d^(sigmaA, sigmaB)(t) - d_safe )`

and then takes:

`M_sep = max over all four pairs`

Decision rule:

- if `M_sep >= 0`, the bilateral collision is considered recoverable
- if `M_sep < 0`, the bilateral collision is considered unrecoverable

## Behavior in This Project

The new resetter changes only bilateral `USER_RESET` triggering:

- unilateral user reset stays unchanged
- wall reset stays the same as `APF_R_Turn_OSP`

When a bilateral collision candidate is detected:

1. Build both users' current states from the live `RedirectedUnit`
2. Estimate each user's speed from its resetter translation speed
3. Estimate each user's maximum curvature as:

`kappa_max = omega_max / v`

where:

- `omega_max` is the configured resetter rotation speed in radians per second
- `v` is the configured resetter translation speed

4. Predict both users under four maximum-curvature response pairs over a short horizon
5. Compute `M_sep`
6. Gate the reset:

- `Recoverable == true`:
  do not trigger bilateral `USER_RESET`
- `Recoverable == false`:
  keep the original bilateral reset flow

## Files

The implementation is located under the reset subsystem:

- `Assets/RDW/02 Script/RDW_Scripts/Resetter/APF_R_BiRecoverability_Resetter_OSP.cs`
- `Assets/RDW/02 Script/RDW_Scripts/Resetter/BidirectionalCollisionRecoverabilityEvaluator.cs`
- `Assets/RDW/02 Script/RDW_Scripts/Resetter/BidirectionalCollisionRecoverabilityAssessment.cs`

The reset enum mapping is updated in:

- `Assets/RDW/02 Script/RDW_Scripts/Setting/UnitSetting.cs`

The base reset hooks were opened for extension in:

- `Assets/RDW/02 Script/RDW_Scripts/Resetter/Resetter.cs`

## Current Parameters

Current built-in parameters in the new resetter:

- horizon: `1.5` seconds
- sample count: `60`
- safety buffer: `0.1`

Safe distance is computed as:

- `radiusA + radiusB + safetyBuffer`

If a user is not represented as `Circle2D`, the evaluator falls back to a radius of `0.5`.

## Notes

- This module is a reset-oriented gate, not a motion planner.
- It does not yet apply the best turn pair back into user control.
- Its current role is only to decide whether bilateral reset is necessary.
- If later needed, the same evaluator can be extended to output the best turn pair for a recovery controller.
