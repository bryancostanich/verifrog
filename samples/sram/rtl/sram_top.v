// sram_top.v — Simple single-port SRAM wrapper
// Verifrog sample: demonstrates TOML-driven memory access

module sram_top #(
    parameter DEPTH = 256,
    parameter WIDTH = 8,
    parameter ADDR_W = 8
) (
    input  wire              clk,
    input  wire              rst_n,
    input  wire [ADDR_W-1:0] addr,
    input  wire [WIDTH-1:0]  wdata,
    input  wire              we,
    input  wire              cs,
    output reg  [WIDTH-1:0]  rdata
);

    // Behavioral SRAM
    reg [WIDTH-1:0] mem [0:DEPTH-1];

    integer i;
    always @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            rdata <= {WIDTH{1'b0}};
        end else if (cs) begin
            if (we) begin
                mem[addr] <= wdata;
            end
            rdata <= mem[addr];
        end
    end

endmodule
