module Verifrog.Cli.Debugger

open System
open System.IO
open Verifrog.Sim

/// Output format for trace/watch
type OutputFormat = Table | Csv | Json

/// Result of executing a command
type CommandResult = Continue | Quit

/// CLI session state
type Session = {
    Sim: Sim
    mutable WatchSignals: string list
    mutable OutputFormat: OutputFormat
    mutable Recording: StreamWriter option
}

/// Extension command handler: takes session and command parts, returns Some result if handled
type CommandExtension = Session -> string list -> CommandResult option

// ---- Helpers ----

/// Format a signal value (generic — no FSM annotations)
let formatValue (_name: string) (value: int64) =
    if value = Int64.MinValue then "ERR"
    else $"{value}"

/// Parse an integer value (decimal or 0x hex)
let parseValue (s: string) : int64 option =
    if s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) then
        match Int64.TryParse(s.[2..], Globalization.NumberStyles.HexNumber, null) with
        | true, v -> Some v
        | _ -> None
    else
        match Int64.TryParse(s) with
        | true, v -> Some v
        | _ -> None

/// Parse an int32 value (decimal or 0x hex)
let parseInt32 (s: string) : int option =
    if s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) then
        match Int32.TryParse(s.[2..], Globalization.NumberStyles.HexNumber, null) with
        | true, v -> Some v
        | _ -> None
    else
        match Int32.TryParse(s) with
        | true, v -> Some v
        | _ -> None

/// Print watch signals after a step
let printWatch (session: Session) =
    if session.WatchSignals.IsEmpty then ()
    else
        let cycle = session.Sim.Cycle
        printf $"  [cycle {cycle}]"
        for name in session.WatchSignals do
            match session.Sim.Read(name) with
            | SimResult.Ok v -> printf $"  {name}={formatValue name v}"
            | SimResult.Error _ -> printf $"  {name}=ERR"
        printfn ""

/// Record a command to the script file
let recordCmd (session: Session) (cmd: string) =
    match session.Recording with
    | Some writer -> writer.WriteLine(cmd); writer.Flush()
    | None -> ()

// ---- Built-in command execution ----

let executeBuiltinCommand (session: Session) (parts: string list) : CommandResult =
    let sim = session.Sim

    match parts with
    | [] -> Continue

    // ---- Basic simulation ----
    | ["step"] | ["s"] ->
        sim.Step()
        printWatch session
        Continue
    | ["step"; n] | ["s"; n] ->
        match Int32.TryParse(n) with
        | true, count ->
            sim.Step(count)
            printWatch session
        | _ -> printfn "  Error: step <n> -- n must be an integer"
        Continue
    | ["reset"] ->
        sim.Reset()
        printfn "  Reset complete. Cycle: 0"
        Continue
    | ["cycle"] | ["c"] ->
        printfn $"  Cycle: {sim.Cycle}"
        Continue

    // ---- Signal read/write ----
    | ["read"; name] | ["r"; name] ->
        match sim.Read(name) with
        | SimResult.Ok v -> printfn $"  {name} = {formatValue name v}"
        | SimResult.Error msg -> printfn $"  Error: {msg}"
        Continue
    | ["write"; name; value] | ["w"; name; value] ->
        match parseValue value with
        | Some v ->
            match sim.Write(name, v) with
            | SimResult.Ok () -> printfn $"  {name} <- {v}"
            | SimResult.Error msg -> printfn $"  Error: {msg}"
        | None -> printfn $"  Error: invalid value '{value}'"
        Continue
    | "read" :: _ | "r" :: _ ->
        printfn "  Usage: read <signal_name>"
        Continue

    // ---- Trace ----
    | "trace" :: signals :: nStr :: _ ->
        let sigList = signals.Split(',') |> Array.toList
        match Int32.TryParse(nStr) with
        | true, n ->
            match session.OutputFormat with
            | Table -> printf "  cycle"; sigList |> List.iter (printf "  %s"); printfn ""
            | Csv -> printfn "cycle,%s" (sigList |> String.concat ",")
            | Json -> printfn "["
            let trace = sim.Trace(sigList, n)
            for (cyc, vals) in trace do
                let valStrs = List.map2 formatValue sigList vals
                match session.OutputFormat with
                | Table ->
                    printf $"  {cyc}"
                    valStrs |> List.iter (printf "  %s")
                    printfn ""
                | Csv ->
                    let csvVals = vals |> List.map string |> String.concat ","
                    printfn $"{cyc},{csvVals}"
                | Json ->
                    let pairs = List.zip sigList vals |> List.map (fun (s, v) -> sprintf "\"%s\":%d" s v)
                    let jsonBody = pairs |> String.concat ", "
                    printfn $"  {{\"cycle\":{cyc}, {jsonBody}}}"
            if session.OutputFormat = Json then printfn "]"
        | _ -> printfn "  Usage: trace <sig1,sig2,...> <cycles>"
        Continue

    // ---- Watch ----
    | "watch" :: signals :: _ ->
        session.WatchSignals <- signals.Split(',') |> Array.toList
        let watchStr = session.WatchSignals |> String.concat ", "
        printfn $"  Watching: {watchStr}"
        Continue
    | ["unwatch"] ->
        session.WatchSignals <- []
        printfn "  Watch cleared"
        Continue

    // ---- Run-until ----
    | ["run-until"; name; "=="; value] | ["until"; name; "=="; value] ->
        match Int64.TryParse(value) with
        | true, target ->
            let (found, cyc) = sim.RunUntilSignal(name, target)
            if found then printfn $"  Reached {name}=={target} at cycle {cyc}"
            else printfn $"  Timeout: {name} never reached {target} (stopped at cycle {cyc})"
            printWatch session
        | _ -> printfn "  Error: value must be an integer"
        Continue

    // ---- Checkpoint/Restore ----
    | ["checkpoint"; name] | ["cp"; name] ->
        let cp = sim.SaveCheckpoint(name)
        printfn $"  Saved checkpoint '{name}' at cycle {cp.Cycle}"
        Continue
    | ["restore"; name] | ["rs"; name] ->
        try
            sim.RestoreCheckpoint(name)
            printfn $"  Restored checkpoint '{name}' (cycle {sim.Cycle})"
        with ex -> printfn $"  Error: {ex.Message}"
        Continue
    | ["checkpoints"] | ["cps"] ->
        let cps = sim.ListCheckpoints()
        if cps.IsEmpty then printfn "  No checkpoints"
        else
            for (name, cp) in cps do
                printfn $"  {name}: cycle {cp.Cycle} -- {cp.Description}"
        Continue

    // ---- Force/Release ----
    | ["force"; name; value] | ["f"; name; value] ->
        match parseValue value with
        | Some v ->
            match sim.Force(name, v) with
            | SimResult.Ok () -> printfn $"  Forced {name} = {v}"
            | SimResult.Error msg -> printfn $"  Error: {msg}"
        | None -> printfn "  Error: invalid value"
        Continue
    | ["release"; name] ->
        match sim.Release(name) with
        | SimResult.Ok () -> printfn $"  Released {name}"
        | SimResult.Error msg -> printfn $"  Error: {msg}"
        Continue
    | ["release-all"] ->
        sim.ReleaseAll()
        printfn "  All forces released"
        Continue

    // ---- Signals listing ----
    | ["signals"] ->
        let sigs = sim.ListSignals() |> List.sort
        for s in sigs do printfn $"  {s}"
        printfn $"  ({sigs.Length} signals)"
        Continue
    | ["signals"; prefix] ->
        let sigs = sim.ListSignals() |> List.filter (fun s -> s.StartsWith(prefix)) |> List.sort
        for s in sigs do printfn $"  {s}"
        printfn $"  ({sigs.Length} signals matching '{prefix}')"
        Continue

    // ---- Output format ----
    | ["format"; "table"] -> session.OutputFormat <- Table; printfn "  Output: table"; Continue
    | ["format"; "csv"] -> session.OutputFormat <- Csv; printfn "  Output: csv"; Continue
    | ["format"; "json"] -> session.OutputFormat <- Json; printfn "  Output: json"; Continue

    // ---- Script recording ----
    | ["record"; path] ->
        session.Recording <- Some (new StreamWriter(path, false))
        printfn $"  Recording to {path}"
        Continue
    | ["record-stop"] ->
        match session.Recording with
        | Some w -> w.Close(); session.Recording <- None; printfn "  Recording stopped"
        | None -> printfn "  Not recording"
        Continue

    // ---- Status (generic) ----
    | ["status"] | ["st"] ->
        printfn $"  Cycle: {sim.Cycle}"
        printfn $"  Signals: {sim.SignalCount}"
        printfn $"  Forces: {sim.ForceCount}"
        let cps = sim.ListCheckpoints()
        printfn $"  Checkpoints: {cps.Length}"
        if not session.WatchSignals.IsEmpty then
            let watchStr = session.WatchSignals |> String.concat ", "
            printfn $"  Watching: {watchStr}"
        Continue

    // ---- Help ----
    | ["help"] | ["h"] | ["?"] ->
        printfn "  Commands:"
        printfn "    step [n]              Step N cycles (default 1). Alias: s"
        printfn "    reset                 Reset the model"
        printfn "    cycle                 Print current cycle count. Alias: c"
        printfn "    status                Print cycle, signal count, forces, etc. Alias: st"
        printfn ""
        printfn "    read <signal>         Read a signal. Alias: r"
        printfn "    write <signal> <val>  Write a signal (hex: 0x...). Alias: w"
        printfn "    trace <sigs> <n>      Trace comma-separated signals for N cycles"
        printfn "    watch <sigs>          Auto-print signals after each step"
        printfn "    unwatch               Clear watch list"
        printfn "    run-until <sig> == <val>  Step until signal equals value"
        printfn ""
        printfn "    checkpoint <name>     Save checkpoint. Alias: cp"
        printfn "    restore <name>        Restore checkpoint. Alias: rs"
        printfn "    checkpoints           List all checkpoints. Alias: cps"
        printfn ""
        printfn "    force <signal> <val>  Force signal (persistent). Alias: f"
        printfn "    release <signal>      Release force"
        printfn "    release-all           Release all forces"
        printfn ""
        printfn "    signals [prefix]      List registered signal names"
        printfn ""
        printfn "    format table|csv|json Set output format"
        printfn "    record <path>         Record commands to file"
        printfn "    record-stop           Stop recording"
        printfn "    quit                  Exit. Alias: q"
        Continue

    // ---- Quit ----
    | ["quit"] | ["q"] | ["exit"] -> Quit

    // ---- Unknown ----
    | cmd :: _ ->
        printfn $"  Unknown command: {cmd}. Type 'help' for commands."
        Continue

/// Execute a command, trying extensions first, then built-in commands
let executeCommand (extensions: CommandExtension list) (session: Session) (input: string) : CommandResult =
    let parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
    // Try each extension in order
    let rec tryExtensions exts =
        match exts with
        | [] -> executeBuiltinCommand session parts
        | ext :: rest ->
            match ext session parts with
            | Some result -> result
            | None -> tryExtensions rest
    tryExtensions extensions

/// Run interactive REPL with optional command extensions
let runInteractive (sim: Sim) (extensions: CommandExtension list) =
    let session = {
        Sim = sim
        WatchSignals = []
        OutputFormat = Table
        Recording = None
    }

    printfn "verifrog debugger -- Interactive RTL simulation debugger"
    printfn "Type 'help' for commands, 'quit' to exit."
    printfn $"  {sim.SignalCount} signals registered. Cycle: {sim.Cycle}"
    printfn ""

    let mutable running = true
    while running do
        printf "sim> "
        let line = Console.ReadLine()
        if isNull line then
            running <- false
        else
            let trimmed = line.Trim()
            if trimmed.Length > 0 && not (trimmed.StartsWith("#")) then
                recordCmd session trimmed
                match executeCommand extensions session trimmed with
                | Continue -> ()
                | Quit -> running <- false

    // Cleanup
    match session.Recording with Some w -> w.Close() | None -> ()

/// Run batch script with optional command extensions
let runScript (sim: Sim) (extensions: CommandExtension list) (scriptPath: string) =
    let session = {
        Sim = sim
        WatchSignals = []
        OutputFormat = Table
        Recording = None
    }

    let lines = File.ReadAllLines(scriptPath)
    let mutable running = true
    for line in lines do
        if running then
            let trimmed = line.Trim()
            if trimmed.Length > 0 && not (trimmed.StartsWith("#")) then
                printfn $"sim> {trimmed}"
                try
                    match executeCommand extensions session trimmed with
                    | Continue -> ()
                    | Quit -> running <- false
                with ex ->
                    printfn $"  Error: {ex.Message}"
