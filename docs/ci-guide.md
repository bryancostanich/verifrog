# CI Integration Guide

How to run Verifrog tests in continuous integration. Covers GitHub Actions and GitLab CI, with caching strategies and test result publishing.

## Overview

A Verifrog CI pipeline has three stages:

1. **Install Verilator 5+** — Required for `--public-flat-rw`. Ubuntu apt ships 4.x, so you need to build from source (cached after first run).
2. **Build** — Verilator compiles RTL into a shared library (`libverifrog_sim`)
3. **Test** — `verifrog test` runs the test suite with the library loaded
4. **Report** — Test results published as markdown or JUnit XML

Both the Verilator install and the simulation library build are cached, so most CI runs only execute the tests.

## GitHub Actions

### Recommended workflow

This is the workflow Verifrog uses for its own CI (`.github/workflows/test.yml`). It builds Verilator from source on first run, then caches it.

```yaml
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

      # Verilator 5+ required. Ubuntu apt has 4.x, so build from source and cache.
      - name: Cache Verilator install
        id: cache-verilator
        uses: actions/cache@v4
        with:
          path: /opt/verilator
          key: verilator-stable-v2

      - name: Build Verilator from source
        if: steps.cache-verilator.outputs.cache-hit != 'true'
        run: |
          sudo apt-get update
          sudo apt-get install -y git perl python3 make autoconf g++ flex bison ccache libgoogle-perftools-dev numactl perl-doc help2man libfl2 libfl-dev zlib1g zlib1g-dev
          git clone https://github.com/verilator/verilator.git /tmp/verilator
          cd /tmp/verilator
          git checkout stable
          autoconf
          ./configure --prefix=/opt/verilator
          make -j$(nproc)
          make install

      - name: Add Verilator to PATH
        run: |
          echo "/opt/verilator/bin" >> $GITHUB_PATH
          echo "VERILATOR_ROOT=/opt/verilator/share/verilator" >> $GITHUB_ENV

      - name: Install build tools
        run: sudo apt-get update && sudo apt-get install -y g++ make

      # Cache the per-design simulation library
      - name: Cache simulation build
        uses: actions/cache@v4
        with:
          path: build
          key: sim-${{ hashFiles('rtl/**', 'verifrog.toml', 'src/shim/**') }}

      - name: Build simulation library
        run: bin/verifrog build

      - name: Run tests
        run: bin/verifrog test --report

      - name: Publish test report
        if: always()
        run: |
          if [ -f test-results.md ]; then
            cat test-results.md >> $GITHUB_STEP_SUMMARY
          else
            echo "No test report generated." >> $GITHUB_STEP_SUMMARY
          fi

      - name: Upload test artifacts
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: |
            test-results.md
            test-results.xml
```

**First run**: ~5 minutes (building Verilator from source). **Subsequent runs**: ~30 seconds (cached Verilator + cached sim library, only tests execute).

The markdown report is published to the GitHub Actions job summary — visible on the PR checks tab without clicking into logs.

### Running specific categories

Run smoke tests on every push, full suite only on main:

```yaml
jobs:
  smoke:
    runs-on: ubuntu-latest
    steps:
      # ... setup steps (Verilator, .NET, build) ...
      - name: Smoke tests
        run: bin/verifrog test --category Smoke --report

  full:
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      # ... setup steps ...
      - name: Full test suite
        run: bin/verifrog test --report
```

### macOS runners

macOS has Verilator 5+ via Homebrew, so no build-from-source needed:

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
        run: bin/verifrog test --report
```

`DYLD_LIBRARY_PATH` is handled automatically by `verifrog test`.

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
    - apt-get update && apt-get install -y git perl python3 make autoconf g++ flex bison help2man libfl-dev zlib1g-dev
    - git clone https://github.com/verilator/verilator.git /tmp/verilator
    - cd /tmp/verilator && git checkout stable && autoconf && ./configure && make -j$(nproc) && make install
    - cd $CI_PROJECT_DIR
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
    - bin/verifrog test --report
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

`verifrog build` also generates a `.verifrog.env` file with the correct library path, useful for IDE integration:

```bash
$ cat .verifrog.env
LD_LIBRARY_PATH=/path/to/project/build
```

If you're running tests manually in CI (without the wrapper script), use absolute paths:

```bash
# Linux
LD_LIBRARY_PATH=$(pwd)/build dotnet run --project tests/

# macOS
DYLD_LIBRARY_PATH=$(pwd)/build dotnet run --project tests/
```

Both `dotnet run` (Expecto native output) and `dotnet test` (VS Test adapter) work. Use absolute paths with `dotnet test` since the test host may change the working directory.

## Caching strategy

### What to cache

Two things benefit from caching:

1. **Verilator install** (`/opt/verilator`) — the Verilator 5+ toolchain built from source. ~5 min to build, ~3 sec to restore from cache.
2. **Simulation library** (`build/`) — your design compiled through Verilator. 10-60 sec to build, ~1 sec to restore.

### Cache key design

Hash the inputs that affect each build:

**Verilator install** — use a static key (changes only when you want a new Verilator version):
```yaml
key: verilator-stable-v2
```

**Simulation library** — hash RTL sources and config:
```yaml
key: sim-${{ hashFiles('rtl/**', 'verifrog.toml', 'src/shim/**') }}
```

### Cache miss behavior

When the cache misses, a full build runs. When it hits, the build step is a no-op (library already exists). `verifrog test` also auto-builds if the library is missing, so the explicit build step is optional with caching — but keeping it makes logs clearer.

**Important**: Caches only save after a successful run. If the job fails, the cache is not updated.

## Test result publishing

### Markdown report to GitHub Actions summary

The `--report` flag generates a markdown file. Append it to `$GITHUB_STEP_SUMMARY` for in-page rendering:

```yaml
- run: bin/verifrog test --report
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

### Verilator version too old

Ubuntu 22.04's `apt install verilator` gives 4.x, but Verifrog requires 5+ for `--public-flat-rw`. Build from source (see the recommended workflow above) or use macOS runners where `brew install verilator` gives 5+.

### `libverifrog_sim.so: cannot open shared object file`

The library path isn't set. Use `verifrog test` (handles it automatically) or set it manually with an **absolute** path:

```bash
export LD_LIBRARY_PATH=$(pwd)/build    # Linux
export DYLD_LIBRARY_PATH=$(pwd)/build  # macOS
```

Relative paths may fail with `dotnet test` since the test host can change the working directory.

### Tests pass locally but fail in CI

Common causes:
1. **Different Verilator version** — pin the cache key version or use a Docker image
2. **Stale cache** — clear the CI cache and rebuild
3. **macOS vs Linux** — if your design uses platform-specific behavior, test on both

### Build is slow in CI

- Enable caching for both Verilator and the simulation library (see above)
- Use a larger runner for the build step
- Split build and test into separate jobs with artifact passing
- First run is always slow (~5 min for Verilator); subsequent runs are fast (~30 sec)
