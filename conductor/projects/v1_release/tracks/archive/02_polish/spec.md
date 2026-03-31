# Track: Polish

Framework improvements for test organization, reporting, and CI readiness.

## Deliverables

1. **Test categorization system** — Domain-specific test tags (Smoke, Unit, Parametric, Integration, Stress, Golden) that can be used for filtering beyond Expecto's testList naming. The built-in .NET test categories don't match hardware verification domains.

2. **CI integration guide** — Documentation and examples for running verifrog tests in CI (GitHub Actions, GitLab CI). Includes DYLD_LIBRARY_PATH/LD_LIBRARY_PATH setup, caching the Verilator build, and test result publishing.
