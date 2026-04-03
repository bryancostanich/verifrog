# Verifrog — Project Instructions

## What this is

Verifrog is an open-source Verilog/SystemVerilog testing and debugging framework in F#. It wraps Verilator-compiled RTL models with a type-safe API for signal access, checkpoints, and test automation.

## Build and test

```bash
verifrog build samples/counter        # Build Verilator model
verifrog test samples/counter         # Run all tests
verifrog test samples/counter --category Smoke  # Run smoke tests only
```

For the framework's own tests:
```bash
DYLD_LIBRARY_PATH=$PWD/samples/counter/build dotnet run --project tests/Verifrog.Tests/Verifrog.Tests.fsproj -- --sequenced
```

## MCP Debug Tools

The `verifrog-counter` MCP server provides direct simulation control. Use these tools when:
- A test fails and you need to investigate signal behavior
- You want to step through a simulation cycle-by-cycle
- You need to inspect signal values at specific points
- You want to test a hypothesis by forcing signals or checkpointing/restoring state

### Available tools

| Tool | What it does | When to use |
|------|-------------|-------------|
| `debug_status` | Cycle count, signal count, forces, checkpoints | First call — orient yourself |
| `debug_signals` | List signal names (optional filter) | Discover what signals exist |
| `debug_read` | Read signal values | Observe current state |
| `debug_trace` | Record signals over N cycles | Watch how signals change over time |
| `debug_write` | Write a signal value | Set up stimulus |
| `debug_step` | Advance N clock cycles | Progress the simulation |
| `debug_checkpoint` | Save simulation state | Before trying something risky |
| `debug_restore` | Restore to a checkpoint | Rewind after investigating |
| `debug_force` | Hold a signal at a value | Inject faults, override logic |
| `debug_release` | Release a forced signal | Let normal logic resume |
| `debug_run_until` | Step until signal == value | Skip to the interesting part |
| `debug_reset` | Reset to cycle 0 | Start over |

### Debugging workflow

1. **Orient**: `debug_status` and `debug_signals` to understand the design
2. **Set up**: `debug_write` to configure inputs, `debug_step` to advance
3. **Observe**: `debug_read` to check signal values
4. **Checkpoint**: `debug_checkpoint` before exploring
5. **Investigate**: step, read, force — try hypotheses
6. **Restore**: `debug_restore` to rewind and try a different approach

### Example: debugging a counter overflow

```
debug_signals(filter="count")    → find signal names
debug_write(signal="enable", value=1)
debug_step(n=250)
debug_read(signals=["count", "overflow"])  → count=250, overflow=0
debug_checkpoint(name="near_overflow")
debug_step(n=10)
debug_read(signals=["count", "overflow"])  → count=4, overflow=1 (wrapped!)
debug_restore(name="near_overflow")        → back to cycle 250
debug_step(n=5)
debug_read(signals=["count"])              → count=255 (about to wrap)
```

## Project layout

```
bin/verifrog              — CLI wrapper (bash)
src/
  Verifrog.Sim/           — Core sim API (F#, P/Invoke to C shim)
  Verifrog.Runner/        — Test infrastructure (Expecto-based)
  Verifrog.Vcd/           — VCD waveform parser
  Verifrog.Cli/           — CLI: init, build, debug, debug-server, mcp-server
  Verifrog.VSCodeExtension/ — VS Code extension (TypeScript)
  shim/                   — C++ Verilator wrapper
tests/Verifrog.Tests/     — Framework tests
samples/counter/          — Minimal sample project
```

## Key conventions

- Tests use Expecto with categories: Smoke, Unit, Integration, Parametric, Stress, Golden, Regression
- `verifrog.toml` is the project config file (design sources, build output, memories, registers)
- `--sequenced` flag is required for test runs (Verilator global state is not thread-safe)
- Sim.Step() returns cycle count (uint64), not unit
- Non-optional overloads exist for debugger compatibility: `StepCycles(n)`, `Save(name)`, `Restore(name)`
