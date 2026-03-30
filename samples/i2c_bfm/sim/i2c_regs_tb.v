// i2c_bfm_tb.v — Testbench demonstrating the I2C Master BFM
//
// Tests the BFM's protocol operations. Since there's no slave to ACK,
// we verify the BFM's error handling (NACK detection) and waveform correctness.
`timescale 1ns / 1ps

module i2c_regs_tb;

    parameter I2C_HALF_PERIOD = 500;  // 1 MHz
    parameter TIMEOUT = 100_000;

    wire scl;
    wire sda_out;
    wire sda_in;

    // No slave — SDA is just the BFM's output (pull-up = always 1 when released)
    assign sda_in = sda_out;

    // Timeout
    initial begin
        #TIMEOUT;
        $display("TIMEOUT");
        $finish;
    end

    // BFM instance
    i2c_master_bfm #(.I2C_HALF_PERIOD(I2C_HALF_PERIOD)) bfm (
        .scl(scl),
        .sda_out(sda_out),
        .sda_in(sda_in)
    );

    integer pass_count = 0;
    integer fail_count = 0;

    task check;
        input integer actual;
        input integer expected;
        input [255:0] msg;
        begin
            if (actual === expected) begin
                $display("PASS: %0s", msg);
                pass_count = pass_count + 1;
            end else begin
                $display("FAIL: %0s — expected %0d, got %0d", msg, expected, actual);
                fail_count = fail_count + 1;
            end
        end
    endtask

    reg ack;

    initial begin
        // ---- Test 1: START condition ----
        $display("[T1] START condition");
        bfm.i2c_start;
        // After START: SDA low, SCL low
        check(bfm.sda_out, 0, "SDA low after START");
        check(bfm.scl, 0, "SCL low after START");

        // ---- Test 2: STOP condition ----
        $display("[T2] STOP condition");
        bfm.i2c_stop;
        check(bfm.sda_out, 1, "SDA released after STOP");
        check(bfm.scl, 1, "SCL high after STOP");

        // ---- Test 3: Write byte — no slave, so NACK ----
        $display("[T3] Write byte (no slave = NACK)");
        bfm.i2c_start;
        bfm.i2c_write_byte(8'hA0, ack);
        check(ack, 1, "NACK when no slave present");
        check(bfm.debug_byte, 8'hA0, "debug_byte captured 0xA0");
        bfm.i2c_stop;

        // ---- Test 4: reg_write — NACK triggers error_count ----
        $display("[T4] reg_write to non-existent slave");
        bfm.i2c_reg_write(7'h50, 8'h00, 8'h42);
        check(bfm.error_count > 0 ? 1 : 0, 1, "error_count incremented on NACK");

        // ---- Test 5: Multiple operations don't crash ----
        $display("[T5] Sequential operations");
        bfm.i2c_start;
        bfm.i2c_write_byte(8'h55, ack);
        bfm.i2c_stop;
        bfm.i2c_start;
        bfm.i2c_write_byte(8'hAA, ack);
        bfm.i2c_stop;
        check(bfm.debug_byte, 8'hAA, "last debug_byte = 0xAA");
        check(1, 1, "sequential operations completed");

        // ---- Results ----
        $display("");
        if (fail_count == 0)
            $display("ALL TESTS PASSED (%0d passed, %0d failed)", pass_count, fail_count);
        else
            $display("TESTS FAILED (%0d passed, %0d failed)", pass_count, fail_count);
        $finish;
    end

endmodule
