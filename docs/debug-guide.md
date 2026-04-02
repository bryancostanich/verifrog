# Debugging Verifrog Tests

This guide covers how to use VS Code's built-in debugger with Verifrog test projects. Because Verifrog tests are standard .NET/F# projects, you get full debugger support — breakpoints, watch expressions, step-through, and conditional breakpoints — all wired into the simulation.

## Prerequisites

- [VS Code](https://code.visualstudio.com/) with the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension (provides the `coreclr` debugger, works with F#)
- A Verifrog project with `verifrog build` already run (the native library must exist)

## Quick Start

1. Run `verifrog init` in your project — it creates `.vscode/launch.json` automatically
2. Edit `verifrog.toml` and add your RTL sources
3. Run `verifrog build` to compile the Verilator model
4. Open the project folder in VS Code
5. Set a breakpoint in your test file
6. Press F5 (or Run > Start Debugging) and select "Debug Tests"

## launch.json Configuration

`verifrog init` generates a `.vscode/launch.json` that looks like this:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Debug Tests",
            "type": "coreclr",
            "request": "launch",
            "program": "dotnet",
            "args": [
                "run",
                "--project", "${workspaceFolder}/tests/Tests.fsproj",
                "--",
                "--sequenced"
            ],
            "cwd": "${workspaceFolder}",
            "env": {
                "DYLD_LIBRARY_PATH": "${workspaceFolder}/build",
                "LD_LIBRARY_PATH": "${workspaceFolder}/build"
            },
            "console": "integratedTerminal",
            "stopAtEntry": false
        },
        {
            "name": "Debug Single Test",
            "type": "coreclr",
            "request": "launch",
            "program": "dotnet",
            "args": [
                "run",
                "--project", "${workspaceFolder}/tests/Tests.fsproj",
                "--",
                "--sequenced",
                "--filter", "${input:testName}"
            ],
            "cwd": "${workspaceFolder}",
            "env": {
                "DYLD_LIBRARY_PATH": "${workspaceFolder}/build",
                "LD_LIBRARY_PATH": "${workspaceFolder}/build"
            },
            "console": "integratedTerminal",
            "stopAtEntry": false
        }
    ],
    "inputs": [
        {
            "id": "testName",
            "type": "promptString",
            "description": "Test name (substring match)"
        }
    ]
}
```

**Key settings:**

- **`DYLD_LIBRARY_PATH`** (macOS) / **`LD_LIBRARY_PATH`** (Linux) — Points to the `build/` directory containing `libverifrog_sim.dylib`/`.so`. Without this, P/Invoke calls to the native simulation library will fail.
- **`--sequenced`** — Verilator's global state is not thread-safe. Tests must run sequentially.
- **`--filter`** — Expecto's substring filter. Use "Debug Single Test" to isolate one test.

## Watch Expressions for Signal Probing

When paused at a breakpoint inside a test, the `sim` variable (a `Verifrog.Sim.Sim` instance) gives you live access to the simulation. Add these as **Watch expressions** in the VS Code debug panel:

### Read a signal value
```
sim.ReadOrFail("signal_name")
```
Returns the current value as `int64`. Replace `signal_name` with any signal in your design — hierarchical paths use dots (e.g., `"u_fsm.state"`).

### Current cycle count
```
sim.Cycle
```
Shows how many clock cycles have elapsed since reset.

### Discover signals
```
sim.ListSignals() |> Array.filter (fun s -> s.Contains("fsm"))
```
Find signals matching a substring. Useful when you don't know the exact hierarchical path.

### Check active forces
```
sim.ForceCount
```
Returns the number of signals currently held by `Force()`. Nonzero means something is overriding normal simulation behavior.

### Read multiple signals at once
```
["clk"; "reset"; "enable"; "count"] |> List.map (fun s -> s, sim.ReadOrFail(s))
```
Shows a list of (name, value) tuples in the Watch panel.

## Conditional Breakpoints as Signal Watchpoints

VS Code's conditional breakpoints let you implement hardware-style signal watchpoints: "pause when a signal reaches a specific value."

### Setup

1. Set a breakpoint on a line inside a loop or after `sim.Step()`:
   ```fsharp
   for _ in 1..1000 do
       sim.Step(1)       // <-- set breakpoint here
       ()
   ```

2. Right-click the breakpoint → **Edit Breakpoint** → **Expression**

3. Enter a condition:
   ```
   sim.ReadOrFail("u_fsm.state") == 15
   ```
   This pauses only when the FSM enters the ERROR state (value 15).

### Example conditions

| Condition | What it catches |
|---|---|
| `sim.ReadOrFail("u_fsm.state") == 15` | FSM enters ERROR state |
| `sim.ReadOrFail("overflow") == 1` | Overflow flag asserts |
| `sim.Cycle > 5000UL` | Simulation runs past cycle 5000 |
| `sim.ReadOrFail("count") > 200` | Counter exceeds threshold |
| `sim.ReadOrFail("valid") == 1 && sim.ReadOrFail("ready") == 0` | Valid asserted but ready deasserted (backpressure) |

### Stepping through cycles

Once paused, use the **Debug Console** (not the Watch panel) to advance the simulation manually:

```fsharp
sim.Step(1)                          // Advance 1 cycle
sim.ReadOrFail("count")              // Read a signal
sim.Step(10)                         // Advance 10 cycles
sim.ReadOrFail("u_fsm.state")       // Check FSM state
```

This gives you interactive, cycle-accurate control from the debugger.

## Checkpoints in the Debugger

Save and restore simulation state without restarting:

```fsharp
// In Debug Console:
sim.SaveCheckpoint("before_bug")     // Snapshot current state
sim.Step(100)                        // Run forward
sim.ReadOrFail("error_flag")         // Observe the bug
sim.RestoreCheckpoint("before_bug")  // Rewind to the saved state
// Now try a different approach from the same point
```

## Tips

- **Build in Debug configuration**: `dotnet build -c Debug` ensures full debug symbols. `verifrog build` handles the native library; the .fsproj handles the managed side.
- **Breakpoint on Expect failures**: Set a breakpoint on `Expect.signal` or `Expect.equal` calls to pause right before an assertion.
- **Use `sim.ListSignals()`** in the Debug Console when you first start debugging a new design — it shows every signal the Verilator model exposes.
- **VCD tracing**: If you need waveforms alongside debugging, add `sim.EnableVcd("debug.vcd")` before stepping, then open the VCD in Surfer or GTKWave after the session.

## Troubleshooting

### "Unable to find libverifrog_sim"
The `DYLD_LIBRARY_PATH` / `LD_LIBRARY_PATH` in launch.json must point to the directory containing the built library. Run `verifrog build` first, then verify the path in launch.json matches `[test].output` in your `verifrog.toml`.

### Breakpoint shows "No executable code"
F# computation expressions (like `test "name" { ... }`) can confuse the debugger's line mapping. Move the breakpoint to a line with an actual expression (e.g., `sim.Step(10)` or `let count = ...`), not the opening brace or `test` keyword.

### Tests run but skip all
If you see "0 tests run," the `--filter` argument might not match any test names. Remove the filter or check your test names with `dotnet run --project tests/ -- --list-tests`.
