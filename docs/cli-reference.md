# CLI Reference

The Verifrog CLI (`verifrog`) manages project setup, builds, cleanup, testing, debugging, and VCD analysis. It reads `verifrog.toml` for configuration and drives Verilator to produce the simulation shared library.

## Installation

After cloning the repo, run the install script to make `verifrog` available on your PATH:

```bash
./install.sh              # Symlinks to /usr/local/bin (default)
./install.sh --profile    # Or add bin/ to PATH in your shell profile
./install.sh --uninstall  # Remove symlinks
```

## Usage

```bash
verifrog <command> [args]
```

The `verifrog` wrapper script auto-detects `VERIFROG_ROOT` from its own location and handles library paths automatically.

> **Without install**: You can use the long form: `dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- <command> [args]`

## Commands

### `init` — Scaffold a new project

```bash
verifrog init <target-dir>
```

Creates the minimal files needed to start writing Verifrog tests in the target directory.

**What it creates:**

```
<target-dir>/
  verifrog.toml        # Design configuration template
  tests/
    Tests.fs           # Sample test file with working examples
    Tests.fsproj       # F# project with Verifrog references
```

**Example:**

```bash
verifrog init .             # Initialize in the current directory
verifrog init my_project    # Initialize in a new directory
```

After init, edit `verifrog.toml` to point at your RTL source files and set the top module name.

### `build` — Compile RTL into simulation library

```bash
verifrog build [<project-dir>]
```

Reads `verifrog.toml` from the project directory (default: current directory), compiles the RTL through Verilator, and links the simulation shared library.

**What it does:**

1. Reads `[design].top` and `[design].sources` from `verifrog.toml`
2. Runs Verilator:
   ```
   verilator --cc --public-flat-rw --trace --top-module <top> <sources> <extra-flags>
   ```
3. Generates `build/verifrog_model.h` — a typedef binding the generic shim to your design's Verilator classes
4. Compiles the C++ shim with Verilator's output:
   ```
   clang++ -std=c++17 -O2 -fPIC verifrog_sim.cpp <verilated sources>
   ```
5. Links into a shared library: `build/libverifrog_sim.dylib` (macOS) or `.so` (Linux)

**Example:**

```bash
verifrog build                  # Build from current directory
verifrog build samples/counter  # Build a specific project
```

**Output:**

```
Building verifrog.toml (top=my_counter)
  Verilating my_counter...
  Built: build/libverifrog_sim.dylib
```

**Common build errors:**

| Error | Cause | Fix |
|-------|-------|-----|
| `verilator: command not found` | Verilator not installed or not in PATH | Install Verilator 5+ |
| `%Error: Can't find file...` | Source file path in TOML doesn't exist | Check `[design].sources` paths |
| `%Error: Top module not found` | `[design].top` doesn't match any module | Verify module name matches exactly |
| `clang++: error: ...` | C++ compilation failure | Check Verilator version compatibility |

### `clean` — Remove build artifacts

```bash
verifrog clean [<project-dir>]
```

Removes the build directory and all generated artifacts.

**Example:**

```bash
verifrog clean                  # Clean current project
verifrog clean samples/counter  # Clean a specific project
```

### `test` — Build (if needed) and run tests

```bash
verifrog test [<project-dir>] [--report [path]] [-- dotnet-args...]
```

Finds `verifrog.toml`, auto-builds the simulation library if it doesn't exist, sets the library path (`DYLD_LIBRARY_PATH` on macOS, `LD_LIBRARY_PATH` on Linux), and runs the test project.

**Options:**

| Flag | Description |
|------|-------------|
| `--category <name>` | Run only tests in a category (Smoke, Unit, Parametric, Integration, Stress, Golden, Regression) |
| `--report` | Generate a Markdown test report (`test-results.md` in the project root) |
| `--report <path>` | Generate the report at a custom path |
| `-- <args>` | Pass remaining args to the test runner (e.g., `--filter`) |

**Example:**

```bash
verifrog test                             # Test current project
verifrog test samples/counter             # Test a specific project
verifrog test --category Smoke            # Run only smoke tests
verifrog test --category Unit --report    # Unit tests + markdown report
verifrog test --report                    # All tests + generate test-results.md
verifrog test --report results.md         # Custom report path
verifrog test -- --filter "checkpoint"    # Pass args to test runner
```

**Output:**

```
Running tests: Tests.fsproj
  Library: build/libverifrog_sim.dylib
  Report: test-results.md

EXPECTO! 12 tests run in 00:00:00.08 — 12 passed, 0 failed. Success!

Wrote test-results.md (12 tests, 2 suites)
```

**Generated markdown report:**

```markdown
# Test Results

**12 tests passed** in 80ms

| Suite | Passed | Failed | Errored | Skipped | Time |
|-------|-------:|-------:|--------:|--------:|-----:|
| Sim   |      8 |      0 |       0 |       0 | 45ms |
| Vcd   |      4 |      0 |       0 |       0 | 35ms |

## Sim

| Status | Test | Time |
|--------|------|-----:|
| PASS | checkpoint and restore | 11ms |
| PASS | create and reset | <1ms |
| PASS | force and release | 3ms |
...
```

### `results` — Convert JUnit XML to Markdown

```bash
verifrog results <junit.xml> [-o output.md]
```

Standalone converter: takes a JUnit XML file (produced by Expecto's `--junit-summary`) and generates a Markdown report. Useful if you already have XML from a CI run or want to re-generate the report.

**Example:**

```bash
verifrog results test-results.xml              # Print markdown to stdout
verifrog results test-results.xml -o report.md # Write to file
```

### `debug` — Interactive simulation debugger

```bash
verifrog debug [<project-dir>] [--script <path>]
```

Launches an interactive REPL where you can step the simulation, read/write signals, set checkpoints, force signals, and trace values — all from the command line. Optionally run a batch script.

**Example:**

```bash
verifrog debug                         # Interactive mode
verifrog debug samples/counter         # Debug a specific project
verifrog debug --script probe.txt      # Run a script
```

**Interactive session:**

```
verifrog debugger -- Interactive RTL simulation debugger
Type 'help' for commands, 'quit' to exit.
  45 signals registered. Cycle: 0

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
sim> trace count,overflow 5
  cycle  count  overflow
  11     11     0
  12     12     0
  13     13     0
  14     14     0
  15     15     0
sim> quit
```

See `help` in the debugger for the full command list (step, read, write, trace, watch, checkpoint, restore, force, release, run-until, signals, format, record).

### `vcd` — Analyze VCD waveform files

```bash
verifrog vcd <file.vcd> [max_time_ns] [--debug] [--signal <pattern>] [--json]
```

Parse and analyze VCD waveform dumps. See the [VCD CLI Reference](vcd-cli.md) for full details.

**Example:**

```bash
verifrog vcd output/sim.vcd
verifrog vcd output/sim.vcd --signal "fsm*" --debug
verifrog vcd output/sim.vcd --json
```

Also available as a standalone command: `verifrog-vcd`.

## Build output structure

After `verifrog build`, the output directory contains:

```
build/
  libverifrog_sim.dylib     # The simulation shared library (macOS)
  libverifrog_sim.so        # The simulation shared library (Linux)
  verifrog_model.h          # Generated model binding header
  verilated/                # Verilator intermediate files
    V<top>.h
    V<top>.cpp
    V<top>___024root.h
    ...
```

## Running tests

The simplest way to run tests is `verifrog test`, which handles everything automatically.

If you need to run tests manually (e.g., from an IDE), set the library path:

```bash
# macOS
DYLD_LIBRARY_PATH=build dotnet run --project tests/

# Linux
LD_LIBRARY_PATH=build dotnet run --project tests/
```

## Environment

| Variable | Purpose |
|----------|---------|
| `VERIFROG_ROOT` | Path to the Verifrog repository. Auto-detected by the `verifrog` script; only needed if running `dotnet` commands directly. |
| `DYLD_LIBRARY_PATH` | (macOS) Directory containing `libverifrog_sim.dylib`. Set automatically by `verifrog test`. |
| `LD_LIBRARY_PATH` | (Linux) Directory containing `libverifrog_sim.so`. Set automatically by `verifrog test`. |
