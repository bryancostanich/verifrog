# Track: Fix F# Computation Expression Breakpoints in netcoredbg DAP Mode

## Problem

Line breakpoints inside F# computation expression (CE) blocks — such as Expecto's `test "name" { ... }` — never fire when debugging via the Debug Adapter Protocol (DAP). Breakpoints show as verified (solid red in VS Code) but the debugger never stops. This affects both Microsoft's vsdbg and Samsung's netcoredbg.

The F# compiler transforms CE bodies into closure classes (e.g., `simTests@20-3.Invoke()`). The DAP `setBreakpoints` protocol fails to resolve source lines to the generated IL sequence points in these closure classes.

Critically, **netcoredbg's CLI `break` command works** — `break SimTests.fs:22` fires correctly when set after modules load. The CLI `break` command and DAP `setBreakpoints` are supposed to converge on the same resolution code (`LineBreakpoints::SetLineBreakpoints()`), so the divergence must be in timing, lifecycle, or a subtle path difference.

## Why This Matters

Every verifrog test is an Expecto CE block. Every khalkulo test is an Expecto CE block. With ~200 tests across both projects, the inability to set line breakpoints in test bodies means the entire VS Code debugging experience requires a workaround (function breakpoints on `ReadOrFail`). This is the single biggest gap in verifrog's debug integration.

This also affects every F# project using Expecto, Fake, or any CE-heavy framework — it's not verifrog-specific.

## Scope

1. **Minimal repro** — standalone F# + Expecto project that reproduces the issue, no verifrog dependency
2. **Upstream issue** — file on Samsung/netcoredbg with the repro
3. **Root cause** — trace through netcoredbg's breakpoint resolution to find exactly where DAP and CLI diverge for F# CE closures
4. **Fix** — implement and test on our fork (`bryancostanich/netcoredbg`)
5. **Upstream PR** — submit to Samsung/netcoredbg

## What We Know

### Codebase structure (netcoredbg, mixed C++/C#)

Both DAP and CLI entry points converge:
- **DAP**: `src/protocols/vscodeprotocol.cpp:571` → `SetLineBreakpoints()`
- **CLI**: `src/protocols/cliprotocol.cpp:1310` → `SetLineBreakpoints()`

Resolution pipeline:
```
LineBreakpoints::SetLineBreakpoints()     breakpoints_line.cpp:356
  → ResolveLineBreakpoint()               breakpoints_line.cpp:137
    → ModulesSources::ResolveBreakpoint()  modules_sources.cpp:709
      → GetMethodTokensByLineNumber()      modules_sources.cpp:134  (find methods containing line)
      → Interop::ResolveBreakPoints()      interop.cpp:432          (C++ → C# bridge)
        → SymbolReader.ResolveBreakPoints() SymbolReader.cs:725     (PDB sequence point lookup)
  → ActivateLineBreakpoint()               breakpoints_line.cpp:179
    → SkipBreakpoint()                     breakpointutils.cpp:107  (JMC + attribute filter)
    → pCode->CreateBreakpoint(ilOffset)    (ICorDebug API)
```

### Leading hypotheses

1. ~~**Timing**~~: **Eliminated.** Repro shows both CLI and DAP exhibit the same breakpoint sliding behavior. The apparent CLI success was the breakpoint sliding to the module initializer, not firing in the CE body. Timing is not the differentiator.

2. **JMC filtering**: Not yet tested, but less likely given that the breakpoint doesn't even resolve to the closure method token — it slides before JMC checks apply.

3. **Method token enumeration** ← **CONFIRMED as root cause area.** `GetMethodTokensByLineNumber()` uses method ranges loaded from PDB during `GetPdbMethodsRanges()`. The F# closure class `ceTests@17` is a nested type with its own method `Invoke` that has correct PDB sequence points at lines 17-20 (token `0x0600000D`). But `GetMethodTokensByLineNumber()` doesn't search closure/nested class methods, so the breakpoint slides to the nearest sequence point in the outer method (the static constructor at line 15).

4. **Sequence point mismatch**: Not applicable — the closure class sequence points reference the correct source file (`Program.fs`). The issue is that the method tokens are never even searched.

### Phase 1 findings (2026-04-03)

- **Both CLI and DAP** slide line 17 breakpoints to line 15 (`.cctor()`). Previous belief that CLI worked was incorrect — it hit the module initializer, not the CE body.
- PDB dump confirms `ceTests@17.Invoke` has correct sequence points:
  ```
  IL_0000  Program.fs:17 (col 9-20)
  IL_0003  Program.fs:18 (col 9-22)
  IL_0007  Program.fs:19 (col 9-36)
  IL_0025  Program.fs:20 (col 9-45)
  ```
- Regular function breakpoints (line 8) work correctly in both modes.
- The fix needs to make `GetMethodTokensByLineNumber()` search nested/closure class methods.
