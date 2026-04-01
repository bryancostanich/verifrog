module Verifrog.Tests.DeclarativeTests

open Expecto
open Verifrog.Runner.Declarative

[<Tests>]
let declarativeTests = discoverFromToml (System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "samples", "counter", "verifrog.toml"))
