# Track Plan: Declarative Test Format

## Phase 1: Format Design

Decide on the file format and nail down the syntax before writing any code.

- [x] Evaluate format options — chose custom line-oriented format over YAML (much more concise, reads like pseudocode)
- [x] Define the 9 core primitives: write, step, expect, load, load-from-file, run-until, force, release, checkpoint, restore
- [x] Define test metadata: name, category via `test "name" [Category]:`
- [x] Define memory expect syntax: `expect mem[bank][addr] == value`
- [x] Reference format example in tracks/05_declarative_tests/format_examples/reference.verifrog

## Phase 2: Parser

- [x] Implement parser for the chosen format (Declarative.fs in Verifrog.Runner)
- [x] Parse into AST (DeclTest with Step list)
- [x] Good error messages with file/line references
- [x] Validate: unknown signals and memory names checked before tests run (validation test runs first, reports all errors with file:line)

## Phase 3: Runner Integration

- [x] Generate Expecto test cases from parsed declarative tests (toExpectoTest)
- [x] Support `--category` filtering (categories from test headers map to testList groups)
- [x] Support `--report` (declarative tests appear in markdown/JUnit output as "Declarative" suite)
- [x] Coexist with F# tests — 30 F# + 8 declarative = 38 total, all pass
- [x] Auto-discovers `.verifrog` files in test directories

## Phase 4: Error Reporting

- [x] Failures reference the declarative file name and line number
- [x] Show expected vs actual value with hex, signal name — same quality as Expect.signal
- [x] If a signal name doesn't exist, validation test fails before other tests run, lists all bad references

## Phase 5: Docs and Samples

- [x] Writing declarative tests guide (docs/declarative-tests.md — full format reference with side-by-side examples)
- [x] Counter sample has declarative test file alongside F# tests (both preserved)
- [x] Side-by-side examples in docs: every declarative example shows equivalent F#
- [x] Cookbook updated with declarative patterns (paired with F# equivalents)
- [x] README updated with declarative section, docs hub updated, doc table updated

## Open Questions

- File extension: `.vftest`? `.verifrog`? `.test.yaml`?
  - .verifrog
- Should declarative tests live in the same `tests/` directory as F# tests, or a separate `tests/declarative/` directory?
  - same tests/ directory
- Should `verifrog init` scaffold a sample declarative test file?
  - yes
- How to handle hex values in the format? `0xFF` or `255` or both?
  - parse either.
- Should `load` support file references for large data sets? e.g., `load weight_sram from weights.hex`
  - yes. use $readmemh format (RTL designers already know it). inline data for small sets, file ref for large.
- Should `expect` support ranges or masks? e.g., `expect status & 0x01 == 1`
  - v1: just `==` and `!=`. masks and bit indexing deferred to v2 if needed.
