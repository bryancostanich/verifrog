module Verifrog.Tests.DeclarativeTests

open Expecto
open Verifrog.Runner.Declarative

/// Auto-discover and run all .verifrog files in the samples directory
[<Tests>]
let declarativeTests =
    let sampleDir =
        let rec findRoot (dir: string) =
            if System.IO.Directory.Exists(System.IO.Path.Combine(dir, "samples")) then dir
            else
                let parent = System.IO.Path.GetDirectoryName(dir)
                if parent = null || parent = dir then dir
                else findRoot parent
        let root = findRoot __SOURCE_DIRECTORY__
        System.IO.Path.Combine(root, "samples", "counter", "tests")
    loadTests sampleDir |> testList "Declarative"
