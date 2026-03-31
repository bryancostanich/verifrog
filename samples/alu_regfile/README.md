# ALU + Register File Sample

Demonstrates TOML-driven register access with a simple ALU and register file.

## What it demonstrates

- Register map declared in `verifrog.toml`
- Named register access: `sim.Register("R0").Read()` / `.Write()`
- Parametric sweeps across ALU operations with `Sweep()`
- `Fork()` / `Compare()` for testing multiple operations from the same state

## Design

```
alu_regfile
  Register file: 8 entries x 8 bits, 2 read ports, 1 write port
  ALU: combinational (ADD, SUB, AND, OR), 1-cycle latency
  Inputs:  clk, rst_n, alu_op[1:0], rd_addr_a[2:0], rd_addr_b[2:0],
           wr_en, wr_addr[2:0], wr_data[7:0], alu_start
  Outputs: alu_result[7:0], alu_valid
```

## Building and running

```bash
export VERIFROG_ROOT=$PWD

# Build
dotnet run --project src/Verifrog.Cli -- build samples/alu_regfile

# Run tests
DYLD_LIBRARY_PATH=samples/alu_regfile/build dotnet test tests/Verifrog.Tests
```

## Configuration

The `verifrog.toml` declares a register map:

```toml
[registers]
path = "u_regfile.regs"
width = 8

[registers.map]
R0 = 0
R1 = 1
R2 = 2
# ...
```

This lets tests use `sim.Register("R0")` instead of hardcoding signal paths.

## Key patterns shown

### Named register access

```fsharp
sim.Register("R0").Write(42L) |> ignore
sim.Register("R1").Write(17L) |> ignore
sim.Step(1)
let sum = sim.ReadOrFail("alu_result")  // 59
```

### Parametric sweep

```fsharp
let results = sim.Sweep(
    [0L; 1L; 2L; 3L],   // ADD, SUB, AND, OR
    fun op s ->
        s.Write("alu_op", op) |> ignore
        s.Step(2)
        s.ReadOrFail("alu_result"))
```

## What to look at

- `verifrog.toml` — Register map configuration
- `rtl/alu_regfile.v` — The design
- Test file in `tests/Verifrog.Tests/` — Sweep and compare patterns
