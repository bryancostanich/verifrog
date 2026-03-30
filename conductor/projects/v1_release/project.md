# Verifrog v1 Release

First public release of the Verifrog Verilog testing framework.

## Goal

Extract and generalize khalkulo's simulation tooling into a standalone, open-source framework that works with any Verilog design. Ship with documentation, sample projects, and a clean API that makes hardware verification in F# accessible.

## Tracks

| Track | Description |
|-------|-------------|
| 01_core | Core framework: Sim, VCD, Runner, CLI, samples, docs, khalkulo migration |

## Key Decisions

- **F# + Expecto** as the test language and framework
- **TOML** for project configuration (`verifrog.toml`)
- **Two simulator backends**: Verilator (fast, cycle-based) + iverilog (timing-accurate, event-driven)
- **Extension model**: Verifrog provides generic access; users build design-specific APIs in their own repos
- **No signal aliasing**: use hierarchical names directly, validate at startup
- **Structured access**: memory regions and register maps declared in TOML
