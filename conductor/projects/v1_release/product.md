# Verifrog

An open-source Verilog testing framework written in F#. Unifies Verilator (fast, cycle-based) and iverilog (event-driven, timing-accurate) under a single test runner with structured access to memories, registers, and signals.

## What It Is

Verifrog lets you write hardware verification tests in F# using the Expecto test framework, driving your Verilog design through both Verilator and Icarus Verilog backends. One command (`dotnet test`), one report, both simulators.

```fsharp
let tests = testList "ALU" [
    test "add produces correct result" {
        use sim = Sim.Create()
        sim.Reset(5)
        sim.Register("OP").Write(0x01)       // ADD
        sim.Register("A").Write(42)
        sim.Register("B").Write(17)
        sim.Step(10)
        Expect.signal sim "result" 59L "42 + 17 = 59"
    }
]
```

## Core Components

### Verifrog.Sim

F# API for driving a Verilator-compiled model of any Verilog design. Generic — works with any top module.

- Create, Reset, Step (clock advancement)
- Read/Write any signal by hierarchical name
- Checkpoint/Restore (snapshot and rewind simulation state)
- Force/Release (hold signals at values for fault injection)
- Signal validation (verify names at startup, not mid-test)
- Structured memory access (declared in `verifrog.toml`)
- Structured register access (declared in `verifrog.toml`)

### Verifrog.Vcd

Fast VCD waveform parser in F#. Standalone library for post-simulation analysis.

- Parse multi-GB VCD files in seconds
- Signal query and filtering
- Timing analysis
- Usable independently or integrated with the test runner

### Verifrog.Runner

Expecto-based test runner infrastructure.

- **Verilator backend**: drives Verifrog.Sim for fast cycle-based tests. Checkpoint/restore for sub-second test setup.
- **iverilog backend**: compiles and runs Verilog testbenches, captures stdout, parses pass/fail. Auto-discovers `*_tb.v` files.
- Parallel execution across both backends
- `dotnet test` integration with filtering, XML/HTML reports

### C Shim (libverifrog_sim)

Generic Verilator wrapper compiled per-design. The `verifrog build` command runs Verilator on the user's Verilog and links the shim into a shared library. No design-specific code — signal access is by name at runtime via Verilator's signal registry.

## Configuration

`verifrog.toml` in the project root:

```toml
[design]
top = "my_module"
sources = ["src/rtl/*.v"]

[verilator]
flags = ["--trace"]

[iverilog]
testbenches = ["src/sim/*_tb.v"]
models = ["src/sim/*.v"]

[test]
output = "scratch"

[memories.data_ram]
path = "u_ram.mem"
banks = 1
depth = 1024
width = 32

[registers]
path = "u_regfile.mem"
width = 8

[registers.map]
CTRL = 0x00
STATUS = 0x01
```

## Extension Model

Verifrog provides generic structured access (memories, registers, signals). Users build design-specific convenience APIs in their own repo on top of Verifrog's API:

```fsharp
// In user's repo, not Verifrog
type MySocSim(sim: VerifrogSim) =
    member _.StartDma(src, dst, len) =
        sim.Register("DMA_SRC").Write(src)
        sim.Register("DMA_DST").Write(dst)
        sim.Register("DMA_LEN").Write(len)
        sim.Register("DMA_CTRL").Write(0x01)
```

## Target Audience

- Open-source silicon projects (chipIgnite, Tiny Tapeout, OpenTitan contributors)
- FPGA developers wanting better test infrastructure than ad-hoc Verilog testbenches
- Anyone doing Verilog who prefers a real programming language for verification
