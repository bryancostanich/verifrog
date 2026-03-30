# Track Plan: Core Framework

## Phase 1: Extract and Generalize the C Shim

Start at the bottom of the stack. Get a generic shared library building from any Verilog design.

- [ ] Fork sim_shim.cpp from khalkulo, strip all design-specific functions (WgtWrite, ActWrite, MacRead, RegfileRead)
- [ ] Keep: sim_create, sim_destroy, sim_reset, sim_step, sim_read, sim_write, sim_force, sim_release, sim_checkpoint, sim_restore, sim_signal_count, sim_signal_name, sim_signal_bits, sim_suppress_display
- [ ] Verify it compiles and links against a trivial Verilog module (counter)
- [ ] Write a minimal Makefile that takes TOP_MODULE and RTL_SOURCES as inputs

## Phase 2: Verifrog.Sim F# Library

The core API. Get F# talking to the generic C shim.

- [ ] Extract Interop.fs P/Invoke bindings (rename to match libverifrog_sim)
- [ ] Extract Sim type from khalkulo Sim.fs, remove design-specific members
- [ ] Add TOML parser (Tomlyn NuGet package or similar)
- [ ] Implement Memory access: parse `[memories.*]` from TOML, generate read/write methods
- [ ] Implement Register access: parse `[registers]` from TOML, generate named read/write
- [ ] Implement ValidateSignals: check all declared memory/register paths exist at startup
- [ ] Test against the trivial counter module from Phase 1

## Phase 3: Verifrog.Vcd Library

Extract and package the VCD parser.

- [ ] Fork vcd_parser from khalkulo
- [ ] Refactor from CLI-only to library + CLI (expose parsing API, keep CLI as thin wrapper)
- [ ] API: Parse(stream) → VcdFile, signal query by name/glob, value at time, transitions
- [ ] Test against a VCD generated from the Phase 1 counter sim

## Phase 4: Verifrog.Runner

The test infrastructure layer.

- [ ] SimFixture: wraps Sim creation + reset + Level 0 checkpoint
- [ ] Verilator backend: run Expecto tests using SimFixture
- [ ] iverilog backend: Iverilog module (compile, run, capture, parse)
- [ ] iverilog auto-discovery from TOML `[iverilog].testbenches` glob
- [ ] iverilog parameter overrides
- [ ] Expect helpers: Expect.signal, Expect.memory, Expect.register
- [ ] Wire up `dotnet test`
- [ ] Test with the trivial counter (Verilator) and a simple _tb.v (iverilog)

## Phase 5: verifrog CLI

The build tool that ties it together.

- [ ] `verifrog init` — scaffold verifrog.toml template + sample test file
- [ ] `verifrog build` — read TOML, run Verilator, compile shim, link .dylib/.so
- [ ] `verifrog clean` — remove build artifacts from output dir
- [ ] Package as dotnet tool
- [ ] Test: `verifrog init` → edit toml → `verifrog build` → `dotnet test` works end-to-end

## Phase 6: Sample Projects

Prove the framework works for real use cases beyond the trivial counter.

- [ ] **Minimal** (counter): step, read, write, checkpoint
- [ ] **With registers** (ALU + register file): register map in TOML, named access
- [ ] **With memory** (small SRAM design): memory regions in TOML, bank/addr access
- [ ] **With iverilog** (design + BFM testbench): dual-backend, timing-accurate test alongside Verilator tests
- [ ] Each sample is self-contained: own verifrog.toml, own test project, own README

## Phase 7: Documentation

- [ ] README.md: what, why, quick start (install → init → build → test)
- [ ] Getting Started guide (longer walkthrough)
- [ ] API reference: all public types and functions
- [ ] Configuration reference: verifrog.toml format
- [ ] Extension guide: building design-specific layers
- [ ] Architecture overview: layer diagram, data flow, how build works

## Phase 8: Khalkulo Migration

Port khalkulo to use Verifrog as a dependency instead of its internal sim_debugger.

- [ ] Create khalkulo extension layer (KhalkuloSim wrapping VerifrogSim)
- [ ] Move SRAM backdoor, MAC access, CLI commands into extension
- [ ] Create khalkulo's verifrog.toml (memories, registers, design config)
- [ ] Verify all existing 04b/04c tests still pass
- [ ] Remove internal sim_debugger and vcd_parser from khalkulo (replaced by Verifrog dependency)
