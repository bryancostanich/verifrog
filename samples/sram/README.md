# SRAM Sample

Demonstrates TOML-driven memory region access.

## What it demonstrates

- Memory region declaration in `verifrog.toml`
- Named memory access: `sim.Memory("data").Read(bank, addr)` / `.Write()`
- Backdoor loading (direct memory write via sim)
- Read-after-write verification

## Design

Single-port 256x8 behavioral SRAM with chip select and write enable.
