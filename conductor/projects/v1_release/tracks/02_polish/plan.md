# Track Plan: Polish

## Phase 1: Test Categorization

Expecto's `testList` naming works but doesn't give formal filter tags that match hardware verification domains. Need a lightweight tagging system.

- [x] Define category taxonomy: Smoke, Unit, Parametric, Integration, Stress, Golden, Regression
- [x] Implement as testList wrappers in Category.fs (Verifrog.Runner)
- [x] Add `--category` flag to verifrog test wrapper (maps to Expecto --filter-test-list)
- [x] Document in API reference, cookbook, and CLI reference
- [x] Update framework tests to demonstrate categorized tests

## Phase 2: CI Integration

- [x] GitHub Actions workflow example (build Verilator model, run tests, publish results)
- [x] GitLab CI example with JUnit integration
- [x] Library path setup guide (DYLD_LIBRARY_PATH on macOS, LD_LIBRARY_PATH on Linux)
- [x] Verilator build caching strategy (hash RTL sources → cache key)
- [x] Test result publishing (markdown to GitHub Actions summary, JUnit XML to GitLab MR widgets)

Note: The markdown test report generator already exists (`verifrog test` produces it). This phase is about integrating that into CI pipelines.
