module Verifrog.Tests.DeclarativeTests

open System.IO
open Expecto
open Verifrog.Runner.Declarative

/// Discover and run all .verifrog files in the counter sample
[<Tests>]
let declarativeTests =
    let sampleDir =
        // Walk up from test binary to find the samples directory
        let rec findRoot (dir: string) =
            if Directory.Exists(Path.Combine(dir, "samples")) then dir
            else
                let parent = Path.GetDirectoryName(dir)
                if parent = null || parent = dir then dir
                else findRoot parent
        let root = findRoot __SOURCE_DIRECTORY__
        Path.Combine(root, "samples", "counter", "tests")

    let tests = loadTests sampleDir

    if tests.IsEmpty then
        testList "Declarative" [
            test "no .verifrog files found" {
                skiptest "No declarative test files found"
            }
        ]
    else
        testList "Declarative" tests
