# I2C BFM Sample

Demonstrates protocol-level testbench integration with the iverilog backend using a reusable I2C Bus Functional Model.

## What it demonstrates

- I2C Master BFM with Verilog tasks: `i2c_reg_write`, `i2c_reg_read`, `i2c_burst_write`, `i2c_burst_read`
- BFM auto-detection via TOML `[iverilog].models`
- Open-drain SDA modeling (wired-AND)
- Timing-accurate I2C protocol verification (configurable speed via `I2C_HALF_PERIOD`)
- Combined Verilator (backdoor) + iverilog (protocol) testing

## BFM Tasks

The `i2c_master_bfm.v` provides:

| Task | Description |
|---|---|
| `i2c_start()` | Generate START condition |
| `i2c_stop()` | Generate STOP condition |
| `i2c_write_byte(data, ack)` | Send 8 bits MSB-first, receive ACK |
| `i2c_read_byte(data, send_ack)` | Receive 8 bits, send ACK/NACK |
| `i2c_reg_write(addr, reg, data, ack)` | Full register write transaction |
| `i2c_reg_read(addr, reg, data, ack)` | Full register read (repeated start) |
| `i2c_burst_write(addr, reg, data[], len, ack)` | Multi-byte write |
| `i2c_burst_read(addr, reg, data[], len, ack)` | Multi-byte read |

## Running

```bash
# Run the BFM test
iverilog -o build/i2c_regs_tb.vvp sim/i2c_master_bfm.v sim/i2c_regs_tb.v
vvp build/i2c_regs_tb.vvp

# To use with your own I2C peripheral, add it to the compile:
iverilog -o build/tb.vvp sim/i2c_master_bfm.v your_peripheral.v your_tb.v
```
