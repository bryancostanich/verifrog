# I2C BFM Sample

Demonstrates protocol-level testbench integration with the iverilog backend using a reusable I2C Bus Functional Model (BFM).

## What it demonstrates

- I2C Master BFM with Verilog tasks for register read/write and burst operations
- BFM auto-detection via TOML `[iverilog].models`
- Open-drain SDA modeling (wired-AND bus)
- Timing-accurate I2C protocol verification with configurable speed (`I2C_HALF_PERIOD`)
- Combined Verilator (backdoor) + iverilog (protocol) testing of the same design

## Prerequisites

```bash
brew install icarus-verilog   # macOS
apt install iverilog           # Linux
```

## Building and running

```bash
verifrog build samples/i2c_bfm
verifrog test samples/i2c_bfm
```

You can also run the iverilog testbench standalone:

```bash
cd samples/i2c_bfm
iverilog -o build/i2c_regs_tb.vvp sim/i2c_master_bfm.v sim/i2c_regs_tb.v rtl/*.v
vvp build/i2c_regs_tb.vvp
```

## BFM tasks

The `i2c_master_bfm.v` provides these Verilog tasks:

| Task | Description |
|---|---|
| `i2c_start()` | Generate START condition (SDA falls while SCL high) |
| `i2c_stop()` | Generate STOP condition (SDA rises while SCL high) |
| `i2c_write_byte(data, ack)` | Send 8 bits MSB-first, receive ACK/NACK |
| `i2c_read_byte(data, send_ack)` | Receive 8 bits, send ACK or NACK |
| `i2c_reg_write(addr, reg, data, ack)` | Full write transaction: START, addr+W, reg, data, STOP |
| `i2c_reg_read(addr, reg, data, ack)` | Full read transaction with repeated start |
| `i2c_burst_write(addr, reg, data[], len, ack)` | Multi-byte write |
| `i2c_burst_read(addr, reg, data[], len, ack)` | Multi-byte read |

## Configuration

```toml
[iverilog]
testbenches = ["sim/*_tb.v"]
models = ["sim/i2c_master_bfm.v"]    # BFM auto-included in all compiles
```

## Using with your own I2C peripheral

To reuse this BFM with your own design:

1. Copy `sim/i2c_master_bfm.v` to your project
2. Add it to your `[iverilog].models` in `verifrog.toml`
3. In your testbench, instantiate and call the tasks:

```verilog
// Testbench
`include "i2c_master_bfm.v"

initial begin
    reg [7:0] read_data;
    reg ack;

    // Write 0x42 to register 0x10 at slave address 0x50
    i2c_reg_write(8'h50, 8'h10, 8'h42, ack);

    // Read it back
    i2c_reg_read(8'h50, 8'h10, read_data, ack);

    if (read_data == 8'h42) $display("PASSED");
    else $display("FAILED: got %h", read_data);
end
```

## What to look at

- `sim/i2c_master_bfm.v` — The reusable BFM with all I2C tasks
- `sim/i2c_regs_tb.v` — Example testbench using the BFM
- `verifrog.toml` — How `[iverilog].models` auto-includes the BFM
- The test file shows protocol tests (iverilog) alongside backdoor tests (Verilator)
