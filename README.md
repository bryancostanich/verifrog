# Verifrog

An open-source Verilog/SystemVerilog testing and debugging framework in F#. Drive your RTL designs through [Verilator](https://verilator.org) and [Icarus Verilog](http://iverilog.icarus.com/) with type-safe, structured access to signals, memories, and registers — all from `dotnet test`.

**Why Verifrog?** Traditional Verilog testbenches are tedious to write and limited in expressiveness. UVM is powerful but heavyweight. Verifrog gives you the speed of Verilator with the ergonomics of a modern programming language — F# with Expecto — so you can write tests that read like specifications, and interactively debug your hardware with tools that don't exist in traditional HDL workflows.

### Testing

Write structured, readable tests in F# that compile and run your RTL through Verilator. Read and write any signal, memory, or register by name. Assert values with clear failure messages. Run Verilator and Icarus Verilog tests side-by-side under a single `dotnet test`.

### Interactive debugging

Verifrog's simulation model is fully controllable from code — you can pause at any point, inspect every signal in the design, and step forward cycle-by-cycle. But the real power is in the tools built on top of this:

- **Checkpoint/Restore** — Snapshot the entire simulation state (every register, every memory cell) and restore it later in microseconds. Hit a bug at cycle 50,000? Save a checkpoint before the failure, then repeatedly restore and probe different signals without re-running the simulation from scratch.

- **Fork** — Explore a what-if scenario and automatically snap back. "What would happen if I forced this signal high?" Fork runs your experiment, captures the result, and restores the original state — so you can try multiple hypotheses from the same point without manual save/restore.

- **Compare and Sweep** — Run two configurations side-by-side from the same state (`Compare`), or sweep a parameter across many values (`Sweep`). Both use checkpoints internally to ensure each scenario starts from identical state.

- **Signal forcing** — Override any internal signal and hold it across clock cycles. Inject faults, disable clock gating, force a bus value — then release and watch the design recover.

- **Tracing and RunUntil** — Record signal values over a window of cycles (`Trace`), or advance the simulation until a condition is met (`RunUntil`, `RunUntilSignal`). No more guessing how many cycles to step.

- **VCD waveform analysis** — Parse simulation waveform dumps and query them programmatically: find when a signal first changed, count pulses, check timing relationships, verify FSM state coverage. Available as both a library (`Verifrog.Vcd`) for use in tests and a command-line tool (`verifrog-vcd`) for quick analysis.

## Quick Start

### Prerequisites

- [.NET 8+ SDK](https://dotnet.microsoft.com/download)
- [Verilator 5+](https://verilator.org/guide/latest/install.html)
- clang++ (macOS, included with Xcode) or g++ (Linux)
- [Icarus Verilog](http://iverilog.icarus.com/) (optional, for timing-accurate testbenches)

### Install

```bash
git clone https://github.com/bryancostanich/verifrog.git
cd verifrog
./install.sh     # Symlinks verifrog to /usr/local/bin
```

Or add `bin/` to your PATH manually: `export PATH="/path/to/verifrog/bin:$PATH"`

### Try the counter sample

```bash
verifrog build samples/counter
verifrog test samples/counter
```

### Start a new project

```bash
cd your-project
verifrog init .

# Edit verifrog.toml with your design, then:
verifrog build
verifrog test
```

See the full [Getting Started Guide](docs/getting-started.md) for a step-by-step walkthrough.

## What it looks like

### Writing a test

```fsharp
open Verifrog.Sim
open Verifrog.Runner

let tests = testList "counter" [
    test "counts to 10 when enabled" {
        use sim = SimFixture.create ()
        sim.Write("enable", 1L) |> ignore
        sim.Step(10)
        Expect.signal sim "count" 10L "count should reach 10"
    }
]
```

### Debugging with checkpoints and Fork

Save simulation state, run forward, restore, try something different — all in code:

```fsharp
test "investigate overflow behavior" {
    use sim = SimFixture.create ()
    sim.Write("enable", 1L) |> ignore
    sim.Step(200)

    // Save state right before the interesting part
    let cp = sim.SaveCheckpoint("before_overflow")

    // Run forward and observe
    sim.Step(60)
    let count = sim.ReadOrFail("count")
    let overflowed = sim.ReadOrFail("overflow")
    printfn "After 60 more cycles: count=%d overflow=%d" count overflowed

    // Restore and try a different approach
    sim.RestoreCheckpoint("before_overflow")

    // What if we load a value near the limit?
    let result = sim.Fork(fun s ->
        s.Write("load_en", 1L) |> ignore
        s.Write("load_value", 250L) |> ignore
        s.Step(1)
        s.Write("load_en", 0L) |> ignore
        s.Step(10)
        s.ReadOrFail("overflow"))
    // sim is back to "before_overflow" — Fork restored automatically

    // Sweep across multiple load values to find the boundary
    let results = sim.Sweep(
        [248L; 249L; 250L; 251L; 252L],
        fun loadVal s ->
            s.Write("load_en", 1L) |> ignore
            s.Write("load_value", loadVal) |> ignore
            s.Step(1)
            s.Write("load_en", 0L) |> ignore
            s.Step(10)
            s.ReadOrFail("overflow"))

    for (loadVal, overflow) in results do
        printfn "  load=%d -> overflow=%d" loadVal overflow
}
```

### Analyzing waveforms

```fsharp
test "verify timing with VCD analysis" {
    use sim = SimFixture.create ()
    // ... run stimulus ...

    let vcd = VcdParser.parseAll "output/sim.vcd"

    // When did the FSM first enter state 5?
    let t = VcdParser.firstTimeAtValue vcd "fsm_state" 5
    // How many times did overflow pulse?
    let pulses = VcdParser.highPulseCount vcd "counter.overflow"
    // What states did the FSM visit?
    let states = VcdParser.uniqueValues vcd "fsm_state"
}
```

## Test organization

Verifrog provides hardware-domain test categories so you can run the right tests at the right time:

```bash
verifrog test --category Smoke          # Quick sanity — design is alive (seconds)
verifrog test --category Unit           # Focused signal/block tests
verifrog test --category Integration    # Multi-block data flow
verifrog test --category Parametric     # Sweeps and value ranges
verifrog test                           # Everything
```

Categories are lightweight `testList` wrappers — just group your tests:

```fsharp
open Verifrog.Runner.Category

let tests = testList "MySoC" [
    smoke [
        test "comes out of reset" { ... }
    ]
    unit [
        test "counter increments" { ... }
    ]
    golden [
        test "matches reference output" { ... }
    ]
]
```

Also available: `stress` (long-running), `golden` (reference outputs), `regression` (bug-fix coverage).

## Architecture

```
Your Test Project (Expecto)
  |
  v
Verifrog.Runner   — SimFixture, Iverilog backend, Expect helpers
  |
  v
Verifrog.Sim      — Sim type, Memory/Register accessors, TOML config
  |
  v
libverifrog_sim   — Generic Verilator C++ wrapper (built per-design)
  |
  v
Verilator         — Your compiled RTL
```

## Components

| Library | What it does |
|---|---|
| **Verifrog.Sim** | Core simulation API: create, step, read/write signals, checkpoint/restore, force, fork/sweep, memory/register access |
| **Verifrog.Runner** | Test infrastructure: SimFixture lifecycle, Iverilog backend, Expect assertions, test categories (Smoke/Unit/Parametric/Integration/Stress/Golden/Regression) |
| **Verifrog.Vcd** | Standalone VCD waveform parser: parse files, query signals, value-at-time, transitions, timing analysis |
| **Verifrog.Vcd.Cli** | Command-line VCD analysis tool with text and JSON output |
| **verifrog CLI** | Build tool: `init` scaffolds a project, `build` compiles RTL through Verilator, `clean` removes artifacts |
| **libverifrog_sim** | Design-agnostic C++ shim: signal discovery, direct-pointer access, checkpoint via memcpy |

## Configuration

All project configuration lives in `verifrog.toml`:

```toml
[design]
top = "my_module"
sources = ["rtl/*.v"]

[test]
output = "build"

[memories.data_ram]
path = "u_ram.mem"
banks = 1
depth = 1024
width = 32

[registers]
path = "u_regfile.regs"
width = 8

[registers.map]
CTRL   = 0x00
STATUS = 0x01
DATA   = 0x02
```

See the full [Configuration Reference](docs/config-reference.md).

## Samples

| Sample | What it demonstrates |
|---|---|
| [counter](samples/counter/) | Minimal: step, read/write, checkpoint, force, fork |
| [alu_regfile](samples/alu_regfile/) | TOML register map, named register access, parametric sweep |
| [sram](samples/sram/) | TOML memory regions, banked access, backdoor loading |
| [iverilog_tb](samples/iverilog_tb/) | Dual-backend: Verilator + iverilog under one `dotnet test` |
| [i2c_bfm](samples/i2c_bfm/) | Protocol-level BFM with auto-detection, timing-accurate I2C |

## Documentation

| Guide | For |
|---|---|
| [Getting Started](docs/getting-started.md) | First-time setup, end-to-end walkthrough |
| [Core Concepts](docs/concepts.md) | How signals, checkpoints, forces, memories, and registers work |
| [API Reference](docs/api-reference.md) | Every method on Sim, Memory, Register, Expect, and more |
| [VCD Parser Guide](docs/vcd-guide.md) | Using the VCD library to analyze waveforms |
| [VCD CLI Reference](docs/vcd-cli.md) | Command-line VCD analysis tool |
| [CLI Reference](docs/cli-reference.md) | `verifrog init`, `build`, `clean`, `test`, `debug`, `results` |
| [Configuration Reference](docs/config-reference.md) | Every `verifrog.toml` section and key |
| [Cookbook](docs/cookbook.md) | Recipes for common test patterns |
| [Extension Guide](docs/extension-guide.md) | Building design-specific layers on top of Verifrog |
| [Architecture](docs/architecture.md) | Layer diagram, data flow, signal resolution internals |
| [Architecture Decisions](docs/ARCHITECTURE_DECISIONS.md) | Why we made the choices we did |
| [Troubleshooting](docs/troubleshooting.md) | Common errors and how to fix them |

## License

Apache 2.0
