# Configuration Reference

All Verifrog configuration lives in `verifrog.toml` at your project root. This file is read by `verifrog build` and by `Sim.Create(tomlPath)` / `SimFixture.createFromToml(path)` at test time.

## [design] (required)

```toml
[design]
top = "my_module"              # Top-level Verilog module name
sources = ["src/rtl/*.v"]      # Glob patterns for RTL source files
```

- `top`: Must match the Verilog `module` name exactly.
- `sources`: Paths relative to `verifrog.toml`. Globs are expanded by the build system.

## [verilator]

```toml
[verilator]
flags = ["--trace", "-Wno-fatal"]
```

- `flags`: Additional flags passed to `verilator`. `--cc`, `--vpi`, `--public-flat-rw`, `--top-module` are always added by Verifrog.

## [iverilog]

```toml
[iverilog]
testbenches = ["src/sim/*_tb.v"]   # Glob patterns for testbench files
models = ["src/sim/bfm_*.v"]       # Supporting model files (BFMs, memory models)
```

- `testbenches`: Auto-discovered by `Iverilog.discover()`. Each matching file is a testbench.
- `models`: Always compiled alongside RTL and testbench. Use for BFMs, bus models, SRAM models.

## [test]

```toml
[test]
output = "build"              # Directory for build artifacts (default: "build")
test_output = "test_output"   # Directory for test output: VCD traces, logs (default: same as output)
```

| Key | Default | Description |
|-----|---------|-------------|
| `output` | `"build"` | Build artifacts (`.vvp`, Verilator intermediates, `libverifrog_sim`) |
| `test_output` | same as `output` | Test runtime output (VCD waveforms from `$dumpfile`, simulation logs). Iverilog simulations run with this as their working directory. |

The `verifrog build` command places `libverifrog_sim.dylib/.so` and Verilator intermediates in `output`. The `test_output` directory is created automatically when tests run.

## [memories.*]

Declare memory regions for structured access. Each memory is a named sub-table.

```toml
[memories.weight_sram]
path = "u_wgt_sram.mem"    # Hierarchical path to the memory array
banks = 16                  # Number of banks (default: 1)
depth = 512                 # Words per bank
width = 32                  # Bits per word

[memories.act_sram]
path = "u_act_sram.bank{bank}.mem"   # {bank} is replaced with 0..banks-1
banks = 8
depth = 512
width = 64
```

- `path`: Hierarchical signal path to the Verilog memory array. Use `{bank}` placeholder for banked memories.
- Access in F#: `sim.Memory("weight_sram").Read(bank, addr)`

## [registers]

Declare a register file with named registers.

```toml
[registers]
path = "u_regfile.regs"    # Hierarchical path to the register array
width = 8                   # Bits per register

[registers.map]
CTRL      = 0x00
STATUS    = 0x01
DATA_LO   = 0x02
DATA_HI   = 0x03
CONFIG    = 0x10
IRQ_MASK  = 0x20
```

- `path`: Points to the Verilog `reg [width-1:0] regs [0:N]` array.
- `map`: Maps friendly names to addresses. Access in F#: `sim.Register("CTRL").Read()`

## Full example

```toml
[design]
top = "my_soc"
sources = ["rtl/*.v", "rtl/submodules/*.v"]

[verilator]
flags = ["--trace"]

[iverilog]
testbenches = ["sim/*_tb.v"]
models = ["sim/i2c_master_bfm.v", "sim/sram_model.v"]

[test]
output = "scratch"

[memories.data_ram]
path = "u_soc.u_ram.mem"
banks = 1
depth = 1024
width = 32

[memories.weight_sram]
path = "u_soc.u_mac.u_wgt.bank{bank}.mem"
banks = 16
depth = 512
width = 32

[registers]
path = "u_soc.u_regfile.regs"
width = 8

[registers.map]
CTRL      = 0x00
STATUS    = 0x01
MODE      = 0x02
NUM_LAYERS = 0x03
LAYER_TYPE = 0x10
IN_CHANNELS = 0x11
OUT_CHANNELS = 0x12
```
