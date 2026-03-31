# Track: Declarative Test Format

Allow hardware tests to be written declaratively instead of in F#. Covers ~75% of typical block-level verification tests.

## Deliverables

1. **Test format** — A file format for expressing stimulus-check tests without F# code. Supports: write, step, expect, load, run-until, force, release, checkpoint, restore. Not TOML (too rigid for sequences). Likely YAML or a lightweight custom format.

2. **Parser and runner** — Reads declarative test files at test time, generates Expecto test cases. Coexists with F# tests in the same runner and report. Same `--category` and `--report` support.

3. **Error reporting** — Failures point back to the declarative file and line number, not generated code.

4. **Documentation and samples** — Guide for writing declarative tests, updated samples showing both declarative and F# tests side by side.

## Non-goals

- Full DSL with conditionals, loops, variables, or expressions. That's a separate effort.
- Replacing F# for complex tests. Error recovery, golden model comparison, and multi-inference workflows stay in F#.
- Inventing a new programming language. The format should be learnable in 5 minutes.

## Context

Analysis of khalkulo (199 tests) and verifrog samples (11 sim tests) shows:
- 34% of tests are pure write/step/check (declarative today)
- 44% would be declarative with loops for data loading and run-until
- 22% genuinely need code (error paths, golden models, computed values)

See issue #3 for the full analysis.
