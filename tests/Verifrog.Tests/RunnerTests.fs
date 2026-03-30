module Verifrog.Tests.RunnerTests

open Expecto
open Verifrog.Sim
open Verifrog.Runner

[<Tests>]
let runnerTests = testList "Verifrog.Runner" [

    test "SimFixture.create returns working sim" {
        use sim = SimFixture.create ()
        Expect.equal sim.Cycle 0UL "should be at cycle 0 after create"
        Expect.equal (sim.ReadOrFail "count") 0L "count should be 0"
    }

    test "SimFixture.createWithCheckpoint" {
        let (sim, cp) = SimFixture.createWithCheckpoint ()
        use sim = sim
        Expect.equal cp.Cycle 0UL "checkpoint should be at cycle 0"
        sim.Write("enable", 1L) |> ignore
        sim.Step(5)
        Expect.equal (sim.ReadOrFail "count") 5L "count should be 5"
        SimFixture.restore sim PostReset
        Expect.equal (sim.ReadOrFail "count") 0L "count should be 0 after restore"
    }

    test "Expect.signal passes on match" {
        use sim = SimFixture.create ()
        Expect.signal sim "count" 0L "count should be 0"
    }

    test "Expect.signal fails on mismatch" {
        use sim = SimFixture.create ()
        sim.Write("enable", 1L) |> ignore
        sim.Step(3)
        Expect.throwsT<Expecto.AssertException>
            (fun () -> Expect.signal sim "count" 99L "should fail")
            "should throw on mismatch"
    }

    test "Expect.signalSatisfies" {
        use sim = SimFixture.create ()
        sim.Write("enable", 1L) |> ignore
        sim.Step(5)
        Expect.signalSatisfies sim "count" (fun v -> v > 0L) "count should be > 0"
    }

    test "SimFixture.saveLevel and restore" {
        use sim = SimFixture.create ()
        sim.Write("enable", 1L) |> ignore
        sim.Step(3)
        SimFixture.saveLevel sim PostConfig |> ignore
        sim.Step(5)
        Expect.equal (sim.ReadOrFail "count") 8L "count should be 8"
        SimFixture.restore sim PostConfig
        Expect.equal (sim.ReadOrFail "count") 3L "count should be 3 after restore to PostConfig"
    }
]
