# CLI Reference

The Verifrog CLI (`verifrog`) manages project setup, builds, and cleanup. It reads `verifrog.toml` for configuration and drives Verilator to produce the simulation shared library.

## Usage

```bash
# From any directory with VERIFROG_ROOT set:
dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- <command> [args]
```

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
# Initialize in the current directory
dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- init .

# Initialize in a new directory
dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- init my_project
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
# Build from current directory (reads ./verifrog.toml)
dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- build

# Build a specific project
dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- build samples/counter

# Build with custom Verilator flags (set in verifrog.toml)
# [verilator]
# flags = ["--trace", "-Wno-fatal", "--threads", "4"]
```

**Output:**

```
[verifrog] Reading config: verifrog.toml
[verifrog] Top module: my_counter
[verifrog] Sources: rtl/my_counter.v
[verifrog] Running Verilator...
[verifrog] Compiling shim...
[verifrog] Built: build/libverifrog_sim.dylib
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
# Clean current project
dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- clean

# Clean a specific project
dotnet run --project $VERIFROG_ROOT/src/Verifrog.Cli -- clean samples/counter
```

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

## Running tests after build

The shared library must be on the library path when running tests:

```bash
# macOS
DYLD_LIBRARY_PATH=build dotnet test tests/

# Linux
LD_LIBRARY_PATH=build dotnet test tests/
```

## Environment

| Variable | Purpose |
|----------|---------|
| `VERIFROG_ROOT` | Path to the Verifrog repository. Used by `.fsproj` references. |
| `DYLD_LIBRARY_PATH` | (macOS) Directory containing `libverifrog_sim.dylib` |
| `LD_LIBRARY_PATH` | (Linux) Directory containing `libverifrog_sim.so` |
