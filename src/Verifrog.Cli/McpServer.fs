module Verifrog.Cli.McpServer

open System
open System.IO
open System.Text.Json
open Verifrog.Sim

/// MCP (Model Context Protocol) server for Verifrog debug sessions.
/// Implements JSON-RPC 2.0 over stdin/stdout with tools for sim control.
/// No external dependencies — raw protocol implementation.

let private jsonOpts =
    let opts = JsonSerializerOptions()
    opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    opts.WriteIndented <- false
    opts

let private writeLine (writer: TextWriter) (json: string) =
    writer.WriteLine(json)
    writer.Flush()

let private jsonResponse (id: JsonElement) (result: obj) =
    use ms = new MemoryStream()
    use w = new Utf8JsonWriter(ms)
    w.WriteStartObject()
    w.WriteString("jsonrpc", "2.0")
    // Write id (can be number or string)
    w.WritePropertyName("id")
    id.WriteTo(w)
    w.WritePropertyName("result")
    JsonSerializer.Serialize(w, result, jsonOpts)
    w.WriteEndObject()
    w.Flush()
    Text.Encoding.UTF8.GetString(ms.ToArray())

let private jsonError (id: JsonElement) (code: int) (message: string) =
    use ms = new MemoryStream()
    use w = new Utf8JsonWriter(ms)
    w.WriteStartObject()
    w.WriteString("jsonrpc", "2.0")
    w.WritePropertyName("id")
    id.WriteTo(w)
    w.WriteStartObject("error")
    w.WriteNumber("code", code)
    w.WriteString("message", message)
    w.WriteEndObject()
    w.WriteEndObject()
    w.Flush()
    Text.Encoding.UTF8.GetString(ms.ToArray())

let private toolResult (text: string) (isError: bool) =
    {| content = [| {| ``type`` = "text"; text = text |} |]; isError = isError |}

// ---- Tool definitions (as raw JSON for mixed-shape schemas) ----

let private toolsJson = """[
  {"name":"debug_status","description":"Get current simulation status: cycle count, signal count, active forces, checkpoints.","inputSchema":{"type":"object","properties":{}}},
  {"name":"debug_step","description":"Advance the simulation by N clock cycles. Returns the new cycle count.","inputSchema":{"type":"object","properties":{"n":{"type":"integer","description":"Number of cycles to step (default 1)"}}}},
  {"name":"debug_read","description":"Read one or more signal values by name. Returns signal names and their current values.","inputSchema":{"type":"object","properties":{"signals":{"type":"array","items":{"type":"string"},"description":"Signal names to read"}},"required":["signals"]}},
  {"name":"debug_write","description":"Write a value to a signal.","inputSchema":{"type":"object","properties":{"signal":{"type":"string","description":"Signal name"},"value":{"type":"integer","description":"Value to write"}},"required":["signal","value"]}},
  {"name":"debug_signals","description":"List all signal names in the design, optionally filtered by substring.","inputSchema":{"type":"object","properties":{"filter":{"type":"string","description":"Substring filter (optional)"}}}},
  {"name":"debug_checkpoint","description":"Save a named checkpoint of the current simulation state.","inputSchema":{"type":"object","properties":{"name":{"type":"string","description":"Checkpoint name"}},"required":["name"]}},
  {"name":"debug_restore","description":"Restore simulation state from a named checkpoint.","inputSchema":{"type":"object","properties":{"name":{"type":"string","description":"Checkpoint name"}},"required":["name"]}},
  {"name":"debug_force","description":"Force a signal to a value (persists across clock cycles until released).","inputSchema":{"type":"object","properties":{"signal":{"type":"string","description":"Signal name"},"value":{"type":"integer","description":"Value to force"}},"required":["signal","value"]}},
  {"name":"debug_release","description":"Release a forced signal.","inputSchema":{"type":"object","properties":{"signal":{"type":"string","description":"Signal name"}},"required":["signal"]}},
  {"name":"debug_run_until","description":"Step the simulation until a signal reaches a target value, or max cycles exceeded.","inputSchema":{"type":"object","properties":{"signal":{"type":"string","description":"Signal name to watch"},"value":{"type":"integer","description":"Target value"},"maxCycles":{"type":"integer","description":"Maximum cycles to wait (default 10000)"}},"required":["signal","value"]}},
  {"name":"debug_reset","description":"Reset the simulation to cycle 0.","inputSchema":{"type":"object","properties":{}}},
  {"name":"debug_trace","description":"Record signal values over N clock cycles. Returns a table of cycle numbers and signal values — useful for observing how signals change over time.","inputSchema":{"type":"object","properties":{"signals":{"type":"array","items":{"type":"string"},"description":"Signal names to trace"},"n":{"type":"integer","description":"Number of cycles to trace (default 10)"}},"required":["signals"]}}
]"""

let private toolsElement = JsonDocument.Parse(toolsJson).RootElement

// ---- Tool dispatch ----

let private callTool (sim: Sim) (name: string) (args: JsonElement) : obj =
    match name with
    | "debug_status" ->
        let cps = sim.ListCheckpoints() |> List.map (fun (n, cp) -> sprintf "%s (cycle %d)" n cp.Cycle)
        let cpStr = String.Join(", ", cps)
        let text = sprintf "Cycle: %d\nSignals: %d\nForces: %d\nCheckpoints: %s" sim.Cycle sim.SignalCount sim.ForceCount cpStr
        toolResult text false

    | "debug_step" ->
        let n = if args.TryGetProperty("n") |> fst then args.GetProperty("n").GetInt32() else 1
        let cycle = sim.StepCycles(n)
        toolResult $"Stepped {n} cycles. Now at cycle {cycle}." false

    | "debug_read" ->
        let signals = args.GetProperty("signals")
        let results = System.Text.StringBuilder()
        results.AppendLine($"Cycle: {sim.Cycle}") |> ignore
        for i in 0 .. signals.GetArrayLength() - 1 do
            let name = signals.[i].GetString()
            match sim.Read(name) with
            | SimResult.Ok v -> results.AppendLine($"  {name} = {v}") |> ignore
            | SimResult.Error msg -> results.AppendLine($"  {name} = ERROR: {msg}") |> ignore
        toolResult (results.ToString().TrimEnd()) false

    | "debug_write" ->
        let signal = args.GetProperty("signal").GetString()
        let value = args.GetProperty("value").GetInt64()
        match sim.Write(signal, value) with
        | SimResult.Ok () -> toolResult $"Wrote {signal} = {value}" false
        | SimResult.Error msg -> toolResult $"Error: {msg}" true

    | "debug_signals" ->
        let filter =
            if args.TryGetProperty("filter") |> fst then args.GetProperty("filter").GetString()
            else ""
        let sigs =
            sim.ListSignals()
            |> (if String.IsNullOrEmpty(filter) then id
                else List.filter (fun (s: string) -> s.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            |> List.sort
        let listing = sigs |> List.map (fun s -> $"  {s}") |> String.concat "\n"
        toolResult $"{sigs.Length} signals:\n{listing}" false

    | "debug_checkpoint" ->
        let name = args.GetProperty("name").GetString()
        let cp = sim.SaveCheckpoint(name)
        toolResult $"Checkpoint \"{name}\" saved at cycle {cp.Cycle}." false

    | "debug_restore" ->
        let name = args.GetProperty("name").GetString()
        try
            sim.RestoreCheckpoint(name)
            toolResult $"Restored \"{name}\". Now at cycle {sim.Cycle}." false
        with ex ->
            toolResult $"Error: {ex.Message}" true

    | "debug_force" ->
        let signal = args.GetProperty("signal").GetString()
        let value = args.GetProperty("value").GetInt64()
        match sim.Force(signal, value) with
        | SimResult.Ok () -> toolResult $"Forced {signal} = {value}" false
        | SimResult.Error msg -> toolResult $"Error: {msg}" true

    | "debug_release" ->
        let signal = args.GetProperty("signal").GetString()
        match sim.Release(signal) with
        | SimResult.Ok () -> toolResult $"Released {signal}" false
        | SimResult.Error msg -> toolResult $"Error: {msg}" true

    | "debug_run_until" ->
        let signal = args.GetProperty("signal").GetString()
        let value = args.GetProperty("value").GetInt64()
        let maxCycles = if args.TryGetProperty("maxCycles") |> fst then args.GetProperty("maxCycles").GetInt32() else 10000
        let (found, cycle) = sim.RunUntilSignal(signal, value, maxCycles)
        if found then
            toolResult $"Reached {signal}=={value} at cycle {cycle}." false
        else
            toolResult $"Timeout: {signal} never reached {value} after {maxCycles} cycles (stopped at cycle {cycle})." true

    | "debug_reset" ->
        sim.Reset()
        toolResult $"Reset complete. Cycle: {sim.Cycle}." false

    | "debug_trace" ->
        let signalsEl = args.GetProperty("signals")
        let signals = [| for i in 0 .. signalsEl.GetArrayLength() - 1 -> signalsEl.[i].GetString() |] |> Array.toList
        let n = if args.TryGetProperty("n") |> fst then args.GetProperty("n").GetInt32() else 10
        let trace = sim.Trace(signals, n)
        let sb = System.Text.StringBuilder()
        // Header
        sb.Append("cycle") |> ignore
        for s in signals do sb.Append($"\t{s}") |> ignore
        sb.AppendLine() |> ignore
        // Rows
        for (cyc, vals) in trace do
            sb.Append($"{cyc}") |> ignore
            for v in vals do sb.Append($"\t{v}") |> ignore
            sb.AppendLine() |> ignore
        toolResult (sb.ToString().TrimEnd()) false

    | _ ->
        toolResult $"Unknown tool: {name}" true

// ---- Server loop ----

let runMcpServer (sim: Sim) =
    let writer = Console.Out
    let reader = Console.In

    // Stderr for server-side logging (doesn't interfere with JSON-RPC on stdout)
    let log (msg: string) = eprintfn $"[verifrog-mcp] {msg}"

    log $"Server starting. {sim.SignalCount} signals, cycle {sim.Cycle}"

    let mutable running = true
    while running do
        let line = reader.ReadLine()
        if isNull line then
            running <- false
        else
            let trimmed = line.Trim()
            if trimmed.Length = 0 then () // skip empty lines
            else
            try
                use doc = JsonDocument.Parse(trimmed)
                let root = doc.RootElement

                let method =
                    if root.TryGetProperty("method") |> fst then root.GetProperty("method").GetString()
                    else ""

                let hasId = root.TryGetProperty("id") |> fst
                let id = if hasId then root.GetProperty("id") else Unchecked.defaultof<JsonElement>

                match method with
                | "initialize" ->
                    log "initialize"
                    let result =
                        {| protocolVersion = "2024-11-05"
                           capabilities = {| tools = {||} |}
                           serverInfo = {| name = "verifrog-debug"; version = "0.1.0" |} |}
                    writeLine writer (jsonResponse id result)

                | "initialized" ->
                    // Notification — no response
                    log "initialized"

                | "tools/list" ->
                    log "tools/list"
                    // Build response manually to embed pre-built tools JSON
                    use ms = new MemoryStream()
                    use w = new Utf8JsonWriter(ms)
                    w.WriteStartObject()
                    w.WriteString("jsonrpc", "2.0")
                    w.WritePropertyName("id")
                    id.WriteTo(w)
                    w.WriteStartObject("result")
                    w.WritePropertyName("tools")
                    toolsElement.WriteTo(w)
                    w.WriteEndObject()
                    w.WriteEndObject()
                    w.Flush()
                    writeLine writer (Text.Encoding.UTF8.GetString(ms.ToArray()))

                | "tools/call" ->
                    let toolName = root.GetProperty("params").GetProperty("name").GetString()
                    let args =
                        let p = root.GetProperty("params")
                        if p.TryGetProperty("arguments") |> fst then p.GetProperty("arguments")
                        else JsonDocument.Parse("{}").RootElement
                    log $"tools/call: {toolName}"
                    let result = callTool sim toolName args
                    writeLine writer (jsonResponse id result)

                | "notifications/cancelled" ->
                    // Ignore cancellation notifications
                    ()

                | "ping" ->
                    writeLine writer (jsonResponse id {||})

                | _ ->
                    if hasId then
                        // Unknown method with id — return error
                        writeLine writer (jsonError id -32601 $"Method not found: {method}")
                    // Unknown notification — ignore

            with ex ->
                log $"Error: {ex.Message}"
                // Try to send error response if we can extract the id
                try
                    use doc = JsonDocument.Parse(trimmed)
                    let root = doc.RootElement
                    if root.TryGetProperty("id") |> fst then
                        writeLine writer (jsonError (root.GetProperty("id")) -32603 ex.Message)
                with _ -> ()

    log "Server shutting down"
