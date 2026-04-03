# Debugging Verifrog Tests

Verifrog provides several ways to debug your RTL simulations, from an interactive command-line REPL to a JSON server for AI-assisted debugging. This guide covers all of them.

## Interactive Debugger (CLI)

The fastest way to debug. Launch with `verifrog debug`:

```bash
verifrog build                     # Build the Verilator model first
verifrog debug                     # Start interactive session
verifrog debug --script probe.txt  # Run a batch script
```

Once in the REPL, you have full control over the simulation:

```
sim> write enable 1
  enable <- 1
sim> step 10
sim> read count
  count = 10
sim> checkpoint before_overflow
  Saved checkpoint 'before_overflow' at cycle 10
sim> step 300
sim> read overflow
  overflow = 1
sim> restore before_overflow
  Restored checkpoint 'before_overflow' (cycle 10)
sim> quit
```

Full command list: `step`, `read`, `write`, `trace`, `watch`, `checkpoint`, `restore`, `force`, `release`, `run-until`, `signals`, `format`, `record`. Type `help` in the REPL for details.

## JSON Debug Server

For programmatic/AI-assisted debugging. Reads JSON commands from stdin, writes JSON responses to stdout:

```bash
verifrog debug-server              # Stays alive until quit
```

Each command is one JSON line, each response is one JSON line:

```json
{"cmd":"step","n":10}
{"status":"ok","cycle":10}

{"cmd":"read","signals":["count","enable"]}
{"status":"ok","cycle":10,"values":{"count":10,"enable":1}}

{"cmd":"trace","signals":["count","overflow"],"n":5}
{"status":"ok","cycles":5,"signals":["count","overflow"],"rows":[{"cycle":1,"values":{"count":1,"overflow":0}},{"cycle":2,"values":{"count":2,"overflow":0}},...]}

{"cmd":"checkpoint","name":"mid"}
{"status":"ok","name":"mid","cycle":10}
```

Commands: `status`, `step`, `read`, `write`, `trace`, `checkpoint`, `restore`, `signals`, `force`, `release`, `run-until`, `reset`, `quit`, `record`, `save-replay`.

### Session Replay

Record a debug session and export it as a `.verifrog` test:

```json
{"cmd":"record"}
{"cmd":"write","signal":"enable","value":1}
{"cmd":"step","n":10}
{"cmd":"checkpoint","name":"mid"}
{"cmd":"save-replay","path":"recorded.verifrog"}
```

This generates a `.verifrog` declarative test file you can run with `verifrog test`.

## MCP Server (for Claude)

An MCP (Model Context Protocol) server that exposes simulation tools directly to Claude:

```bash
verifrog mcp-server                # Speaks JSON-RPC 2.0 over stdio
```

Tools available: `debug_status`, `debug_step`, `debug_read`, `debug_write`, `debug_trace`, `debug_signals`, `debug_checkpoint`, `debug_restore`, `debug_force`, `debug_release`, `debug_run_until`, `debug_reset`.

To configure in Claude Code, add to your MCP settings:

```json
{
  "mcpServers": {
    "verifrog": {
      "command": "verifrog",
      "args": ["mcp-server", "/path/to/your/project"]
    }
  }
}
```

## VS Code Integration

### Running Tests

Running tests in VS Code works reliably. `verifrog init` generates a `.vscode/launch.json` that lets you run your test suite:

```bash
verifrog init my-project    # Generates .vscode/launch.json
verifrog build my-project
code my-project             # Open in VS Code, press F5 to run tests
```

### VS Code Extension

The Verifrog VS Code extension (`src/Verifrog.VSCodeExtension/`) provides:

- **Syntax highlighting** for `.verifrog` declarative test files
- **Outline panel** with clean test names and categories
- **Signals panel** showing live signal values when paused in a debug session
- **Checkpoints panel** for save/restore
- **Toolbar commands**: Step N Cycles, Run Until Signal, Toggle VCD Tracing

### Debugging (Experimental)

> **Warning**: VS Code step-through debugging of F# test code has significant limitations. The interactive CLI debugger (`verifrog debug`) and JSON/MCP servers are the recommended debugging interfaces.

**The problem**: Verifrog tests use Expecto's `test "name" { ... }` computation expressions. The F# compiler transforms these into closure classes, and neither Microsoft's debugger (vsdbg) nor Samsung's netcoredbg can reliably hit line breakpoints inside them via the DAP protocol. Breakpoints in regular F# functions work fine — only CE bodies are affected.

**What works**:
- Breakpoints in **regular F# code** (non-CE functions, `Program.fs`, `Sim.fs`, etc.)
- **Function breakpoints** (e.g., break on `Verifrog.Sim.Sim.ReadOrFail`) — the extension auto-sets this
- **Signal inspection** via the Signals panel when paused at any Sim method
- **Watch expressions** like `sim.ReadOrFail("count")` and `sim.Cycle`

**What doesn't work**:
- Line breakpoints inside `test "name" { ... }` blocks — they appear valid (solid red dot) but never fire
- This affects both vsdbg and netcoredbg in DAP mode
- netcoredbg's CLI mode CAN hit these breakpoints (used by `verifrog debug-dap`), but DAP mode cannot

**Recommended workflow**: Use the interactive debugger (`verifrog debug`) or JSON/MCP server for simulation debugging. Use VS Code for test execution, syntax highlighting, and signal inspection.

## Troubleshooting

### "Unable to find libverifrog_sim"
Run `verifrog build` first. The `DYLD_LIBRARY_PATH` / `LD_LIBRARY_PATH` in launch.json must point to the directory containing the built library.

### Tests run but skip all
Expecto uses `--filter-test-case` for substring matching, not `--filter`. Check your test names with `dotnet run --project tests/ -- --list-tests`.

### Debug server exits immediately
Make sure `verifrog.toml` exists and the sim library is built. The server emits a `{"status":"ready"}` message on startup — if you don't see it, check stderr for errors.
