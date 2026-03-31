# Track: Package Distribution (NuGet + Homebrew)

Make Verifrog installable without cloning the repo. NuGet packages for the F# libraries, Homebrew formula for the CLI tools.

## Deliverables

1. **NuGet packages** — Publish `Verifrog.Sim`, `Verifrog.Runner`, and `Verifrog.Vcd` to nuget.org. Users reference them with `<PackageReference>` instead of `$(VERIFROG_ROOT)` ProjectReferences. This is the blocker for all other distribution.

2. **`verifrog init` generates PackageReferences** — Scaffolded test projects use NuGet packages by default. No `VERIFROG_ROOT` needed.

3. **Homebrew tap** — `bryancostanich/homebrew-tap` with a formula for the Verifrog CLI. Installs `verifrog`, `verifrog-vcd`, and the C shim/Makefile. Declares Verilator and .NET as dependencies.

4. **Updated docs** — Getting started shows `brew install` (macOS) or `dotnet new` + NuGet (any platform) as primary install paths. Clone-from-source becomes the contributor path.

## Scope notes

- Linux package distribution (apt PPA, Nix) is out of scope for this track.
- Verilator is already in Homebrew core. The formula declares it as a dependency.
- The C shim + Makefile must ship with the CLI (brew or clone) since it's compiled per-design at `verifrog build` time.
