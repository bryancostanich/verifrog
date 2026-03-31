# Cookbook

Recipes for common test patterns. Each recipe is self-contained — copy, adapt, and use.

## Organizing tests by category

Use hardware-domain categories to organize your tests. Categories create named groups in the test hierarchy that you can filter with `--category`:

```fsharp
open Verifrog.Runner.Category

let tests = testList "MySoC" [
    smoke [
        test "design comes out of reset" { ... }
        test "clock is toggling" { ... }
    ]
    unit [
        test "register write-read" { ... }
        test "counter increments" { ... }
    ]
    parametric [
        test "ALU op sweep" { ... }
    ]
    integration [
        test "DMA transfer end-to-end" { ... }
    ]
    golden [
        test "conv layer matches reference" { ... }
    ]
    regression [
        test "issue-42: overflow at boundary" { ... }
    ]
]
```

Run by category:

```bash
verifrog test --category Smoke          # Quick sanity checks
verifrog test --category Unit           # Focused signal/block tests
verifrog test --category Integration    # Multi-block tests
verifrog test                           # All tests
```

Available categories: `Smoke`, `Unit`, `Parametric`, `Integration`, `Stress`, `Golden`, `Regression`.

## Basic patterns

### Test a counter

```fsharp
test "counter increments" {
    use sim = SimFixture.create ()
    sim.Write("enable", 1L) |> ignore
    sim.Step(10)
    Expect.signal sim "count" 10L "counted to 10"
}
```

### Test a state machine

```fsharp
test "FSM reaches done state" {
    use sim = SimFixture.create ()
    sim.Write("start", 1L) |> ignore
    sim.Step(1)
    sim.Write("start", 0L) |> ignore

    let (found, cycle) = sim.RunUntilSignal("done", 1L, 1000)
    Expecto.Expect.isTrue found "FSM should reach done"
    printfn "Completed in %d cycles" (cycle - 1UL)
}
```

### Test reset behavior

```fsharp
test "all outputs zero after reset" {
    use sim = SimFixture.create ()
    // SimFixture.create already resets — just check
    Expect.signal sim "count" 0L "count zeroed"
    Expect.signal sim "overflow" 0L "overflow zeroed"
    Expect.signal sim "valid" 0L "valid zeroed"
}
```

## Checkpoint patterns

### Fast test setup with shared initialization

```fsharp
// Expensive one-time setup
let mutable configCheckpoint: CheckpointHandle option = None

let tests = testList "with_config" [
    test "setup checkpoint" {
        use sim = SimFixture.create ()
        // ... 50 cycles of configuration ...
        sim.Register("MODE").Write(2L) |> ignore
        sim.Register("DEPTH").Write(16L) |> ignore
        sim.Step(50)
        configCheckpoint <- Some (sim.SaveCheckpoint("configured"))
    }

    test "test A (from checkpoint)" {
        use sim = SimFixture.create ()
        sim.RestoreCheckpoint("configured")
        // Start testing from configured state — skip the 50-cycle setup
        sim.Write("start", 1L) |> ignore
        sim.Step(100)
        Expect.signal sim "result" 42L "correct result"
    }

    test "test B (from checkpoint)" {
        use sim = SimFixture.create ()
        sim.RestoreCheckpoint("configured")
        // Different test, same starting point
        sim.Write("mode_override", 1L) |> ignore
        sim.Step(100)
        Expect.signal sim "result" 84L "doubled result in override mode"
    }
]
```

### Explore without losing state

```fsharp
test "fork to check a hypothesis" {
    use sim = SimFixture.create ()
    sim.Write("enable", 1L) |> ignore
    sim.Step(50)

    // What would happen if we loaded 250?
    let wouldOverflow = sim.Fork(fun s ->
        s.Write("load_en", 1L) |> ignore
        s.Write("load_value", 250L) |> ignore
        s.Step(1)
        s.Write("load_en", 0L) |> ignore
        s.Step(10)
        s.ReadOrFail("overflow") = 1L)

    // Original state is intact
    Expect.signal sim "count" 50L "count unchanged after fork"
    Expecto.Expect.isTrue wouldOverflow "loading 250 should cause overflow"
}
```

## Parametric testing

### Sweep ALU operations

```fsharp
test "ALU produces correct results for all ops" {
    use sim = SimFixture.createFromToml "verifrog.toml"
    sim.Register("A").Write(42L) |> ignore
    sim.Register("B").Write(17L) |> ignore
    sim.Step(1)

    let expected = [
        (0L, 59L)    // ADD: 42 + 17
        (1L, 25L)    // SUB: 42 - 17
        (2L, 0L)     // AND: 42 & 17
        (3L, 59L)    // OR:  42 | 17
    ]

    let results = sim.Sweep(
        expected |> List.map fst,
        fun op s ->
            s.Write("alu_op", op) |> ignore
            s.Step(2)
            s.ReadOrFail("alu_result"))

    for ((op, expectedVal), (_, actualVal)) in List.zip expected results do
        Expecto.Expect.equal actualVal expectedVal $"ALU op {op}"
}
```

### Compare two implementations

```fsharp
test "fast mode produces same result as normal mode" {
    use sim = SimFixture.create ()
    // ... load stimulus ...

    let (fastResult, normalResult) = sim.Compare(
        (fun s ->
            s.Write("fast_mode", 1L) |> ignore
            s.Step(200)
            s.ReadOrFail("output")),
        (fun s ->
            s.Write("fast_mode", 0L) |> ignore
            s.Step(200)
            s.ReadOrFail("output")))

    Expecto.Expect.equal fastResult normalResult "fast mode should match normal"
}
```

## Memory patterns

### Backdoor load and verify

```fsharp
test "load data via backdoor, verify via bus" {
    use sim = SimFixture.createFromToml "verifrog.toml"
    let ram = sim.Memory("data_ram")

    // Backdoor: write test pattern
    for addr in 0 .. 15 do
        ram.Write(0, addr, int64 (addr * 3)) |> ignore

    // Drive the design's read port
    for addr in 0 .. 15 do
        sim.Write("rd_addr", int64 addr) |> ignore
        sim.Step(2)   // Read latency
        Expect.signal sim "rd_data" (int64 (addr * 3)) $"addr {addr}"
}
```

### Initialize multi-bank SRAM

```fsharp
test "fill weight SRAM" {
    use sim = SimFixture.createFromToml "verifrog.toml"
    let wgt = sim.Memory("weight_sram")

    for bank in 0 .. wgt.Banks - 1 do
        for addr in 0 .. wgt.Depth - 1 do
            let value = int64 ((bank <<< 8) ||| addr)
            wgt.Write(bank, addr, value) |> ignore

    // Spot-check
    Expect.memory sim "weight_sram" 3 100 (int64 ((3 <<< 8) ||| 100)) "bank 3 addr 100"
}
```

## Register patterns

### Configure and verify status

```fsharp
test "writing CTRL starts operation, STATUS reflects busy" {
    use sim = SimFixture.createFromToml "verifrog.toml"

    Expect.register sim "STATUS" 0x00L "idle before start"

    sim.Register("CTRL").Write(0x01L) |> ignore   // Start
    sim.Step(1)
    Expect.register sim "STATUS" 0x01L "busy after start"

    let (found, _) = sim.RunUntilSignal("done", 1L, 5000)
    Expecto.Expect.isTrue found "should complete"
    Expect.register sim "STATUS" 0x00L "idle after done"
}
```

## Force/release patterns

### Fault injection

```fsharp
test "design recovers from stuck data bus" {
    use sim = SimFixture.create ()
    sim.Write("enable", 1L) |> ignore
    sim.Step(10)

    // Inject fault: force data bus to all-ones
    sim.Force("data_bus", 0xFFL) |> ignore
    sim.Step(5)

    // Release and check recovery
    sim.Release("data_bus") |> ignore
    sim.Step(20)
    Expect.signal sim "error_flag" 0L "should recover after release"
}
```

### Test with clock gating disabled

```fsharp
test "pipeline works without clock gating" {
    use sim = SimFixture.create ()
    sim.Force("clk_gate_en", 0L) |> ignore   // Disable clock gating

    sim.Write("data_in", 42L) |> ignore
    sim.Write("valid_in", 1L) |> ignore
    sim.Step(5)
    Expect.signal sim "data_out" 42L "data flows through ungated"

    sim.ReleaseAll()
}
```

## Iverilog patterns

### Run all discovered testbenches

```fsharp
let config = Config.parse "verifrog.toml"
let root = "."

let iverilogTests = testList "iverilog" [
    for tbName in Iverilog.discover root config do
        test tbName {
            let result = Iverilog.runAuto root config tbName
            Expect.iverilogPassed result $"{tbName} should pass"
        }
]
```

### Run with parameter overrides

```fsharp
test "FIFO with depth 32" {
    let result = Iverilog.run root config "fifo_tb"
        [("-P", "fifo_tb.DEPTH=32"); ("-P", "fifo_tb.WIDTH=16")]
        []
    Expect.iverilogPassed result "FIFO depth=32 should pass"
}
```

## VCD analysis patterns

### Verify timing in post-simulation

```fsharp
test "output valid within 3 cycles of input valid" {
    use sim = SimFixture.create ()
    sim.Write("data_in", 42L) |> ignore
    sim.Write("valid_in", 1L) |> ignore
    sim.Step(1)
    sim.Write("valid_in", 0L) |> ignore
    sim.Step(20)

    let vcd = VcdParser.parseAll "output/sim.vcd"
    let inputTime =
        VcdParser.firstTimeAtValue vcd "valid_in" 1
        |> Option.defaultWith (fun () -> failwith "valid_in never asserted")
    let outputTime =
        VcdParser.firstTimeAtValue vcd "valid_out" 1
        |> Option.defaultWith (fun () -> failwith "valid_out never asserted")

    let clockPeriod = 10_000L   // 10ns = 10,000 ps
    let latency = (outputTime - inputTime) / clockPeriod
    Expecto.Expect.isLessThanOrEqual latency 3L "latency <= 3 cycles"
}
```

### Count events

```fsharp
test "correct number of overflow pulses" {
    use sim = SimFixture.create ()
    sim.Write("enable", 1L) |> ignore
    sim.Step(1000)

    let vcd = VcdParser.parseAll "output/sim.vcd"
    let overflows = VcdParser.highPulseCount vcd "counter.overflow"
    Expecto.Expect.equal overflows 3 "3 overflows in 1000 cycles"
}
```

## Debugging patterns

### Print signal trace on failure

```fsharp
test "debug: trace around failure point" {
    use sim = SimFixture.create ()
    sim.Write("enable", 1L) |> ignore
    sim.Step(95)

    // Trace the next 10 cycles to see what happens
    let trace = sim.Trace(["count"; "overflow"; "state"], 10)
    for (cycle, vals) in trace do
        printfn "  cycle %3d: count=%-4d overflow=%d state=%d"
            cycle vals.[0] vals.[1] vals.[2]

    Expect.signal sim "state" 0L "should return to idle"
}
```

### List all signals for discovery

```fsharp
test "discover available signals" {
    use sim = SimFixture.create ()
    let signals = sim.ListSignals()
    for s in signals |> List.sort do
        printfn "  %s [%d bits]" s (sim.SignalBits(s))
}
```

### Validate config paths

```fsharp
test "all TOML paths resolve" {
    use sim = SimFixture.createFromToml "verifrog.toml"
    let errors = sim.ValidateSignals()
    Expecto.Expect.isEmpty errors "all memory/register paths should resolve"
}
```
