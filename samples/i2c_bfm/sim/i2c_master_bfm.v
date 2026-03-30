// i2c_master_bfm.v - I2C Master Bus Functional Model
//
// Provides Verilog tasks for driving I2C transactions against the DUT.
// Open-drain SDA modeling: sda_out active-low, release = tri-state (pull-up).
// SCL driven directly (no clock stretching support from DUT).
//
// I2C_HALF_PERIOD sets the SCL half-period in ns. Recommended values:
//   1250 ns — 400 kHz (I2C Fast mode, conservative)
//    500 ns — 1 MHz (I2C Fast-mode Plus, matches FT232H operating speed)
//    200 ns — ~2.5 MHz (CDC-limited maximum for khalkulo RTL)
//
// The read path sets the speed floor: I2C peripheral → CDC handshake (io→core)
// → register file read → CDC handshake (core→io) → drive SDA takes ~12 clk_io
// cycles (240ns). The BFM gives 2×half_period between ACK and first read bit,
// so half_period must be > ~150ns. 200ns provides margin.
//
// Write path has no CDC back-pressure constraint — the BFM doesn't wait for
// write completion, so writes work at any speed.
//
// Tasks:
//   i2c_start()           - Generate START condition
//   i2c_stop()            - Generate STOP condition
//   i2c_write_byte()      - Send 8 bits MSB-first, receive ACK/NACK
//   i2c_read_byte()       - Receive 8 bits MSB-first, send ACK/NACK
//   i2c_reg_write()       - Full single-byte register write transaction
//   i2c_reg_read()        - Full single-byte register read (repeated start)
//   i2c_burst_write()     - Multi-byte sequential write
//   i2c_burst_read()      - Multi-byte sequential read
//
// Verilog-2005
`timescale 1ns / 1ps

module i2c_master_bfm #(
    parameter I2C_HALF_PERIOD = 1250  // Half-period in ns (default: 400 kHz -> 2500 ns period)
) (
    output reg  scl,
    output reg  sda_out,    // 0 = pull SDA low, 1 = release (pull-up)
    input  wire sda_in      // Actual SDA line state (wired-AND of all drivers)
);

    // =========================================================
    // Internal state
    // =========================================================
    reg [7:0] debug_byte;   // Last byte written/read (for waveform debug)
    reg       debug_ack;    // Last ACK received
    integer   error_count;

    initial begin
        scl     = 1'b1;
        sda_out = 1'b1;
        debug_byte  = 8'h00;
        debug_ack   = 1'b0;
        error_count = 0;
    end

    // =========================================================
    // i2c_start - Generate START condition (SDA falls while SCL high)
    // =========================================================
    task i2c_start;
    begin
        // Ensure SDA is high before START
        sda_out = 1'b1;
        #(I2C_HALF_PERIOD);
        scl = 1'b1;
        #(I2C_HALF_PERIOD);
        // START: SDA goes low while SCL is high
        sda_out = 1'b0;
        #(I2C_HALF_PERIOD);
        // Pull SCL low to begin first bit
        scl = 1'b0;
        #(I2C_HALF_PERIOD);
    end
    endtask

    // =========================================================
    // i2c_stop - Generate STOP condition (SDA rises while SCL high)
    // =========================================================
    task i2c_stop;
    begin
        // Ensure SDA is low before STOP
        sda_out = 1'b0;
        #(I2C_HALF_PERIOD);
        // Raise SCL first
        scl = 1'b1;
        #(I2C_HALF_PERIOD);
        // STOP: SDA goes high while SCL is high
        sda_out = 1'b1;
        #(I2C_HALF_PERIOD * 2);
    end
    endtask

    // =========================================================
    // i2c_write_byte - Send 8 bits MSB-first, receive ACK/NACK
    //   data: byte to send
    //   ack:  output, 0 = ACK received, 1 = NACK
    // =========================================================
    task i2c_write_byte;
        input  [7:0] data;
        output       ack;
        integer i;
    begin
        debug_byte = data;
        // Send 8 data bits, MSB first
        for (i = 7; i >= 0; i = i - 1) begin
            // Drive SDA while SCL is low
            sda_out = data[i];
            #(I2C_HALF_PERIOD);
            // SCL rising edge - slave samples SDA
            scl = 1'b1;
            #(I2C_HALF_PERIOD);
            // SCL falling edge
            scl = 1'b0;
        end
        // ACK bit: release SDA, let slave drive
        sda_out = 1'b1;  // Release SDA (pull-up)
        #(I2C_HALF_PERIOD);
        // SCL rising edge - sample ACK from slave
        scl = 1'b1;
        #(I2C_HALF_PERIOD / 2);
        ack = sda_in;     // 0 = ACK, 1 = NACK
        debug_ack = sda_in;
        #(I2C_HALF_PERIOD / 2);
        // SCL falling edge
        scl = 1'b0;
        #(I2C_HALF_PERIOD);
    end
    endtask

    // =========================================================
    // i2c_read_byte - Receive 8 bits MSB-first, send ACK/NACK
    //   data:     output, received byte
    //   send_ack: input, 1 = send ACK (continue), 0 = send NACK (last byte)
    // =========================================================
    task i2c_read_byte;
        output [7:0] data;
        input        send_ack;
        integer i;
        reg [7:0] rx_data;
    begin
        // Release SDA so slave can drive
        sda_out = 1'b1;
        rx_data = 8'd0;
        // Receive 8 data bits, MSB first
        for (i = 7; i >= 0; i = i - 1) begin
            #(I2C_HALF_PERIOD);
            // SCL rising edge - sample SDA from slave
            scl = 1'b1;
            #(I2C_HALF_PERIOD / 2);
            rx_data[i] = sda_in;
            #(I2C_HALF_PERIOD / 2);
            // SCL falling edge
            scl = 1'b0;
        end
        data = rx_data;
        debug_byte = rx_data;
        // ACK/NACK bit: master drives SDA
        if (send_ack)
            sda_out = 1'b0;  // ACK = pull SDA low
        else
            sda_out = 1'b1;  // NACK = release SDA
        #(I2C_HALF_PERIOD);
        // SCL rising edge - slave samples ACK
        scl = 1'b1;
        #(I2C_HALF_PERIOD);
        // SCL falling edge
        scl = 1'b0;
        // Release SDA after ACK cycle
        sda_out = 1'b1;
        #(I2C_HALF_PERIOD);
    end
    endtask

    // =========================================================
    // i2c_reg_write - Full register write transaction
    //   chip_addr: 7-bit I2C device address
    //   reg_addr:  8-bit register address
    //   data:      8-bit data to write
    // =========================================================
    task i2c_reg_write;
        input [6:0] chip_addr;
        input [7:0] reg_addr;
        input [7:0] data;
        reg   ack;
    begin
        i2c_start;
        // Address + Write bit (0)
        i2c_write_byte({chip_addr, 1'b0}, ack);
        if (ack) begin
            $display("ERROR: [I2C BFM] NACK on address 0x%02h (write)", chip_addr);
            error_count = error_count + 1;
            i2c_stop;
            disable i2c_reg_write;
        end
        // Register address
        i2c_write_byte(reg_addr, ack);
        if (ack) begin
            $display("ERROR: [I2C BFM] NACK on reg addr 0x%02h", reg_addr);
            error_count = error_count + 1;
            i2c_stop;
            disable i2c_reg_write;
        end
        // Data byte
        i2c_write_byte(data, ack);
        if (ack) begin
            $display("ERROR: [I2C BFM] NACK on data write 0x%02h", data);
            error_count = error_count + 1;
        end
        i2c_stop;
    end
    endtask

    // =========================================================
    // i2c_reg_read - Full register read with repeated start
    //   chip_addr: 7-bit I2C device address
    //   reg_addr:  8-bit register address
    //   data:      output 8-bit read data
    // =========================================================
    task i2c_reg_read;
        input  [6:0] chip_addr;
        input  [7:0] reg_addr;
        output [7:0] data;
        reg    ack;
    begin
        // Write phase: send register address
        i2c_start;
        i2c_write_byte({chip_addr, 1'b0}, ack);
        if (ack) begin
            $display("ERROR: [I2C BFM] NACK on address 0x%02h (read-write phase)", chip_addr);
            error_count = error_count + 1;
            i2c_stop;
            data = 8'hFF;
            disable i2c_reg_read;
        end
        i2c_write_byte(reg_addr, ack);
        if (ack) begin
            $display("ERROR: [I2C BFM] NACK on reg addr 0x%02h (read)", reg_addr);
            error_count = error_count + 1;
            i2c_stop;
            data = 8'hFF;
            disable i2c_reg_read;
        end
        // Repeated START
        i2c_start;
        // Address + Read bit (1)
        i2c_write_byte({chip_addr, 1'b1}, ack);
        if (ack) begin
            $display("ERROR: [I2C BFM] NACK on address 0x%02h (read phase)", chip_addr);
            error_count = error_count + 1;
            i2c_stop;
            data = 8'hFF;
            disable i2c_reg_read;
        end
        // Read data byte with NACK (single byte read)
        i2c_read_byte(data, 1'b0);
        i2c_stop;
    end
    endtask

    // =========================================================
    // i2c_burst_write - Multi-byte sequential write
    //   chip_addr:  7-bit I2C device address
    //   start_addr: 8-bit starting register address
    //   data_array: array of bytes to write (caller must define)
    //   length:     number of bytes
    //
    // Uses I2C auto-increment: sends start_addr once, then streams
    // data bytes. The DUT's register file auto-increments the address.
    // =========================================================
    task i2c_burst_write;
        input  [6:0]  chip_addr;
        input  [7:0]  start_addr;
        input  [7:0]  data_array;   // Note: caller passes byte-at-a-time via index
        input  integer length;
        reg    ack;
        integer i;
    begin
        i2c_start;
        // Address + Write
        i2c_write_byte({chip_addr, 1'b0}, ack);
        if (ack) begin
            $display("ERROR: [I2C BFM] NACK on address 0x%02h (burst write)", chip_addr);
            error_count = error_count + 1;
            i2c_stop;
            disable i2c_burst_write;
        end
        // Register address (auto-increments)
        i2c_write_byte(start_addr, ack);
        if (ack) begin
            $display("ERROR: [I2C BFM] NACK on reg addr 0x%02h (burst write)", start_addr);
            error_count = error_count + 1;
            i2c_stop;
            disable i2c_burst_write;
        end
        // This task is a template - actual burst writes use the dedicated
        // burst_write_data task below, which takes a memory array.
        i2c_stop;
    end
    endtask

    // =========================================================
    // burst_write_data - Write multiple bytes from a memory array
    //   Starts a transaction, sends reg_addr, then streams bytes.
    //   The I2C peripheral auto-increments the register address.
    //
    // Usage from testbench:
    //   reg [7:0] mem [0:1023];
    //   // ... fill mem[] ...
    //   bfm.burst_write_data(7'h15, 8'h67, mem, 0, 256);
    // =========================================================
    task burst_write_data;
        input [6:0]   chip_addr;
        input [7:0]   start_addr;
        // Memory array and range passed via indices; caller references
        // shared memory in the testbench. This task writes byte-by-byte.
        input integer mem_start;
        input integer mem_count;
        reg   ack;
        integer i;
    begin
        // Note: This is a stub. The actual testbench uses a wrapper
        // that calls i2c_start, sends address bytes, then loops
        // calling i2c_write_byte for each data byte from its local memory.
        // See khalkulo_system_tb.v load_weights() task for the real implementation.
        $display("INFO: [I2C BFM] burst_write_data called. Use testbench wrapper tasks instead.");
    end
    endtask

    // =========================================================
    // i2c_burst_read - Multi-byte sequential read
    //   chip_addr:  7-bit I2C device address
    //   start_addr: 8-bit starting register address
    //   data_array: output array (caller must index)
    //   length:     number of bytes to read
    //
    // Similar structure: this is a template/stub. Real burst reads
    // are implemented as loops in the testbench using i2c_read_byte.
    // =========================================================
    task i2c_burst_read;
        input  [6:0]  chip_addr;
        input  [7:0]  start_addr;
        output [7:0]  data_array;
        input  integer length;
        reg    ack;
    begin
        // Stub - see testbench wrapper tasks for actual implementation
        $display("INFO: [I2C BFM] burst_read called. Use testbench wrapper tasks instead.");
    end
    endtask

endmodule
