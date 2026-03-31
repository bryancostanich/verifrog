# Troubleshooting

Common errors and how to fix them.

## Build errors

### `verilator: command not found`

Verilator 5+ is not installed or not in your PATH.

```bash
# macOS (gives 5.x)
brew install verilator

# Linux — Ubuntu apt has 4.x which is too old. Build from source:
git clone https://github.com/verilator/verilator.git
cd verilator && git checkout stable
autoconf && ./configure && make -j$(nproc) && sudo make install

# Verify
verilator --version   # Must show 5.x+
```

See the [CI Integration Guide](ci-guide.md) for cached Verilator builds in CI.

### `%Error: Can't find file containing module 'my_module'`

The `[design].top` in `verifrog.toml` doesn't match any module in the source files.

**Check:**
- The module name in your Verilog matches `top` exactly (case-sensitive)
- The source file paths in `[design].sources` are correct and relative to `verifrog.toml`
- Glob patterns actually match your files

```bash
# Verify sources resolve
ls rtl/*.v
```

### `%Error: ... syntax error`

Your Verilog has a syntax error that Verilator catches.

```bash
# Run Verilator lint-only to see the full error
verilator --lint-only rtl/my_module.v
```

### `clang++: error: no such file or directory: 'verifrog_sim.cpp'`

The shim source file wasn't found. Make sure `VERIFROG_ROOT` is set correctly:

```bash
echo $VERIFROG_ROOT
ls $VERIFROG_ROOT/src/shim/verifrog_sim.cpp
```

### `ld: library not found for -lverilated`

Verilator's support library isn't in the expected location. Check your Verilator installation:

```bash
verilator --getenv VERILATOR_ROOT
ls $(verilator --getenv VERILATOR_ROOT)/include/
```

## Runtime errors

### `sim_create() returned null — library failed to load`

The shared library wasn't found at runtime. Check the library path:

```bash
# macOS
DYLD_LIBRARY_PATH=build dotnet test tests/

# Linux
LD_LIBRARY_PATH=build dotnet test tests/
```

Make sure the library exists:

```bash
ls build/libverifrog_sim.dylib   # macOS
ls build/libverifrog_sim.so      # Linux
```

### `Signal not found: my_signal`

The signal name you're using doesn't match any signal in the design.

**Debug steps:**

1. List all available signals:
   ```fsharp
   let signals = sim.ListSignals()
   for s in signals do printfn "%s" s
   ```

2. Check if you need the hierarchical path:
   ```fsharp
   // These might differ:
   sim.Read("count")              // leaf name
   sim.Read("counter.count")      // full path
   ```

3. Verilator renames signals internally (e.g., `my_module__DOT__sub__DOT__sig`). Use `ListSignals()` to find the actual registered name.

### `Memory not found in config: xxx. Available: ...`

The memory name doesn't match what's in `verifrog.toml`.

```fsharp
// Check what's available
printfn "%A" sim.MemoryNames
```

Same applies to `Register not found in config`.

### `Memory read/write failed: ... (path=...)`

The TOML path template resolved to a signal path that doesn't exist in the design.

**Check:**
- The `path` in `[memories.xxx]` matches the actual Verilog hierarchy
- Bank indices are correct (`{bank}` expands to 0..banks-1)
- The Verilog memory is declared with `--public-flat-rw` visible arrays

**Use `ValidateSignals()` to catch this early:**
```fsharp
let errors = sim.ValidateSignals()
for e in errors do eprintfn "%s" e
```

### `sim_checkpoint() failed`

Checkpoint creation failed. This is rare but can happen if:
- The model is in a bad state (e.g., after a failed restore)
- Memory allocation failed (very large model)

### Tests pass locally but fail in CI

**Common causes:**

1. **Missing library path**: CI doesn't have `DYLD_LIBRARY_PATH`/`LD_LIBRARY_PATH` set
2. **Different Verilator version**: Different versions may produce slightly different models
3. **macOS SIP**: System Integrity Protection strips `DYLD_LIBRARY_PATH` from some contexts. Copy the library to a standard location or use `@rpath`.

## TOML configuration errors

### `Config.parse` fails with unclear error

Common TOML mistakes:

```toml
# Wrong: quotes around integers
depth = "256"

# Right: bare integers
depth = 256

# Wrong: missing quotes on string
top = my_module

# Right: quoted string
top = "my_module"

# Wrong: sources as a string
sources = "rtl/*.v"

# Right: sources as an array
sources = ["rtl/*.v"]
```

### Memories or registers not showing up

Make sure you're using `Sim.Create(tomlPath)` or `SimFixture.createFromToml(path)`, not `Sim.Create()`:

```fsharp
// This won't have memory/register access:
use sim = Sim.Create()

// This will:
use sim = Sim.Create("verifrog.toml")
// or
use sim = SimFixture.createFromToml "verifrog.toml"
```

## VCD parser errors

### `Error: file not found`

The VCD file path is wrong, or the simulation didn't produce a dump.

**Check:**
- Your design has `$dumpfile` / `$dumpvars`, or you're using Verilator's `--trace` flag
- The test output directory matches where you're looking for the file
- The `[test].test_output` in `verifrog.toml` is set correctly

### Empty transitions / no data

If `VcdParser.transitions` returns an empty list:

1. The signal name might not match — use `findSignals` to search:
   ```fsharp
   let matches = VcdParser.findSignals vcd "count"
   for s in matches do printfn "%s" s.FullPath
   ```

2. With `parseFiltered`, the signal might not match your patterns

3. The signal might genuinely have no transitions (constant value)

### Large VCD files are slow or use too much memory

Use filtering and time limits:

```fsharp
// Don't do this for large files:
let vcd = VcdParser.parseAll "huge.vcd"

// Do this instead:
let vcd = VcdParser.parseFiltered "huge.vcd" ["signal_i_care_about"] 1_000_000L
```

## Iverilog errors

### `iverilog: command not found`

```bash
brew install icarus-verilog   # macOS
apt install iverilog           # Linux
```

### `Unknown module type: xxx`

A module instantiated in the testbench isn't in the compile list. Check that `[iverilog].models` includes all supporting files (BFMs, SRAM models, etc.).

### Test passes in iverilog but fails in Verilator (or vice versa)

Verilator is cycle-based (no delays). Iverilog is event-driven (honors `#delays`). Common differences:
- Combinational loops may resolve differently
- `#0` delays in iverilog can reorder events
- Verilator may optimize away signals that iverilog keeps

This is expected — the two backends test different properties.

## Platform-specific issues

### macOS: `dyld: Library not loaded`

```bash
# Set the library path
export DYLD_LIBRARY_PATH=build

# Or copy the library
cp build/libverifrog_sim.dylib /usr/local/lib/
```

### Linux: `error while loading shared libraries`

```bash
export LD_LIBRARY_PATH=build
# Or install system-wide
sudo cp build/libverifrog_sim.so /usr/local/lib/
sudo ldconfig
```

### Apple Silicon: `mach-o file, but is an incompatible architecture`

You built the library on x86_64 but are running on ARM (or vice versa). Rebuild:

```bash
verifrog clean
verifrog build
```
