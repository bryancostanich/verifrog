# Core Concepts

This guide explains the fundamental ideas behind Verifrog and how the pieces fit together. Read this after [Getting Started](getting-started.md) to build a deeper mental model.

## The simulation model

When you run `verifrog build`, Verilator compiles your Verilog into a C++ model. Verifrog wraps this model in a generic C shim (`libverifrog_sim`) that provides a uniform API regardless of your design. The F# `Sim` type talks to this shim via P/Invoke (FFI).

```
Your F# Test  -->  Sim (F#)  -->  libverifrog_sim (C++)  -->  Verilator model (C++)
```

The `Sim` instance owns the simulation state. When you dispose it (`use sim = ...`), the model is destroyed and memory is freed.

## Signals

Everything in your Verilog design — ports, wires, registers, memory arrays — is a **signal**. Verifrog discovers all signals automatically at init time by walking Verilator's internal scope/variable tables.

### Reading and writing

```fsharp
// Read a signal's current value
let value = sim.ReadOrFail("count")        // throws on error
let result = sim.Read("count")             // returns SimResult<int64>

// Write a signal
sim.Write("enable", 1L) |> ignore

// Check signal width
let bits = sim.SignalBits("count")         // e.g., 8 for an 8-bit counter

// List all discovered signals
let allSignals = sim.ListSignals()
```

### Signal naming

Signals are registered with **friendly names** (just the leaf name, like `count`) and **full hierarchical paths** (like `counter.count`). Both work with `Read` and `Write`. If a leaf name is ambiguous (exists in multiple scopes), use the full path.

**Important**: Verilator creates internal copies of top-level ports (e.g., `counter__DOT__enable`). Verifrog prioritizes the actual port entries so writes go to the right place. You don't need to worry about this — just use the signal's Verilog name.

### Signal values

All signal values are `int64`. For multi-bit signals, the value is the integer interpretation. For signals wider than 64 bits, you'll need to read individual words.

## Simulation control

### Reset

```fsharp
sim.Reset()       // Hold reset for 10 clock cycles (default)
sim.Reset(20)     // Hold reset for 20 cycles
```

`SimFixture.create()` calls `Reset()` automatically, so you start in a known state.

### Stepping

```fsharp
sim.Step()        // Advance 1 clock cycle
sim.Step(100)     // Advance 100 clock cycles
```

Each step toggles the clock (low, eval, high, eval) and increments the cycle counter. Forces are applied after each eval.

### Cycle tracking

```fsharp
let cycle = sim.Cycle    // Current cycle count (uint64)
```

The cycle counter resets to 0 on `Reset()` and increments by 1 per `Step`.

## Checkpoints

Checkpoints let you save and restore the complete simulation state — every register, every memory cell, the cycle counter, everything. They're implemented as a `memcpy` of the Verilator model struct, so they're fast (microseconds for most designs).

### Named checkpoints

```fsharp
// Save
let cp = sim.SaveCheckpoint("after_config", "configured layer 0")

// ... run more simulation ...

// Restore
sim.RestoreCheckpoint("after_config")
// Simulation is now back to the saved state

// List what's saved
let names = sim.ListCheckpoints()  // [("after_config", handle)]
```

Named checkpoints are managed by the `Sim` instance and freed automatically on dispose.

### Anonymous checkpoints

```fsharp
let cp = sim.Checkpoint()
// ... do things ...
sim.Restore(cp)
Sim.FreeCheckpoint(cp)    // You must free anonymous checkpoints yourself
```

### Why checkpoints matter

Checkpoints are the foundation for:
- **Fast test setup**: Save state after expensive initialization, restore in each test
- **What-if exploration**: `Fork`, `Compare`, `Sweep` all use checkpoints internally
- **Debugging**: Save a known-good state, then probe different scenarios

### Checkpoint levels (SimFixture)

`SimFixture` defines standard checkpoint levels for test acceleration:

```fsharp
type Level = PostReset | PostConfig | PostWeights | PostInference
```

If your test setup has expensive phases (loading weights, configuring registers), save a checkpoint at each level. Later tests can restore to the appropriate level and skip the setup:

```fsharp
// First test: do full setup and save checkpoints
let sim, cp = SimFixture.createWithCheckpoint()  // Saves PostReset
// ... configure ...
SimFixture.saveLevel sim PostConfig
// ... load weights ...
SimFixture.saveLevel sim PostWeights

// Later test: skip straight to PostWeights
SimFixture.restore sim PostWeights |> ignore
// Start testing from here
```

## Forces

Forces override a signal's value and **persist across clock steps** until released. Unlike `Write`, which sets a value once (and it may be overwritten by the design's logic on the next eval), a force is reapplied after every evaluation.

```fsharp
// Force a signal
sim.Force("clk_gate_en", 0L) |> ignore   // Hold clock gate disabled

sim.Step(100)                              // Force persists for all 100 cycles

// Release
sim.Release("clk_gate_en") |> ignore      // Design logic takes over again

// Release everything
sim.ReleaseAll()

// Check how many forces are active
let count = sim.ForceCount
```

**When to use Force vs Write**:
- Use `Write` to set inputs that your design samples on a clock edge (normal stimulus)
- Use `Force` to override internal signals for testing (fault injection, clock gating, reset hold)

## What-if exploration

These methods use checkpoints under the hood to let you explore scenarios without manually saving and restoring state.

### Fork

Run a scenario, get the result, and automatically restore to the original state:

```fsharp
let result = sim.Fork(fun s ->
    s.Write("mode", 3L) |> ignore
    s.Step(50)
    s.ReadOrFail("output"))

// sim is back to its state before Fork
```

### Compare

Run two scenarios from the same starting point:

```fsharp
let (resultA, resultB) = sim.Compare(
    (fun s ->
        s.Write("fast_mode", 1L) |> ignore
        s.Step(100)
        s.ReadOrFail("result")),
    (fun s ->
        s.Write("fast_mode", 0L) |> ignore
        s.Step(100)
        s.ReadOrFail("result")))
```

### Sweep

Run a scenario across multiple parameter values:

```fsharp
let results = sim.Sweep(
    [0L; 1L; 2L; 3L],              // ALU operation codes
    fun op s ->
        s.Write("alu_op", op) |> ignore
        s.Step(2)
        s.ReadOrFail("alu_result"))

// results: [(0L, 59L); (1L, 25L); (2L, 2L); (3L, 63L)]
```

## Memories

TOML-driven memory access lets you read and write Verilog memory arrays by name, with support for banked memories.

### Configuration

```toml
[memories.weight_sram]
path = "u_wgt.bank{bank}.mem"    # {bank} replaced with 0..banks-1
banks = 16
depth = 512
width = 32

[memories.data_ram]
path = "u_ram.mem"               # Single-bank: no {bank} placeholder
banks = 1
depth = 1024
width = 8
```

### Usage

```fsharp
use sim = SimFixture.createFromToml "verifrog.toml"

// Write and read a memory word
sim.Memory("data_ram").Write(0, 42, 0xABL)    // bank, addr, value
let v = sim.Memory("data_ram").Read(0, 42)     // bank, addr -> SimResult<int64>

// Inspect config
let mem = sim.Memory("weight_sram")
printfn "Banks: %d, Depth: %d, Width: %d" mem.Banks mem.Depth mem.Width

// List all configured memories
let names = sim.MemoryNames    // ["weight_sram"; "data_ram"]
```

Memory access is **backdoor** — it reads/writes the Verilog array directly, bypassing any address decoding or bus protocol in your design. This is intentional: use it for test setup (loading weights, initializing RAM) and verification (checking final memory contents).

## Registers

TOML-driven register access maps human-readable names to addresses in a register file.

### Configuration

```toml
[registers]
path = "u_regfile.regs"    # Path to the Verilog register array
width = 8

[registers.map]
CTRL      = 0x00
STATUS    = 0x01
IRQ_MASK  = 0x20
```

### Usage

```fsharp
use sim = SimFixture.createFromToml "verifrog.toml"

sim.Register("CTRL").Write(0x01L) |> ignore
sim.Step(1)
let status = sim.Register("STATUS").Read()

// Inspect
let reg = sim.Register("CTRL")
printfn "Name: %s, Address: 0x%02X" reg.Name reg.Address

// List all configured registers
let names = sim.RegisterNames    // ["CTRL"; "STATUS"; "IRQ_MASK"]
```

Like memory access, register access is backdoor — it writes directly to the register array. To test your design's register interface (bus protocol, read-only fields, write-1-to-clear), drive the bus signals with `Write` and `Step` instead.

## Tracing and RunUntil

### Trace

Record signal values over a window of cycles:

```fsharp
let trace = sim.Trace(["count"; "overflow"], 20)
// trace: [(cycle, [count_val; overflow_val]); ...]

for (cycle, vals) in trace do
    printfn "Cycle %d: count=%d overflow=%d" cycle vals.[0] vals.[1]
```

### RunUntil

Step until a condition is met (or a timeout):

```fsharp
// Wait for a signal
let (found, cycle) = sim.RunUntilSignal("done", 1L, 10000)
if not found then failwith "Timed out waiting for done"

// Wait for a predicate
let (found, cycle) = sim.RunUntil(
    (fun () -> sim.ReadOrFail("state") = 5L),
    5000)
```

## Test categories

Verifrog provides hardware-domain test categories that map to how verification engineers think about their test suites. Categories are `testList` wrappers — they create named groups in Expecto's test hierarchy.

### Available categories

| Category | Purpose | Typical runtime |
|----------|---------|-----------------|
| `smoke` | Quick sanity — is the design alive? | Seconds |
| `unit` | Focused tests for individual signals, blocks, or operations | Seconds to minutes |
| `parametric` | Sweeps, value ranges, `Sim.Sweep`/`Sim.Compare` | Minutes |
| `integration` | Multi-block interactions, bus protocols, end-to-end flow | Minutes |
| `stress` | Long-running: deep pipelines, large memories, many iterations | Minutes to hours |
| `golden` | Reference output comparisons against known-good data | Varies |
| `regression` | Bug-fix coverage — each test tied to a specific issue | Varies |

### Using categories

```fsharp
open Verifrog.Runner.Category

let tests = testList "MyDesign" [
    smoke [
        test "comes out of reset" {
            use sim = SimFixture.create ()
            Expect.signal sim "count" 0L "zero after reset"
        }
    ]
    unit [
        test "counter increments" {
            use sim = SimFixture.create ()
            sim.Write("enable", 1L) |> ignore
            sim.Step(10)
            Expect.signal sim "count" 10L "counted to 10"
        }
    ]
    regression [
        test "issue-42: overflow at boundary" {
            use sim = SimFixture.create ()
            // ...
        }
    ]
]
```

### Filtering by category

```bash
verifrog test --category Smoke       # Run only smoke tests
verifrog test --category Unit        # Run only unit tests
verifrog test                        # Run all categories
```

A typical workflow: run `Smoke` during development for fast feedback, `Unit` before committing, and everything in CI.

## Iverilog backend

Verifrog supports running traditional Verilog testbenches through Icarus Verilog alongside Verilator-based F# tests. This gives you:

- **Verilator**: Fast cycle-based simulation driven from F# (the primary mode)
- **Icarus Verilog**: Timing-accurate simulation for protocol verification, gate-level sims

Both backends run under one `dotnet test` invocation. See the [iverilog_tb sample](../samples/iverilog_tb/) for a working example.

## VCD analysis

The `Verifrog.Vcd` library is a standalone VCD parser you can use in tests or from the command line. It's useful for post-simulation analysis: checking timing, counting events, comparing waveforms.

See the [VCD Parser Guide](vcd-guide.md) for details.

## Display suppression

Verilator compiles `$display` statements in your RTL into C++ print calls. During testing, this output can be noisy. Verifrog suppresses it by default when using `SimFixture`:

```fsharp
Sim.SuppressDisplay(true)    // Redirect $display to /dev/null
Sim.SuppressDisplay(false)   // Show $display output (useful for debugging)
```

## Signal validation

After creating a `Sim` with TOML config, you can check that all declared memory and register paths actually resolve to signals in the design:

```fsharp
use sim = SimFixture.createFromToml "verifrog.toml"
let errors = sim.ValidateSignals()
if not errors.IsEmpty then
    for e in errors do eprintfn "Config error: %s" e
    failwith "Signal validation failed"
```

This catches typos and path mismatches early, before your tests fail with cryptic "signal not found" errors.
