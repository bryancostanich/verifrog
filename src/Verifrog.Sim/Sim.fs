namespace Verifrog.Sim

open System
open System.Collections.Generic
open Verifrog.Sim.Interop
open Verifrog.Sim.Config

/// Result type for signal operations
type SimResult<'T> =
    | Ok of 'T
    | Error of string

/// Opaque checkpoint handle with metadata
type CheckpointHandle = {
    Ptr: nativeint
    Cycle: uint64
    Description: string
}

/// TOML-driven memory accessor for a named memory region.
/// Resolves {bank} in the path template to produce signal paths.
type MemoryAccessor internal (ctx: nativeint, config: MemoryConfig) =
    let resolvePath (bank: int) (addr: int) =
        let basePath = config.Path.Replace("{bank}", string bank)
        $"{basePath}[{addr}]"

    member _.Name = config.Name
    member _.Banks = config.Banks
    member _.Depth = config.Depth
    member _.Width = config.Width

    member _.Read(bank: int, addr: int) : SimResult<int64> =
        let path = resolvePath bank addr
        let mutable value = 0L
        let rc = sim_read(ctx, path, &value)
        if rc = 0 then Ok value
        else Error $"Memory read failed: {config.Name} bank={bank} addr={addr} (path={path})"

    member _.Write(bank: int, addr: int, value: int64) : SimResult<unit> =
        let path = resolvePath bank addr
        let rc = sim_write(ctx, path, value)
        if rc = 0 then Ok ()
        else Error $"Memory write failed: {config.Name} bank={bank} addr={addr} (path={path})"

/// TOML-driven register accessor for named registers.
/// Maps register names to addresses in a register file memory.
type RegisterAccessor internal (ctx: nativeint, regConfig: RegisterConfig, entry: RegisterEntry) =
    let path = $"{regConfig.Path}[{entry.Address}]"

    member _.Name = entry.Name
    member _.Address = entry.Address

    member _.Read() : SimResult<int64> =
        let mutable value = 0L
        let rc = sim_read(ctx, path, &value)
        if rc = 0 then Ok value
        else Error $"Register read failed: {entry.Name} addr=0x{entry.Address:X} (path={path})"

    member _.Write(value: int64) : SimResult<unit> =
        let rc = sim_write(ctx, path, value)
        if rc = 0 then Ok ()
        else Error $"Register write failed: {entry.Name} addr=0x{entry.Address:X} (path={path})"

/// High-level wrapper around the Verilator simulation model.
/// Extracted from khalkulo's SimDebugger.Sim, generalized for any design.
/// Design-specific members (WgtWrite, ActWrite, RegfileRead, MAC accessors)
/// are replaced by TOML-driven Memory and Register accessors.
type Sim private (ctx: nativeint, config: VerifrogConfig option) =

    let mutable disposed = false
    let checkpoints = Dictionary<string, CheckpointHandle>()
    let memories = Dictionary<string, MemoryAccessor>()
    let registers = Dictionary<string, RegisterAccessor>()

    do
        // Build memory accessors from config
        match config with
        | Some cfg ->
            for mem in cfg.Memories do
                memories.[mem.Name] <- MemoryAccessor(ctx, mem)
            // Build register accessors from config
            match cfg.Registers with
            | Some regCfg ->
                for entry in regCfg.Map do
                    registers.[entry.Name] <- RegisterAccessor(ctx, regCfg, entry)
            | None -> ()
        | None -> ()

    /// Create a new simulation instance (no TOML config)
    static member Create() =
        let ctx = sim_create()
        if ctx = IntPtr.Zero then
            failwith "sim_create() returned null — library failed to load"
        new Sim(ctx, None)

    /// Create a new simulation instance with TOML config
    static member Create(config: VerifrogConfig) =
        let ctx = sim_create()
        if ctx = IntPtr.Zero then
            failwith "sim_create() returned null — library failed to load"
        new Sim(ctx, Some config)

    /// Create a new simulation instance, loading verifrog.toml from a directory
    static member Create(tomlPath: string) =
        let config = Config.parse tomlPath
        Sim.Create(config)

    /// Reset the model (holds reset for `cycles` clock cycles)
    member _.Reset(?cycles: int) =
        let c = defaultArg cycles 10
        sim_reset(ctx, c)

    /// Advance the simulation by N clock cycles. Returns the new cycle count.
    member this.Step(?n: int) : uint64 =
        this.StepCycles(defaultArg n 1)

    /// Advance the simulation by exactly n clock cycles. Returns the new cycle count.
    /// (Non-optional overload for debugger compatibility)
    member _.StepCycles(n: int) : uint64 =
        sim_step(ctx, n)
        sim_get_cycle(ctx)

    /// Current cycle count (since last reset)
    member _.Cycle = sim_get_cycle(ctx)

    /// Read a signal value by hierarchical name
    member _.Read(name: string) : SimResult<int64> =
        let mutable value = 0L
        let rc = sim_read(ctx, name, &value)
        if rc = 0 then Ok value
        else Error $"Signal not found: {name}"

    /// Read a signal, returning the value or raising an exception
    member this.ReadOrFail(name: string) : int64 =
        match this.Read(name) with
        | Ok v -> v
        | Error msg -> failwith msg

    /// Write a signal value by hierarchical name
    member _.Write(name: string, value: int64) : SimResult<unit> =
        let rc = sim_write(ctx, name, value)
        if rc = 0 then Ok ()
        else Error $"Signal not found: {name}"

    /// Get signal bit width (or -1 if not found)
    member _.SignalBits(name: string) : int =
        sim_signal_bits(ctx, name)

    /// Get total number of registered signals
    member _.SignalCount = sim_signal_count(ctx)

    /// List all registered signal names
    member _.ListSignals() : string list =
        let count = sim_signal_count(ctx)
        let buf = Array.zeroCreate<byte> 256
        [ for i in 0..count-1 do
            let len = sim_signal_name(ctx, i, buf, 256)
            if len > 0 then
                yield System.Text.Encoding.ASCII.GetString(buf, 0, len) ]

    /// Control $display suppression (true = suppress, false = show)
    static member SuppressDisplay(suppress: bool) =
        sim_suppress_display(if suppress then 1 else 0)

    // ---- Named checkpoint management ----

    /// Save a named checkpoint of the current simulation state
    member _.SaveCheckpoint(name: string, ?description: string) =
        let desc = defaultArg description $"cycle {sim_get_cycle(ctx)}"
        let ptr = sim_checkpoint(ctx)
        if ptr = IntPtr.Zero then
            failwith "sim_checkpoint() failed"
        match checkpoints.TryGetValue(name) with
        | true, old -> sim_checkpoint_free(old.Ptr)
        | _ -> ()
        let cp = { Ptr = ptr; Cycle = sim_checkpoint_cycle(ptr); Description = desc }
        checkpoints.[name] <- cp
        cp

    /// Save a named checkpoint (non-optional overload for debugger compatibility)
    member this.Save(name: string) = this.SaveCheckpoint(name)

    /// Restore from a named checkpoint
    member _.RestoreCheckpoint(name: string) =
        match checkpoints.TryGetValue(name) with
        | true, cp ->
            let rc = sim_restore(ctx, cp.Ptr)
            if rc <> 0 then failwith $"sim_restore() failed for checkpoint '{name}'"
        | false, _ -> failwith $"Checkpoint not found: {name}"

    /// Restore from a named checkpoint. Returns the restored cycle count.
    /// (Non-optional overload for debugger compatibility)
    member this.Restore(name: string) : uint64 =
        this.RestoreCheckpoint(name)
        sim_get_cycle(ctx)

    /// List all named checkpoints
    member _.ListCheckpoints() =
        checkpoints |> Seq.map (fun kv -> kv.Key, kv.Value) |> Seq.toList

    /// Create an anonymous checkpoint (returned directly, not stored by name)
    member _.Checkpoint(?description: string) : CheckpointHandle =
        let desc = defaultArg description $"cycle {sim_get_cycle(ctx)}"
        let ptr = sim_checkpoint(ctx)
        if ptr = IntPtr.Zero then
            failwith "sim_checkpoint() failed"
        { Ptr = ptr; Cycle = sim_checkpoint_cycle(ptr); Description = desc }

    /// Restore simulation state from a checkpoint handle
    member _.Restore(cp: CheckpointHandle) =
        let rc = sim_restore(ctx, cp.Ptr)
        if rc <> 0 then failwith "sim_restore() failed"

    /// Free a checkpoint handle
    static member FreeCheckpoint(cp: CheckpointHandle) =
        sim_checkpoint_free(cp.Ptr)

    // ---- Signal forcing ----

    /// Force a signal to a value. Persists across steps until released.
    member _.Force(name: string, value: int64) : SimResult<unit> =
        let rc = sim_force(ctx, name, value)
        if rc = 0 then Ok ()
        else Error $"Force failed (signal not found): {name}"

    /// Release a forced signal
    member _.Release(name: string) : SimResult<unit> =
        let rc = sim_release(ctx, name)
        if rc = 0 then Ok ()
        else Error $"Release failed (signal not forced): {name}"

    /// Release all forced signals
    member _.ReleaseAll() = sim_release_all(ctx)

    /// Number of active force overrides
    member _.ForceCount = sim_force_count(ctx)

    // ---- Fork/Compare (what-if exploration) ----

    /// Fork: checkpoint current state, run a scenario, return result, restore state.
    member this.Fork(scenario: Sim -> 'T) : 'T =
        let cp = this.Checkpoint(description = "fork")
        let result = scenario this
        this.Restore(cp)
        Sim.FreeCheckpoint(cp)
        result

    /// Compare: run two scenarios from current state, return both results.
    member this.Compare(scenarioA: Sim -> 'T, scenarioB: Sim -> 'T) : 'T * 'T =
        let cp = this.Checkpoint(description = "compare_base")
        let resultA = scenarioA this
        this.Restore(cp)
        let resultB = scenarioB this
        this.Restore(cp)
        Sim.FreeCheckpoint(cp)
        (resultA, resultB)

    /// Sweep: run multiple scenarios from current state, each with a different parameter.
    member this.Sweep(values: 'P list, scenario: 'P -> Sim -> 'T) : ('P * 'T) list =
        let cp = this.Checkpoint(description = "sweep_base")
        let results =
            [ for v in values do
                this.Restore(cp)
                yield (v, scenario v this) ]
        this.Restore(cp)
        Sim.FreeCheckpoint(cp)
        results

    // ---- Trace/RunUntil ----

    /// Trace named signals for N cycles
    member this.Trace(signals: string list, cycles: int) : (uint64 * int64 list) list =
        let invalid = signals |> List.filter (fun s -> this.SignalBits(s) = -1)
        if not invalid.IsEmpty then
            eprintfn "  Warning: unknown signals will show as ERR: %s" (invalid |> String.concat ", ")
        [ for _ in 1..cycles do
            this.Step()
            let vals = signals |> List.map (fun s ->
                match this.Read(s) with
                | Ok v -> v
                | Error _ -> Int64.MinValue)
            yield (this.Cycle, vals) ]

    /// Step until predicate returns true, or maxCycles reached
    member this.RunUntil(predicate: unit -> bool, ?maxCycles: int) : bool * uint64 =
        let max = defaultArg maxCycles 10000
        let mutable found = false
        let mutable i = 0
        while not found && i < max do
            this.Step()
            i <- i + 1
            if predicate() then found <- true
        (found, this.Cycle)

    /// Step until a signal equals a target value
    member this.RunUntilSignal(name: string, target: int64, ?maxCycles: int) : bool * uint64 =
        let max = defaultArg maxCycles 10000
        this.RunUntil((fun () -> this.ReadOrFail(name) = target), max)

    // ---- TOML-driven Memory/Register access ----

    /// Get a named memory accessor (declared in verifrog.toml [memories.*])
    member _.Memory(name: string) : MemoryAccessor =
        match memories.TryGetValue(name) with
        | true, acc -> acc
        | false, _ ->
            let available = String.Join(", ", memories.Keys)
            failwith $"Memory not found in config: {name}. Available: {available}"

    /// Get a named register accessor (declared in verifrog.toml [registers.map])
    member _.Register(name: string) : RegisterAccessor =
        match registers.TryGetValue(name) with
        | true, acc -> acc
        | false, _ ->
            let available = String.Join(", ", registers.Keys)
            failwith $"Register not found in config: {name}. Available: {available}"

    /// List all configured memory names
    member _.MemoryNames = memories.Keys |> Seq.toList

    /// List all configured register names
    member _.RegisterNames = registers.Keys |> Seq.toList

    /// Validate that all declared memory/register signal paths resolve to real signals.
    /// Call after Create to catch config errors early.
    member this.ValidateSignals() : string list =
        let errors = ResizeArray<string>()

        // Validate memory paths
        for mem in memories.Values do
            for bank in 0 .. mem.Banks - 1 do
                let testPath = mem.Name // Memory validation requires actual array paths
                // We validate that the base signal exists
                let basePath = config |> Option.bind (fun c -> c.Memories |> List.tryFind (fun m -> m.Name = mem.Name)) |> Option.map (fun m -> m.Path.Replace("{bank}", string bank))
                match basePath with
                | Some p ->
                    if this.SignalBits(p) = -1 then
                        errors.Add($"Memory '{mem.Name}' bank {bank}: signal '{p}' not found")
                | None -> ()

        // Validate register paths
        match config with
        | Some cfg ->
            match cfg.Registers with
            | Some regCfg ->
                for entry in regCfg.Map do
                    let path = $"{regCfg.Path}[{entry.Address}]"
                    // Register array elements may not be individually enumerable
                    // Validate the base path exists
                    if this.SignalBits(regCfg.Path) = -1 then
                        errors.Add($"Register base path '{regCfg.Path}' not found")
            | None -> ()
        | None -> ()

        errors |> Seq.toList

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                for kv in checkpoints do
                    sim_checkpoint_free(kv.Value.Ptr)
                checkpoints.Clear()
                sim_destroy(ctx)
                disposed <- true

    member this.Dispose() = (this :> IDisposable).Dispose()
