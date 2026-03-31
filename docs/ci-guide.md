# CI Integration Guide

How to run Verifrog tests in continuous integration. Covers GitHub Actions and GitLab CI, with caching strategies and test result publishing.

## Overview

A Verifrog CI pipeline has three stages:

1. **Build** — Verilator compiles RTL into a shared library (`libverifrog_sim`)
2. **Test** — `verifrog test` runs the test suite with the library loaded
3. **Report** — Test results published as markdown or JUnit XML

The build step is the slowest (Verilator + C++ compilation), so caching it is critical.

## GitHub Actions

### Basic workflow

```yaml
# .github/workflows/test.yml
name: Test

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - name: Install .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Install Verilator
        run: |
          sudo apt-get update
          sudo apt-get install -y verilator

      - name: Build simulation library
        run: |
          export VERIFROG_ROOT=$PWD
          bin/verifrog build

      - name: Run tests
        run: |
          export VERIFROG_ROOT=$PWD
          bin/verifrog test --report test-results.md

      - name: Publish test report
        if: always()
        run: |
          echo "## Test Results" >> $GITHUB_STEP_SUMMARY
          echo "" >> $GITHUB_STEP_SUMMARY
          tail -n +2 test-results.md >> $GITHUB_STEP_SUMMARY 2>/dev/null || echo "No report generated." >> $GITHUB_STEP_SUMMARY
```

This publishes the markdown report directly to the GitHub Actions job summary — visible on the PR checks tab without clicking into logs.

### With Verilator build caching

Verilator compilation takes 10-60 seconds depending on design size. Cache it:

```yaml
      - name: Cache Verilator build
        uses: actions/cache@v4
        with:
          path: build
          key: verilator-${{ hashFiles('rtl/**', 'verifrog.toml', 'path/to/verifrog/src/shim/**') }}

      - name: Build simulation library
        run: |
          export VERIFROG_ROOT=$PWD
          bin/verifrog build
```

The cache key hashes your RTL sources, TOML config, and the C++ shim. When any of these change, the cache misses and a fresh build runs. Otherwise, the cached `libverifrog_sim.so` is restored in seconds.

### With test artifacts

Upload the JUnit XML and markdown report as downloadable artifacts:

```yaml
      - name: Upload test artifacts
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: |
            test-results.md
            test-results.xml
```

### Running specific categories

Run smoke tests on every push, full suite only on main:

```yaml
jobs:
  smoke:
    runs-on: ubuntu-latest
    steps:
      # ... setup steps ...
      - name: Smoke tests
        run: bin/verifrog test --category Smoke --report smoke-results.md

  full:
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      # ... setup steps ...
      - name: Full test suite
        run: bin/verifrog test --report test-results.md
```

### macOS runners

For macOS CI (e.g., Apple Silicon builds):

```yaml
jobs:
  test-macos:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v4

      - name: Install .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Install Verilator
        run: brew install verilator

      - name: Build and test
        run: |
          export VERIFROG_ROOT=$PWD
          bin/verifrog test --report test-results.md
```

Note: `DYLD_LIBRARY_PATH` is handled automatically by `verifrog test`.

## GitLab CI

```yaml
# .gitlab-ci.yml
stages:
  - build
  - test

variables:
  VERIFROG_ROOT: $CI_PROJECT_DIR

build:
  stage: build
  image: mcr.microsoft.com/dotnet/sdk:8.0
  before_script:
    - apt-get update && apt-get install -y verilator g++
  script:
    - bin/verifrog build
  artifacts:
    paths:
      - build/
  cache:
    key:
      files:
        - rtl/**/*
        - verifrog.toml
    paths:
      - build/

test:
  stage: test
  image: mcr.microsoft.com/dotnet/sdk:8.0
  dependencies:
    - build
  script:
    - bin/verifrog test --report test-results.md
  artifacts:
    when: always
    paths:
      - test-results.md
      - test-results.xml
    reports:
      junit: test-results.xml
```

GitLab natively renders JUnit XML in merge request widgets when specified under `reports.junit`.

## Library path handling

The `verifrog test` script handles library paths automatically:

| Platform | Variable | Set by script |
|----------|----------|---------------|
| macOS | `DYLD_LIBRARY_PATH` | Yes |
| Linux | `LD_LIBRARY_PATH` | Yes |

If you're running tests manually in CI (without the wrapper script):

```bash
# Linux
LD_LIBRARY_PATH=build dotnet run --project tests/

# macOS
DYLD_LIBRARY_PATH=build dotnet run --project tests/
```

## Caching strategy

### What to cache

Cache the entire build output directory (`build/` by default). It contains:
- `libverifrog_sim.so` / `.dylib` — the simulation library
- `verilated/` — Verilator intermediate C++ files
- `verifrog_model.h` — generated model binding

### Cache key design

Hash the inputs that affect the build:

| Input | Why |
|-------|-----|
| `rtl/**` | RTL source changes → rebuild |
| `verifrog.toml` | Config changes (top module, flags) → rebuild |
| `src/shim/**` | Shim code changes → rebuild |

```yaml
# GitHub Actions
key: verilator-${{ hashFiles('rtl/**', 'verifrog.toml') }}

# GitLab CI
cache:
  key:
    files:
      - rtl/**/*
      - verifrog.toml
```

### Cache miss behavior

When the cache misses, `verifrog build` runs a full Verilator + C++ compilation. When it hits, the build step is a no-op (library already exists). `verifrog test` also auto-builds if the library is missing, so the build step is technically optional with caching — but making it explicit keeps logs clear.

## Test result publishing

### Markdown report to GitHub Actions summary

The `--report` flag generates a markdown file. Append it to `$GITHUB_STEP_SUMMARY` for in-page rendering:

```yaml
- run: |
    bin/verifrog test --report test-results.md
- if: always()
  run: cat test-results.md >> $GITHUB_STEP_SUMMARY
```

### JUnit XML to CI systems

`verifrog test --report` also generates `test-results.xml` (JUnit format). Most CI systems can consume this:

- **GitHub Actions**: Use a third-party action like `dorny/test-reporter` or `mikepenz/action-junit-report`
- **GitLab CI**: Add `reports: junit: test-results.xml` to the job artifacts
- **Jenkins**: Use the JUnit plugin with the XML path

### Converting existing XML

If you have JUnit XML from a previous run:

```bash
verifrog results test-results.xml -o report.md
```

## Troubleshooting CI

### `verilator: command not found`

Install Verilator in the CI environment:

```yaml
# Ubuntu
- run: sudo apt-get update && sudo apt-get install -y verilator

# macOS
- run: brew install verilator
```

### `libverifrog_sim.so: cannot open shared object file`

The library path isn't set. Use `verifrog test` (handles it automatically) or set it manually:

```bash
export LD_LIBRARY_PATH=build    # Linux
export DYLD_LIBRARY_PATH=build  # macOS
```

### Tests pass locally but fail in CI

Common causes:
1. **Different Verilator version** — pin the version in CI or use a Docker image
2. **Stale cache** — clear the CI cache and rebuild
3. **macOS vs Linux** — if your design uses platform-specific behavior, test on both

### Build is slow in CI

- Enable caching (see above)
- Use a larger runner for the build step
- Split build and test into separate jobs with artifact passing
