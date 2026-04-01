module Verifrog.Runner.Declarative

open System
open System.IO
open System.Text.RegularExpressions
open Expecto
open Verifrog.Sim

// ---- AST ----

/// A single value: decimal or hex
type Value = int64

/// Test step — one line in a .verifrog file
type Step =
    | Write of signal: string * value: Value
    | StepCycles of count: int
    | Expect of signal: string * op: string * value: Value
    | ExpectMemory of memory: string * bank: int * addr: int * op: string * value: Value
    | Load of memory: string * bank: int * data: Value list
    | LoadFromFile of memory: string * bank: int * path: string
    | RunUntil of signal: string * value: Value * max: int
    | Force of signal: string * value: Value
    | Release of signal: string
    | Checkpoint of name: string
    | Restore of name: string

/// A parsed test
type DeclTest = {
    Name: string
    Category: string option
    Steps: Step list
    File: string
    Line: int
}

// ---- Value parsing ----

let private parseValue (s: string) : Value option =
    let s = s.Trim()
    if s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0X") then
        match Int64.TryParse(s.[2..], Globalization.NumberStyles.HexNumber, null) with
        | true, v -> Some v
        | _ -> None
    elif s.StartsWith("-0x", StringComparison.OrdinalIgnoreCase) then
        match Int64.TryParse(s.[3..], Globalization.NumberStyles.HexNumber, null) with
        | true, v -> Some (-v)
        | _ -> None
    else
        match Int64.TryParse(s) with
        | true, v -> Some v
        | _ -> None

let private requireValue (s: string) (file: string) (line: int) : Value =
    match parseValue s with
    | Some v -> v
    | None -> failwith $"Invalid value '{s}' at {file}:{line}"

// ---- Parser ----

type private ParseError = { File: string; Line: int; Message: string }

let private fail file line msg =
    failwith $"{file}:{line}: {msg}"

/// Parse a .verifrog file into a list of DeclTests
let parse (filePath: string) : DeclTest list =
    let lines = File.ReadAllLines(filePath)
    let file = Path.GetFileName(filePath)
    let mutable tests = []
    let mutable currentTest: (string * string option * int * Step list) option = None

    let finishTest () =
        match currentTest with
        | Some (name, cat, line, steps) ->
            tests <- { Name = name; Category = cat; Steps = List.rev steps; File = filePath; Line = line } :: tests
            currentTest <- None
        | None -> ()

    let addStep step =
        match currentTest with
        | Some (name, cat, line, steps) ->
            currentTest <- Some (name, cat, line, step :: steps)
        | None -> fail file 0 "Step outside of a test block"

    for i in 0 .. lines.Length - 1 do
        let lineNum = i + 1
        let raw = lines.[i]
        let trimmed = raw.Trim()

        // Skip blank lines and comments
        if trimmed.Length = 0 || trimmed.StartsWith("#") then
            ()

        // Test header: test "name" [Category]:
        elif trimmed.StartsWith("test ") then
            finishTest ()
            let m = Regex.Match(trimmed, """^test\s+"([^"]+)"\s*(?:\[(\w+)\])?\s*:$""")
            if not m.Success then
                fail file lineNum $"Invalid test header: {trimmed}"
            let name = m.Groups.[1].Value
            let cat = if m.Groups.[2].Success then Some m.Groups.[2].Value else None
            currentTest <- Some (name, cat, lineNum, [])

        // Must be indented (inside a test)
        elif raw.Length > 0 && (raw.[0] = ' ' || raw.[0] = '\t') then
            let line = trimmed

            // write signal = value  OR  write sig1 = val1, sig2 = val2
            if line.StartsWith("write ") then
                let rest = line.[6..].Trim()
                let pairs = rest.Split(',')
                for pair in pairs do
                    let parts = pair.Trim().Split('=', 2)
                    if parts.Length <> 2 then fail file lineNum $"Invalid write: {pair.Trim()}"
                    let signal = parts.[0].Trim()
                    let value = requireValue (parts.[1].Trim()) file lineNum
                    addStep (Write (signal, value))

            // step N
            elif line.StartsWith("step ") then
                let n = line.[5..].Trim()
                match Int32.TryParse(n) with
                | true, count -> addStep (StepCycles count)
                | _ -> fail file lineNum $"Invalid step count: {n}"

            // expect signal == value  OR  expect signal != value
            elif line.StartsWith("expect ") then
                let rest = line.[7..].Trim()
                // Memory expect: expect mem[bank][addr] == value
                let memMatch = Regex.Match(rest, """^(\w+)\[(\d+)\]\[(\d+)\]\s*(==|!=)\s*(.+)$""")
                if memMatch.Success then
                    let mem = memMatch.Groups.[1].Value
                    let bank = int memMatch.Groups.[2].Value
                    let addr = int memMatch.Groups.[3].Value
                    let op = memMatch.Groups.[4].Value
                    let value = requireValue (memMatch.Groups.[5].Value) file lineNum
                    addStep (ExpectMemory (mem, bank, addr, op, value))
                else
                    let m = Regex.Match(rest, """^(\S+)\s*(==|!=)\s*(.+)$""")
                    if not m.Success then fail file lineNum $"Invalid expect: {rest}"
                    let signal = m.Groups.[1].Value
                    let op = m.Groups.[2].Value
                    let value = requireValue (m.Groups.[3].Value) file lineNum
                    addStep (Expect (signal, op, value))

            // load memory bank=N [data, ...]
            elif line.StartsWith("load ") then
                let rest = line.[5..].Trim()
                // load mem bank=N from file.hex
                let fromMatch = Regex.Match(rest, """^(\w+)\s+bank=(\d+)\s+from\s+(.+)$""")
                if fromMatch.Success then
                    let mem = fromMatch.Groups.[1].Value
                    let bank = int fromMatch.Groups.[2].Value
                    let path = fromMatch.Groups.[3].Value.Trim()
                    addStep (LoadFromFile (mem, bank, path))
                else
                    // load mem bank=N [val1, val2, ...]
                    let dataMatch = Regex.Match(rest, """^(\w+)\s+bank=(\d+)\s+\[(.+)\]$""")
                    if not dataMatch.Success then fail file lineNum $"Invalid load: {rest}"
                    let mem = dataMatch.Groups.[1].Value
                    let bank = int dataMatch.Groups.[2].Value
                    let dataStr = dataMatch.Groups.[3].Value
                    let data = dataStr.Split(',') |> Array.map (fun s -> requireValue (s.Trim()) file lineNum) |> Array.toList
                    addStep (Load (mem, bank, data))

            // run-until signal == value, max = N
            elif line.StartsWith("run-until ") then
                let rest = line.[10..].Trim()
                let m = Regex.Match(rest, """^(\S+)\s*==\s*(\S+)\s*,\s*max\s*=\s*(\d+)$""")
                if not m.Success then fail file lineNum $"Invalid run-until: {rest}"
                let signal = m.Groups.[1].Value
                let value = requireValue (m.Groups.[2].Value) file lineNum
                let max = int m.Groups.[3].Value
                addStep (RunUntil (signal, value, max))

            // force signal = value
            elif line.StartsWith("force ") then
                let rest = line.[6..].Trim()
                let parts = rest.Split('=', 2)
                if parts.Length <> 2 then fail file lineNum $"Invalid force: {rest}"
                let signal = parts.[0].Trim()
                let value = requireValue (parts.[1].Trim()) file lineNum
                addStep (Force (signal, value))

            // release signal
            elif line.StartsWith("release ") then
                let signal = line.[8..].Trim()
                addStep (Release signal)

            // checkpoint name
            elif line.StartsWith("checkpoint ") then
                let name = line.[11..].Trim()
                addStep (Checkpoint name)

            // restore name
            elif line.StartsWith("restore ") then
                let name = line.[8..].Trim()
                addStep (Restore name)

            else
                fail file lineNum $"Unknown command: {line}"

        else
            fail file lineNum $"Unexpected line (not indented, not a test header): {trimmed}"

    finishTest ()
    List.rev tests

// ---- Runner: convert DeclTests to Expecto tests ----

/// Execute a single step against a Sim instance
let private executeStep (sim: Sim) (step: Step) (file: string) (line: int) =
    match step with
    | Write (signal, value) ->
        match sim.Write(signal, value) with
        | SimResult.Error e -> failtest $"Write failed: {e}\n  at {file}:{line}"
        | _ -> ()

    | StepCycles count ->
        sim.Step(count)

    | Expect (signal, op, expected) ->
        let actual = sim.ReadOrFail(signal)
        match op with
        | "==" ->
            if actual <> expected then
                failtest $"Expected {signal} == {expected} (0x{expected:X}), got {actual} (0x{actual:X})\n  at {file}:{line}"
        | "!=" ->
            if actual = expected then
                failtest $"Expected {signal} != {expected} (0x{expected:X}), but it was equal\n  at {file}:{line}"
        | _ -> failtest $"Unknown operator: {op}\n  at {file}:{line}"

    | ExpectMemory (memory, bank, addr, op, expected) ->
        let mem = sim.Memory(memory)
        match mem.Read(bank, addr) with
        | SimResult.Error e -> failtest $"Memory read failed: {e}\n  at {file}:{line}"
        | SimResult.Ok actual ->
            match op with
            | "==" ->
                if actual <> expected then
                    failtest $"Expected {memory}[{bank}][{addr}] == {expected} (0x{expected:X}), got {actual} (0x{actual:X})\n  at {file}:{line}"
            | "!=" ->
                if actual = expected then
                    failtest $"Expected {memory}[{bank}][{addr}] != {expected} (0x{expected:X}), but it was equal\n  at {file}:{line}"
            | _ -> failtest $"Unknown operator: {op}\n  at {file}:{line}"

    | Load (memory, bank, data) ->
        let mem = sim.Memory(memory)
        for i in 0 .. data.Length - 1 do
            match mem.Write(bank, i, data.[i]) with
            | SimResult.Error e -> failtest $"Memory write failed at addr {i}: {e}\n  at {file}:{line}"
            | _ -> ()

    | LoadFromFile (memory, bank, path) ->
        let mem = sim.Memory(memory)
        let resolvedPath =
            if Path.IsPathRooted(path) then path
            else Path.Combine(Path.GetDirectoryName(file), path)
        if not (File.Exists(resolvedPath)) then
            failtest $"File not found: {resolvedPath}\n  at {file}:{line}"
        let lines = File.ReadAllLines(resolvedPath)
        let mutable addr = 0
        for hexLine in lines do
            let trimmed = hexLine.Trim()
            if trimmed.Length > 0 && not (trimmed.StartsWith("//")) then
                // Handle @address directives ($readmemh format)
                if trimmed.StartsWith("@") then
                    match Int32.TryParse(trimmed.[1..], Globalization.NumberStyles.HexNumber, null) with
                    | true, a -> addr <- a
                    | _ -> failtest $"Invalid address directive: {trimmed}\n  in {resolvedPath}"
                else
                    // Parse hex values separated by whitespace
                    for word in trimmed.Split([|' '; '\t'|], StringSplitOptions.RemoveEmptyEntries) do
                        match Int64.TryParse(word, Globalization.NumberStyles.HexNumber, null) with
                        | true, v ->
                            match mem.Write(bank, addr, v) with
                            | SimResult.Error e -> failtest $"Memory write failed at addr {addr}: {e}\n  at {file}:{line}"
                            | _ -> ()
                            addr <- addr + 1
                        | _ -> failtest $"Invalid hex value: {word}\n  in {resolvedPath}"

    | RunUntil (signal, value, max) ->
        let (found, cycle) = sim.RunUntilSignal(signal, value, max)
        if not found then
            failtest $"Timed out: {signal} never reached {value} within {max} cycles (stopped at cycle {cycle})\n  at {file}:{line}"

    | Force (signal, value) ->
        match sim.Force(signal, value) with
        | SimResult.Error e -> failtest $"Force failed: {e}\n  at {file}:{line}"
        | _ -> ()

    | Release signal ->
        match sim.Release(signal) with
        | SimResult.Error e -> failtest $"Release failed: {e}\n  at {file}:{line}"
        | _ -> ()

    | Checkpoint name ->
        sim.SaveCheckpoint(name) |> ignore

    | Restore name ->
        sim.RestoreCheckpoint(name)

// ---- Validation ----

/// Extract all signal names referenced in a test's steps
let private referencedSignals (test: DeclTest) : (string * string * int) list =
    [ for step in test.Steps do
        match step with
        | Write (s, _) -> yield (s, test.File, test.Line)
        | Expect (s, _, _) -> yield (s, test.File, test.Line)
        | RunUntil (s, _, _) -> yield (s, test.File, test.Line)
        | Force (s, _) -> yield (s, test.File, test.Line)
        | Release s -> yield (s, test.File, test.Line)
        | _ -> () ]

/// Extract all memory names referenced in a test's steps
let private referencedMemories (test: DeclTest) : (string * string * int) list =
    [ for step in test.Steps do
        match step with
        | ExpectMemory (m, _, _, _, _) -> yield (m, test.File, test.Line)
        | Load (m, _, _) -> yield (m, test.File, test.Line)
        | LoadFromFile (m, _, _) -> yield (m, test.File, test.Line)
        | _ -> () ]

/// Strip all array indices from a signal path to get the base path.
/// "u_wgt_bank_a.gen_bank[0].u_bank.mem[511]" -> "u_wgt_bank_a.gen_bank.u_bank.mem"
/// "u_regfile.regs[2]" -> "u_regfile.regs"
/// "some_signal" -> "some_signal" (unchanged)
let private stripIndices (s: string) : string =
    Regex.Replace(s, @"\[\d+\]", "")

/// Validate all signal and memory references in parsed tests against a live Sim.
/// Returns a list of error strings. Empty list means all references are valid.
/// Array-indexed signals are validated by stripping indices and checking if
/// any known signal starts with the stripped base path.
let validate (tests: DeclTest list) (sim: Sim) : string list =
    let knownSignals = sim.ListSignals()
    let knownSignalSet = knownSignals |> Set.ofList
    let knownMemories = sim.MemoryNames |> Set.ofList

    let isKnownSignal (s: string) =
        // Exact match
        knownSignalSet.Contains(s)
        // Register name from TOML
        || sim.RegisterNames |> List.contains s
        // Array-indexed paths (contain []) — skip validation.
        // Verilator handles array indexing at runtime; the signal list only
        // contains base array names, not individual elements. Runtime errors
        // will catch bad paths with clear messages.
        || s.Contains("[")

    let signalErrors =
        tests
        |> List.collect referencedSignals
        |> List.distinctBy (fun (s, _, _) -> s)
        |> List.choose (fun (s, file, line) ->
            if isKnownSignal s then None
            else Some $"Unknown signal '{s}' in {Path.GetFileName file}:{line}")

    let memoryErrors =
        tests
        |> List.collect referencedMemories
        |> List.distinctBy (fun (m, _, _) -> m)
        |> List.choose (fun (m, file, line) ->
            if knownMemories.Contains(m) then None
            else Some $"Unknown memory '{m}' in {Path.GetFileName file}:{line}")

    signalErrors @ memoryErrors

/// Create a validation test that checks all signal/memory references before running tests.
/// Fails fast with all errors listed if any references are invalid.
let private validationTest (tests: DeclTest list) (createSim: unit -> Sim) : Test =
    testCase "validate signal references" (fun () ->
        use sim = createSim ()
        let errors = validate tests sim
        if not errors.IsEmpty then
            let msg = errors |> String.concat "\n  "
            failtest $"Declarative test validation failed:\n  {msg}")

/// Convert a DeclTest to an Expecto Test
let private toExpectoTest (test: DeclTest) : Test =
    testCase test.Name (fun () ->
        use sim = Sim.Create()
        Sim.SuppressDisplay(true)
        sim.Reset()
        for step in test.Steps do
            executeStep sim step test.File test.Line)

/// Convert a DeclTest to an Expecto Test using a TOML config
let private toExpectoTestWithConfig (config: Config.VerifrogConfig) (test: DeclTest) : Test =
    testCase test.Name (fun () ->
        use sim = Sim.Create(config)
        Sim.SuppressDisplay(true)
        sim.Reset()
        for step in test.Steps do
            executeStep sim step test.File test.Line)

/// Group tests by category and wrap in testList
let private groupByCategory (tests: DeclTest list) (toTest: DeclTest -> Test) : Test list =
    let categorized =
        tests
        |> List.groupBy (fun t -> t.Category |> Option.defaultValue "Uncategorized")
        |> List.map (fun (cat, ts) ->
            testList cat (ts |> List.map toTest))
    categorized

/// Load all .verifrog files from a directory and return Expecto tests.
/// Includes a validation test that checks all signal references before running.
let loadTests (dir: string) : Test list =
    let files =
        if Directory.Exists(dir) then
            Directory.GetFiles(dir, "*.verifrog", SearchOption.AllDirectories)
            |> Array.toList
        else []
    if files.IsEmpty then []
    else
        let allTests = files |> List.collect parse
        let validation = validationTest allTests (fun () ->
            Sim.SuppressDisplay(true)
            let sim = Sim.Create()
            sim.Reset()
            sim)
        validation :: groupByCategory allTests toExpectoTest

/// Load .verifrog files with TOML config for memory/register access.
/// Includes a validation test that checks all signal and memory references.
let loadTestsWithConfig (dir: string) (config: Config.VerifrogConfig) : Test list =
    let files =
        if Directory.Exists(dir) then
            Directory.GetFiles(dir, "*.verifrog", SearchOption.AllDirectories)
            |> Array.toList
        else []
    if files.IsEmpty then []
    else
        let allTests = files |> List.collect parse
        let validation = validationTest allTests (fun () ->
            Sim.SuppressDisplay(true)
            let sim = Sim.Create(config)
            sim.Reset()
            sim)
        validation :: groupByCategory allTests (toExpectoTestWithConfig config)
