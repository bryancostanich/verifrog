# API Reference

## Verifrog.Sim

### Sim

The core simulation type. Wraps a Verilator-compiled model.

#### Creation

| Method | Description |
|---|---|
| `Sim.Create()` | Create a new simulation instance (no TOML config) |
| `Sim.Create(config)` | Create with a parsed `VerifrogConfig` |
| `Sim.Create(tomlPath)` | Create from a `verifrog.toml` file path |
| `Sim.SuppressDisplay(bool)` | Enable/disable `$display` output suppression |

#### Simulation Control

| Method | Description |
|---|---|
| `sim.Reset(?cycles)` | Reset the model. Default: 10 cycles. |
| `sim.Step(?n)` | Advance N clock cycles. Default: 1. |
| `sim.Cycle` | Current cycle count (since last reset) |

#### Signal Access

| Method | Returns | Description |
|---|---|---|
| `sim.Read(name)` | `SimResult<int64>` | Read signal by hierarchical name |
| `sim.ReadOrFail(name)` | `int64` | Read signal, throw on error |
| `sim.Write(name, value)` | `SimResult<unit>` | Write signal value |
| `sim.SignalBits(name)` | `int` | Get signal bit width (-1 if not found) |
| `sim.SignalCount` | `int` | Total registered signals |
| `sim.ListSignals()` | `string list` | All signal names |

#### Checkpoints

| Method | Description |
|---|---|
| `sim.SaveCheckpoint(name, ?desc)` | Save named checkpoint, returns `CheckpointHandle` |
| `sim.RestoreCheckpoint(name)` | Restore to a named checkpoint |
| `sim.Checkpoint(?desc)` | Create anonymous checkpoint |
| `sim.Restore(handle)` | Restore from a handle |
| `Sim.FreeCheckpoint(handle)` | Free checkpoint memory |
| `sim.ListCheckpoints()` | List all named checkpoints |

#### Force/Release

| Method | Description |
|---|---|
| `sim.Force(name, value)` | Force signal to value (persists across steps) |
| `sim.Release(name)` | Release a forced signal |
| `sim.ReleaseAll()` | Release all forces |
| `sim.ForceCount` | Number of active forces |

#### What-If Exploration

| Method | Description |
|---|---|
| `sim.Fork(scenario)` | Run scenario, return result, restore original state |
| `sim.Compare(a, b)` | Run two scenarios from same state, return both results |
| `sim.Sweep(values, scenario)` | Run scenario for each value, return all results |

#### Trace/RunUntil

| Method | Description |
|---|---|
| `sim.Trace(signals, cycles)` | Record signal values over N cycles |
| `sim.RunUntil(predicate, ?max)` | Step until predicate is true |
| `sim.RunUntilSignal(name, target, ?max)` | Step until signal equals value |

#### TOML-Driven Access

| Method | Description |
|---|---|
| `sim.Memory(name)` | Get `MemoryAccessor` for named memory region |
| `sim.Register(name)` | Get `RegisterAccessor` for named register |
| `sim.MemoryNames` | List configured memory names |
| `sim.RegisterNames` | List configured register names |
| `sim.ValidateSignals()` | Check all declared paths resolve; returns error list |

### MemoryAccessor

Returned by `sim.Memory("name")`. Path templates from TOML.

| Method | Description |
|---|---|
| `mem.Read(bank, addr)` | Read word at bank/addr |
| `mem.Write(bank, addr, value)` | Write word at bank/addr |
| `mem.Name` / `Banks` / `Depth` / `Width` | Config metadata |

### RegisterAccessor

Returned by `sim.Register("NAME")`. Address from TOML register map.

| Method | Description |
|---|---|
| `reg.Read()` | Read register value |
| `reg.Write(value)` | Write register value |
| `reg.Name` / `Address` | Config metadata |

---

## Verifrog.Vcd

### VcdParser

| Function | Description |
|---|---|
| `VcdParser.parseAll(filename)` | Parse entire VCD file |
| `VcdParser.parse(filename, maxTime)` | Parse with time limit |
| `VcdParser.parseFiltered(filename, patterns, maxTime)` | Parse only matching signals |
| `VcdParser.findSignals(vcd, pattern)` | Find signals by name/glob |
| `VcdParser.transitions(vcd, fullPath)` | Get transitions for a signal |
| `VcdParser.valueAtTime(vcd, fullPath, time)` | Value at specific time |
| `VcdParser.transitionCount(vcd, fullPath)` | Count transitions |
| `VcdParser.firstTimeAtValue(vcd, fullPath, value)` | First time signal has value |
| `VcdParser.uniqueValues(vcd, fullPath)` | All unique values |
| `VcdParser.highPulseCount(vcd, fullPath)` | Count of 0->1 transitions |
| `VcdParser.parseBinValue(str)` | Parse binary string to int |
| `VcdParser.timeToUs(timePs)` | Convert ps to microseconds |

---

## Verifrog.Runner

### SimFixture

| Function | Description |
|---|---|
| `SimFixture.create()` | Create sim, suppress display, reset |
| `SimFixture.createFromConfig(config)` | Create from TOML config |
| `SimFixture.createFromToml(path)` | Create from .toml file |
| `SimFixture.createWithCheckpoint()` | Create with L0 checkpoint |
| `SimFixture.restore(sim, level)` | Restore to checkpoint level |
| `SimFixture.saveLevel(sim, level)` | Save checkpoint at level |

Levels: `PostReset`, `PostConfig`, `PostWeights`, `PostInference`

### Expect

| Function | Description |
|---|---|
| `Expect.signal sim name expected msg` | Assert signal value |
| `Expect.signalSatisfies sim name pred msg` | Assert signal matches predicate |
| `Expect.memory sim memName bank addr expected msg` | Assert memory value |
| `Expect.register sim regName expected msg` | Assert register value |
| `Expect.iverilogPassed result msg` | Assert iverilog test passed |

### Iverilog

| Function | Description |
|---|---|
| `Iverilog.run root config tbName overrides extras` | Compile and run testbench |
| `Iverilog.runSimple root config tbName` | Run with no overrides |
| `Iverilog.runAuto root config tbName` | Auto-detect BFM dependencies |
| `Iverilog.discover root config` | Find testbenches matching TOML patterns |
| `Iverilog.passed result` | Check if stdout indicates PASS |
| `Iverilog.parseSummary stdout` | Parse pass/fail counts |
