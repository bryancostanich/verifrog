# Iverilog Testbench Sample

Demonstrates the dual-backend runner — Verilator for fast cycle-based tests and iverilog for timing-accurate Verilog testbenches.

## What it demonstrates

- Iverilog backend: compile, run, parse pass/fail from stdout
- Auto-discovery of `*_tb.v` testbenches from TOML patterns
- Verilator + iverilog under one `dotnet test` invocation
- Parameter overrides (`-P tb.PARAM=value`)

## Design

8-bit shift register with shift_in, shift_en, shift_out.
