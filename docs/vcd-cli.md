# VCD CLI Reference

The VCD CLI (`verifrog-vcd`) is a command-line tool for analyzing VCD waveform files. It parses a VCD dump and prints a summary of all signals, their transition counts, value ranges, and optionally full transition timelines.

## Usage

```bash
verifrog vcd <file.vcd> [options]
# or
verifrog-vcd <file.vcd> [options]
```

### Synopsis

```
verifrog vcd <file.vcd> [max_time_ns] [--debug] [--signal <pattern>] [--json]
```

### Arguments

| Argument | Description |
|----------|-------------|
| `<file.vcd>` | Path to the VCD file to analyze (required) |
| `max_time_ns` | Stop parsing after this simulation time in nanoseconds (optional) |

### Options

| Option | Description |
|--------|-------------|
| `--signal <pattern>` | Only track signals matching the pattern. Can be repeated for multiple patterns. |
| `--debug`, `--verbose` | Show full transition timelines (up to 50 transitions per signal) |
| `--json` | Output the report as JSON instead of text |

## Examples

### Basic analysis

```bash
verifrog vcd output/sim.vcd
```

Output:

```
[0.2 s] Parsed: 45 signals, 32 tracked, 12847 transitions

=== VCD Analysis: sim.vcd ===
Total signals in file: 45
Signals with transitions: 32

Signal                                              Width    Trans  First                 Last                  Unique Values
----------------------------------------------------------------------------------------------------------------------------------
counter.clk                                             1     2000  0                     1                     {0, 1}
counter.count                                           8      256  0                     255                   {0, 1, 2, ... 255} (256 unique)
counter.enable                                          1        2  0                     1                     {0, 1}
counter.overflow                                        1        6  0                     1                     {0, 1}
counter.rst_n                                           1        2  0                     1                     {0, 1}
```

### Filter to specific signals

```bash
verifrog vcd output/sim.vcd --signal "count" --signal "overflow"
```

Only tracks transitions for matching signals. The header still lists all signals, but only matching ones appear in the table.

### Limit parse time

```bash
verifrog vcd output/sim.vcd 5000
```

Stops parsing at t=5000 ns. Useful for large dumps when you only care about the beginning.

### Debug mode with transition timelines

```bash
verifrog vcd output/sim.vcd --signal "fsm_state" --debug
```

Output includes transition-by-transition detail:

```
============================================================
TRANSITION DETAILS (debug mode)
============================================================

--- counter.fsm_state (24 transitions) ---
  t=       0.000 us  val=0 (0x0)
  t=       0.010 us  val=1 (0x1)
  t=       0.030 us  val=2 (0x2)
  t=       0.045 us  val=3 (0x3)
  t=       0.060 us  val=0 (0x0)
  ...
```

### JSON output

```bash
verifrog vcd output/sim.vcd --json
```

```json
{
  "filename": "sim.vcd",
  "total_signals": 45,
  "tracked_signals": 32,
  "max_time_ns": 0,
  "signal_patterns": [],
  "signals": [
    {
      "path": "counter.count",
      "width": 8,
      "transition_count": 256,
      "first_value": 0,
      "last_value": 255,
      "unique_values": [0, 1, 2, 3, 4, 5]
    }
  ]
}
```

With `--debug`, each signal entry includes a `transitions` array:

```json
{
  "path": "counter.count",
  "width": 8,
  "transition_count": 256,
  "transitions": [
    { "time": 0, "time_us": 0.0, "value": 0, "bits": "0" },
    { "time": 10000, "time_us": 0.01, "value": 1, "bits": "1" }
  ]
}
```

### Combine options

```bash
verifrog vcd output/sim.vcd 10000 --signal "fsm*" --signal "done" --debug --json
```

Parse up to t=10,000 ns, track only FSM and done signals, include transition timelines, output as JSON.

## Signal patterns

Patterns match the same way as `VcdParser.findSignals`:

| Pattern | Matches |
|---------|---------|
| `count` | Leaf name `count` or full path ending in `.count` |
| `counter.count` | Exact full path |
| `fsm*` | Any signal containing `fsm` in path or starting with `fsm` in leaf name |

## Performance

Parsing performance is reported to stderr:

```
[0.2 s] Parsed: 45 signals, 32 tracked, 12847 transitions
[0.2 s] Done.
```

For large VCD files, use `--signal` filtering and a time limit to reduce parsing time and memory usage.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Invalid arguments |
| 2 | File not found |
