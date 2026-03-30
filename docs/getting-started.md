# Getting Started with Verifrog

This guide walks through setting up Verifrog with a real Verilog design.

## Prerequisites

Install these before starting:

- **.NET 8+ SDK**: `brew install dotnet` (macOS) or see [dotnet.microsoft.com](https://dotnet.microsoft.com/download)
- **Verilator 5+**: `brew install verilator` (macOS) or [verilator.org](https://verilator.org/guide/latest/install.html)
- **clang++** (macOS, included with Xcode) or **g++** (Linux)
- **Icarus Verilog** (optional): `brew install icarus-verilog` — only needed for timing-accurate testbenches

## Step 1: Clone Verifrog

```bash
git clone https://github.com/your-org/verifrog.git
export VERIFROG_ROOT=$(pwd)/verifrog
```

## Step 2: Initialize your project

```bash
cd your-project
dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- init .
```

This creates:
- `verifrog.toml` — design configuration
- `tests/` — sample F# test project

## Step 3: Configure your design

Edit `verifrog.toml`:

```toml
[design]
top = "my_counter"           # Your Verilog top module name
sources = ["rtl/my_counter.v"]  # Path(s) to your RTL files

[verilator]
flags = ["--trace"]          # Verilator flags (--trace enables VCD)

[test]
output = "build"             # Build artifact directory
```

## Step 4: Build the simulation library

```bash
dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- build
```

This runs Verilator on your RTL, compiles the generic C shim, and links everything into `build/libverifrog_sim.dylib` (macOS) or `.so` (Linux).

## Step 5: Write your first test

Edit `tests/Tests.fs`:

```fsharp
module Tests

open Expecto
open Verifrog.Sim
open Verifrog.Runner

[<Tests>]
let tests = testList "my_counter" [
    test "counts when enabled" {
        use sim = SimFixture.create ()
        sim.Write("enable", 1L) |> ignore
        sim.Step(10)
        Expect.signal sim "count" 10L "count should be 10"
    }

    test "checkpoint and restore" {
        use sim = SimFixture.create ()
        sim.Write("enable", 1L) |> ignore
        sim.Step(5)
        let cp = sim.SaveCheckpoint("mid", "count=5")
        sim.Step(5)
        Expect.signal sim "count" 10L "count reaches 10"
        sim.RestoreCheckpoint("mid")
        Expect.signal sim "count" 5L "restored to 5"
    }
]
```

Update `tests/Tests.fsproj` to reference Verifrog:

```xml
<ProjectReference Include="$(VERIFROG_ROOT)/src/Verifrog.Sim/Verifrog.Sim.fsproj" />
<ProjectReference Include="$(VERIFROG_ROOT)/src/Verifrog.Runner/Verifrog.Runner.fsproj" />
```

## Step 6: Run tests

```bash
DYLD_LIBRARY_PATH=build dotnet test tests/   # macOS
LD_LIBRARY_PATH=build dotnet test tests/      # Linux
```

## Adding memory and register access

If your design has SRAM or a register file, declare them in `verifrog.toml`:

```toml
[memories.data_ram]
path = "u_ram.mem"       # Hierarchical path to the memory array
banks = 1
depth = 256
width = 8

[registers]
path = "u_regfile.regs"  # Path to register file array
width = 8

[registers.map]
CTRL   = 0x00
STATUS = 0x01
DATA   = 0x02
```

Then use named access in tests:

```fsharp
test "register write-read" {
    use sim = SimFixture.createFromToml "verifrog.toml"
    sim.Register("CTRL").Write(0x42L)
    sim.Step(1)
    let v = sim.Register("CTRL").Read()
    // ...
}
```

## Adding iverilog testbenches

For timing-accurate tests with Verilog testbenches:

```toml
[iverilog]
testbenches = ["sim/*_tb.v"]
models = ["sim/bfm_*.v"]
```

```fsharp
test "shift register TB" {
    let result = Iverilog.runSimple projectRoot config "shift_reg_tb"
    Expect.iverilogPassed result "shift register should pass"
}
```

## Next steps

- [API Reference](api-reference.md) — full Sim, Memory, Register, Expect API
- [Configuration Reference](config-reference.md) — all verifrog.toml sections
- [Extension Guide](extension-guide.md) — building design-specific layers
- [Samples](../samples/) — working examples
