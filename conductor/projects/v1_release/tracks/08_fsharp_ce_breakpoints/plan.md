# Track Plan: Fix F# CE Breakpoints in netcoredbg DAP Mode

## Repo locations

- **netcoredbg fork**: `/Users/bryancostanich/Git_Repos/bryan_costanich/netcoredbg/`
  - origin: `bryancostanich/netcoredbg`
  - upstream: `Samsung/netcoredbg`
- **verifrog**: `/Users/bryancostanich/Git_Repos/bryan_costanich/verifrog/`
- **khalkulo**: `/Users/bryancostanich/Git_Repos/bryan_costanich/khalkulo/`

## Phase 1: Minimal Repro

Create a standalone reproduction case with zero verifrog dependency. This is what we'll attach to the upstream issue.

- [x] Create `repro/fsharp-ce-breakpoints/` in the netcoredbg fork
- [x] Scaffold minimal F# console project (net8.0 + Expecto 10.2.3)
- [x] Write minimal test file (`Program.fs`) with:
  - One `test "name" { ... }` CE block (lines 16-21) with `let`, `printfn`, `Expect.equal`
  - One regular function (lines 7-11) with the same pattern
  - Entry point that runs both
- [x] Verify breakpoint behavior:
  - **CLI mode** (`test-cli.sh`): `break Program.fs:17` **slides to line 15** (`.cctor()`), fires in module initializer, NOT the CE body. Previously thought CLI mode worked — it doesn't for CE bodies either.
  - **DAP mode** (`test-dap.py`): `setBreakpoints` line 17 returns `verified=False` (pending), resolves after module load, **slides to line 15** (`.cctor()`). Line 8 (regular function) fires correctly.
  - **PDB verified**: `ceTests@17.Invoke` (token `0x0600000D`) has correct sequence points at lines 17-20. The data is in the PDB — `GetMethodTokensByLineNumber()` doesn't find the closure class.
- [x] Write `README.md` with exact reproduction steps and PDB analysis
- [ ] Test on Linux x86_64 (not yet done, macOS arm64 confirmed)

## Phase 2: File Upstream Issue

- [x] Filed Samsung/netcoredbg#221: "F# computation expression line breakpoints don't fire (closure class methods not searched)"
  - Includes DAP and CLI transcripts, PDB analysis, root cause analysis, and link to repro project
  - Updated title to reflect that both modes are affected (not just DAP)
- [ ] Link from verifrog issue #9

## Phase 3: Root Cause Investigation

Instrument netcoredbg to trace exactly where the DAP and CLI paths diverge for F# CE closures. This phase produces a root cause document — no code changes to netcoredbg yet.

### Root cause (completed 2026-04-03)

Instrumented `AddMethodData` and `GetMethodTokensByLineNumber` in `modules_sources.cpp`. The F# closure IS correctly nested in the method data:

- Level 0: `.cctor` (0x0E, line 15-21) — the module initializer's SP spans the entire `let ceTests = test "..." { ... }` block
- Level 1: `ceTests@17.Invoke` (0x0D, line 17-20) — correctly nested inside .cctor

`GetMethodTokensByLineNumber` for line 17 correctly returns:
- `Tokens` = [.cctor]
- `closestNestedToken` = ceTests@17.Invoke

The bug is in the **managed resolver** `SymbolReader.ResolveBreakPoints()` at line 812-816:

```csharp
// When nested method is fully contained within outer SP, use OUTER method
if ((nested_start_p.StartLine > current_p.StartLine || ...) &&
    (nested_end_p.EndLine < current_p.EndLine || ...))
{
    list.Add(new resolved_bp_t(current_p.StartLine, ..., methodToken));  // <-- picks .cctor
    break;
}
```

This was designed for C# `await Parallel.ForEachAsync(... async () => ...)` where the user sets a breakpoint on the outer call line and the lambda is nested. But for F# CEs, the outer `.cctor` SP spans the entire expression (lines 15-21), so the closure (17-20) is always "fully contained," and the code unconditionally picks the outer method.

**Fix**: when `sourceLine >= nested_start_p.StartLine`, the target is inside the closure body — use the nested method. Only pick outer when `sourceLine < nested_start_p.StartLine`.

**Secondary issue**: `NestedInto()` assertion at `modules_sources.h:68` fails in Debug builds when loading FSharp.Core.dll, because some F# compiler-generated methods share start positions. This is a separate bug but it blocks Debug-mode testing. In Release mode, the assertion is compiled out.

## Phase 4: Fix Implementation

Work on our fork (`bryancostanich/netcoredbg`, branch `fix/fsharp-ce-breakpoints`).

- [ ] Create branch `fix/fsharp-ce-breakpoints` from upstream main
- [ ] Implement fix based on Phase 3 findings. Likely scenarios:

  **If timing/deferred resolution** (hypothesis 1):
  - Fix `ManagedCallbackLoadModule()` to properly re-resolve breakpoints against nested/closure methods
  - May need to re-run `GetMethodTokensByLineNumber()` with updated method ranges after module load
  - Ensure `closestNestedToken` is populated during deferred resolution

  **If JMC filtering** (hypothesis 2):
  - Fix `SkipBreakpoint()` or the JMC setup during module load to recognize F# CE closure classes as user code
  - The runtime sets JMC status based on `DebuggableAttribute` and `DebuggerNonUserCode` — check what the F# compiler emits on closure classes
  - May need to walk up to the containing type and inherit JMC status

  **If method token enumeration** (hypothesis 3):
  - Fix `GetPdbMethodsRanges()` or `FillSourcesCodeLinesForModule()` to include closure class methods in `methodsData`
  - Ensure nested method levels properly capture closure classes, not just syntactically nested functions
  - The `closestNestedToken` logic in `GetMethodTokensByLineNumber()` may need to be extended

  **If sequence point mismatch** (hypothesis 4):
  - Fix path comparison in `SequencePointForSourceLine()` (`SymbolReader.cs:762`)
  - May need path normalization or case-insensitive comparison
  - Or fix document handle resolution in `GetSequencePointCollection()`

- [x] Build netcoredbg from source (Debug mode)
- [x] Test with the Phase 1 repro project — **CE breakpoints fire in DAP mode**
  - Stock netcoredbg: line 17 slides to line 15 (.cctor) — bug confirmed
  - Fixed build: line 17 fires in `ceTests@17.Invoke()` — fix works
- [x] Regression: non-CE F# breakpoints (line 8, regularFunction) still work
- [ ] Test with verifrog counter sample — breakpoints in `test "..." { ... }` blocks
- [ ] Test with khalkulo tests — breakpoints in integration test CE blocks
- [ ] Regression: confirm C# breakpoints still work (netcoredbg's primary use case)
- [ ] Regression: run netcoredbg's own test suite (`cmake --build . --target check`)

## Phase 5: Upstream PR

- [ ] Clean up diagnostic logging (remove or gate behind `--log` flag)
- [ ] Write clear commit message explaining:
  - What: F# CE breakpoints don't fire in DAP mode
  - Why: (root cause from Phase 3)
  - How: (summary of fix)
  - Test: reference the repro project
- [ ] Submit PR to Samsung/netcoredbg
- [ ] Link PR from verifrog issue #9 and the upstream issue from Phase 2
- [ ] If PR is accepted, update verifrog's netcoredbg build instructions to reference the fixed version
- [ ] If PR is slow to merge, publish our fork build as a stopgap and document in verifrog's debug guide

## Key Files in netcoredbg

| File | What | Why it matters |
|------|------|----------------|
| `src/protocols/vscodeprotocol.cpp:571` | DAP `setBreakpoints` entry | Where DAP requests arrive |
| `src/protocols/cliprotocol.cpp:1310` | CLI `break` entry | Where CLI commands arrive |
| `src/debugger/breakpoints_line.cpp:356` | `SetLineBreakpoints()` | Convergence point — both paths land here |
| `src/debugger/breakpoints_line.cpp:137` | `ResolveLineBreakpoint()` | Calls into module/source resolution |
| `src/debugger/breakpoints_line.cpp:179` | `ActivateLineBreakpoint()` | Creates ICorDebug breakpoints, JMC filter |
| `src/debugger/breakpoints_line.cpp:232` | `ManagedCallbackLoadModule()` | Deferred breakpoint resolution on module load |
| `src/metadata/modules_sources.cpp:134` | `GetMethodTokensByLineNumber()` | Finds method tokens containing a source line |
| `src/metadata/modules_sources.cpp:709` | `ModulesSources::ResolveBreakpoint()` | Main resolution logic |
| `src/metadata/modules_sources.cpp:210` | `GetPdbMethodsRanges()` | Loads method ranges from PDB (are closures included?) |
| `src/managed/SymbolReader.cs:725` | `ResolveBreakPoints()` | Managed-side PDB sequence point lookup |
| `src/managed/SymbolReader.cs:749` | `SequencePointForSourceLine()` | Finds sequence point at/after source line |
| `src/debugger/breakpointutils.cpp:107` | `SkipBreakpoint()` | JMC + DebuggerHidden filter |
