# Getting Started

This guide walks you through setting up Verifrog, building a Verilog design, writing tests, and running them. By the end you'll have a working test that reads and writes signals, uses checkpoints, and verifies behavior.

## Prerequisites

Install these before starting:

| Tool | Install (macOS) | Install (Linux) | Notes |
|------|-----------------|-----------------|-------|
| .NET 8+ SDK | `brew install dotnet` | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) | Runtime + compiler |
| Verilator 5+ | `brew install verilator` | `apt install verilator` | Cycle-based simulator |
| clang++ | Included with Xcode | `apt install clang` | Or g++ on Linux |
| Icarus Verilog | `brew install icarus-verilog` | `apt install iverilog` | Optional, for timing-accurate tests |

Verify your installations:

```bash
dotnet --version    # 8.0.x or higher
verilator --version # Verilator 5.x or higher
```

## Step 1: Clone and install

```bash
git clone https://github.com/bryancostanich/verifrog.git
cd verifrog
./install.sh
```

This symlinks the `verifrog` and `verifrog-vcd` commands to `/usr/local/bin`. Alternatively, add `bin/` to your PATH: `export PATH="/path/to/verifrog/bin:$PATH"`.

> **Without install.sh**: You can skip the install and use the long form instead:
> `dotnet run --project /path/to/verifrog/src/Verifrog.Cli -- <command>`.
> The scripts just wrap this and handle library paths automatically.

## Step 2: Try the counter sample

Before setting up your own project, make sure everything works with the included counter sample.

### Build and test

```bash
verifrog build samples/counter
verifrog test samples/counter
```

The `build` command runs Verilator on the counter RTL, compiles the generic C++ shim, and links everything into a shared library. You should see output like:

```
Building samples/counter/verifrog.toml (top=counter)
  Verilating counter...
  Built: samples/counter/build/libverifrog_sim.dylib
```

The `test` command automatically sets the library path and runs the tests:

```
Running tests: Verifrog.Tests.fsproj
  Library: samples/counter/build/libverifrog_sim.dylib

EXPECTO! 30 tests run in 00:00:00.15 — 30 passed, 0 failed. Success!
```

> **Note**: `verifrog test` will auto-build if the library doesn't exist yet, so you can often just run `verifrog test` directly.

## Step 3: Initialize your own project

Now set up Verifrog for your own Verilog design.

```bash
cd /path/to/your-project
verifrog init .
```

This creates:

```
your-project/
  verifrog.toml        # Design configuration (edit this)
  tests/
    Tests.fs           # Sample test file
    Tests.fsproj       # F# project referencing Verifrog
```

## Step 4: Configure your design

Edit `verifrog.toml` to point at your RTL:

```toml
[design]
top = "my_counter"                 # Your top-level Verilog module name
sources = ["rtl/my_counter.v"]     # Path(s) to your RTL source files

[verilator]
flags = ["--trace"]                # Enable VCD waveform tracing

[test]
output = "build"                   # Where build artifacts go
```

The `top` field must exactly match your Verilog `module` declaration. The `sources` field supports glob patterns like `"rtl/**/*.v"`.

## Step 5: Build the simulation library

```bash
verifrog build
```

This:
1. Reads `verifrog.toml` to find your top module and sources
2. Runs Verilator with `--cc --public-flat-rw --trace` to compile your RTL to C++
3. Generates a `verifrog_model.h` header that binds the generic shim to your design
4. Compiles everything into `build/libverifrog_sim.dylib` (macOS) or `.so` (Linux)

If the build fails, check:
- Your Verilog has no syntax errors (`verilator --lint-only your_file.v`)
- The `top` in `verifrog.toml` matches your module name exactly
- All source files exist at the paths listed in `sources`

## Step 6: Write your first test

Edit `tests/Tests.fs`:

```fsharp
module Tests

open Expecto
open Verifrog.Sim
open Verifrog.Runner

[<Tests>]
let tests = testList "my_counter" [

    test "starts at zero after reset" {
        use sim = SimFixture.create ()
        // SimFixture.create() loads the library, resets for 10 cycles,
        // and suppresses $display output. sim is IDisposable.
        Expect.signal sim "count" 0L "count should be 0 after reset"
    }

    test "counts when enabled" {
        use sim = SimFixture.create ()
        sim.Write("enable", 1L) |> ignore
        sim.Step(10)
        Expect.signal sim "count" 10L "should have counted to 10"
    }

    test "checkpoint saves and restores state" {
        use sim = SimFixture.create ()
        sim.Write("enable", 1L) |> ignore
        sim.Step(5)
        Expect.signal sim "count" 5L "count is 5"

        // Save state
        let cp = sim.SaveCheckpoint("halfway")

        // Continue running
        sim.Step(5)
        Expect.signal sim "count" 10L "count reaches 10"

        // Restore to saved state
        sim.RestoreCheckpoint("halfway")
        Expect.signal sim "count" 5L "back to 5 after restore"
    }
]
```

Make sure `tests/Tests.fsproj` references Verifrog:

```xml
<ItemGroup>
  <ProjectReference Include="$(VERIFROG_ROOT)/src/Verifrog.Sim/Verifrog.Sim.fsproj" />
  <ProjectReference Include="$(VERIFROG_ROOT)/src/Verifrog.Runner/Verifrog.Runner.fsproj" />
</ItemGroup>
```

## Step 7: Run your tests

```bash
verifrog test
```

This auto-detects your `verifrog.toml`, sets the library path, and runs the tests. If the library hasn't been built yet, it builds automatically.

> **Without the script**: `DYLD_LIBRARY_PATH=build dotnet run --project tests/` (macOS) or `LD_LIBRARY_PATH=build dotnet run --project tests/` (Linux).

## Adding memory access

If your design has SRAM, declare it in `verifrog.toml`:

```toml
[memories.data_ram]
path = "u_ram.mem"         # Hierarchical path to the Verilog memory array
banks = 1                  # Number of banks (use {bank} placeholder if > 1)
depth = 256                # Words per bank
width = 8                  # Bits per word
```

Then use named access in tests:

```fsharp
test "backdoor memory write and read" {
    use sim = SimFixture.createFromToml "verifrog.toml"
    sim.Memory("data_ram").Write(0, 42, 0xDEL)   // bank 0, addr 42, value 0xDE
    sim.Step(1)
    Expect.memory sim "data_ram" 0 42 0xDEL "backdoor write should stick"
}
```

## Adding register access

For register files, declare the register map:

```toml
[registers]
path = "u_regfile.regs"   # Path to the register array
width = 8                  # Bits per register

[registers.map]
CTRL   = 0x00
STATUS = 0x01
DATA   = 0x02
```

Then use named registers:

```fsharp
test "register write-read" {
    use sim = SimFixture.createFromToml "verifrog.toml"
    sim.Register("CTRL").Write(0x42L) |> ignore
    sim.Step(1)
    Expect.register sim "CTRL" 0x42L "CTRL should hold written value"
}
```

## Adding iverilog testbenches

For timing-accurate Verilog testbenches alongside Verilator tests:

```toml
[iverilog]
testbenches = ["sim/*_tb.v"]       # Glob patterns for testbench files
models = ["sim/bfm_*.v"]           # Supporting models (BFMs, SRAM models)
```

```fsharp
test "shift register timing" {
    let result = Iverilog.runSimple projectRoot config "shift_reg_tb"
    Expect.iverilogPassed result "shift register should pass"
}
```

Both Verilator and iverilog tests run under a single `dotnet test` invocation.

## Next steps

- **[Core Concepts](concepts.md)** — Understand signals, checkpoints, forces, what-if exploration
- **[API Reference](api-reference.md)** — Full API with code examples
- **[VCD Parser Guide](vcd-guide.md)** — Analyze waveform dumps in your tests
- **[Cookbook](cookbook.md)** — Recipes for common test patterns
- **[Configuration Reference](config-reference.md)** — All `verifrog.toml` options
- **[Samples](../samples/)** — Working examples to study and modify
