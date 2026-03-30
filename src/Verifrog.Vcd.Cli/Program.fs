open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open Verifrog.Vcd

// ---- Arg parsing ----

type CliArgs = {
    Filename: string
    MaxTimeNs: int64
    Debug: bool
    SignalPatterns: string list
    JsonOutput: bool
}

let parseArgs (argv: string[]) : Result<CliArgs, string> =
    if argv.Length = 0 then
        Error "No filename provided"
    else
        let mutable filename = ""
        let mutable maxTimeNs = 0L
        let mutable debug = false
        let mutable jsonOutput = false
        let patterns = ResizeArray<string>()
        let mutable i = 0
        let mutable error = None

        while i < argv.Length && error.IsNone do
            match argv.[i] with
            | "--debug" | "--verbose" ->
                debug <- true
                i <- i + 1
            | "--json" ->
                jsonOutput <- true
                i <- i + 1
            | "--signal" ->
                if i + 1 < argv.Length then
                    patterns.Add(argv.[i + 1])
                    i <- i + 2
                else
                    error <- Some "--signal requires a pattern argument"
            | arg when arg.StartsWith("--") ->
                error <- Some (sprintf "Unknown option: %s" arg)
            | arg ->
                if filename = "" then
                    filename <- arg
                else
                    match Int64.TryParse(arg) with
                    | true, v -> maxTimeNs <- v
                    | _ -> error <- Some (sprintf "Unknown argument: %s" arg)
                i <- i + 1

        match error with
        | Some msg -> Error msg
        | None when filename = "" -> Error "No filename provided"
        | None ->
            Ok { Filename = filename
                 MaxTimeNs = maxTimeNs
                 Debug = debug
                 SignalPatterns = patterns |> Seq.toList
                 JsonOutput = jsonOutput }

// ---- Reporting helpers ----

let inline psToUs (ps: int64) = float ps / 1e6

let uniqueIntValues (trans: VcdTransition list) =
    trans |> List.map (fun t -> t.IntVal) |> List.distinct |> List.sort

let printTextReport (args: CliArgs) (vcd: VcdFile) =
    let trackedCount = vcd.Transitions.Count

    // Header
    printfn "=== VCD Analysis: %s ===" (Path.GetFileName args.Filename)
    printfn "Total signals in file: %d" vcd.Signals.Length
    printfn "Signals with transitions: %d" trackedCount
    if args.SignalPatterns.Length > 0 then
        printfn "Filter patterns: %s" (String.Join(", ", args.SignalPatterns))
    if args.MaxTimeNs > 0L then
        printfn "Max time: %d ns" args.MaxTimeNs
    printfn ""

    // Per-signal summary
    printfn "%-50s %6s %8s  %-20s  %-20s  %s"
        "Signal" "Width" "Trans" "First" "Last" "Unique Values"
    printfn "%s" (String.replicate 130 "-")

    let paths =
        vcd.Transitions.Keys
        |> Seq.toList
        |> List.sort

    for fullPath in paths do
        let trans = vcd.Transitions.[fullPath]
        let signal =
            vcd.Signals |> List.tryFind (fun s -> s.FullPath = fullPath)
        let width =
            match signal with Some s -> s.Width | None -> 0

        let count = trans.Length
        let firstVal =
            match trans with
            | h :: _ -> if h.IntVal >= 0 then sprintf "%d" h.IntVal else h.Bits
            | [] -> "-"
        let lastVal =
            match trans with
            | _ :: _ ->
                let last = trans |> List.last
                if last.IntVal >= 0 then sprintf "%d" last.IntVal else last.Bits
            | [] -> "-"
        let uniq = uniqueIntValues trans
        let uniqStr =
            if uniq.Length <= 10 then
                sprintf "{%s}" (String.Join(", ", uniq))
            else
                sprintf "{%s, ...} (%d unique)" (String.Join(", ", uniq |> List.take 10)) uniq.Length

        printfn "%-50s %6d %8d  %-20s  %-20s  %s"
            fullPath width count firstVal lastVal uniqStr

    // Debug/verbose: print transition timelines
    if args.Debug then
        printfn ""
        printfn "%s" (String.replicate 60 "=")
        printfn "TRANSITION DETAILS (debug mode)"
        printfn "%s" (String.replicate 60 "=")

        for fullPath in paths do
            let trans = vcd.Transitions.[fullPath]
            if trans.Length > 0 then
                printfn ""
                printfn "--- %s (%d transitions) ---" fullPath trans.Length
                let limit = min 50 trans.Length
                for i in 0 .. limit - 1 do
                    let t = trans.[i]
                    if t.IntVal >= 0 then
                        printfn "  t=%12.3f us  val=%d (0x%x)" (psToUs t.Time) t.IntVal t.IntVal
                    else
                        printfn "  t=%12.3f us  val=%s" (psToUs t.Time) t.Bits
                if trans.Length > limit then
                    printfn "  ... (%d more transitions)" (trans.Length - limit)

let printJsonReport (args: CliArgs) (vcd: VcdFile) =
    let options = JsonSerializerOptions(WriteIndented = true)

    let signalData =
        vcd.Transitions.Keys
        |> Seq.toList
        |> List.sort
        |> List.map (fun fullPath ->
            let trans = vcd.Transitions.[fullPath]
            let signal = vcd.Signals |> List.tryFind (fun s -> s.FullPath = fullPath)
            let width = match signal with Some s -> s.Width | None -> 0
            let uniq = uniqueIntValues trans
            let firstVal = match trans with h :: _ -> h.IntVal | [] -> -1
            let lastVal = match trans with _ :: _ -> (trans |> List.last).IntVal | [] -> -1

            let obj = dict [
                "path", box fullPath
                "width", box width
                "transition_count", box trans.Length
                "first_value", box firstVal
                "last_value", box lastVal
                "unique_values", box (uniq |> List.toArray)
            ]

            if args.Debug then
                let limit = min 200 trans.Length
                let timeline =
                    trans
                    |> List.take limit
                    |> List.map (fun t ->
                        dict [
                            "time", box t.Time
                            "time_us", box (psToUs t.Time)
                            "value", box t.IntVal
                            "bits", box t.Bits
                        ])
                    |> List.toArray
                let extended = Dictionary<string, obj>(obj)
                extended.["transitions"] <- box timeline
                extended :> Collections.Generic.IDictionary<string, obj>
            else
                obj)
        |> List.toArray

    let report = dict [
        "filename", box (Path.GetFileName args.Filename)
        "total_signals", box vcd.Signals.Length
        "tracked_signals", box vcd.Transitions.Count
        "max_time_ns", box args.MaxTimeNs
        "signal_patterns", box (args.SignalPatterns |> List.toArray)
        "signals", box signalData
    ]

    let json = JsonSerializer.Serialize(report, options)
    printfn "%s" json

// ---- Entry point ----

let usage () =
    eprintfn "Usage: verifrog-vcd <file.vcd> [max_time_ns] [--debug] [--signal <pattern>] [--json]"
    eprintfn ""
    eprintfn "Options:"
    eprintfn "  max_time_ns        Stop parsing after this simulation time (nanoseconds)"
    eprintfn "  --debug, --verbose Show full transition timelines (up to 50 per signal)"
    eprintfn "  --signal <pattern> Only track signals matching pattern (repeatable)"
    eprintfn "                     Patterns: exact leaf name, full path, or prefix*"
    eprintfn "  --json             Output report as JSON"

[<EntryPoint>]
let main argv =
    match parseArgs argv with
    | Error msg ->
        eprintfn "Error: %s" msg
        usage ()
        1
    | Ok args ->
        if not (File.Exists args.Filename) then
            eprintfn "Error: file not found: %s" args.Filename
            2
        else
            let sw = Diagnostics.Stopwatch.StartNew()

            // Convert ns to ps for the library (VCD timestamps are in ps)
            let maxTimePs =
                if args.MaxTimeNs > 0L then args.MaxTimeNs * 1000L
                else 0L

            let vcd =
                if args.SignalPatterns.Length > 0 then
                    VcdParser.parseFiltered args.Filename args.SignalPatterns maxTimePs
                else
                    VcdParser.parse args.Filename maxTimePs

            let parseMs = sw.ElapsedMilliseconds
            eprintfn "[%.1f s] Parsed: %d signals, %d tracked, %d transitions"
                (float parseMs / 1000.0)
                vcd.Signals.Length
                vcd.Transitions.Count
                (vcd.Transitions.Values |> Seq.sumBy (fun t -> t.Length))

            if args.JsonOutput then
                printJsonReport args vcd
            else
                printTextReport args vcd

            eprintfn "[%.1f s] Done." (float sw.ElapsedMilliseconds / 1000.0)
            0
