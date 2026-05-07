---
name: open-rdw-unity-architect
description: Review, understand, plan refactors for, and cautiously optimize this repo-scoped Unity/C# redirected walking project. Use for Unity RDW architecture reviews; multiplayer RDW control flow checks; GlobalCoordinationManager or similar manager optimization; prediction, risk, Voronoi/partition, target selection, reset, logging, evaluator, visualizer refactors; experiment CSV/log reliability checks; reset/collision/trigger/distance metric validation; Unity Update/FixedUpdate/Coroutine lifecycle review; baseline fairness checks for APF_OSP, OSP, ARC, S2C, TAPF, proposed methods; and OpenRDW/OpenRDW2-inspired architecture planning without copying their code structure.
---

# Open RDW Unity Architect

## Scope

Use this skill only for the current Open-RDW / OSP-Archive Unity/RDW repository. Treat it as a research-code architecture and experiment-integrity review workflow, not as permission to rewrite the project.

Prefer OpenRDW/OpenRDW2-style research-toolchain ideas: configurable physical/virtual spaces, swappable controllers, simulation support, visualization, result export, multi-user experiments, and benchmark comparisons. Do not require this repo to copy OpenRDW code structure.

For deeper architecture criteria, read `references/rdw-architecture-principles.md` when the task involves nontrivial review or refactor planning.

## Default Workflow

Always review before editing code. If the user asks for a plan, review, audit, architecture map, or "how should we refactor", do not modify code.

1. Confirm the Git root with `git rev-parse --show-toplevel` if the current directory may not be the repo root.
2. Scan Unity `Assets` for RDW scripts and locate manager/controller/resetter/logger/evaluator/visualizer/predictor/partition classes.
3. Find experiment output paths, CSV writing logic, episode initialization/end logic, and configuration capture.
4. Find baseline and proposed-method entry points and the switch or branching logic between them.
5. Build an architecture map before judging problems.
6. Classify findings by experiment risk first, maintainability second.
7. Give small, behavior-preserving refactor steps. Avoid one-shot manager rewrites.

Use `rg` / `rg --files` first. Useful search terms include:

```text
GlobalCoordinationManager
RDW
Redirected
Controller
Reset
Resetter
Logger
CSV
Episode
Collision
Trigger
Voronoi
Partition
Prediction
Predictor
Risk
APF
OSP
ARC
S2C
TAPF
```

## Architecture Map To Produce

List these chains explicitly when reviewing:

- Main orchestration entry points.
- Per-frame call chain, including `Update`, `FixedUpdate`, coroutines, and event callbacks.
- Episode lifecycle: initialization, seed/config selection, start, termination, cleanup, output.
- Reset decision chain: trigger, arbitration, executor, cooldown/duration, active/passive distinction.
- Collision decision chain: user-user, user-wall, trigger-only contacts, evaluator/statistics path.
- Logger write chain: metric producers, aggregation, CSV headers/rows, per-episode reset, final export.
- Baseline/proposed branch points and any shared helper logic.

## Review Priorities

Judge whether the code supports replaceable algorithm modules:

- RDW controller
- resetter
- predictor
- risk evaluator
- partition updater
- local target selector
- logger/evaluator/visualizer

Flag manager-heavy designs when a single manager directly owns prediction, risk, partitioning, reset arbitration, target selection, logging, and visualization details. A manager such as `GlobalCoordinationManager` should mainly coordinate lifecycle and dependencies.

For multiplayer RDW, verify that user state, physical position, virtual target, heading, and reset state are distinct concepts. Do not let active reset, passive reset, bilateral user reset, user-user collision, and user-wall collision collapse into one ambiguous counter.

For reproducibility, verify controllable random seeds, clear episode start/end, no cumulative CSV pollution across episodes, logger independence from temporary Inspector state, traceable baseline/proposed parameters, and a configuration snapshot in outputs.

For baseline fairness, protect APF_OSP, OSP, ARC, S2C, TAPF, and other baselines from implicit enhancements. Proposed-method-only information such as prediction horizon, trigger window, cooldown, reset duration, risk thresholds, and extra coordination signals must be logged and not leaked into baselines.

## Required Output Format

When reviewing, answer in this exact structure:

```markdown
# 1. Current Structure Judgment
State whether the project appears manager-heavy, controller-centric, service-layered, logger-coupled, experiment-driven, or a combination.

# 2. Call Chain Map
List the main orchestration, per-frame, episode, reset, collision, logger, and baseline/proposed chains.

# 3. Key Issues
Group findings as:
- P0: issues that can make experiment results wrong.
- P1: issues that can make strategy comparisons unfair.
- P2: issues that make the code hard to maintain or extend.
- P3: naming, style, comment, and readability issues.

# 4. Recommended Refactor Order
Give 3 to 6 small steps. Do not propose a single large rewrite.

# 5. Experiment Metrics To Protect
List statistics and log semantics that must not change accidentally.

# 6. Next Executable Task
Give the smallest safe next change or verification task.
```

Each concrete recommendation must include: current problem, why it affects RDW experiments or maintainability, recommended change, involved files, risk, and verification method.

## Editing Rules

Only edit code after the user explicitly asks for implementation. Then:

- Change one module boundary or one bug at a time.
- Preserve external behavior unless the user explicitly requests a metric or behavior change.
- Do not rewrite the manager wholesale.
- Do not change experiment statistics definitions without explicit approval.
- Do not rename public serialized Unity fields unless there is a migration/compatibility plan.
- Do not casually edit prefabs/scenes or delete log fields.
- Keep experiment analysis separate from real-time control logic.
- Explain which Unity Inspector parameters need checking after edits.
- Run available compile/test scripts when practical. If Unity compilation cannot run, state what remains unverified.

Suggested service boundaries, only when they reduce real complexity:

- `EpisodeService`
- `SimulationClock`
- `UserStateRepository`
- `PredictionService`
- `RiskEvaluationService`
- `PartitionUpdateCoordinator`
- `ResetDecisionService`
- `LocalTargetService`
- `ExperimentLogger`
- `ResultAggregator`
