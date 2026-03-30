module Verifrog.Runner.Expect

open Expecto
open Verifrog.Sim

/// Expect a signal to have a specific value.
/// Produces readable failure output with signal name, expected/actual, cycle.
let signal (sim: Sim) (name: string) (expected: int64) (message: string) =
    let actual = sim.ReadOrFail(name)
    if actual <> expected then
        let cycle = sim.Cycle
        failtest $"{message}\n  signal:   {name}\n  expected: {expected} (0x{expected:X})\n  actual:   {actual} (0x{actual:X})\n  cycle:    {cycle}"

/// Expect a signal to match a predicate.
let signalSatisfies (sim: Sim) (name: string) (pred: int64 -> bool) (message: string) =
    let actual = sim.ReadOrFail(name)
    if not (pred actual) then
        let cycle = sim.Cycle
        failtest $"{message}\n  signal: {name}\n  value:  {actual} (0x{actual:X})\n  cycle:  {cycle}"

/// Expect a TOML-configured memory location to have a specific value.
let memory (sim: Sim) (memName: string) (bank: int) (addr: int) (expected: int64) (message: string) =
    let acc = sim.Memory(memName)
    match acc.Read(bank, addr) with
    | SimResult.Error e -> failtest $"Memory read failed: {e}"
    | SimResult.Ok actual ->
        if actual <> expected then
            failtest $"{message}\n  memory:   {memName} bank={bank} addr={addr}\n  expected: {expected} (0x{expected:X})\n  actual:   {actual} (0x{actual:X})"

/// Expect a TOML-configured register to have a specific value.
let register (sim: Sim) (regName: string) (expected: int64) (message: string) =
    let acc = sim.Register(regName)
    match acc.Read() with
    | SimResult.Error e -> failtest $"Register read failed: {e}"
    | SimResult.Ok actual ->
        if actual <> expected then
            failtest $"{message}\n  register: {regName}\n  expected: {expected} (0x{expected:X})\n  actual:   {actual} (0x{actual:X})"

/// Expect an iverilog simulation result to indicate all tests passed.
let iverilogPassed (result: Iverilog.IverilogResult) (message: string) =
    if result.ExitCode <> 0 then
        failtest $"{message}\n  exit code: {result.ExitCode}\n  stderr: {result.Stderr}"
    if not (Iverilog.passed result) then
        let summary = Iverilog.parseSummary result.Stdout
        let summaryStr =
            match summary with
            | Some (p, f) -> $" ({p} passed, {f} failed)"
            | None -> ""
        let tail = result.Stdout.[max 0 (result.Stdout.Length - 500)..]
        failtest $"{message}{summaryStr}\n  stdout (last 500):\n{tail}"
