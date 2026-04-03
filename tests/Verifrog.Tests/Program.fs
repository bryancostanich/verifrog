module Verifrog.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    printfn "Verifrog tests starting"  // line 7 — set breakpoint here
    runTestsInAssemblyWithCLIArgs [] argv
