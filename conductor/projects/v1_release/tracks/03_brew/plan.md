# Track Plan: Package Distribution

## Phase 1: NuGet Packages

This is the blocker. Without NuGet packages, every consumer needs a cloned repo and `VERIFROG_ROOT`.

- [x] Add package metadata to Verifrog.Sim, Verifrog.Runner, Verifrog.Vcd .fsproj files (PackageId, Version, Description, Authors, License, RepositoryUrl)
- [x] Verify `dotnet pack` produces clean .nupkg files for all three libraries
- [x] Test packages locally: create a fresh project, reference via local NuGet feed, run tests
- [x] Publish to nuget.org — Verifrog.Sim, Verifrog.Runner, Verifrog.Vcd all live at 1.0.0
- [x] Set up GitHub Actions workflow to auto-publish on release tag (`.github/workflows/publish.yml`)
- [x] Create `bryancostanich/homebrew-tap` repo on GitHub (empty, ready for formula)

Resolved issues during setup:
- API key special chars broke bash — fixed by passing via env var
- GitHub Release needed `contents: write` permission on workflow
- v1.0.0 published and GitHub Release created with .nupkg assets

## Phase 2: Update verifrog init

Once NuGet packages exist, `verifrog init` should generate projects that use them.

- [ ] Change scaffolded .fsproj template to use `<PackageReference>` instead of `<ProjectReference Include="$(VERIFROG_ROOT)/...">`
- [ ] Pin version to the current Verifrog release
- [ ] Keep ProjectReference mode available for contributors (detect if running from repo clone vs installed CLI)
- [ ] Test: `verifrog init` → `verifrog build` → `verifrog test` works without VERIFROG_ROOT

## Phase 3: Homebrew Tap

With NuGet solving the library distribution, brew only needs to ship the CLI tools.

- [ ] Write formula in `bryancostanich/homebrew-tap` (`Formula/verifrog.rb`)
  - Source: tarball from GitHub release tag
  - Dependencies: `verilator`, `dotnet` (cask)
  - Install: `bin/verifrog`, `bin/verifrog-vcd`, `bin/junit-to-md.fsx`, `src/shim/` (Makefile + verifrog_sim.cpp)
  - Links scripts to Homebrew bin
- [ ] Test: `brew install bryancostanich/tap/verifrog` → `verifrog init my_project` → `verifrog test` on clean machine
- [ ] Add `brew install` to README and getting-started as primary macOS install path

## Phase 4: Release Automation

- [x] GitHub release workflow: tag push → `dotnet pack` → publish NuGet → create GitHub release with tarball
- [ ] Formula auto-update: GitHub Action in homebrew-tap repo watches for new releases, updates formula URL + SHA256
- [ ] Version management: single source of truth for version number across NuGet packages and brew formula

## Open Questions

- Should the shim Makefile be distributed inside the NuGet package (as a build asset) or only via the CLI?
- Do we need a `dotnet new` template in addition to `verifrog init`?
