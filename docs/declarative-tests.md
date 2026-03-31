# Declarative Tests

Write hardware tests without F# code. The `.verifrog` format is a line-oriented test description language that covers ~75% of typical block-level verification tests.

## When to use declarative tests

Use `.verifrog` files for tests that are **stimulus-check sequences**: set signals, step the simulation, check results. This covers smoke tests, basic unit tests, register write-read, memory load-verify, and simple inference completion checks.

Use F# for tests that need **control flow**: error recovery workflows, golden model comparisons, multi-inference analysis, computed expected values, or fork/sweep exploration.

Both run in the same test suite under `verifrog test`.

## Quick example

```
# counter.verifrog

test "counter reaches 10" [Smoke]:
  write enable = 1
  step 10
  expect count == 10
```

That's it. No imports, no boilerplate, no semicolons.

**Equivalent F#:**

```fsharp
test "counter reaches 10" {
    use sim = SimFixture.create ()
    sim.Write("enable", 1L) |> ignore
    sim.Step(10)
    Expect.signal sim "count" 10L "count should reach 10"
}
```

## Format reference

### Test header

```
test "test name" [Category]:
```

- **Name**: quoted string
- **Category**: optional, in brackets. One of: `Smoke`, `Unit`, `Parametric`, `Integration`, `Stress`, `Golden`, `Regression`
- Line must end with `:`
- Everything indented below belongs to this test

### Commands

All commands are indented under a test header.

#### `write` — Set signal values

```
  write enable = 1
  write load_value = 0x2A
  write data_in = 255
```

Multiple signals on one line:

```
  write load_value = 42, load_en = 1
```

Supports decimal and `0x` hex values.

#### `step` — Advance clock cycles

```
  step 10
  step 1
```

#### `expect` — Assert signal value

```
  expect count == 10
  expect overflow == 1
  expect status != 0
```

Operators: `==` and `!=`.

#### `expect` (memory) — Assert memory contents

```
  expect data_ram[0][42] == 0xAB
```

Format: `expect <memory>[<bank>][<addr>] == <value>`. Memory names come from `verifrog.toml`.

#### `load` — Write data to memory

Inline data:

```
  load data_ram bank=0 [0xAB, 0xCD, 0xEF, 0x01]
```

Writes values to consecutive addresses starting at 0.

From a hex file (`$readmemh` format):

```
  load weight_sram bank=0 from weights.hex
```

The hex file uses the same format as Verilog's `$readmemh`: hex values separated by whitespace, with optional `@address` directives.

#### `run-until` — Step until a condition

```
  run-until done == 1, max = 10000
  run-until fsm_state == 5, max = 50000
```

Steps the simulation until the signal equals the value, or the max cycle count is reached. If the max is reached, the test fails with a timeout message.

#### `force` / `release` — Signal overrides

```
  force clk_en = 0
  step 10
  release clk_en
```

`force` holds a signal at a value across clock steps. `release` removes the override and lets the design drive the signal again.

#### `checkpoint` / `restore` — State snapshots

```
  checkpoint before_test
  step 100
  restore before_test
```

`checkpoint` saves the entire simulation state. `restore` snaps back to it. Named checkpoints are scoped to the test.

### Comments

Lines starting with `#` are comments:

```
# This is a comment
test "my test" [Smoke]:
  # Set up stimulus
  write enable = 1
  step 10
  expect count == 10
```

## Full example

```
# counter.verifrog — declarative tests for the counter sample

test "counter starts at zero" [Smoke]:
  expect count == 0

test "counter reaches 10" [Smoke]:
  write enable = 1
  step 10
  expect count == 10

test "counter wraps after 255" [Smoke]:
  write enable = 1
  step 256
  expect count == 0

test "load value" [Unit]:
  write load_value = 0x2A
  write load_en = 1
  step 1
  write load_en = 0
  expect count == 0x2A

test "load then count" [Unit]:
  write load_value = 42
  write load_en = 1
  step 1
  write load_en = 0
  write enable = 1
  step 5
  expect count == 47

test "force holds count" [Unit]:
  write enable = 1
  step 5
  expect count == 5
  force enable = 0
  step 10
  expect count == 5
  release enable

test "checkpoint and restore" [Unit]:
  write enable = 1
  step 5
  checkpoint mid
  step 5
  expect count == 10
  restore mid
  expect count == 5

test "run-until overflow" [Unit]:
  write enable = 1
  write load_value = 250
  write load_en = 1
  step 1
  write load_en = 0
  run-until overflow == 1, max = 20
  step 1
  expect count == 0
```

## Side-by-side: declarative vs F#

### Simple signal check

**Declarative:**
```
test "starts at zero" [Smoke]:
  expect count == 0
```

**F#:**
```fsharp
test "starts at zero" {
    use sim = SimFixture.create ()
    Expect.signal sim "count" 0L "count should be 0"
}
```

### Write, step, check

**Declarative:**
```
test "load then count" [Unit]:
  write load_value = 42, load_en = 1
  step 1
  write load_en = 0, enable = 1
  step 5
  expect count == 47
```

**F#:**
```fsharp
test "load then count" {
    use sim = SimFixture.create ()
    sim.Write("load_value", 42L) |> ignore
    sim.Write("load_en", 1L) |> ignore
    sim.Step(1)
    sim.Write("load_en", 0L) |> ignore
    sim.Write("enable", 1L) |> ignore
    sim.Step(5)
    Expect.signal sim "count" 47L "should be 47"
}
```

### Checkpoint/restore

**Declarative:**
```
test "checkpoint and restore" [Unit]:
  write enable = 1
  step 5
  checkpoint mid
  step 5
  expect count == 10
  restore mid
  expect count == 5
```

**F#:**
```fsharp
test "checkpoint and restore" {
    use sim = SimFixture.create ()
    sim.Write("enable", 1L) |> ignore
    sim.Step(5)
    let cp = sim.SaveCheckpoint("mid")
    sim.Step(5)
    Expect.signal sim "count" 10L "count reaches 10"
    sim.RestoreCheckpoint("mid")
    Expect.signal sim "count" 5L "back to 5"
}
```

### When to stay in F#

**Fork/sweep** — needs closures:
```fsharp
let result = sim.Fork(fun s ->
    s.Write("mode", 3L) |> ignore
    s.Step(50)
    s.ReadOrFail("output"))
```

**Golden model comparison** — needs computed values:
```fsharp
let expected = GoldenModel.run config weights activations
Expect.equal actual expected "should match golden"
```

**Error recovery** — needs conditional control flow:
```fsharp
sim.RunUntilSignal("fsm_state", 3L, 5000) |> ignore  // Wait for COMPUTE
sim.Write("abort", 1L) |> ignore                       // Inject abort
sim.Step(1)
sim.Write("abort", 0L) |> ignore
sim.RunUntilSignal("fsm_state", 0L, 1000) |> ignore   // Should return to IDLE
Expect.signal sim "error_flag" 0L "abort is not an error"
```

## Using declarative tests in your project

### 1. Write a `.verifrog` file

Create a file with the `.verifrog` extension in your `tests/` directory (or anywhere your test project can find it).

### 2. Load in your test project

```fsharp
open Verifrog.Runner.Declarative

[<Tests>]
let declarativeTests =
    let tests = loadTests "path/to/tests"
    testList "Declarative" tests
```

Or with TOML config (for memory/register access):

```fsharp
[<Tests>]
let declarativeTests =
    let config = Config.parse "verifrog.toml"
    let tests = loadTestsWithConfig "path/to/tests" config
    testList "Declarative" tests
```

### 3. Run

```bash
verifrog test                    # Runs both F# and declarative tests
verifrog test --category Smoke   # Filters work across both
verifrog test --report           # Report includes both
```

## Error messages

When a declarative test fails, the error points to the `.verifrog` file:

```
Expected count == 10 (0xA), got 5 (0x5)
  at counter.verifrog:7
```

Parse errors also include file and line:

```
counter.verifrog:12: Invalid write: bad syntax
```
