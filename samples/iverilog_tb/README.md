# Iverilog Testbench Sample

Demonstrates the dual-backend runner — Verilator for fast cycle-based tests and Icarus Verilog for timing-accurate Verilog testbenches, both under one `dotnet test` invocation.

## What it demonstrates

- Iverilog backend: compile, run, and parse pass/fail from stdout
- Auto-discovery of `*_tb.v` testbenches from TOML patterns
- Running Verilator + iverilog tests together in Expecto
- Parameter overrides with `-P tb.PARAM=value`

## Design

```
shift_reg #(.WIDTH(8))
  8-bit shift register
  Inputs:  clk, rst_n, shift_en, shift_in
  Outputs: shift_out
```

## Prerequisites

This sample requires Icarus Verilog in addition to Verilator:

```bash
brew install icarus-verilog   # macOS
apt install iverilog           # Linux
```

## Building and running

```bash
verifrog build samples/iverilog_tb
verifrog test samples/iverilog_tb
```

## Configuration

```toml
[design]
top = "shift_reg"
sources = ["rtl/shift_reg.v"]

[iverilog]
testbenches = ["sim/*_tb.v"]    # Auto-discovered testbenches
models = []                      # No BFMs for this sample
```

## Key patterns shown

### Auto-discover and run testbenches

```fsharp
let tbs = Iverilog.discover root config
// ["shift_reg_tb"]

let result = Iverilog.runSimple root config "shift_reg_tb"
Expect.iverilogPassed result "shift register should pass"
```

### Parameter overrides

```fsharp
let result = Iverilog.run root config "shift_reg_tb"
    [("-P", "shift_reg_tb.WIDTH=16")]   // Override WIDTH parameter
    []
```

### Parse test summary

```fsharp
match Iverilog.parseSummary result.Stdout with
| Some (passed, failed) -> printfn "%d passed, %d failed" passed failed
| None -> printfn "No summary in output"
```

## What to look at

- `verifrog.toml` — `[iverilog]` configuration
- `sim/shift_reg_tb.v` — Verilog testbench with `$display`-based pass/fail
- `rtl/shift_reg.v` — The design
- Test file shows both Verilator F# tests and iverilog testbench execution
