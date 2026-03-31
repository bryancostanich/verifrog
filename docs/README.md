# Verifrog Documentation

## Where to start

**New to Verifrog?** Start here:

1. **[Getting Started](getting-started.md)** — Install prerequisites, build your first design, write and run your first test. Takes about 15 minutes.

2. **[Core Concepts](concepts.md)** — Understand how Verifrog works: signals, simulation control, checkpoints, forces, memories, registers, and what-if exploration.

3. **[Cookbook](cookbook.md)** — Copy-paste recipes for common patterns: testing a state machine, sweeping parameters, debugging with VCD, and more.

## Reference

- **[API Reference](api-reference.md)** — Every method on `Sim`, `MemoryAccessor`, `RegisterAccessor`, `SimFixture`, `Expect`, `Iverilog`, and `VcdParser`, with code examples.
- **[Configuration Reference](config-reference.md)** — All `verifrog.toml` sections and keys.
- **[CLI Reference](cli-reference.md)** — `verifrog init`, `build`, and `clean` commands.
- **[VCD CLI Reference](vcd-cli.md)** — The `verifrog-vcd` command-line analysis tool.

## Guides

- **[VCD Parser Guide](vcd-guide.md)** — Using the `Verifrog.Vcd` library in your tests to parse and analyze waveform dumps.
- **[Extension Guide](extension-guide.md)** — Building design-specific convenience APIs (like `MySocSim`) on top of Verifrog.
- **[Troubleshooting](troubleshooting.md)** — Common errors and how to fix them.

## Internals

- **[Architecture](architecture.md)** — Layer diagram, data flow, signal resolution, checkpoint implementation.
- **[Architecture Decisions](ARCHITECTURE_DECISIONS.md)** — Why we chose direct-pointer access over VPI, memcpy checkpoints, build-time model binding, and `src/` layout.

## Samples

Each sample is a self-contained project you can build and run:

| Sample | Focus |
|---|---|
| [counter](../samples/counter/) | Minimal starting point — covers the essential API |
| [alu_regfile](../samples/alu_regfile/) | TOML-driven register access and parametric sweeps |
| [sram](../samples/sram/) | Memory regions with banked access |
| [iverilog_tb](../samples/iverilog_tb/) | Dual-backend testing (Verilator + Icarus Verilog) |
| [i2c_bfm](../samples/i2c_bfm/) | Protocol-level BFM integration |
