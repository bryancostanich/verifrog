module Verifrog.Cli.DebugServer

open System
open System.IO
open System.Text.Json
open Verifrog.Sim

/// JSON debug server — reads one JSON command per line from stdin,
/// writes one JSON response per line to stdout. The process stays alive
/// until a "quit" command is received.
///
/// Protocol:
///   Request:  { "cmd": "step", "n": 5 }
///   Response: { "status": "ok", "cycle": 5 }
///
///   Request:  { "cmd": "read", "signals": ["count", "enable"] }
///   Response: { "status": "ok", "cycle": 5, "values": { "count": 0, "enable": 1 } }

// ---- JSON helpers ----

let private jsonOpts =
    let opts = JsonSerializerOptions()
    opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    opts.WriteIndented <- false
    opts

let private writeResponse (writer: TextWriter) (obj: obj) =
    let json = JsonSerializer.Serialize(obj, jsonOpts)
    writer.WriteLine(json)
    writer.Flush()

let private errorResponse (msg: string) =
    {| status = "error"; message = msg |}

// ---- Command dispatch ----

let private handleCommand (sim: Sim) (doc: JsonDocument) : obj =
    let root = doc.RootElement
    let cmd =
        if root.TryGetProperty("cmd") |> fst then
            root.GetProperty("cmd").GetString()
        else ""

    match cmd with
    | "status" ->
        {| status = "ok"
           cycle = sim.Cycle
           signalCount = sim.SignalCount
           forceCount = sim.ForceCount
           checkpoints = sim.ListCheckpoints() |> List.map (fun (n, cp) -> {| name = n; cycle = cp.Cycle; description = cp.Description |}) |}

    | "step" ->
        let n =
            if root.TryGetProperty("n") |> fst then root.GetProperty("n").GetInt32()
            else 1
        let cycle = sim.StepCycles(n)
        {| status = "ok"; cycle = cycle |}

    | "read" ->
        let signals =
            if root.TryGetProperty("signals") |> fst then
                let arr = root.GetProperty("signals")
                [| for i in 0 .. arr.GetArrayLength() - 1 -> arr.[i].GetString() |]
            elif root.TryGetProperty("signal") |> fst then
                [| root.GetProperty("signal").GetString() |]
            else [||]
        if signals.Length = 0 then
            errorResponse "read requires 'signal' or 'signals' field"
        else
            let values = Collections.Generic.Dictionary<string, obj>()
            let mutable err = None
            for s in signals do
                match sim.Read(s) with
                | SimResult.Ok v -> values.[s] <- v
                | SimResult.Error msg ->
                    if err.IsNone then err <- Some msg
                    values.[s] <- null
            match err with
            | Some msg -> {| status = "error"; message = msg; cycle = sim.Cycle; values = values |}
            | None -> {| status = "ok"; cycle = sim.Cycle; values = values |}

    | "write" ->
        let signal =
            if root.TryGetProperty("signal") |> fst then root.GetProperty("signal").GetString()
            else ""
        let value =
            if root.TryGetProperty("value") |> fst then root.GetProperty("value").GetInt64()
            else 0L
        if String.IsNullOrEmpty(signal) then
            errorResponse "write requires 'signal' field"
        else
            match sim.Write(signal, value) with
            | SimResult.Ok () -> {| status = "ok"; signal = signal; value = value |}
            | SimResult.Error msg -> errorResponse msg

    | "checkpoint" ->
        let name =
            if root.TryGetProperty("name") |> fst then root.GetProperty("name").GetString()
            else ""
        if String.IsNullOrEmpty(name) then
            errorResponse "checkpoint requires 'name' field"
        else
            let cp = sim.SaveCheckpoint(name)
            {| status = "ok"; name = name; cycle = cp.Cycle |}

    | "restore" ->
        let name =
            if root.TryGetProperty("name") |> fst then root.GetProperty("name").GetString()
            else ""
        if String.IsNullOrEmpty(name) then
            errorResponse "restore requires 'name' field"
        else
            try
                sim.RestoreCheckpoint(name)
                {| status = "ok"; name = name; cycle = sim.Cycle |}
            with ex ->
                errorResponse ex.Message

    | "signals" ->
        let filter =
            if root.TryGetProperty("filter") |> fst then root.GetProperty("filter").GetString()
            else ""
        let sigs =
            sim.ListSignals()
            |> (if String.IsNullOrEmpty(filter) then id
                else List.filter (fun (s: string) -> s.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            |> List.sort
        {| status = "ok"; signals = sigs; count = sigs.Length |}

    | "force" ->
        let signal =
            if root.TryGetProperty("signal") |> fst then root.GetProperty("signal").GetString()
            else ""
        let value =
            if root.TryGetProperty("value") |> fst then root.GetProperty("value").GetInt64()
            else 0L
        if String.IsNullOrEmpty(signal) then
            errorResponse "force requires 'signal' field"
        else
            match sim.Force(signal, value) with
            | SimResult.Ok () -> {| status = "ok"; signal = signal; value = value |}
            | SimResult.Error msg -> errorResponse msg

    | "release" ->
        let signal =
            if root.TryGetProperty("signal") |> fst then root.GetProperty("signal").GetString()
            else ""
        if String.IsNullOrEmpty(signal) then
            errorResponse "release requires 'signal' field"
        else
            match sim.Release(signal) with
            | SimResult.Ok () -> {| status = "ok"; signal = signal |}
            | SimResult.Error msg -> errorResponse msg

    | "release-all" ->
        sim.ReleaseAll()
        {| status = "ok"; forceCount = sim.ForceCount |}

    | "reset" ->
        sim.Reset()
        {| status = "ok"; cycle = sim.Cycle |}

    | "run-until" ->
        let signal =
            if root.TryGetProperty("signal") |> fst then root.GetProperty("signal").GetString()
            else ""
        let value =
            if root.TryGetProperty("value") |> fst then root.GetProperty("value").GetInt64()
            else 0L
        let maxCycles =
            if root.TryGetProperty("maxCycles") |> fst then root.GetProperty("maxCycles").GetInt32()
            else 10000
        if String.IsNullOrEmpty(signal) then
            errorResponse "run-until requires 'signal' field"
        else
            let (found, cycle) = sim.RunUntilSignal(signal, value, maxCycles)
            {| status = "ok"; found = found; cycle = cycle; signal = signal; value = value |}

    | "quit" ->
        {| status = "quit" |}

    | "" ->
        errorResponse "missing 'cmd' field"

    | _ ->
        errorResponse $"unknown command: {cmd}"

// ---- Server loop ----

/// Run the JSON debug server. Reads from stdin, writes to stdout.
/// Emits a ready message on startup, then processes commands until quit.
let runServer (sim: Sim) =
    let writer = Console.Out
    let reader = Console.In

    // Emit ready message
    writeResponse writer
        {| status = "ready"
           cycle = sim.Cycle
           signalCount = sim.SignalCount |}

    let mutable running = true
    while running do
        let line = reader.ReadLine()
        if isNull line then
            running <- false
        else
            let trimmed = line.Trim()
            if trimmed.Length > 0 then
                try
                    use doc = JsonDocument.Parse(trimmed)
                    let response = handleCommand sim doc
                    writeResponse writer response
                    // Check for quit
                    match response with
                    | :? {| status: string |} as r when r.status = "quit" ->
                        running <- false
                    | _ ->
                        // Check status field via JSON round-trip
                        let respJson = JsonSerializer.Serialize(response, jsonOpts)
                        if respJson.Contains("\"status\":\"quit\"") then
                            running <- false
                with ex ->
                    writeResponse writer (errorResponse $"JSON parse error: {ex.Message}")
