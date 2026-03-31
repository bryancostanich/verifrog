# Counter Sample

Minimal Verifrog example — 8-bit counter with enable, load, and overflow. This is the best place to start if you're new to Verifrog.

## What it demonstrates

- Creating a simulation with `Sim.Create()` and `SimFixture.create()`
- `Reset()`, `Step()` for simulation control
- `Read()` / `Write()` for signal access
- `SaveCheckpoint()` / `RestoreCheckpoint()` for state snapshots
- `Force()` / `Release()` for signal override
- `Fork()` for what-if exploration

## Design

```
counter #(.WIDTH(8), .LIMIT(0))
  Inputs:  clk, rst_n, enable, load_en, load_value[7:0]
  Outputs: count[7:0], overflow
```

Free-running 8-bit counter. Increments on each clock when `enable=1`. Wraps at 255 (or `LIMIT-1` if `LIMIT>0`). `overflow` pulses for one cycle when count reaches the max value.

## Building and running

```bash
# From the verifrog root directory
export VERIFROG_ROOT=$PWD

# Build the Verilator model
dotnet run --project src/Verifrog.Cli -- build samples/counter

# Run the tests
DYLD_LIBRARY_PATH=samples/counter/build dotnet test tests/Verifrog.Tests
```

On Linux, use `LD_LIBRARY_PATH` instead of `DYLD_LIBRARY_PATH`.

## What to look at

- `rtl/counter.v` — The Verilog design (simple and readable)
- `verifrog.toml` — Minimal configuration pointing at the counter
- The test file in `tests/Verifrog.Tests/SimTests.fs` exercises this design

## Key patterns shown

### Basic read/write

```fsharp
sim.Write("enable", 1L) |> ignore
sim.Step(10)
let count = sim.ReadOrFail("count")   // 10
```

### Checkpoint/restore

```fsharp
let cp = sim.SaveCheckpoint("mid")
sim.Step(100)
sim.RestoreCheckpoint("mid")   // Back to where we saved
```

### Force override

```fsharp
sim.Force("enable", 1L) |> ignore    // Stays enabled even if design logic tries to clear it
sim.Step(50)
sim.Release("enable") |> ignore
```

### Fork exploration

```fsharp
let result = sim.Fork(fun s ->
    s.Write("load_value", 200L) |> ignore
    s.Write("load_en", 1L) |> ignore
    s.Step(1)
    s.ReadOrFail("count"))
// sim state is unchanged after Fork
```
