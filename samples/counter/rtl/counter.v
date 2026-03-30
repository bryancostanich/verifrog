// counter.v — Trivial 8-bit counter for Verifrog framework testing
//
// Features exercised:
//   - clk/rst_n (standard interface)
//   - 8-bit count register (read/write test)
//   - enable signal (force/release test)
//   - overflow flag (signal observation)
//   - configurable limit (parameter sweep test)

module counter #(
    parameter WIDTH = 8,
    parameter LIMIT = 0   // 0 = free-running (wraps at 2^WIDTH)
) (
    input  wire             clk,
    input  wire             rst_n,
    input  wire             enable,
    input  wire [WIDTH-1:0] load_value,
    input  wire             load_en,
    output reg  [WIDTH-1:0] count,
    output wire             overflow
);

    wire [WIDTH-1:0] max_val = (LIMIT == 0) ? {WIDTH{1'b1}} : LIMIT[WIDTH-1:0] - 1;

    assign overflow = (count == max_val) && enable;

    always @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            count <= {WIDTH{1'b0}};
        end else if (load_en) begin
            count <= load_value;
        end else if (enable) begin
            if (count == max_val)
                count <= {WIDTH{1'b0}};
            else
                count <= count + 1;
        end
    end

endmodule
