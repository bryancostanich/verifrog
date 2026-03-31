# Track 01: Core Framework

## Objective

Build Verifrog v1 — extract and generalize the khalkulo sim_debugger, VCD parser, and test runner into a standalone, design-agnostic framework. Ship with documentation, sample projects, and a clean API.

## Source Material

Porting from three proven khalkulo implementations:

| khalkulo source | Verifrog target | What moves | What stays in khalkulo |
|---|---|---|---|
| `tools/sim_debugger/` | Verifrog.Sim + C shim | Sim type, Interop.fs, generic sim_shim functions | WgtWrite, ActWrite, MacRead, CLI commands |
| `tools/vcd_parser/` | Verifrog.Vcd | Parser core, signal query | CLI entry point (thin wrapper) |
| `tests/Fixtures/` | Verifrog.Runner | SimFixture, Iverilog, generic Expect helpers | Expect.weightSram, macWeight, macAcc; all of Stimulus module |
| `tests/` structure | Sample projects | Test organization patterns | All khalkulo-specific tests |

The khalkulo test suite (151 tests, 130 passing) is the proof that these patterns work. Verifrog extracts the framework; khalkulo consumes Verifrog with a design-specific extension layer. Migration is complete — no duplicate code remains in khalkulo.

### Proven Patterns from khalkulo (already working)

The following are battle-tested in khalkulo's 150-test suite and should be preserved in the extraction:

**SimFixture** — checkpoint levels (PostReset → PostConfig → PostWeights → PostInference), create/restore lifecycle, display suppression. Currently khalkulo-hardcoded to `libkhalkulo_sim.dylib` path; generalize to read from `verifrog.toml`.

**Iverilog backend** — compile/run/capture/parse flow, auto-discovery of `*_tb.v` files, parameter overrides (`-P tb.PARAM=value`), BFM auto-detection, timeout handling. Currently hardcoded to `source/rtl/` and `source/sim/` paths; generalize to read from `verifrog.toml`.

**Expect helpers** — `Expect.signal`, `Expect.register` are generic and move to Verifrog. `Expect.weightSram`, `Expect.actSram`, `Expect.macWeight`, `Expect.macAcc` are khalkulo-specific and stay as extensions.

**Dual-backend runner** — Verilator tests (<1s, 117 tests) and iverilog tests (20-30 min, 32 tests) under one `dotnet test` invocation. This architecture moves wholesale to Verifrog.

## Deliverables

### Verifrog.Sim (F# library)

- [x]`Sim` type: Create, Reset, Step, Read, Write, SignalBits, ListSignals
- [x]Checkpoint/Restore: SaveCheckpoint, RestoreCheckpoint (named snapshots)
- [x]Force/Release: hold signals at values, release
- [x]Signal validation: ValidateSignals checks all declared paths exist at startup
- [x]Memory access: `sim.Memory("name").Read(bank, addr)` / `.Write(bank, addr, value)` driven from TOML config
- [x]Register access: `sim.Register("NAME").Read()` / `.Write(value)` driven from TOML config
- [x]P/Invoke bindings to libverifrog_sim (extracted from khalkulo Interop.fs)
- [x]TOML config parser (reads `verifrog.toml`, builds memory/register maps, `test_output` for VCD/log directory)

### libverifrog_sim (C shim)

- [x]Generic Verilator wrapper: sim_create, sim_destroy, sim_reset, sim_step
- [x]Signal access: sim_read, sim_write, sim_force, sim_release by hierarchical name
- [x]Signal enumeration: sim_signal_count, sim_signal_name, sim_signal_bits
- [x]Checkpoint: sim_checkpoint, sim_restore, sim_checkpoint_free
- [x]Display suppression: sim_suppress_display
- [x]No design-specific code — everything resolved by name at runtime
- [x]Extracted from khalkulo sim_shim.cpp, removing WgtWrite/ActWrite/MacRead etc.

### Verifrog.Vcd (F# library)

- [x]VCD parser: parse multi-GB files efficiently
- [x]Signal query by name/pattern
- [x]Value-at-time lookup
- [x]Transition counting and timing analysis
- [x]Extracted from khalkulo vcd_parser, packaged as a library (not just a CLI)

### Verifrog.Runner (F# library)

Generalized from khalkulo's `tests/Fixtures/`:

- [x]SimFixture: create instance, reset, checkpoint Level 0, restore per test. Reads lib path and config from `verifrog.toml` instead of hardcoded paths.
- [x]Verilator backend: creates Sim instances, manages fixtures, runs Expecto tests
- [x]Iverilog backend: compile, run vvp, capture stdout, parse pass/fail. Reads source paths from `verifrog.toml` instead of hardcoded `source/rtl/`.
- [x]Iverilog auto-discovery: scan testbench directories from TOML `[iverilog].testbenches` glob
- [x]Iverilog parameter override: pass Verilog parameters at compile time (proven: `I2C_HALF_PERIOD`)
- [x]Iverilog extra sources: auto-detect BFM/model dependencies or declare in TOML `[iverilog].models`
- [x]Expect helpers: Expect.signal, Expect.memory, Expect.register with readable failure output (signal name, expected/actual, cycle count)
- [x]Parallel execution: separate Verilator instances per test list
- [x]`dotnet test` integration via YoloDev.Expecto.TestSdk

### verifrog CLI

- [x]`verifrog build` — reads `verifrog.toml`, runs Verilator on declared sources, compiles C shim, links shared library into `[test].output` directory
- [x]`verifrog clean` — remove build artifacts
- [x]`verifrog init` — scaffold a new project (create `verifrog.toml` template, sample test file, sample .fsproj)
- [x]F# dotnet tool, installable via `dotnet tool install`

### Documentation

- [x]README.md: what Verifrog is, quick start, architecture overview
- [x]Getting Started guide: install deps, `verifrog init`, write first test, run it
- [x]API reference: Sim, Memory, Register, Checkpoint, Force, Expect helpers
- [x]Configuration reference: `verifrog.toml` format, all sections
- [x]Extension guide: how to build design-specific APIs on top of Verifrog (using khalkulo as the example)
- [x]Architecture doc: how the layers connect (F# → P/Invoke → C shim → Verilator)

### Sample Projects

- [x]**Minimal**: simple counter module, 3-4 tests demonstrating Sim basics (step, read, write, checkpoint)
- [x]**With registers**: ALU or small SoC with a register file, demonstrates register map access from TOML
- [x]**With memory**: design with SRAM, demonstrates memory region access from TOML
- [x]**With iverilog**: design with a Verilog testbench (BFM, timing), demonstrates dual-backend runner and auto-discovery
- [x]Each sample includes its own `verifrog.toml`, test project, and README

## Architecture

```
┌─────────────────────────────────────────────────┐
│  User's Test Project (Expecto)                   │
│  - Design-specific extensions (optional)         │
│  - Test functions using Verifrog API             │
├─────────────────────────────────────────────────┤
│  Verifrog.Runner                                 │
│  - SimFixture (checkpoint levels, lifecycle)     │
│  - Verilator backend                             │
│  - iverilog backend (compile/run/parse/discover) │
│  - Expect helpers (signal, memory, register)     │
├─────────────────────────────────────────────────┤
│  Verifrog.Sim              Verifrog.Vcd          │
│  - Sim type                - VCD parser          │
│  - Memory/Register access  - Signal query        │
│  - TOML-driven config      - Timing analysis     │
│  - P/Invoke                                      │
├─────────────────────────────────────────────────┤
│  libverifrog_sim.dylib/.so                       │
│  - Generic Verilator C wrapper                   │
│  - Built per-design via `verifrog build`         │
├─────────────────────────────────────────────────┤
│  Verilator (user's compiled RTL)                 │
└─────────────────────────────────────────────────┘
```

### Extension model (user's repo, not Verifrog)

```
khalkulo/verifrog/
├── Khalkulo.TestRunner.fsproj          ← extension library
│   ├── Khalkulo.fs                     ← signal accessors (regfile, SRAM, MAC)
│   ├── KhalkuloExpect.fs               ← design-specific assertions + delegates to Runner.Expect
│   ├── KhalkuloIverilog.fs             ← delegates to Runner.Iverilog with khalkulo paths
│   ├── Stimulus.fs                     ← register addresses, layer types, config helpers
│   └── KhalkuloCli.fs                  ← interactive debugger commands
├── tests/
│   └── Khalkulo.Tests.fsproj           ← test project (151 tests)
│       ├── Unit/                       ← FSM, MAC, Regfile, DPC, Requant, Proof
│       ├── Integration/                ← Dimensions, Quant, Address, MultiLayer
│       ├── Stress/                     ← CDC, ErrorRecovery, Sequential, Patterns
│       └── Golden/                     ← Path 1/2 golden inference
├── verifrog.toml                       ← design config (memories, registers, paths)
└── test_output/                        ← VCD traces, logs (gitignored)
```

The extension layer uses Verifrog via project references (Verifrog.Sim, Verifrog.Runner, Verifrog.Cli). SimFixture is used directly from Verifrog.Runner — no khalkulo copy.

## Non-Goals for v1

- NuGet package distribution (clone-and-reference for v1, package later)
- Windows support (macOS and Linux only — Verilator doesn't have great Windows support anyway)
- SystemVerilog design support (Verilator handles the synthesizable subset, but we don't test or document it)
- Constrained random generation
- Fault injection framework (users can do this with Force/Release, but no structured campaign runner)
- GUI / waveform viewer integration
