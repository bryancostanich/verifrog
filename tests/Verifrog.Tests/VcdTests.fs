module Verifrog.Tests.VcdTests

open System.IO
open Expecto
open Verifrog.Vcd
open Verifrog.Runner.Category

let private testVcdPath =
    Path.Combine(__SOURCE_DIRECTORY__, "test_counter.vcd")

[<Tests>]
let vcdTests = testList "Verifrog.Vcd" [

    smoke [
        test "parse header finds all signals" {
            let vcd = VcdParser.parseAll testVcdPath
            Expect.equal vcd.Signals.Length 5 "should have 5 signals"
        }
    ]

    unit [
        test "signal metadata" {
            let vcd = VcdParser.parseAll testVcdPath
            let count = vcd.Signals |> List.find (fun s -> s.LeafName = "count")
            Expect.equal count.Width 8 "count should be 8 bits"
            Expect.equal count.FullPath "counter.count" "count full path"
        }

        test "findSignals by name" {
            let vcd = VcdParser.parseAll testVcdPath
            let clkSignals = VcdParser.findSignals vcd "clk"
            Expect.equal clkSignals.Length 1 "should find one clk signal"
            Expect.equal clkSignals.[0].LeafName "clk" "should be clk"
        }

        test "findSignals by glob" {
            let vcd = VcdParser.parseAll testVcdPath
            let matches = VcdParser.findSignals vcd "counter.*"
            Expect.isTrue (matches.Length >= 3) "glob should match multiple signals"
        }

        test "transitions are parsed" {
            let vcd = VcdParser.parseAll testVcdPath
            let countTrans = VcdParser.transitions vcd "counter.count"
            Expect.isTrue (countTrans.Length > 0) "count should have transitions"
        }

        test "valueAtTime" {
            let vcd = VcdParser.parseAll testVcdPath
            let v = VcdParser.valueAtTime vcd "counter.count" 130L
            Expect.isSome v "should have a value at t=130"
            Expect.equal v.Value.IntVal 3 "count should be 3 at t=130"
        }

        test "valueAtTime before first transition" {
            let vcd = VcdParser.parseAll testVcdPath
            let v = VcdParser.valueAtTime vcd "counter.count" 0L
            Expect.isSome v "should have initial value"
            Expect.equal v.Value.IntVal 0 "count should be 0 at t=0"
        }

        test "transitionCount" {
            let vcd = VcdParser.parseAll testVcdPath
            let count = VcdParser.transitionCount vcd "counter.count"
            Expect.equal count 6 "count should have 6 transitions (0, 1, 2, 3, 4, 5)"
        }

        test "firstTimeAtValue" {
            let vcd = VcdParser.parseAll testVcdPath
            let t = VcdParser.firstTimeAtValue vcd "counter.count" 3
            Expect.isSome t "should find value 3"
            Expect.equal t.Value 130L "count=3 at t=130"
        }

        test "uniqueValues" {
            let vcd = VcdParser.parseAll testVcdPath
            let vals = VcdParser.uniqueValues vcd "counter.count"
            Expect.equal vals [0; 1; 2; 3; 4; 5] "count values 0-5"
        }

        test "highPulseCount" {
            let vcd = VcdParser.parseAll testVcdPath
            let pulses = VcdParser.highPulseCount vcd "counter.enable"
            Expect.equal pulses 1 "enable goes high once"
        }

        test "parseBinValue" {
            Expect.equal (VcdParser.parseBinValue "00000101") 5 "binary 101 = 5"
            Expect.equal (VcdParser.parseBinValue "11111111") 255 "binary 11111111 = 255"
            Expect.equal (VcdParser.parseBinValue "x0x1") 1 "x treated as 0"
        }

        test "parse with time limit" {
            let vcd = VcdParser.parse testVcdPath 100L
            let countTrans = VcdParser.transitions vcd "counter.count"
            Expect.isTrue (countTrans |> List.forall (fun t -> t.Time <= 100L)) "all transitions should be <= 100ps"
        }
    ]
]
