module Verifrog.Runner.Category

open Expecto

/// Hardware verification test categories.
///
/// Use these to organize tests by purpose. Categories create named groups
/// in the test hierarchy, so Expecto's --filter works naturally:
///
///   verifrog test --category Smoke       # Run only smoke tests
///   verifrog test -- --filter "/Smoke/"  # Same thing, raw Expecto filter
///
/// Example:
///   let tests = testList "Sim" [
///       smoke [
///           test "resets to zero" { ... }
///       ]
///       unit [
///           test "step increments" { ... }
///       ]
///       parametric [
///           test "ALU sweep" { ... }
///       ]
///   ]

/// Quick sanity checks that the design is alive. Should run in seconds.
/// Run these first — if smoke fails, nothing else matters.
let smoke (tests: Test list) : Test = testList "Smoke" tests

/// Focused tests for individual operations or signal behaviors.
/// The bulk of your test suite.
let unit (tests: Test list) : Test = testList "Unit" tests

/// Tests that sweep parameters, compare configurations, or exercise
/// value ranges. Use with Sim.Sweep and Sim.Compare.
let parametric (tests: Test list) : Test = testList "Parametric" tests

/// Tests that exercise multi-block interactions, bus protocols, or
/// end-to-end data flow through the design.
let integration (tests: Test list) : Test = testList "Integration" tests

/// Long-running tests that push the design hard: deep pipelines,
/// large memories, many iterations. May take minutes.
let stress (tests: Test list) : Test = testList "Stress" tests

/// Reference outputs verified against a known-good model or hand
/// calculation. Failures here mean the design changed behavior.
let golden (tests: Test list) : Test = testList "Golden" tests

/// Tests added to cover specific bugs. Each should reference the
/// issue or commit that introduced the fix.
let regression (tests: Test list) : Test = testList "Regression" tests

/// All category names, for tooling and documentation.
let allCategories = ["Smoke"; "Unit"; "Parametric"; "Integration"; "Stress"; "Golden"; "Regression"]
