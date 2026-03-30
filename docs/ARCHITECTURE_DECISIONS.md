# Architecture Decisions

Running log of non-obvious choices made during Verifrog development.

---

## AD-001: Direct pointer access via --public-flat-rw (Phase 1)

**Decision**: Use Verilator's `--public-flat-rw` flag with `VerilatedScope`/`VerilatedVar` API to discover signal pointers at init time, then read/write via direct pointer dereference. NOT VPI.

**Alternatives considered**:
1. **Hardcoded signal map** (khalkulo approach): `REG_ROOT` macros pointing to struct members. Fastest, but not generic.
2. **VPI (`--vpi`)**: Industry-standard. But VPI writes to input ports don't persist across `eval()` in Verilator — the internal copies get overwritten. Would require `vpiForceFlag` for all writes, making normal write semantics messy.
3. **`--public-flat-rw` + scope/var enumeration**: Populates a signal map automatically at init (like khalkulo's `register_signals`, but generic). Direct pointer access gives same performance as khalkulo's approach.

**Why direct pointers**: VPI writes don't persist for input ports in Verilator (tested and confirmed). The scope/var API gives us the same direct-pointer performance as khalkulo while being fully generic — signals are discovered automatically, not hardcoded.

**Critical subtlety**: Verilator creates both top-level port copies and internal module copies (e.g., `rootp->enable` vs `rootp->counter__DOT__enable`). Writing to the internal copy is useless — `eval()` overwrites it from the port. The signal map must prioritize top-level scope entries to ensure writes target the actual ports.

---

## AD-002: Checkpoint via model memcpy (Phase 1)

**Decision**: Implement checkpoint/restore by memcpy of the Verilator root model struct (`V<top>___024root`), plus a model-level `__VlSave`/`__VlRestore` approach if `--savable` is enabled.

**Alternatives considered**:
1. **Per-signal VPI save/restore**: Standard but extremely slow for large designs (iterating thousands of signals).
2. **`--savable` only**: Verilator's built-in serialization. Correct for all designs including generate-loop hierarchies, but writes to files (I/O overhead for in-memory checkpoints).
3. **Root struct memcpy**: Fast, in-memory, works for most designs. Misses state in separate submodule structs created by `generate` blocks with separate module definitions.

**Why memcpy + savable fallback**: Memcpy is the proven khalkulo approach and works for the majority of target designs (simple-to-moderate complexity). For complex designs with generate arrays that Verilator splits into separate classes, we document this limitation and recommend `--savable`. Phase 5 (`verifrog build`) will auto-detect and set the appropriate flag.

---

## AD-003: Build-time model binding via generated header (Phase 1)

**Decision**: The `verifrog build` step generates a tiny `verifrog_model.h` that typedefs the design's Verilator classes. The generic shim includes this header.

**Alternatives considered**:
1. **Preprocessor `-D` defines**: `-DVERIFROG_TOP=Vcounter`. Requires fragile token-pasting macros for `#include`.
2. **dlopen/dlsym at runtime**: Load the Verilator model as a plugin. Too complex, loses type safety.
3. **Generated header with typedef**: Clean, explicit, no preprocessor gymnastics.

**Why generated header**: Simple, readable, and debuggable. The build script writes one file; the shim includes it. No macro magic.

---

## AD-004: Project structure — src/ not lib/ (Phase 1)

**Decision**: Use `src/` as the root for all source code (shim, F# libraries, CLI).

**Alternatives considered**:
1. `lib/` for libraries, `bin/` for CLI — conventional in some ecosystems but splits related code.
2. `src/` for everything — matches .NET conventions and keeps the tree shallow.

**Why src/**: Matches `dotnet` ecosystem conventions. F# projects live in `src/ProjectName/`, which is what `dotnet new sln` expects.
