# VCD Parser Guide

`Verifrog.Vcd` is a standalone F# library for parsing and analyzing Value Change Dump (VCD) waveform files. It's independent of `Verifrog.Sim` — you can use it in any .NET project, not just Verifrog tests.

VCD files are the standard waveform format produced by Verilator (`--trace`), Icarus Verilog (`$dumpfile`/`$dumpvars`), and most other Verilog simulators. They record every signal transition during simulation.

## When to use VCD analysis

- **Post-simulation assertions**: Verify timing relationships, pulse counts, or signal sequences that are easier to check after the fact than cycle-by-cycle
- **Debugging**: Find when a signal first went to an unexpected value, or what values it took over a time range
- **Regression checking**: Compare waveform properties (transition counts, timing) across runs
- **Coverage analysis**: Check which values a signal exercised

## Quick start

Add a reference to `Verifrog.Vcd` in your `.fsproj`:

```xml
<ProjectReference Include="$(VERIFROG_ROOT)/src/Verifrog.Vcd/Verifrog.Vcd.fsproj" />
```

Parse a VCD file and query it:

```fsharp
open Verifrog.Vcd

// Parse the entire file
let vcd = VcdParser.parseAll "output/sim.vcd"

// What signals are in this dump?
printfn "Total signals: %d" vcd.Signals.Length
for s in vcd.Signals do
    printfn "  %s [%d bits]" s.FullPath s.Width

// Find signals by name
let clocks = VcdParser.findSignals vcd "clk"
let allCounters = VcdParser.findSignals vcd "count*"

// Get transitions for a specific signal
let trans = VcdParser.transitions vcd "counter.count"
printfn "count had %d transitions" trans.Length

// What was the value at a specific time?
match VcdParser.valueAtTime vcd "counter.count" 50000L with
| Some t -> printfn "count = %d at t=%d ps" t.IntVal t.Time
| None   -> printfn "No data for count at that time"
```

## Parsing

### Parse everything

```fsharp
let vcd = VcdParser.parseAll "sim.vcd"
```

Reads the entire file, tracking all signals. Fine for small-to-medium VCD files (up to a few hundred MB).

### Parse with a time limit

```fsharp
let vcd = VcdParser.parse "sim.vcd" 1_000_000L    // Stop at t=1,000,000 ps (1 us)
```

For large dumps, stop parsing early. The time is in VCD time units — typically picoseconds (check your simulator's `$timescale`).

### Parse with signal filtering

```fsharp
let vcd = VcdParser.parseFiltered "sim.vcd" ["count"; "overflow"; "fsm*"] 0L
```

Only track transitions for signals matching the patterns. This dramatically reduces memory usage for large designs where you only care about a few signals. The last argument is the time limit (`0L` = no limit).

### Pattern matching

Signal patterns support three forms:

| Pattern | Matches |
|---------|---------|
| `count` | Any signal with leaf name `count`, or full path ending in `.count` |
| `counter.count` | Exact full path match |
| `fsm*` | Any signal whose full path contains `fsm` or leaf name starts with `fsm` |

## The VcdFile type

After parsing, you get a `VcdFile` with three fields:

```fsharp
type VcdFile = {
    Signals: VcdSignal list                                    // All signals in the header
    SignalMap: IReadOnlyDictionary<string, VcdSignal>          // Signal ID -> metadata
    Transitions: IReadOnlyDictionary<string, VcdTransition list>  // Full path -> transitions
}
```

### VcdSignal

```fsharp
type VcdSignal = {
    Id: string        // Short VCD identifier (e.g., "!")
    LeafName: string  // Just the signal name (e.g., "count")
    FullPath: string  // Hierarchical path (e.g., "counter.count")
    Width: int        // Bit width
}
```

### VcdTransition

```fsharp
[<Struct>]
type VcdTransition = {
    Time: int64    // Time in VCD time units (typically picoseconds)
    Bits: string   // Raw bit string (e.g., "01101001")
    IntVal: int    // Integer value (-1 if x/z)
}
```

The `IntVal` field treats `x` and `z` bits as 0 for the integer conversion. If the signal contains unknown values, `IntVal` will be `-1` only if the entire value string is unparseable. For mixed `x`/`0`/`1` values, check the `Bits` string directly.

## Query API

### Find signals

```fsharp
// Find by name (leaf or full path)
let signals = VcdParser.findSignals vcd "count"

// Find by glob
let allFsm = VcdParser.findSignals vcd "fsm*"

// Result is a list of VcdSignal
for s in signals do
    printfn "%s [%d bits]" s.FullPath s.Width
```

### Get transitions

```fsharp
// All transitions for a signal (time-ordered)
let trans = VcdParser.transitions vcd "counter.count"

for t in trans do
    printfn "  t=%d ps  value=%d  bits=%s" t.Time t.IntVal t.Bits

// Count transitions
let n = VcdParser.transitionCount vcd "counter.count"
```

### Value at a specific time

Returns the last transition at or before the given time:

```fsharp
match VcdParser.valueAtTime vcd "counter.count" 100_000L with
| Some t ->
    printfn "count = %d at t=%d ps" t.IntVal t.Time
    // t.Time may be earlier than 100_000 if the signal hasn't changed since then
| None ->
    printfn "No transitions for this signal before t=100,000 ps"
```

### Find first occurrence of a value

```fsharp
match VcdParser.firstTimeAtValue vcd "fsm_state" 5 with
| Some time -> printfn "FSM entered state 5 at t=%d ps (%.3f us)" time (VcdParser.timeToUs time)
| None      -> printfn "FSM never reached state 5"
```

### Unique values

```fsharp
let states = VcdParser.uniqueValues vcd "fsm_state"
// e.g., [0; 1; 2; 3; 5]  — state 4 was never visited
printfn "FSM visited %d states: %A" states.Length states
```

### High-pulse count

Count 0-to-1 transitions for a 1-bit signal:

```fsharp
let pulses = VcdParser.highPulseCount vcd "counter.overflow"
printfn "Overflow pulsed %d times" pulses
```

### Helpers

```fsharp
// Parse a binary string to int (x/z treated as 0)
let v = VcdParser.parseBinValue "01101001"    // 105

// Convert picoseconds to microseconds
let us = VcdParser.timeToUs 50_000_000L       // 50.0
```

## Examples in tests

### Verify overflow pulse count

```fsharp
test "overflow pulses correct number of times" {
    use sim = SimFixture.create ()
    sim.Write("enable", 1L) |> ignore
    sim.Step(1000)

    let vcd = VcdParser.parseAll "output/sim.vcd"
    let pulses = VcdParser.highPulseCount vcd "counter.overflow"
    Expecto.Expect.equal pulses 3 "should overflow 3 times in 1000 cycles"
}
```

### Check timing relationship

```fsharp
test "done asserts within 100 cycles of start" {
    use sim = SimFixture.create ()
    // ... run stimulus ...

    let vcd = VcdParser.parseAll "output/sim.vcd"
    let startTime =
        VcdParser.firstTimeAtValue vcd "start" 1
        |> Option.defaultWith (fun () -> failwith "start never asserted")
    let doneTime =
        VcdParser.firstTimeAtValue vcd "done" 1
        |> Option.defaultWith (fun () -> failwith "done never asserted")

    let cycleDelta = (doneTime - startTime) / 10_000L   // assuming 10ns clock period
    Expecto.Expect.isLessThan cycleDelta 100L "done should assert within 100 cycles"
}
```

### Check FSM coverage

```fsharp
test "FSM visits all states" {
    use sim = SimFixture.create ()
    // ... run stimulus ...

    let vcd = VcdParser.parseAll "output/sim.vcd"
    let states = VcdParser.uniqueValues vcd "fsm_state"
    let expected = [0; 1; 2; 3; 4; 5]
    for s in expected do
        Expecto.Expect.contains states s $"FSM should visit state {s}"
}
```

### Filtered parsing for large dumps

```fsharp
test "check only the signals we care about" {
    // Only track FSM and output signals — ignore everything else
    let vcd = VcdParser.parseFiltered "output/big_sim.vcd" ["fsm*"; "output*"] 0L
    let fsmSignals = VcdParser.findSignals vcd "fsm*"
    printfn "Tracking %d FSM signals out of %d total" fsmSignals.Length vcd.Signals.Length
    // ...
}
```

## Command-line analysis

For quick VCD analysis without writing F# code, use the [VCD CLI tool](vcd-cli.md).

## Performance notes

- The parser reads files sequentially in a single pass (two passes: header, then transitions)
- Signal filtering (`parseFiltered`) skips transition recording for non-matching signals, saving memory
- Time limiting (`parse` with `maxTime > 0`) stops reading early
- For very large VCD files (multi-GB), use both filtering and time limiting
- `VcdTransition` is a value type (`[<Struct>]`) to minimize GC pressure
