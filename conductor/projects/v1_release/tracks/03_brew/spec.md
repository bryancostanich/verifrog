# Track: Homebrew Distribution

Package Verifrog as a Homebrew formula so users can install with `brew install bryancostanich/tap/verifrog` instead of cloning the repo.

## Deliverables

1. **Homebrew tap repo** — `bryancostanich/homebrew-tap` with a formula for Verifrog. Installs the CLI, wrapper scripts, and F# libraries. Declares Verilator 5+ as a dependency (Homebrew already has it).

2. **Formula that handles the full toolchain** — After `brew install`, users should be able to run `verifrog init`, `verifrog build`, and `verifrog test` without cloning the repo or setting `VERIFROG_ROOT`.

3. **Updated docs** — Getting started guide and README updated to show `brew install` as the primary install path on macOS, with clone-from-source as the alternative.

## Scope notes

- macOS only (Homebrew). Linux package distribution (apt PPA, Nix, etc.) is out of scope for this track.
- Verilator is already in Homebrew core (`brew install verilator` gives 5.x on macOS). The formula should declare it as a dependency, not bundle it.
- The .NET SDK is a dependency. Homebrew has `dotnet` as a cask. The formula should require it or guide the user.
