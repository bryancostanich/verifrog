# Track Plan: Debug Integration

## Phase 1: Document Existing Debug Workflow

Zero new code — just docs and config that unlock what already works.

- [ ] Verify `netcoredbg` works with Verifrog test projects (install, test)
- [ ] Write `launch.json` template for VS Code F# test debugging
  - DYLD_LIBRARY_PATH / LD_LIBRARY_PATH in env
  - Program path to test project
  - Working directory set correctly
- [ ] Have `verifrog init` generate `.vscode/launch.json` alongside the test project
- [ ] Document watch expression patterns for signal probing:
  - `sim.ReadOrFail("signal_name")` as a watch
  - `sim.Cycle` as a watch
  - `sim.ListSignals() |> List.filter (fun s -> s.Contains("fsm"))` for discovery
  - `sim.ForceCount` to check active forces
- [ ] Document conditional breakpoints as signal watchpoints:
  - "Break when `sim.ReadOrFail("u_fsm.state") = 15L`" (ERROR state)
- [ ] Add debug guide to docs/
- [ ] Update README with debug section

## Phase 2: `verifrog debug-dap` CLI Wrapper

Thin wrapper that manages a DAP session for non-GUI debugging (Claude, SSH, CI).

- [ ] Check if `netcoredbg --interpreter=cli` gives a usable GDB-like interface
- [ ] If yes: wrapper script that launches with the right args and auto-breakpoints
- [ ] If no: write a minimal DAP client in F# that speaks JSON to `netcoredbg --interpreter=vscode`
- [ ] Commands:
  - `verifrog debug-dap <project-dir> [--test "name"]` — launch and pause
  - `verifrog debug-eval <expr>` — evaluate F# expression in paused context
  - `verifrog debug-step [n]` — step N sim cycles (eval `sim.Step(n)`)
  - `verifrog debug-read <signal>` — shorthand for eval ReadOrFail
  - `verifrog debug-signals [filter]` — list signals matching filter
  - `verifrog debug-checkpoint <name>` — save state
  - `verifrog debug-restore <name>` — restore state
  - `verifrog debug-quit` — tear down session
- [ ] Session state persisted via the running netcoredbg process (stays alive between commands)
- [ ] Test: Claude can run a full debug session across multiple bash calls

## Phase 3: VS Code Extension

The premium experience — IDE-native RTL debugging.

- [ ] Extension scaffold (TypeScript, VS Code extension API)
- [ ] `.verifrog` language support:
  - Syntax highlighting
  - Breakpoints on declarative test lines (write, step, expect, etc.)
  - Map breakpoints to `Declarative.executeStep` calls in the DAP session
- [ ] Signals panel (TreeView):
  - Lists all signals from `sim.ListSignals()`
  - Shows current value, updates on each debug pause
  - Filter/search
  - Click to add as watch expression
- [ ] Signal watchpoints:
  - "Break when signal == value" UI
  - Implemented as conditional breakpoints on the sim step loop
- [ ] Waveform integration:
  - Button to start VCD tracing
  - Auto-open in Surfer VS Code extension (if installed)
  - Or dump to file for GTKWave
- [ ] Debug toolbar additions:
  - "Step N cycles" button (configurable N)
  - "Run until signal" quick input
  - Checkpoint/restore buttons

## Phase 4: Claude Integration

Make interactive debugging a first-class capability for AI-assisted RTL debugging.

- [ ] MCP tool or structured CLI that Claude can call tool-by-tool:
  - `debug_start(project, test_name)` → session
  - `debug_step(n)` → signal snapshot
  - `debug_read(signals[])` → values
  - `debug_checkpoint(name)` → ok
  - `debug_restore(name)` → ok
  - `debug_eval(expression)` → result
  - `debug_quit()` → ok
- [ ] Each tool call returns structured JSON, not raw text
- [ ] Auto-attach to failing tests: when a test fails, offer to debug it
- [ ] Session replay: record debug commands → save as `.verifrog` script or F# test

## Concept Mapping

| Debug concept | RTL equivalent | Implementation |
|---|---|---|
| Watch expression | Signal probe | `sim.ReadOrFail("name")` in DAP evaluate |
| Breakpoint | Test line pause | DAP setBreakpoints on .fs/.verifrog line |
| Conditional breakpoint | Signal watchpoint | `sim.ReadOrFail("sig") == value` condition |
| Step Over | Advance 1 cycle | DAP evaluate `sim.Step(1)` |
| Step N | Advance N cycles | DAP evaluate `sim.Step(n)` |
| Continue | Run until condition | DAP evaluate `sim.RunUntilSignal(...)` |
| Variables panel | Signal snapshot | Extension queries all watched signals |
| Call Stack | Test → helper → sim | Standard .NET call stack |
| Checkpoint | Save sim state | DAP evaluate `sim.SaveCheckpoint("name")` |
| Restore | Restore sim state | DAP evaluate `sim.RestoreCheckpoint("name")` |

## Open Questions

- Does `netcoredbg` support evaluate-in-frame for F# closures (needed for `sim.ReadOrFail`)?
- Can we evaluate during a conditional breakpoint check without side effects?
- How to handle the native library path (`DYLD_LIBRARY_PATH`) in the DAP launch config?
- Should Phase 3 (VS Code extension) be a separate repo (`verifrog-vscode`)?
- For Phase 4 (Claude), is MCP the right interface or should it be simpler CLI tools?
