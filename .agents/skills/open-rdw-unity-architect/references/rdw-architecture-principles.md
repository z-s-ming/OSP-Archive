# RDW Architecture Principles

Use these principles when reviewing or planning changes for this repo-scoped Unity/C# redirected walking project.

## Research Toolchain Fit

Favor architecture that supports experiments:

- Scene, physical space, virtual space, user count, controller parameters, reset parameters, and random seed should be configurable and traceable.
- Controllers should be replaceable without changing logging, episode lifecycle, or visualization.
- Simulation mode should be able to run without manual Inspector state changes.
- Visualization should observe state; it should not define control or metric semantics.
- Result export should include enough context to reproduce the run.
- Multi-user logic should be first-class, not bolted onto single-user counters.
- Benchmark comparison should be explicit about shared code and method-specific code.

## Multiplayer RDW State Model

Keep these concepts separate in code and logs:

- Physical pose and heading.
- Virtual pose, target, and desired direction.
- Current RDW controller output.
- Partition or Voronoi ownership state.
- Prediction state and horizon.
- Risk score and risk category.
- Reset trigger state.
- Reset execution state, including active/passive and bilateral/single-user reset.
- Collision state, including user-user and user-wall events.

Avoid ambiguous counters such as a single `resetCount` or `collisionCount` when the experiment needs to distinguish cause, type, participants, and timing.

## Manager Boundaries

A global manager may:

- Own scene-level lifecycle.
- Wire services and strategy instances.
- Advance the simulation clock.
- Coordinate episode start/end.
- Dispatch per-frame phases in a predictable order.

A global manager should not directly contain detailed algorithms for:

- Prediction.
- Risk scoring.
- Partition/Voronoi updates.
- Local target selection.
- Reset arbitration and execution details.
- CSV row construction and aggregation.
- Visualization state derivation.

Do not split code merely to look architectural. Split only when a boundary protects experiment validity, baseline fairness, testability, or replaceability.

## Baseline Fairness

When reviewing APF_OSP, OSP, ARC, S2C, TAPF, or other baselines:

- Confirm that proposed-method-only prediction, risk, trigger windows, cooldowns, reset durations, or coordination hints are not shared with baselines unless the benchmark definition explicitly says so.
- Confirm that baseline parameters are logged and distinguishable from proposed-method parameters.
- Confirm that common utilities do not silently alter baseline behavior.
- Confirm that any metric bug fix affects all methods consistently, or is documented as a change in experiment semantics.

## Reproducibility

Check for:

- Controlled random seed and seed logging.
- Deterministic episode initialization where feasible.
- Clear episode termination and cleanup.
- Per-episode CSV buffers reset or explicitly accumulated with identifiers.
- Stable CSV headers and units.
- Config snapshot exported with each experiment run.
- Logger behavior not depending on temporary Inspector edits that are absent from output metadata.

## Logging Semantics To Protect

Do not accidentally change the meaning of:

- Total distance walked.
- Redirected distance or gain-integrated distance.
- Reset count by type.
- Active reset vs passive reset.
- Single-user reset vs bilateral user reset.
- User-user collision count.
- User-wall collision count.
- Trigger count vs executed reset count.
- Cooldown and reset duration timing.
- Per-episode vs whole-run aggregate metrics.
- Baseline/proposed method identifiers and parameter columns.

## Unity Lifecycle Review

When inspecting lifecycle code:

- Identify which logic runs in `Awake`, `Start`, `OnEnable`, `Update`, `FixedUpdate`, coroutines, collision callbacks, and application quit hooks.
- Check whether physics-dependent logic is in a physics-consistent phase.
- Check whether frame-order dependencies affect logs or reset decisions.
- Check whether coroutines can overlap across episode boundaries.
- Check whether static fields or singletons retain state between runs.

## Small-Step Refactor Pattern

Prefer changes in this order:

1. Add read-only architecture documentation or comments only where they prevent mistakes.
2. Extract pure metric calculation or row construction without changing fields.
3. Extract state repositories or DTOs while preserving serialized field names.
4. Extract decision services behind existing method calls.
5. Add config snapshots and explicit parameter logging.
6. Only then consider deeper controller/reset/prediction boundaries.

After every code change, verify compile status if possible, inspect Unity serialized fields that may be affected, and compare a before/after sample log for unchanged columns and intended values.
