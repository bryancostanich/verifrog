# Track Plan: Core Framework

This is primarily an extraction and generalization from khalkulo's proven 150-test implementation — not a greenfield build. Each phase has a working khalkulo implementation as the starting point.

## Phase 1: Extract and Generalize the C Shim ✅

Source: `khalkulo/tools/sim_debugger/src/sim_shim.cpp`

- [x] Fork sim_shim.cpp, strip all design-specific functions (sim_wgt_write, sim_act_write, sim_regfile_read, sim_mac_weight_reg, etc.)
- [x] Keep generic API: sim_create, sim_destroy, sim_reset, sim_step, sim_read, sim_write, sim_force, sim_release, sim_signal_count, sim_signal_name, sim_signal_bits, sim_checkpoint, sim_restore, sim_checkpoint_free, sim_suppress_display
- [x] Rename to libverifrog_sim
- [x] Verify it compiles against a trivial Verilog module (counter)
- [x] Write build script that takes top module and source list as inputs

**Key decision:** Used `--public-flat-rw` + VerilatedScope/VerilatedVar for generic signal discovery instead of VPI (VPI writes don't persist for input ports). See AD-001 in ARCHITECTURE_DECISIONS.md.

## Phase 2: Verifrog.Sim F# Library ✅

Source: `khalkulo/tools/sim_debugger/src/Sim.fs`, `Interop.fs`

- [x] Extract Interop.fs P/Invoke bindings, retarget to libverifrog_sim
- [x] Extract Sim type, remove design-specific members (WgtWrite, ActWrite, RegfileWrite, etc.)
- [x] Add TOML parser (Tomlyn NuGet or similar)
- [x] Implement Memory access: parse `[memories.*]` sections from verifrog.toml, resolve `{bank}` parameterized paths, expose `sim.Memory("name").Read(bank, addr)` / `.Write(bank, addr, value)`
- [x] Implement Register access: parse `[registers]` section, expose `sim.Register("NAME").Read()` / `.Write(value)`
- [x] Implement ValidateSignals: on Sim creation, verify all declared memory/register paths resolve to real signals
- [x] Test against the trivial counter from Phase 1

11 Expecto tests pass against counter module.

## Phase 3: Verifrog.Vcd Library ✅

Source: `khalkulo/tools/vcd_parser/`

- [x] Fork vcd_parser
- [x] Refactor from CLI-only to library + optional CLI (expose parsing API as public module)
- [x] API: Parse(stream) → VcdFile, signal query by name/glob, value at time, transition count/timing
- [x] Package as a standalone F# library project
- [x] Test against a VCD generated from the Phase 1 counter sim

13 VCD parser tests pass.

## Phase 4: Verifrog.Runner ✅

Source: `khalkulo/tests/Fixtures/` (SimFixture.fs, Iverilog.fs, Expect.fs)

- [x] Extract SimFixture: replace hardcoded `libkhalkulo_sim.dylib` path with TOML-driven `[test].output` path. Keep checkpoint level pattern (PostReset, PostConfig, PostWeights, PostInference).
- [x] Extract Iverilog module: replace hardcoded `source/rtl/`, `source/sim/` paths with TOML-driven `[design].sources`, `[iverilog].testbenches`, `[iverilog].models`. Keep auto-discovery, parameter overrides, BFM auto-detection, timeout handling, pass/fail parsing.
- [x] Extract generic Expect helpers: `Expect.signal`, `Expect.register`, `Expect.memory`, `Expect.iverilogPassed`. Drop khalkulo-specific helpers (weightSram, actSram, macWeight, macAcc — those become extension-layer).
- [x] Wire up `dotnet test` via YoloDev.Expecto.TestSdk
- [x] Test with trivial counter (Verilator) and a simple `*_tb.v` (iverilog)

6 Runner tests pass. Total: 30 tests across all libraries.

## Phase 5: verifrog CLI ✅

- [x] `verifrog init` — scaffold verifrog.toml template + sample test .fsproj + sample test file
- [x] `verifrog build` — read TOML, run Verilator, compile shim, link .dylib/.so into output dir
- [x] `verifrog clean` — remove build artifacts from output dir
- [x] Package as dotnet tool
- [x] End-to-end test: `verifrog init` → edit toml → `verifrog build` → `dotnet test` works

## Phase 6: Sample Projects ✅

Each sample is self-contained with its own verifrog.toml, .fsproj, README.

- [x] **Minimal** (counter): step, read, write, checkpoint. Proves the basic flow works.
- [x] **With registers** (ALU + register file): register map in TOML, named access, parametric sweeps
- [x] **With memory** (small SRAM design): memory regions in TOML, bank/addr access, backdoor loading
- [x] **With iverilog** (design + BFM testbench): dual-backend, timing-accurate test alongside Verilator tests, auto-discovery
- [x] **With I2C BFM** (from khalkulo): reusable I2C master BFM with protocol tasks, testbench demonstrating START/STOP/write/read/error handling

## Phase 7: Documentation ✅

- [x] README.md: what, why, quick start (install → init → build → test)
- [x] Getting Started guide: longer walkthrough with a real design
- [x] API reference: Sim, Memory, Register, Checkpoint, Force, Expect, SimFixture, Iverilog
- [x] Configuration reference: verifrog.toml format, all sections with examples
- [x] Extension guide: building design-specific layers (khalkulo as the worked example — Stimulus module, KhalkuloSim wrapper, custom Expect helpers)
- [x] Architecture doc: layer diagram, data flow, how `verifrog build` works

## Phase 8: Khalkulo Migration ✅

Port khalkulo to consume Verifrog as a dependency.

- [x] Create khalkulo extension layer as module functions (not wrapper type):
  - Khalkulo.Verifrog.Sim (`Khalkulo.fs`): regfileRead/Write, wgtRead/Write, actRead/Write, macWeightReg/AccBank0/Product, waitForDone
  - Khalkulo.Verifrog.Expect (`KhalkuloExpect.fs`): register, weightSram, actSram, macWeight, macAcc + delegates to Verifrog.Runner.Expect for signal/signalSatisfies/iverilogPassed
  - Khalkulo.Verifrog.Stimulus (`Stimulus.fs`): register addresses, layer types, config helpers, triggerStart
  - Khalkulo.Verifrog.Iverilog (`KhalkuloIverilog.fs`): delegates to Verifrog.Runner.Iverilog with khalkulo-specific paths/config
  - Khalkulo.Verifrog.Cli (`KhalkuloCli.fs`): design-specific CLI commands (mac, regfile, wgt-write, act-write, load-hex, status)
- [x] SimFixture used directly from Verifrog.Runner (no khalkulo copy)
- [x] Create khalkulo's `verifrog.toml` (with `test_output` for VCD traces)
- [x] Establish baseline: 116 passed, 14 skipped, 20 failed (150 total)
- [x] Update all test files to import from Verifrog + extension layer
- [x] Resolve issue #9, unblock 14 inference tests: **118 passed, 0 skipped, 12 failed** (12 are pre-existing iverilog TB issues)
- [x] Port sim_debugger CLI to Verifrog (generic REPL in Debugger.fs + khalkulo KhalkuloCli.fs extension)
- [x] Port vcd_parser CLI to Verifrog.Vcd.Cli (standalone tool, no design-specific code)
- [x] Update khalkulo docs/development.md to reference Verifrog
- [x] Remove internal sim_debugger from khalkulo (3,727 lines deleted, all doc refs updated)
- [x] Remove internal vcd_parser from khalkulo
- [x] Consolidate: tests moved into `khalkulo/verifrog/tests/`, projects renamed (Khalkulo.TestRunner.fsproj, Khalkulo.Tests.fsproj)

**Architecture:** Extension layer is pure module functions taking `Verifrog.Sim.Sim`. No wrapper types. Dependency flows one direction: khalkulo → Verifrog, never the reverse.

**Issue #9 resolved (2026-03-30):** The reported Verilator dual-port register file off-by-one (bryancostanich/khalkulo#9) was a misdiagnosis. Deep analysis of Verilator source code and generated C++ proved the NBA scheduling is correct. The actual root cause was a test register layout mismatch — tests used the old 5-byte per-layer stride but the FSM expects 6 bytes (OUT_CH split into high/low). Fix: updated register writes, unblocked 14 inference tests, all 118 Verilator tests pass.

**Post-migration cleanup (2026-03-30):**
- Deleted KhalkuloFixture.fs (was a verbatim copy of Verifrog.Runner.SimFixture) — tests now use SimFixture directly
- Rewrote KhalkuloIverilog.fs to delegate to Verifrog.Runner.Iverilog instead of reimplementing it
- KhalkuloExpect.fs generic helpers (signal, signalSatisfies, iverilogPassed) now delegate to Verifrog.Runner.Expect
- Removed duplicate triggerStart from Khalkulo.fs (Stimulus.fs has the version with error handling)
- Moved tests from `khalkulo/tests/` into `khalkulo/verifrog/tests/` to consolidate test infrastructure
- Renamed projects: Khalkulo.Verifrog.fsproj → Khalkulo.TestRunner.fsproj, khalkulo_tests.fsproj → Khalkulo.Tests.fsproj
- Added `test_output` config to verifrog.toml so VCD traces go to `verifrog/test_output/` instead of project root
