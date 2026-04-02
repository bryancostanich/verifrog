# Track: Debug Integration

Use the .NET Debug Adapter Protocol (DAP) to turn VS Code into an interactive RTL debug environment. Step through hardware tests, inspect signal values, set watchpoints, checkpoint/restore — all from the IDE or from Claude via CLI.

## Deliverables

1. **VS Code launch.json + docs** — Out-of-the-box F# test debugging with signal watch expressions. Zero new code, just documentation and a generated config.

2. **`verifrog debug-dap` CLI wrapper** — Thin wrapper around `netcoredbg` that launches a test under the debugger with simplified eval commands. Enables Claude (and any CLI user) to interactively debug simulations without a GUI.

3. **VS Code extension** — `.verifrog` file support: breakpoints on declarative test lines, a Signals panel showing live values, waveform viewer integration.

## Why this matters

The existing `verifrog debug` REPL is powerful but isolated — it doesn't integrate with the IDE or the test runner. DAP-based debugging means:

- **For users:** Set a breakpoint in a test, hit F5, see signal values in the watch panel. The same workflow they use for any .NET code, but now it's debugging hardware.
- **For Claude:** Interactive RTL debugging sessions — step, probe, reason, checkpoint, try something else — without restarting the sim. Follows the RTL debugging protocol naturally.
- **For CI:** Conditional debug sessions — if a test fails, dump a debug script that can be replayed locally.

## Context

The infrastructure already exists:
- `netcoredbg` is the open-source .NET DAP implementation (same as VS Code uses for C#/F#)
- DAP is a JSON protocol over stdin/stdout — CLI-friendly
- Verifrog's `Sim` type is fully inspectable from the .NET debugger (all signal reads are just method calls)
- Watch expressions like `sim.ReadOrFail("u_fsm.state")` work in any .NET debugger today
