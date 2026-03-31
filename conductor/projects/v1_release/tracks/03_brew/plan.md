# Track Plan: Homebrew Distribution

## Phase 1: Tap Setup

- [ ] Create `bryancostanich/homebrew-tap` repo on GitHub
- [ ] Write Verifrog formula (`Formula/verifrog.rb`)
  - Source: tarball from GitHub release tag
  - Dependencies: `verilator`, `dotnet` (cask)
  - Install: copies `bin/`, `src/`, F# projects to libexec, links `bin/verifrog` and `bin/verifrog-vcd` to Homebrew bin
- [ ] Test formula locally with `brew install --build-from-source`

## Phase 2: Release Automation

- [ ] Create a GitHub release workflow in verifrog repo (tag → tarball → formula update)
- [ ] Formula should pin to release tag URL with SHA256
- [ ] Test `brew install bryancostanich/tap/verifrog` from a clean machine

## Phase 3: VERIFROG_ROOT Elimination

Currently the F# test projects reference Verifrog via `$(VERIFROG_ROOT)/src/...` ProjectReferences. For brew to work, we need an alternative:

- [ ] Publish Verifrog.Sim, Verifrog.Runner, Verifrog.Vcd as NuGet packages (local feed or nuget.org)
- [ ] Or: have `verifrog init` generate .fsproj with absolute paths to the brew-installed location
- [ ] Update `bin/verifrog` to detect brew-installed vs repo-cloned and set paths accordingly

## Open Questions

- NuGet vs ProjectReference: NuGet is cleaner for distribution but adds a publish step. ProjectReference works for repo-cloned users. Can we support both?
- Should `verifrog init` detect whether it's running from brew and adjust the generated .fsproj accordingly?
- Should the formula pre-build the F# projects during install to avoid first-run compilation latency?
