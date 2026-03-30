// shift_reg.v — Simple shift register
// Verifrog sample: demonstrates iverilog backend with Verilog testbench

module shift_reg #(
    parameter WIDTH = 8
) (
    input  wire             clk,
    input  wire             rst_n,
    input  wire             shift_in,
    input  wire             shift_en,
    output wire [WIDTH-1:0] data,
    output wire             shift_out
);

    reg [WIDTH-1:0] sr;

    assign data = sr;
    assign shift_out = sr[WIDTH-1];

    always @(posedge clk or negedge rst_n) begin
        if (!rst_n)
            sr <= {WIDTH{1'b0}};
        else if (shift_en)
            sr <= {sr[WIDTH-2:0], shift_in};
    end

endmodule
