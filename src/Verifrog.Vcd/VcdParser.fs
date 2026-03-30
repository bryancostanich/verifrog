namespace Verifrog.Vcd

open System
open System.Collections.Generic
open System.IO

/// Signal metadata from VCD header
type VcdSignal = {
    /// Short identifier (e.g., "!")
    Id: string
    /// Leaf name in the module hierarchy (e.g., "count")
    LeafName: string
    /// Full hierarchical path (e.g., "counter.count")
    FullPath: string
    /// Bit width
    Width: int
}

/// A single value transition
[<Struct>]
type VcdTransition = {
    /// Time in VCD time units (typically picoseconds)
    Time: int64
    /// Raw bit string (e.g., "01101")
    Bits: string
    /// Integer value (-1 if unknown/x/z)
    IntVal: int
}

/// Parsed VCD file with signal data and transitions
type VcdFile = {
    /// All signals declared in the VCD header
    Signals: VcdSignal list
    /// Signal ID -> VcdSignal lookup
    SignalMap: IReadOnlyDictionary<string, VcdSignal>
    /// Signal full path -> list of transitions (time-ordered)
    Transitions: IReadOnlyDictionary<string, VcdTransition list>
}

/// VCD parsing and analysis API.
/// Extracted from khalkulo's vcd_parser, refactored from CLI to library.
module VcdParser =

    // ---- Helpers ----

    /// Parse a binary string to an integer. x/z bits treated as 0. Returns -1 on error.
    let parseBinValue (s: string) : int =
        let mutable result = 0
        let mutable ok = true
        for c in s do
            result <- result <<< 1
            match c with
            | '0' -> ()
            | '1' -> result <- result ||| 1
            | 'x' | 'X' | 'z' | 'Z' -> ()
            | _ -> ok <- false
        if ok then result else -1

    /// Convert VCD time units to microseconds (assumes picosecond timescale)
    let timeToUs (timePs: int64) : float = float timePs / 1e6

    // ---- Phase 1: Parse header ----

    let private parseHeader (reader: StreamReader) : Dictionary<string, VcdSignal> =
        let signals = Dictionary<string, VcdSignal>()
        let scopeStack = Stack<string>()
        let mutable line = reader.ReadLine()
        while line <> null do
            let trimmed = line.AsSpan().Trim()
            if trimmed.StartsWith("$scope".AsSpan()) then
                let parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                if parts.Length >= 3 then
                    scopeStack.Push(parts.[2])
            elif trimmed.StartsWith("$upscope".AsSpan()) then
                if scopeStack.Count > 0 then
                    scopeStack.Pop() |> ignore
            elif trimmed.StartsWith("$var".AsSpan()) then
                let parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                if parts.Length >= 5 then
                    let width = Int32.Parse(parts.[2])
                    let sid = parts.[3]
                    let name = parts.[4]
                    let scopeArr = scopeStack.ToArray()
                    Array.Reverse(scopeArr)
                    let fullPath = String.Join(".", Array.append scopeArr [| name |])
                    signals.[sid] <- { Id = sid; LeafName = name; FullPath = fullPath; Width = width }
            elif trimmed.IndexOf("$enddefinitions".AsSpan()) >= 0 then
                line <- null  // break
            else
                ()
            if line <> null then
                line <- reader.ReadLine()
        signals

    // ---- Phase 2: Parse transitions ----

    let private parseTransitions (reader: StreamReader) (tracked: Dictionary<string, VcdSignal>) (maxTime: int64) : Dictionary<string, ResizeArray<VcdTransition>> =
        let transLists = Dictionary<string, ResizeArray<VcdTransition>>()
        let getList (path: string) =
            match transLists.TryGetValue(path) with
            | true, l -> l
            | _ ->
                let l = ResizeArray<VcdTransition>()
                transLists.[path] <- l
                l

        let mutable pastDefs = false
        let mutable currentTime = 0L
        let mutable line = reader.ReadLine()

        while line <> null do
            if not pastDefs then
                if line.Contains("$enddefinitions") then
                    pastDefs <- true
            else
                let len = line.Length
                if len > 0 then
                    let c0 = line.[0]
                    if c0 = '#' then
                        currentTime <- Int64.Parse(line.AsSpan().Slice(1))
                        if maxTime > 0L && currentTime > maxTime then
                            line <- null
                    elif c0 = '0' || c0 = '1' || c0 = 'x' || c0 = 'z' || c0 = 'X' || c0 = 'Z' then
                        if len >= 2 then
                            let sid = line.Substring(1)
                            match tracked.TryGetValue(sid) with
                            | true, info ->
                                let valStr = string c0
                                let intVal = match c0 with '0' -> 0 | '1' -> 1 | _ -> -1
                                (getList info.FullPath).Add({ Time = currentTime; Bits = valStr; IntVal = intVal })
                            | _ -> ()
                    elif c0 = 'b' then
                        let spaceIdx = line.IndexOf(' ', 1)
                        if spaceIdx > 0 && spaceIdx < len - 1 then
                            let sid = line.Substring(spaceIdx + 1)
                            match tracked.TryGetValue(sid) with
                            | true, info ->
                                let bits = line.Substring(1, spaceIdx - 1)
                                let intVal = parseBinValue bits
                                (getList info.FullPath).Add({ Time = currentTime; Bits = bits; IntVal = intVal })
                            | _ -> ()
                    else
                        ()

            if line <> null then
                line <- reader.ReadLine()

        transLists

    // ---- Public API ----

    let private toReadOnlyDict (d: seq<string * 'T>) : IReadOnlyDictionary<string, 'T> =
        let dict = Dictionary<string, 'T>()
        for (k, v) in d do dict.[k] <- v
        dict :> IReadOnlyDictionary<_,_>

    /// Parse a VCD file. Tracks all signals.
    /// maxTime: stop parsing after this time (0 = parse entire file).
    let parse (filename: string) (maxTime: int64) : VcdFile =
        let maxT = maxTime

        // Parse header
        let headerSignals =
            use reader = new StreamReader(filename, Text.Encoding.ASCII, false, 1 <<< 16)
            parseHeader reader

        // Parse transitions (track all signals)
        let trans =
            use reader = new StreamReader(filename, Text.Encoding.ASCII, false, 1 <<< 16)
            parseTransitions reader headerSignals maxT

        let signalList = [ for kv in headerSignals -> kv.Value ]
        let signalMap = headerSignals |> Seq.map (fun kv -> kv.Key, kv.Value) |> toReadOnlyDict
        let transMap =
            trans
            |> Seq.map (fun kv -> kv.Key, kv.Value |> Seq.toList)
            |> toReadOnlyDict

        { Signals = signalList
          SignalMap = signalMap
          Transitions = transMap }

    /// Parse all signals with no time limit
    let parseAll (filename: string) : VcdFile = parse filename 0L

    /// Parse a VCD stream. Tracks only specified signal patterns.
    let parseFiltered (filename: string) (patterns: string list) (maxTime: int64) : VcdFile =
        let maxT = maxTime

        let headerSignals =
            use reader = new StreamReader(filename, Text.Encoding.ASCII, false, 1 <<< 16)
            parseHeader reader

        // Filter to only track matching signals
        let tracked = Dictionary<string, VcdSignal>()
        for kv in headerSignals do
            let info = kv.Value
            let matches =
                patterns |> List.exists (fun pat ->
                    if pat.Contains("*") then
                        let prefix = pat.Replace("*", "")
                        info.FullPath.Contains(prefix) || info.LeafName.StartsWith(prefix)
                    else
                        info.FullPath.EndsWith("." + pat) || info.FullPath = pat || info.LeafName = pat)
            if matches then
                tracked.[kv.Key] <- info

        let trans =
            use reader = new StreamReader(filename, Text.Encoding.ASCII, false, 1 <<< 16)
            parseTransitions reader tracked maxT

        let signalList = [ for kv in headerSignals -> kv.Value ]
        let signalMap = headerSignals |> Seq.map (fun kv -> kv.Key, kv.Value) |> toReadOnlyDict
        let transMap =
            trans
            |> Seq.map (fun kv -> kv.Key, kv.Value |> Seq.toList)
            |> toReadOnlyDict

        { Signals = signalList
          SignalMap = signalMap
          Transitions = transMap }

    // ---- Query API ----

    /// Find signals matching a name pattern (glob-like: * matches any substring)
    let findSignals (vcd: VcdFile) (pattern: string) : VcdSignal list =
        if pattern.Contains("*") then
            let prefix = pattern.Replace("*", "")
            vcd.Signals |> List.filter (fun s ->
                s.FullPath.Contains(prefix) || s.LeafName.StartsWith(prefix))
        else
            vcd.Signals |> List.filter (fun s ->
                s.FullPath.EndsWith("." + pattern) || s.FullPath = pattern || s.LeafName = pattern)

    /// Get transitions for a signal by its full path
    let transitions (vcd: VcdFile) (fullPath: string) : VcdTransition list =
        match vcd.Transitions.TryGetValue(fullPath) with
        | true, t -> t
        | _ -> []

    /// Get the value of a signal at a specific time (last transition before or at time)
    let valueAtTime (vcd: VcdFile) (fullPath: string) (time: int64) : VcdTransition option =
        match vcd.Transitions.TryGetValue(fullPath) with
        | true, trans ->
            trans |> List.tryFindBack (fun t -> t.Time <= time)
        | _ -> None

    /// Count transitions for a signal
    let transitionCount (vcd: VcdFile) (fullPath: string) : int =
        match vcd.Transitions.TryGetValue(fullPath) with
        | true, t -> t.Length
        | _ -> 0

    /// Get the time of the first transition where the signal has a specific value
    let firstTimeAtValue (vcd: VcdFile) (fullPath: string) (value: int) : int64 option =
        match vcd.Transitions.TryGetValue(fullPath) with
        | true, trans ->
            trans |> List.tryFind (fun t -> t.IntVal = value) |> Option.map (fun t -> t.Time)
        | _ -> None

    /// Get all unique values a signal takes
    let uniqueValues (vcd: VcdFile) (fullPath: string) : int list =
        match vcd.Transitions.TryGetValue(fullPath) with
        | true, trans ->
            trans |> List.map (fun t -> t.IntVal) |> List.distinct |> List.sort
        | _ -> []

    /// Get high-pulse count (transitions to value 1) for a 1-bit signal
    let highPulseCount (vcd: VcdFile) (fullPath: string) : int =
        match vcd.Transitions.TryGetValue(fullPath) with
        | true, trans -> trans |> List.filter (fun t -> t.IntVal = 1) |> List.length
        | _ -> 0
