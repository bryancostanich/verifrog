# Counter Sample

Minimal Verifrog example — 8-bit counter with enable, load, and overflow.

## What it demonstrates

- `Sim.Create()`, `Reset()`, `Step()`
- `Read()` / `Write()` for signal access
- `Checkpoint` / `Restore` for state snapshots
- `Force` / `Release` for signal override
- `Fork` for what-if exploration

## Running

```bash
# Build the Verilator model
export VERIFROG_ROOT=/path/to/Verifrog
verifrog build

# Run tests
DYLD_LIBRARY_PATH=build dotnet test tests/
```

## Design

```
counter #(.WIDTH(8), .LIMIT(0))
  Inputs:  clk, rst_n, enable, load_en, load_value[7:0]
  Outputs: count[7:0], overflow
```

Free-running 8-bit counter. Increments on each clock when `enable=1`. Wraps at 255 (or `LIMIT-1` if `LIMIT>0`). `overflow` pulses when count reaches max.
