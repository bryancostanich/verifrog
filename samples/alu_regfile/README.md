# ALU + Register File Sample

Demonstrates TOML-driven register access with a simple ALU.

## What it demonstrates

- Register map in `verifrog.toml`
- Named register access: `sim.Register("R0").Read()` / `.Write()`
- Parametric sweeps across ALU operations
- Fork/Compare for testing multiple operations from same state

## Design

8-entry x 8-bit register file with two read ports and one write port, plus a combinational ALU (ADD, SUB, AND, OR) with 1-cycle latency.
