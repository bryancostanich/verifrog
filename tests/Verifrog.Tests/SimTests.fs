module Verifrog.Tests.SimTests

open Expecto
open Verifrog.Sim
open Verifrog.Runner.Category

[<Tests>]
let simTests = testList "Verifrog.Sim" [

    smoke [
        test "create and reset" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            Expect.equal sim.Cycle 0UL "cycle should be 0 after reset"
        }

        test "read count after reset" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            let count = sim.ReadOrFail("count")
            Expect.equal count 0L "count should be 0 after reset"
        }

        test "list signals returns non-empty" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            let signals = sim.ListSignals()
            Expect.isTrue (signals.Length > 0) "should have at least one signal"
        }
    ]

    unit [
        test "step increments cycle count" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            sim.Step(10)
            Expect.equal sim.Cycle 10UL "cycle should be 10 after 10 steps"
        }

        test "write enable and count increments" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            sim.Write("enable", 1L) |> ignore
            sim.Step(5)
            let count = sim.ReadOrFail("count")
            Expect.equal count 5L "count should be 5 after 5 steps"
        }

        test "signal bits" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            Expect.equal (sim.SignalBits("count")) 8 "count should be 8 bits"
            Expect.equal (sim.SignalBits("overflow")) 1 "overflow should be 1 bit"
            Expect.equal (sim.SignalBits("nonexistent")) -1 "unknown signal returns -1"
        }

        test "checkpoint and restore" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            sim.Write("enable", 1L) |> ignore
            sim.Step(5)
            let cp = sim.SaveCheckpoint("after5", "count=5")
            sim.Step(10)
            Expect.equal (sim.ReadOrFail("count")) 15L "count should be 15"
            sim.RestoreCheckpoint("after5")
            Expect.equal (sim.ReadOrFail("count")) 5L "count should be 5 after restore"
            Expect.equal sim.Cycle 5UL "cycle should be 5 after restore"
        }

        test "force and release" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            sim.Write("enable", 1L) |> ignore
            sim.Step(5)
            sim.Force("enable", 0L) |> ignore
            sim.Step(5)
            Expect.equal (sim.ReadOrFail("count")) 5L "count should stay 5 with enable forced off"
            sim.Release("enable") |> ignore
        }

        test "fork returns result without side effects" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            sim.Write("enable", 1L) |> ignore
            sim.Step(5)
            let forkedCount = sim.Fork(fun s ->
                s.Step(10)
                s.ReadOrFail("count"))
            Expect.equal forkedCount 15L "forked scenario should see count=15"
            Expect.equal (sim.ReadOrFail("count")) 5L "original should still be count=5"
        }

        test "load value via write" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            sim.Write("load_value", 42L) |> ignore
            sim.Write("load_en", 1L) |> ignore
            sim.Step(1)
            sim.Write("load_en", 0L) |> ignore
            Expect.equal (sim.ReadOrFail("count")) 42L "count should be 42 after load"
        }

        test "RunUntilSignal" {
            use sim = Sim.Create()
            Sim.SuppressDisplay(true)
            sim.Reset()
            sim.Write("enable", 1L) |> ignore
            let (found, cycle) = sim.RunUntilSignal("count", 10L)
            Expect.isTrue found "should find count=10"
            Expect.equal (sim.ReadOrFail("count")) 10L "count should be 10"
        }
    ]
]
