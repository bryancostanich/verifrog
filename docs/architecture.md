# Architecture

## Layer Diagram

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
│  - P/Invoke to C shim                            │
├─────────────────────────────────────────────────┤
│  libverifrog_sim.dylib/.so                       │
│  - Generic Verilator C wrapper                   │
│  - Built per-design via `verifrog build`         │
├─────────────────────────────────────────────────┤
│  Verilator (user's compiled RTL)                 │
└─────────────────────────────────────────────────┘
```

## Data Flow

### `verifrog build`

```
verifrog.toml
  → read [design].top and [design].sources
  → verilator --cc --vpi --public-flat-rw --trace --top-module <top> <sources>
  → generates C++ model in build/verilated/
  → generates build/verifrog_model.h (typedef for the specific design)
  → clang++ compiles: verifrog_sim.cpp + verilated/*.cpp + verilator support
  → outputs build/libverifrog_sim.dylib
```

### Test execution

```
Test calls Sim.Create()
  → P/Invoke: sim_create()
  → Instantiates Verilator model
  → Calls model->eval() to populate scope tables
  → Walks VerilatedScope/VerilatedVar to build signal map
  → Returns SimContext*

Test calls sim.Write("enable", 1L)
  → P/Invoke: sim_write(ctx, "enable", 1)
  → Looks up "enable" in signal map → gets direct pointer to rootp->enable
  → Writes value through pointer
  → Calls eval_model() to propagate combinational logic

Test calls sim.Step(5)
  → P/Invoke: sim_step(ctx, 5)
  → For each cycle: clk=0 → eval → apply_forces → clk=1 → eval → apply_forces
  → Increments cycle_count

Test calls sim.SaveCheckpoint("L0")
  → P/Invoke: sim_checkpoint(ctx)
  → memcpy of rootp → heap-allocated buffer
  → Stores cycle_count in checkpoint
```

## Signal Resolution

The C shim discovers signals at init using Verilator's `--public-flat-rw` flag:

1. `Verilated::scopeNameMap()` returns all scopes (e.g., `TOP`, `TOP.counter`)
2. For each scope, `scope->varsp()` returns all variables
3. Each `VerilatedVar` provides `datap()` (direct pointer) and `entBits()` (bit width)
4. Signals are registered with friendly names (e.g., `count`) and full paths (e.g., `TOP.counter.count`)

**Important**: The TOP scope's port entries take priority over sub-module internal copies. Verilator creates both `rootp->enable` (the actual port) and `rootp->counter__DOT__enable` (internal copy). Writing to the internal copy is useless — `eval()` overwrites it from the port.

## Checkpoint Implementation

Checkpoints use `memcpy` of the Verilator root model struct (`V<top>___024root`).

**Works for**: Most designs where Verilator inlines all state into the root struct.

**Limitation**: Designs with `generate` loops that Verilator splits into separate C++ classes (e.g., `Vkhalkulo_top_mac_group`) need custom checkpoint logic. These submodule structs are not captured by the root memcpy.

**Workaround**: For complex designs, the extension layer can add custom checkpoint logic that also saves/restores submodule state (as khalkulo does for its 8 MAC groups).

## Iverilog Backend

Separate from Verilator. Used for timing-accurate tests with Verilog testbenches:

```
verifrog.toml [iverilog].testbenches
  → discover *_tb.v files matching globs
  → iverilog -o build/tb.vvp <rtl sources> <tb file> <model sources>
  → vvp build/tb.vvp
  → capture stdout
  → parse for "PASSED" / "ALL TESTS PASSED"
  → return IverilogResult with exit code, stdout, stderr, elapsed time
```

Both backends run under one `dotnet test` invocation via Expecto.

## Further reading

- [Architecture Decisions](ARCHITECTURE_DECISIONS.md) — Why direct pointers over VPI, memcpy checkpoints, and other non-obvious choices
- [Extension Guide](extension-guide.md) — How to build design-specific layers on top of this architecture
