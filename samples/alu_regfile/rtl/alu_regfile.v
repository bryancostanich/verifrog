// alu_regfile.v — Simple ALU with 8-entry register file
// Verifrog sample: demonstrates TOML-driven register access

module alu_regfile (
    input  wire        clk,
    input  wire        rst_n,
    // Register write port
    input  wire [2:0]  wr_addr,
    input  wire [7:0]  wr_data,
    input  wire        wr_en,
    // Register read ports
    input  wire [2:0]  rd_addr_a,
    input  wire [2:0]  rd_addr_b,
    output wire [7:0]  rd_data_a,
    output wire [7:0]  rd_data_b,
    // ALU control
    input  wire [1:0]  alu_op,    // 00=ADD, 01=SUB, 10=AND, 11=OR
    input  wire        alu_start,
    output reg  [7:0]  alu_result,
    output reg         alu_done
);

    // Register file: 8 x 8-bit
    reg [7:0] regs [0:7];

    // Read ports (combinational)
    assign rd_data_a = regs[rd_addr_a];
    assign rd_data_b = regs[rd_addr_b];

    // Register write
    integer i;
    always @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            for (i = 0; i < 8; i = i + 1)
                regs[i] <= 8'd0;
        end else if (wr_en) begin
            regs[wr_addr] <= wr_data;
        end
    end

    // ALU (1-cycle latency)
    always @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            alu_result <= 8'd0;
            alu_done <= 1'b0;
        end else if (alu_start) begin
            case (alu_op)
                2'b00: alu_result <= rd_data_a + rd_data_b;
                2'b01: alu_result <= rd_data_a - rd_data_b;
                2'b10: alu_result <= rd_data_a & rd_data_b;
                2'b11: alu_result <= rd_data_a | rd_data_b;
            endcase
            alu_done <= 1'b1;
        end else begin
            alu_done <= 1'b0;
        end
    end

endmodule
