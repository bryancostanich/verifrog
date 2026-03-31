# Track Plan: Polish

## Phase 1: Test Categorization

Expecto's `testList` naming works but doesn't give formal filter tags that match hardware verification domains. Need a lightweight tagging system.

- [ ] Define category taxonomy: Smoke, Unit, Parametric, Integration, Stress, Golden, Regression
- [ ] Implement as Expecto test attributes or wrapper (evaluate `testCategory` helper vs custom attributes)
- [ ] Document how consumers define and filter by category
- [ ] Update samples to demonstrate categorized tests

## Phase 2: CI Integration

- [ ] GitHub Actions workflow example (build Verilator model, run tests, publish results)
- [ ] Library path setup guide (DYLD_LIBRARY_PATH on macOS, LD_LIBRARY_PATH on Linux)
- [ ] Verilator build caching strategy (hash RTL sources → cache key)
- [ ] Test result publishing (TRX → GitHub Actions summary, or markdown report)

Note: The markdown test report generator already exists (`verifrog test` produces it). This phase is about integrating that into CI pipelines.
