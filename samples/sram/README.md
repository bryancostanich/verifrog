# SRAM Sample

Demonstrates TOML-driven memory region access with a behavioral SRAM.

## What it demonstrates

- Memory region declaration in `verifrog.toml` with `[memories.*]`
- Named memory access: `sim.Memory("data").Read(bank, addr)` / `.Write()`
- Backdoor loading — writing directly to the Verilog memory array
- Read-after-write verification

## Design

```
sram #(.DEPTH(256), .WIDTH(8))
  Single-port 256x8 behavioral SRAM
  Inputs:  clk, cs_n, we_n, addr[7:0], din[7:0]
  Outputs: dout[7:0]
```

## Building and running

```bash
export VERIFROG_ROOT=$PWD

# Build
dotnet run --project src/Verifrog.Cli -- build samples/sram

# Run tests
DYLD_LIBRARY_PATH=samples/sram/build dotnet test tests/Verifrog.Tests
```

## Configuration

```toml
[memories.data]
path = "u_sram.mem"
banks = 1
depth = 256
width = 8
```

## Key patterns shown

### Backdoor write and read

```fsharp
let ram = sim.Memory("data")
ram.Write(0, 42, 0xABL)              // bank 0, addr 42, value 0xAB
let v = ram.Read(0, 42)               // SimResult<int64>
```

### Bulk load test data

```fsharp
for addr in 0 .. 255 do
    ram.Write(0, addr, int64 addr) |> ignore
```

### Verify via design's read port

```fsharp
sim.Write("addr", 42L) |> ignore
sim.Write("cs_n", 0L) |> ignore
sim.Write("we_n", 1L) |> ignore      // Read mode
sim.Step(2)
Expect.signal sim "dout" 0xABL "read matches backdoor write"
```

## What to look at

- `verifrog.toml` — Memory configuration
- `rtl/sram.v` — Behavioral SRAM model
- Test file in `tests/Verifrog.Tests/` — Backdoor and bus-driven patterns
