# Verifrog

An open-source Verilog testing framework in F#. Drive your hardware designs through Verilator and Icarus Verilog with structured access to signals, memories, and registers — all from `dotnet test`.

## Quick Start

### Prerequisites

- [.NET 8+ SDK](https://dotnet.microsoft.com/download)
- [Verilator](https://verilator.org/guide/latest/install.html) (5.x+)
- [Icarus Verilog](http://iverilog.icarus.com/) (optional, for timing-accurate tests)
- clang++ (macOS) or g++ (Linux)

### Install and run

```bash
# Clone
git clone https://github.com/your-org/verifrog.git
cd verifrog

# Initialize a new project
export VERIFROG_ROOT=$PWD
dotnet run --project src/Verifrog.Cli -- init my_project
cd my_project

# Edit verifrog.toml with your design info, then:
verifrog build
dotnet test tests/
```

### Or use the counter sample directly

```bash
export VERIFROG_ROOT=$PWD
dotnet run --project src/Verifrog.Cli -- build samples/counter
DYLD_LIBRARY_PATH=samples/counter/build dotnet test tests/Verifrog.Tests
```

## What it looks like

```fsharp
open Verifrog.Sim
open Verifrog.Runner

let tests = testList "ALU" [
    test "add produces correct result" {
        use sim = SimFixture.create ()
        sim.Write("alu_op", 0L) |> ignore    // ADD
        sim.Write("rd_addr_a", 0L) |> ignore  // R0
        sim.Write("rd_addr_b", 1L) |> ignore  // R1
        sim.Write("alu_start", 1L) |> ignore
        sim.Step(2)
        Expect.signal sim "alu_result" 59L "42 + 17 = 59"
    }
]
```

## Architecture

```
User's Test Project (Expecto)
  |
  v
Verifrog.Runner   — SimFixture, Iverilog backend, Expect helpers
  |
  v
Verifrog.Sim      — Sim type, Memory/Register access, TOML config
  |
  v
libverifrog_sim   — Generic Verilator C wrapper (built per-design)
  |
  v
Verilator         — User's compiled RTL
```

**Verifrog.Vcd** is a standalone VCD parser library, usable independently.

## Components

| Library | Purpose |
|---|---|
| **Verifrog.Sim** | Core simulation API: Create, Reset, Step, Read/Write, Checkpoint, Force, Memory, Register |
| **Verifrog.Vcd** | VCD waveform parser: parse, query signals, value-at-time, transitions |
| **Verifrog.Runner** | Test infrastructure: SimFixture, Iverilog backend, Expect helpers |
| **verifrog CLI** | Build tool: `verifrog init`, `verifrog build`, `verifrog clean` |
| **libverifrog_sim** | C shim: generic Verilator wrapper compiled per-design |

## Configuration

All configuration lives in `verifrog.toml`. See [docs/config-reference.md](docs/config-reference.md).

```toml
[design]
top = "my_module"
sources = ["src/rtl/*.v"]

[test]
output = "build"
test_output = "test_output"  # VCD traces, logs (default: same as output)

[memories.data_ram]
path = "u_ram.mem"
banks = 1
depth = 1024
width = 32

[registers]
path = "u_regfile.mem"
width = 8

[registers.map]
CTRL = 0x00
STATUS = 0x01
```

## Samples

| Sample | What it shows |
|---|---|
| [counter](samples/counter/) | Minimal: step, read, write, checkpoint |
| [alu_regfile](samples/alu_regfile/) | Register map in TOML, named access |
| [sram](samples/sram/) | Memory regions, bank/addr access |
| [iverilog_tb](samples/iverilog_tb/) | Dual-backend, Verilog testbench |

## Extension Model

Verifrog provides generic access. Build design-specific APIs in your own repo:

```fsharp
type MySocSim(sim: Sim) =
    member _.StartDma(src, dst, len) =
        sim.Register("DMA_SRC").Write(src)
        sim.Register("DMA_DST").Write(dst)
        sim.Register("DMA_LEN").Write(len)
        sim.Register("DMA_CTRL").Write(0x01)
```

See [docs/extension-guide.md](docs/extension-guide.md).

## Documentation

- [Getting Started](docs/getting-started.md)
- [API Reference](docs/api-reference.md)
- [Configuration Reference](docs/config-reference.md)
- [Extension Guide](docs/extension-guide.md)
- [Architecture](docs/architecture.md)
- [Architecture Decisions](docs/ARCHITECTURE_DECISIONS.md)

## License

Apache 2.0
