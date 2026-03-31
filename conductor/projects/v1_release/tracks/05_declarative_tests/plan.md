# Track Plan: Declarative Test Format

## Phase 1: Format Design

Decide on the file format and nail down the syntax before writing any code.

- [ ] Evaluate format options: YAML, custom line-oriented format, or something else
  - YAML: familiar, good tooling, but verbose for simple cases and has foot-guns (Norway problem, implicit typing)
  - Custom format: concise, purpose-built, but no existing parser/editor support
  - Consider: can we get 90% of the value with YAML and a strict schema?
- [ ] Define the 9 core primitives: write, step, expect, load, run-until, force, release, checkpoint, restore
- [ ] Define test metadata: name, category, description
- [ ] Define how tests reference verifrog.toml (for memory/register names)
- [ ] Write 10+ example test files covering the range of declarative-capable tests from khalkulo
- [ ] Get feedback on the format before implementing

## Phase 2: Parser

- [ ] Implement parser for the chosen format (F# module in Verifrog.Runner)
- [ ] Parse into an intermediate representation (list of test steps)
- [ ] Validate: unknown signals, bad memory names, type errors
- [ ] Good error messages with file/line references

## Phase 3: Runner Integration

- [ ] Generate Expecto test cases from parsed declarative tests
- [ ] Support `--category` filtering (categories declared in the test file)
- [ ] Support `--report` (declarative tests appear in markdown/JUnit output)
- [ ] Coexist with F# tests in the same test project — both run under one `verifrog test`
- [ ] `verifrog test` auto-discovers `.vftest` (or whatever extension) files

## Phase 4: Error Reporting

- [ ] Failures reference the declarative file name and line number
- [ ] Show the expected vs actual value, signal name, and cycle — same quality as Expect.signal
- [ ] If a signal name doesn't exist, fail at parse time (not runtime) with a clear message

## Phase 5: Docs and Samples

- [ ] Writing declarative tests guide
- [ ] Update counter sample with a declarative test file alongside the F# tests (keep both — don't replace F#)
- [ ] Side-by-side examples in docs: every declarative example should show the equivalent F# so users can see both approaches and choose
- [ ] Update cookbook with declarative patterns (paired with F# equivalents)
- [ ] Update README to mention declarative testing

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
