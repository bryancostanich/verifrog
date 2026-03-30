// shift_reg_tb.v — iverilog testbench for shift register
`timescale 1ns/1ps

module shift_reg_tb;

    parameter WIDTH = 8;
    parameter TIMEOUT = 10000;

    reg clk, rst_n, shift_in, shift_en;
    wire [WIDTH-1:0] data;
    wire shift_out;

    shift_reg #(.WIDTH(WIDTH)) dut (
        .clk(clk),
        .rst_n(rst_n),
        .shift_in(shift_in),
        .shift_en(shift_en),
        .data(data),
        .shift_out(shift_out)
    );

    // Clock generation
    initial clk = 0;
    always #5 clk = ~clk;

    // Timeout
    initial begin
        #TIMEOUT;
        $display("TIMEOUT");
        $finish;
    end

    integer pass_count = 0;
    integer fail_count = 0;

    task check;
        input [WIDTH-1:0] expected;
        input [255:0] msg;
        begin
            if (data !== expected) begin
                $display("FAIL: %0s — expected 0x%02x, got 0x%02x", msg, expected, data);
                fail_count = fail_count + 1;
            end else begin
                $display("PASS: %0s", msg);
                pass_count = pass_count + 1;
            end
        end
    endtask

    initial begin
        // Reset
        rst_n = 0;
        shift_in = 0;
        shift_en = 0;
        #20;
        rst_n = 1;
        #10;

        check(8'h00, "reset clears register");

        // Shift in 0xA5 (10100101) MSB first
        shift_en = 1;

        shift_in = 1; @(posedge clk); #1;
        shift_in = 0; @(posedge clk); #1;
        shift_in = 1; @(posedge clk); #1;
        shift_in = 0; @(posedge clk); #1;
        shift_in = 0; @(posedge clk); #1;
        shift_in = 1; @(posedge clk); #1;
        shift_in = 0; @(posedge clk); #1;
        shift_in = 1; @(posedge clk); #1;

        check(8'hA5, "shifted in 0xA5");

        shift_en = 0;
        @(posedge clk); #1;
        check(8'hA5, "data holds when shift disabled");

        $display("");
        if (fail_count == 0)
            $display("ALL TESTS PASSED (%0d passed, %0d failed)", pass_count, fail_count);
        else
            $display("TESTS FAILED (%0d passed, %0d failed)", pass_count, fail_count);
        $finish;
    end

endmodule
