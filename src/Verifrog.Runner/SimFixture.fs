namespace Verifrog.Runner

open System.IO
open Verifrog.Sim
open Verifrog.Sim.Config

/// Checkpoint levels for test setup acceleration.
/// Tests restore from the deepest checkpoint that covers their setup needs.
type Level =
    | PostReset     // Level 0: after reset
    | PostConfig    // Level 1: config loaded (user-defined)
    | PostWeights   // Level 2: weights loaded (user-defined)
    | PostInference // Level 3: inference complete (user-defined)

/// Manages a shared Verilator simulation instance with checkpoint/restore.
/// Extracted from khalkulo's SimFixture, generalized to read config from TOML.
module SimFixture =

    /// Create a fresh simulation instance, reset it, and return it.
    /// Auto-discovers verifrog.toml from the current directory for config
    /// (signal path prefixing, memory/register accessors). Falls back to
    /// no-config if not found.
    let create () : Sim =
        Sim.SuppressDisplay(true)
        let sim =
            match Config.findToml (Directory.GetCurrentDirectory()) with
            | Some tomlPath -> Sim.Create(Config.parse tomlPath)
            | None -> Sim.Create()
        sim.Reset()
        sim

    /// Create a simulation from TOML config, reset it, and return it.
    let createFromConfig (config: VerifrogConfig) : Sim =
        Sim.SuppressDisplay(true)
        let sim = Sim.Create(config)
        sim.Reset()
        sim

    /// Create a simulation from a verifrog.toml path, reset it, and return it.
    let createFromToml (tomlPath: string) : Sim =
        let config = Config.parse tomlPath
        createFromConfig config

    /// Create a simulation with a Level 0 (post-reset) checkpoint.
    let createWithCheckpoint () : Sim * CheckpointHandle =
        let sim = create ()
        let cp = sim.SaveCheckpoint("L0_PostReset", "Post-reset")
        (sim, cp)

    /// Create a simulation from config with a Level 0 checkpoint.
    let createFromConfigWithCheckpoint (config: VerifrogConfig) : Sim * CheckpointHandle =
        let sim = createFromConfig config
        let cp = sim.SaveCheckpoint("L0_PostReset", "Post-reset")
        (sim, cp)

    /// Get the checkpoint name for a level.
    let checkpointName (level: Level) =
        match level with
        | PostReset     -> "L0_PostReset"
        | PostConfig    -> "L1_PostConfig"
        | PostWeights   -> "L2_PostWeights"
        | PostInference -> "L3_PostInference"

    /// Restore a simulation to a named checkpoint level.
    let restore (sim: Sim) (level: Level) =
        sim.RestoreCheckpoint(checkpointName level)

    /// Save a checkpoint at the specified level.
    let saveLevel (sim: Sim) (level: Level) =
        let name = checkpointName level
        sim.SaveCheckpoint(name, $"Level {name}")
