# API Reference

Complete reference for all Verifrog types and functions. Each section includes method signatures and code examples.

For conceptual explanations, see [Core Concepts](concepts.md). For recipes, see the [Cookbook](cookbook.md).

---

## Verifrog.Sim

### Sim

The core simulation type. Wraps a Verilator-compiled model via P/Invoke.

#### Creation

| Method | Description |
|---|---|
| `Sim.Create()` | Create with no TOML config |
| `Sim.Create(config: VerifrogConfig)` | Create with parsed config |
| `Sim.Create(tomlPath: string)` | Create from a `.toml` file path |
| `Sim.SuppressDisplay(bool)` | Enable/disable `$display` output |

```fsharp
// Basic — no config, just the model
use sim = Sim.Create()
sim.Reset()

// From TOML — enables Memory() and Register() access
use sim = Sim.Create("verifrog.toml")
sim.Reset()

// From parsed config
let config = Config.parse "verifrog.toml"
use sim = Sim.Create(config)
sim.Reset()

// Suppress noisy $display output
Sim.SuppressDisplay(true)
use sim = Sim.Create()
```

#### Simulation Control

| Method | Description |
|---|---|
| `sim.Reset(?cycles)` | Reset the model (default: 10 cycles) |
| `sim.Step(?n)` | Advance N clock cycles (default: 1) |
| `sim.Cycle` | Current cycle count (`uint64`) |

```fsharp
sim.Reset()         // 10-cycle reset
sim.Reset(50)       // 50-cycle reset for designs with slow init

sim.Step()          // 1 cycle
sim.Step(100)       // 100 cycles

let c = sim.Cycle   // e.g., 110UL
```

#### Signal Access

| Method | Returns | Description |
|---|---|---|
| `sim.Read(name)` | `SimResult<int64>` | Read signal value |
| `sim.ReadOrFail(name)` | `int64` | Read signal, throw on error |
| `sim.Write(name, value)` | `SimResult<unit>` | Write signal value |
| `sim.SignalBits(name)` | `int` | Signal bit width (-1 if not found) |
| `sim.SignalCount` | `int` | Total discovered signals |
| `sim.ListSignals()` | `string list` | All signal names |

```fsharp
// Safe read (pattern match the result)
match sim.Read("count") with
| SimResult.Ok v    -> printfn "count = %d" v
| SimResult.Error e -> printfn "Error: %s" e

// Throwing read (for tests where failure = test failure)
let count = sim.ReadOrFail("count")

// Write
sim.Write("enable", 1L) |> ignore
sim.Write("data_in", 0xDEADBEEFL) |> ignore

// Introspect signals
let bits = sim.SignalBits("count")     // 8
let total = sim.SignalCount            // e.g., 45
let names = sim.ListSignals()          // ["clk"; "rst_n"; "enable"; "count"; ...]
```

#### Checkpoints

| Method | Description |
|---|---|
| `sim.SaveCheckpoint(name, ?desc)` | Save named checkpoint, returns `CheckpointHandle` |
| `sim.RestoreCheckpoint(name)` | Restore to a named checkpoint |
| `sim.Checkpoint(?desc)` | Create anonymous checkpoint |
| `sim.Restore(handle)` | Restore from a handle |
| `Sim.FreeCheckpoint(handle)` | Free checkpoint memory (static) |
| `sim.ListCheckpoints()` | List `(name, handle)` pairs |

```fsharp
// Named checkpoints (managed by Sim, freed on dispose)
let cp = sim.SaveCheckpoint("baseline", "after config + weight load")
sim.Step(1000)
sim.RestoreCheckpoint("baseline")   // Back to saved state

// Anonymous checkpoints (you manage the lifetime)
let cp = sim.Checkpoint("before experiment")
sim.Step(500)
sim.Restore(cp)
Sim.FreeCheckpoint(cp)              // Must free manually

// List saved checkpoints
for (name, handle) in sim.ListCheckpoints() do
    printfn "%s: cycle %d — %s" name handle.Cycle handle.Description
```

#### Force/Release

| Method | Description |
|---|---|
| `sim.Force(name, value)` | Force signal to value (persists across steps) |
| `sim.Release(name)` | Release a forced signal |
| `sim.ReleaseAll()` | Release all forces |
| `sim.ForceCount` | Number of active forces |

```fsharp
// Force a clock enable off to test stall behavior
sim.Force("clk_en", 0L) |> ignore
sim.Step(10)
Expect.signal sim "state" 3L "state should not advance"

// Release and verify normal operation resumes
sim.Release("clk_en") |> ignore
sim.Step(10)
Expect.signal sim "state" 5L "state advances after release"

// Cleanup
sim.ReleaseAll()
printfn "Active forces: %d" sim.ForceCount   // 0
```

#### What-If Exploration

| Method | Description |
|---|---|
| `sim.Fork(scenario)` | Run scenario, return result, restore state |
| `sim.Compare(a, b)` | Run two scenarios from same state |
| `sim.Sweep(values, scenario)` | Run scenario for each value |

```fsharp
// Fork: explore without losing state
let overflowed = sim.Fork(fun s ->
    s.Write("load_value", 250L) |> ignore
    s.Write("load_en", 1L) |> ignore
    s.Step(1)
    s.Write("load_en", 0L) |> ignore
    s.Step(10)
    s.ReadOrFail("overflow"))
// sim state is restored

// Compare: test two configurations
let (fast, slow) = sim.Compare(
    (fun s -> s.Write("turbo", 1L) |> ignore; s.Step(100); s.ReadOrFail("cycles_to_done")),
    (fun s -> s.Write("turbo", 0L) |> ignore; s.Step(100); s.ReadOrFail("cycles_to_done")))
printfn "Turbo: %d cycles, Normal: %d cycles" fast slow

// Sweep: parametric exploration
let results = sim.Sweep(
    [0L; 1L; 2L; 3L],
    fun op s ->
        s.Write("alu_op", op) |> ignore
        s.Step(2)
        s.ReadOrFail("result"))
for (op, result) in results do
    printfn "op=%d -> result=%d" op result
```

#### Trace and RunUntil

| Method | Description |
|---|---|
| `sim.Trace(signals, cycles)` | Record signal values over N cycles |
| `sim.RunUntil(predicate, ?max)` | Step until predicate is true |
| `sim.RunUntilSignal(name, target, ?max)` | Step until signal equals value |

```fsharp
// Trace: record a window of values
let trace = sim.Trace(["count"; "overflow"; "state"], 20)
for (cycle, vals) in trace do
    printfn "cycle %d: count=%d overflow=%d state=%d" cycle vals.[0] vals.[1] vals.[2]

// RunUntilSignal: wait for completion
let (found, cycle) = sim.RunUntilSignal("done", 1L, 50000)
if not found then failwith $"Timed out after 50000 cycles"
printfn "Done at cycle %d" cycle

// RunUntil: wait for a complex condition
let (found, cycle) = sim.RunUntil(
    (fun () ->
        sim.ReadOrFail("valid") = 1L &&
        sim.ReadOrFail("data") > 100L),
    10000)
```

#### TOML-Driven Access

| Method | Description |
|---|---|
| `sim.Memory(name)` | Get `MemoryAccessor` for a named memory |
| `sim.Register(name)` | Get `RegisterAccessor` for a named register |
| `sim.MemoryNames` | List configured memory names |
| `sim.RegisterNames` | List configured register names |
| `sim.ValidateSignals()` | Check all declared paths resolve |

```fsharp
use sim = Sim.Create("verifrog.toml")
sim.Reset()

// Memory access
let ram = sim.Memory("data_ram")
ram.Write(0, 100, 0xABL)             // bank 0, addr 100, value 0xAB
let v = ram.Read(0, 100)              // SimResult<int64>
printfn "Banks=%d Depth=%d Width=%d" ram.Banks ram.Depth ram.Width

// Register access
sim.Register("CTRL").Write(0x01L) |> ignore
let status = sim.Register("STATUS").Read()

// Validation
let errors = sim.ValidateSignals()
if not errors.IsEmpty then
    failwith $"Config errors: {errors}"
```

### SimResult<'T>

```fsharp
type SimResult<'T> =
    | Ok of 'T
    | Error of string
```

Used by `Read`, `Write`, `Force`, `Release`, and memory/register operations. Pattern match to handle errors, or pipe to `ignore` in tests where you want exceptions on failure.

### CheckpointHandle

```fsharp
type CheckpointHandle = {
    Ptr: nativeint         // Opaque pointer to checkpoint data
    Cycle: uint64          // Cycle count when saved
    Description: string    // User-provided label
}
```

### MemoryAccessor

Returned by `sim.Memory("name")`. Resolves `{bank}` in TOML path templates.

| Method | Description |
|---|---|
| `mem.Read(bank, addr)` | Read word — `SimResult<int64>` |
| `mem.Write(bank, addr, value)` | Write word — `SimResult<unit>` |
| `mem.Name` | Memory region name |
| `mem.Banks` / `mem.Depth` / `mem.Width` | Config metadata |

```fsharp
let wgt = sim.Memory("weight_sram")
for bank in 0 .. wgt.Banks - 1 do
    for addr in 0 .. wgt.Depth - 1 do
        wgt.Write(bank, addr, int64 (bank * 256 + addr)) |> ignore
```

### RegisterAccessor

Returned by `sim.Register("NAME")`. Maps to an address in the TOML register map.

| Method | Description |
|---|---|
| `reg.Read()` | Read register — `SimResult<int64>` |
| `reg.Write(value)` | Write register — `SimResult<unit>` |
| `reg.Name` | Register name |
| `reg.Address` | Register address |

```fsharp
let ctrl = sim.Register("CTRL")
printfn "%s is at address 0x%02X" ctrl.Name ctrl.Address
ctrl.Write(0x01L) |> ignore
```

---

## Verifrog.Runner

### SimFixture

Convenience functions for common test setup patterns.

| Function | Description |
|---|---|
| `SimFixture.create()` | Create, suppress display, reset — returns `Sim` |
| `SimFixture.createFromConfig(config)` | Create from parsed config |
| `SimFixture.createFromToml(path)` | Create from `.toml` file |
| `SimFixture.createWithCheckpoint()` | Create with PostReset checkpoint — returns `Sim * CheckpointHandle` |
| `SimFixture.restore(sim, level)` | Restore to a checkpoint level |
| `SimFixture.saveLevel(sim, level)` | Save checkpoint at a level |

```fsharp
// Most common: create and reset
use sim = SimFixture.create ()

// With TOML config (enables Memory/Register)
use sim = SimFixture.createFromToml "verifrog.toml"

// With initial checkpoint for fast restore
let (sim, baseCheckpoint) = SimFixture.createWithCheckpoint ()
// ... run expensive setup ...
SimFixture.saveLevel sim PostConfig
// In later tests:
SimFixture.restore sim PostConfig |> ignore
```

**Checkpoint levels:**

```fsharp
type Level = PostReset | PostConfig | PostWeights | PostInference
```

### Expect

Expecto-integrated assertions with descriptive failure messages.

| Function | Description |
|---|---|
| `Expect.signal sim name expected msg` | Assert signal equals value |
| `Expect.signalSatisfies sim name pred msg` | Assert signal matches predicate |
| `Expect.memory sim memName bank addr expected msg` | Assert memory word |
| `Expect.register sim regName expected msg` | Assert register value |
| `Expect.iverilogPassed result msg` | Assert iverilog test passed |

```fsharp
// Signal value check
Expect.signal sim "count" 42L "count should be 42"

// Signal predicate
Expect.signalSatisfies sim "count" (fun v -> v > 0L && v < 100L) "count in range"

// Memory check
Expect.memory sim "data_ram" 0 10 0xABL "RAM[0][10] should be 0xAB"

// Register check
Expect.register sim "STATUS" 0x00L "STATUS should be clear"

// Iverilog result
let result = Iverilog.runSimple root config "my_tb"
Expect.iverilogPassed result "testbench should pass"
```

### Iverilog

Compile and run Verilog testbenches through Icarus Verilog.

| Function | Description |
|---|---|
| `Iverilog.run root config tbName overrides extras` | Full control: compile and run |
| `Iverilog.runSimple root config tbName` | Run with no overrides |
| `Iverilog.runAuto root config tbName` | Auto-detect BFM dependencies |
| `Iverilog.discover root config` | Find testbenches matching TOML patterns |
| `Iverilog.passed result` | Check stdout for PASS |
| `Iverilog.parseSummary stdout` | Parse `(passed, failed)` counts |

```fsharp
let config = Config.parse "verifrog.toml"
let root = "."

// Discover available testbenches
let tbs = Iverilog.discover root config
// ["shift_reg_tb"; "fifo_tb"]

// Run a testbench
let result = Iverilog.runSimple root config "shift_reg_tb"
printfn "Exit: %d, Time: %d ms" result.ExitCode result.ElapsedMs
printfn "Passed: %b" (Iverilog.passed result)

// Run with parameter overrides
let result = Iverilog.run root config "fifo_tb" [("-P", "tb.DEPTH=32")] []

// Parse summary from stdout
match Iverilog.parseSummary result.Stdout with
| Some (passed, failed) -> printfn "%d passed, %d failed" passed failed
| None -> printfn "No summary found in output"
```

**IverilogResult:**

```fsharp
type IverilogResult = {
    ExitCode: int
    Stdout: string
    Stderr: string
    ElapsedMs: int64
}
```

---

## Verifrog.Vcd

### VcdParser

Parse and analyze VCD waveform files. See the [VCD Parser Guide](vcd-guide.md) for a full walkthrough.

#### Parsing

| Function | Description |
|---|---|
| `VcdParser.parseAll(filename)` | Parse entire file, all signals |
| `VcdParser.parse(filename, maxTime)` | Parse with time limit (0 = no limit) |
| `VcdParser.parseFiltered(filename, patterns, maxTime)` | Parse only matching signals |

```fsharp
// Parse everything
let vcd = VcdParser.parseAll "sim.vcd"

// Parse first microsecond only
let vcd = VcdParser.parse "sim.vcd" 1_000_000L

// Parse only specific signals
let vcd = VcdParser.parseFiltered "sim.vcd" ["fsm*"; "done"; "valid"] 0L
```

#### Queries

| Function | Returns | Description |
|---|---|---|
| `findSignals(vcd, pattern)` | `VcdSignal list` | Find signals by name/glob |
| `transitions(vcd, fullPath)` | `VcdTransition list` | All transitions (time-ordered) |
| `valueAtTime(vcd, fullPath, time)` | `VcdTransition option` | Value at specific time |
| `transitionCount(vcd, fullPath)` | `int` | Number of transitions |
| `firstTimeAtValue(vcd, fullPath, value)` | `int64 option` | First time at value |
| `uniqueValues(vcd, fullPath)` | `int list` | All unique integer values |
| `highPulseCount(vcd, fullPath)` | `int` | Count of 0->1 transitions |

```fsharp
let vcd = VcdParser.parseAll "sim.vcd"

// Find signals
let clocks = VcdParser.findSignals vcd "clk"

// Get transitions
let trans = VcdParser.transitions vcd "counter.count"
printfn "%d transitions" trans.Length

// Value at time
match VcdParser.valueAtTime vcd "counter.count" 50000L with
| Some t -> printfn "count=%d at t=%d" t.IntVal t.Time
| None   -> printfn "no data"

// First occurrence
match VcdParser.firstTimeAtValue vcd "done" 1 with
| Some t -> printfn "done asserted at t=%d ps (%.3f us)" t (VcdParser.timeToUs t)
| None   -> printfn "done never asserted"

// Coverage
let states = VcdParser.uniqueValues vcd "fsm_state"

// Pulse counting
let overflows = VcdParser.highPulseCount vcd "overflow"
```

#### Helpers

| Function | Description |
|---|---|
| `parseBinValue(str)` | Parse binary string to int (x/z -> 0) |
| `timeToUs(timePs)` | Convert picoseconds to microseconds |

```fsharp
VcdParser.parseBinValue "01101001"    // 105
VcdParser.timeToUs 50_000_000L        // 50.0
```

### Types

```fsharp
type VcdSignal = {
    Id: string          // VCD short ID
    LeafName: string    // e.g., "count"
    FullPath: string    // e.g., "counter.count"
    Width: int          // Bit width
}

[<Struct>]
type VcdTransition = {
    Time: int64         // VCD time units (typically ps)
    Bits: string        // Raw bit string
    IntVal: int         // Integer value (-1 if unparseable)
}

type VcdFile = {
    Signals: VcdSignal list
    SignalMap: IReadOnlyDictionary<string, VcdSignal>
    Transitions: IReadOnlyDictionary<string, VcdTransition list>
}
```

---

## Verifrog.Sim.Config

TOML configuration types. Parsed by `Config.parse`.

```fsharp
// Parse a verifrog.toml file
let config = Config.parse "verifrog.toml"

// Access fields
printfn "Top: %s" config.Design.Top
printfn "Sources: %A" config.Design.Sources
printfn "Memories: %A" (config.Memories |> List.map (fun m -> m.Name))
```

See [Configuration Reference](config-reference.md) for all fields.
