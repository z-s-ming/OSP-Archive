# Interface Map (T1)

Scope: map existing integration points for the target control chain:

short-term occupancy prediction
-> risk assessment
-> event-triggered local Voronoi partition update
-> in-cell local safe target selection
-> RDW gain/steering safety correction
-> experiment logging and comparative evaluation

## T1.1 Main Loop Call Chain (current code)

Primary owner: Assets/RDW/02 Script/GlobalCoordination/GlobalCoordinationManager.cs

1. Episode reset and startup
- `ResetEpisode()` at line 364
- calls `SetResetParameters()` at line 369
- starts evaluator episode `predictionEvaluator.BeginEpisode(...)` at line 373

2. Per-frame state capture (FixedUpdate)
- `ProcessStep()` at line 381
- frame state capture: `stateCollector.CaptureFrameState(...)` at line 390

3. Predictor update and prediction registration
- predictor update: `velocityPredictor.Update(...)` at line 400
- sample register: `predictionEvaluator.RegisterPredictions(...)` at line 405
- sample resolve: `predictionEvaluator.ResolveDuePredictions(...)` at line 418

4. Episode-end decision and evaluation export
- end check: `episodeService.ShouldEndEpisode(...)` at line 426
- evaluator close: `predictionEvaluator.EndEpisode(true)` at line 435
- episode samples CSV: `predictionEvaluator.ExportEpisodeSamplesCsv(...)` at line 448
- overall summary CSV: `predictionEvaluator.ExportOverallSummaryCsv(...)` at line 468
- episode finalize: `episodeService.FinalizeEpisode(...)` at line 454

5. Voronoi build and partition consume
- partition build: `voronoiPartitioner.Build(...)` at line 484
- consume partition: `ApplyPartitionResult(...)` at line 492 / method at line 505

6. Steering target injection and RDW simulation
- per-user center/region consume in `ApplyPartitionResult(...)`
- S2C center set: `S2CRedirector.SetCenterPoint(...)` at line 532
- run RDW: `RDWSimulationManager.instance.SimulateRDW()` at line 501

7. Reset decision in RDW layer
- in `RedirectedUnit.CheckCurrentStatus(...)` line 100
- wall reset check: `resetter.NeedWallReset(...)` line 173
- user reset check: `resetter.NeedUserReset(...)` line 194

## T1.2 Reusable Classes and Responsibilities

Required classes:

- GlobalCoordinationManager
  - Responsibility: orchestration of per-frame pipeline, evaluator lifecycle, Voronoi building/consumption.
  - Current role: main integration layer.

- VelocityPredictor
  - Responsibility: velocity smoothing, offset generation, prediction sequence generation.
  - Inputs: users, dt, predictor params.
  - Outputs: offsets and horizon predictions.

- PredictionEvaluator
  - Responsibility: register/resolve prediction samples, compute episode and overall stats, export CSV.
  - Inputs: sampled predictions and realized user positions.
  - Outputs: summaries and CSV files.

- VoronoiPartitioner
  - Responsibility: update seed points, build Voronoi, output per-user region vertices/centroids/areas.
  - Inputs: frame state, velocity offsets, room bounds.
  - Outputs: `PartitionResult`.

- PartitionResult
  - Responsibility: data carrier for seed points, centroids, areas, region polygons, edge vertices.
  - Inputs: filled by `VoronoiPartitioner`.
  - Outputs: consumed by visualizer/redirectors.

Steering/gain related classes:

- RedirectedUnit
  - Responsibility: status machine (IDLE/WALL_RESET/USER_RESET), call redirector and controller move.

- Redirector (base), GainRedirector, SteerToTargetRedirector, S2CRedirector, APFRedirector_OSP
  - Responsibility: compute redirection type and magnitude (translation/rotation/curvature), and steering target policy.

- SimulationController
  - Responsibility: apply computed redirection to real user (`RealMove(...)`).

- Resetter
  - Responsibility: wall/user reset decision and reset execution.

## T1.3 Interface Table

| Class | Main Inputs | Main Outputs | Call Frequency | Reuse | Need Change |
|---|---|---|---|---|---|
| GlobalCoordinationManager | Unity FixedUpdate, `StateCollector`, `VelocityPredictor`, `VoronoiPartitioner`, `PredictionEvaluator` | per-frame pipeline side effects; partition publish; RDW trigger | per frame + per episode | High | Yes (integration points only) |
| VelocityPredictor | physical users, dt, params (`AlphaMax`, `VMax`, etc.) | offsets, prediction sequence | per frame | High | Maybe (if new predictor/risk uses uncertainty) |
| PredictionEvaluator | sampled predictions + realized positions | episode stats, overall stats, CSV exports | sampled frames + episode end | High | No (keep stable for baseline comparability) |
| VoronoiPartitioner | `FrameState`, offsets, dt, bounds | `PartitionResult` | per frame | High | Yes (event-triggered local partition update hook) |
| PartitionResult | built data from partitioner | region/centroid carrier to consumers | per frame data object | High | Maybe (if risk fields are needed) |
| RedirectedUnit | redirector outputs, resetter decisions | movement execution and reset transitions | per user per frame | High | No direct logic rewrite (unless strictly required) |
| SteerToTargetRedirector / S2CRedirector | user pose, target policy input (`centerPoint`) | `GainType` + steering magnitude | per user per frame | High | Yes (safe steering correction hook) |
| APFRedirector_OSP | user pose, partition vertices from `dic_AreaSegmentsVertex` | `GainType` + steering magnitude | per user per frame | High | Yes (cell-local safe target selection hook) |
| SimulationController | `(GainType, degree)` | real user transform update | per user per frame | Medium | No (treat as actuator layer) |
| Resetter | user pose, space boundaries, user interactions | reset decision + reset action | per user per frame | High | No (stability-sensitive) |
| EpisodeService | distances and reset counters | episode finalize + GM_DataRecord batch writes | per frame tick + episode end | High | No (stability-sensitive) |

## T1.4 Do-Not-Touch Zones (for now)

Freeze these paths to reduce chain bugs:

1. Episode management
- `Assets/RDW/02 Script/GlobalCoordination/EpisodeService.cs`
- `Assets/RDW/02 Script/GlobalCoordination/GlobalCoordinationManager.cs` episode lifecycle block around lines 426-479

2. CSV and batch export
- `Assets/RDW/02 Script/GlobalCoordination/PredictionEvaluator.cs` export methods around lines 428-523
- `Assets/RDW/02 Script/GM_DataRecord.cs` and `EpisodeService.FlushBatchToRecord()` around line 132+

3. Prediction evaluation main flow
- `GlobalCoordinationManager.ProcessStep()` prediction block lines 403-418
- `PredictionEvaluator` lifecycle: `BeginEpisode/ClearEpisodeData/EndEpisode`

## Recommended Insertion Layers for New Method

To answer "where should new methods be inserted":

1. Short-term occupancy prediction
- insert in predictor layer
- best point: after `CaptureFrameState(...)` and before `voronoiPartitioner.Build(...)`
- files: `GlobalCoordinationManager.ProcessStep`, plus `VelocityPredictor` extension/new predictor adapter

2. Risk assessment
- insert in coordination layer right after prediction registration/resolution and before Voronoi build
- pass risk summary into partition optimizer and steering policy

3. Event-triggered local Voronoi partition update
- insert inside/adjacent to `VoronoiPartitioner.Build(...)`, but keep the trigger logic outside the core builder when possible
- update only the triggered user's seed, preserve other users' seeds, then rebuild the partition once
- do not treat physical boundary danger as the main driver of this phase
- keep `PartitionResult` contract stable unless extra update-state fields are strictly needed

4. In-cell local safe target selection
- insert in redirector policy layer
- candidates: `APFRedirector_OSP.GetWandT(...)`, `S2CRedirector.PickSteeringTarget()`
- input source: `dic_AreaSegmentsVertex` from `GlobalCoordinationManager.ApplyPartitionResult(...)`

5. RDW gain/steering safety correction
- insert in `SteerToTargetRedirector.ApplyRedirection(...)` or redirector subclasses
- avoid changing `SimulationController.RealMove(...)` unless actuator constraints are required

6. Experiment record and comparison evaluation
- do not rewrite evaluator/export flow
- add new metrics as additive columns or parallel logs, preserving existing baseline outputs

## Phase 4 Redefined Scope

Phase 4 is narrowed to a local, event-driven partition repair stage. It is responsible for correcting partition mismatch and local crowding in shared space, but it is not the layer that saves a user who is already too close to the physical boundary.

### Phase 4 resolves

- mismatch between the current cell and the user's short-term occupied region
- local crowding between Voronoi neighbors
- overly abrupt or unstable partition changes
- small corrective seed shifts that make future partitioning more consistent

### Phase 4 does not resolve directly

- immediate physical-boundary danger
- cases that already require strong steering or pre-reset intervention
- global candidate search across all users
- continuous nonlinear optimization
- APF, gain, or reset policy changes

### Trigger policy

- monitor `cellRisk` and `neighborRisk` every frame
- trigger only when the user is moving and the configured cooldown has elapsed
- trigger only after the relevant risk persists for consecutive frames
- do not trigger on physical boundary risk alone

Recommended defaults:

- `tauCellRisk = 0.6`
- `tauNeighborRisk = 0.6`
- `persistFramesCell = 3`
- `persistFramesNeighbor = 3`
- `seedUpdateCooldown = 0.75s`

### Seed update policy

- compute a trend direction from the predicted occupied-band center to the current user position
- compute a neighbor repulsion direction from risky Voronoi neighbors
- combine both directions into a bounded seed shift
- clamp the per-update shift and the cumulative offset from the user
- accept the update only when the recalculated local risk improves

Recommended defaults:

- `seedTrendWeight = 0.6`
- `seedNeighborWeight = 0.4`
- `seedStepLow = 0.05m`
- `seedStepMedium = 0.10m`
- `seedStepHigh = 0.15m`
- `maxSeedShiftPerUpdate = 0.20m`
- `maxSeedOffsetFromUser = 0.60m`

### Implementation tasks

T4.1 Add `SeedUpdateState`

- suggested file: `Assets/RDW/02 Script/GlobalCoordination/RiskDrivenSeedUpdateState.cs`
- fields: `currentSeed`, `committedSeed`, `lastUpdateTime`, `cellRiskPersistCount`, `neighborRiskPersistCount`, `pendingUpdate`

T4.2 Add `PartitionUpdateTriggerEvaluator`

- suggested file: `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdateTriggerEvaluator.cs`
- responsibility: maintain persistence counters, cooldown checks, and trigger decisions
- input: per-user risk values, speed, current time, config
- output: `ShouldTriggerUpdate(userId)`

T4.3 Add `RiskDrivenSeedUpdater`

- suggested file: `Assets/RDW/02 Script/GlobalCoordination/RiskDrivenSeedUpdater.cs`
- responsibility: compute the proposed seed shift from trend and neighbor repulsion
- input: user position, current seed, predicted occupied-band center, neighbor list, pair risk, config
- output: `proposedSeed`

T4.4 Integrate Phase 4-lite into `GlobalCoordinationManager`

- initialize `SeedUpdateState[]`
- evaluate triggers after risk assessment each frame
- update only the triggered user's seed
- rebuild Voronoi with the updated seed set
- recompute local risk for the triggered user
- accept the update only if local risk improves, otherwise roll back

T4.5 Add configuration fields

- `enableRiskDrivenPartitionUpdate`
- `tauCellRisk`
- `tauNeighborRisk`
- `persistFramesCell`
- `persistFramesNeighbor`
- `seedUpdateCooldown`
- `seedTrendWeight`
- `seedNeighborWeight`
- `seedStepLow`
- `seedStepMedium`
- `seedStepHigh`
- `maxSeedShiftPerUpdate`
- `maxSeedOffsetFromUser`

T4.6 Add logs

- output file: `CGnA_DataLog/partitionUpdate/partition_update_log_*.csv`
- record: frame, time, userId, triggerType, oldSeed, newSeed, seedShiftDist, cellRiskBefore/After, neighborRiskBefore/After, accepted

T4.7 Add visualization

- show current seed points
- show the update arrow from old seed to new seed
- show the triggered user id
- show the trigger reason (`cell` or `neighbor`)
- show whether the update was accepted

### Implementation status

The Phase 4-lite pipeline is now implemented in code with the following runtime pieces:

- `Assets/RDW/02 Script/GlobalCoordination/GlobalCoordinationManager.cs`
  - orchestrates the trigger, candidate rebuild, acceptance check, logging, and gizmo overlay
- `Assets/RDW/02 Script/GlobalCoordination/RiskDrivenSeedUpdateState.cs`
  - stores per-user seed state, trigger type, and last attempt metadata
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdateTriggerEvaluator.cs`
  - applies persistence and cooldown checks before a seed update is attempted
- `Assets/RDW/02 Script/GlobalCoordination/RiskDrivenSeedUpdater.cs`
  - proposes bounded local seed shifts from trend direction and neighbor repulsion
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdateLogger.cs`
  - writes `partition_update_log_*.csv` under `CGnA_DataLog/partitionUpdate/`
- `Assets/RDW/02 Script/GlobalCoordination/PartitionUpdateVisualizer.cs`
  - draws current seeds and accepted/rejected update arrows in the scene view
- `Assets/RDW/02 Script/GlobalCoordination/VoronoiPartitioner.cs`
  - exposes seed getters/setters for local update and rollback
- `Assets/RDW/02 Script/GlobalCoordination/Risk/PartitionRiskEvaluator.cs`
  - exposes temporal snapshots so candidate evaluation can be rolled back safely
- `Assets/RDW/02 Script/GlobalCoordination/Prediction/VelocityPredictor.cs`
  - exposes current speed for trigger evaluation

Runtime behavior summary:

- the manager evaluates `cellRisk` and `neighborRisk` after the current partition is built
- if a user stays above threshold long enough and is moving, a local seed update is proposed
- the candidate partition is rebuilt once, then accepted only if local risk improves
- rejected candidates restore the prior seed state and evaluator temporal state
- physical-boundary emergency handling remains outside Phase 4

### Acceptance criteria

- only users that satisfy the trigger conditions update their seeds
- users that do not satisfy the conditions keep their seeds unchanged
- the partition can be rebuilt after an accepted update
- the phase remains real-time because it does not do global candidate search
- a trigger caused by physical boundary danger alone does not enter Phase 4
- at least one of `cellRisk` or `neighborRisk` decreases after a successful update

### Recommended execution order

1. Add the new seed-update state and evaluator classes.
2. Wire the trigger evaluation into `GlobalCoordinationManager`.
3. Add the bounded seed updater and rollback path.
4. Add logging and visualization.
5. Verify that the update does not interfere with APF, steering, or reset logic.

### Confirmation and execution flow

1. First confirm the phase boundary: Phase 4 only handles partition mismatch and local crowding, not physical-boundary emergency recovery.
2. Freeze the non-goals before coding: no global candidate search, no continuous optimizer, no APF or reset rewrite.
3. Execute the implementation tasks in the order above, starting from state and trigger evaluation.
4. After each seed update, rebuild the partition once, then check local risk improvement before accepting the update.
5. Keep the change set minimal and local; if a later layer is needed for steering or safety, leave that responsibility outside Phase 4.

## Decision Summary (keep/modify)

- Keep as-is first: `PredictionEvaluator`, `EpisodeService`, `GM_DataRecord`, `Resetter`, `SimulationController`.
- Extend with controlled hooks: `GlobalCoordinationManager`, `VelocityPredictor`, `VoronoiPartitioner`, `APFRedirector_OSP`/`S2CRedirector`, `PartitionResult` only if extra fields are unavoidable, plus the new Phase 4-lite helper classes.

This map is intended to be the single reference before coding, to avoid speculative edits.

## Directory Convention (GlobalCoordination)

To keep the coordination layer maintainable as new modules grow, use this folder policy:

1. Prediction-related files go to:
- `Assets/RDW/02 Script/GlobalCoordination/Prediction/`
- examples: occupancy model/visualizer, horizon selector, future predictor adapters.

2. Risk-related files go to:
- `Assets/RDW/02 Script/GlobalCoordination/Risk/`
- examples: risk evaluator, risk logger, risk visualizer, risk metric extensions.

3. Keep orchestration and stable core files at root `GlobalCoordination/`:
- examples: `GlobalCoordinationManager`, `VoronoiPartitioner`, `PartitionResult`, `StateCollector`, `EpisodeService`, `PredictionEvaluator`.

4. New metric/feature rule:
- If a file's primary responsibility is prediction data generation/interpretation, place it in `Prediction/`.
- If a file's primary responsibility is safety/risk computation, logging, or visualization, place it in `Risk/`.

5. Unity asset integrity rule:
- move `.cs` and matching `.meta` together.
- if IDE project path references are explicit (e.g., `.csproj`), update them after move.
