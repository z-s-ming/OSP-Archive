# Interface Map (T1)

Scope: map existing integration points for the target control chain:

short-term occupancy prediction
-> risk assessment
-> dynamic Voronoi partition optimization
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
| VoronoiPartitioner | `FrameState`, offsets, dt, bounds | `PartitionResult` | per frame | High | Yes (dynamic Voronoi optimization hook) |
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

3. Dynamic Voronoi optimization
- insert inside/adjacent to `VoronoiPartitioner.Build(...)`
- keep `PartitionResult` contract stable unless extra risk fields are strictly needed

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

## Decision Summary (keep/modify)

- Keep as-is first: `PredictionEvaluator`, `EpisodeService`, `GM_DataRecord`, `Resetter`, `SimulationController`.
- Extend with controlled hooks: `GlobalCoordinationManager`, `VelocityPredictor`, `VoronoiPartitioner`, `APFRedirector_OSP`/`S2CRedirector`, `PartitionResult` (only if extra fields are unavoidable).

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
