/// Convert JUnit XML test results to a Markdown report.
/// Usage: dotnet fsi junit-to-md.fsx <input.xml> [output.md]
/// If output is omitted, writes to stdout.

open System
open System.IO
open System.Xml.Linq

// ---- Types ----

type TestResult = Passed | Failed of string | Errored of string | Skipped of string

type TestCase = {
    Suite: string
    Name: string
    Time: float
    Result: TestResult
}

type SuiteSummary = {
    Name: string
    Tests: TestCase list
    Passed: int
    Failed: int
    Errored: int
    Skipped: int
    TotalTime: float
}

// ---- Parse ----

let parseTestCase (el: XElement) : TestCase =
    let rawName = el.Attribute(XName.Get "name") |> Option.ofObj |> Option.map (fun a -> a.Value) |> Option.defaultValue "unknown"
    let time = el.Attribute(XName.Get "time") |> Option.ofObj |> Option.map (fun a -> float a.Value) |> Option.defaultValue 0.0

    // Expecto names: "[Suite; test name]"
    let suite, name =
        if rawName.StartsWith("[") && rawName.Contains(";") then
            let inner = rawName.TrimStart('[').TrimEnd(']')
            let idx = inner.IndexOf(';')
            let s = inner.[..idx-1].Trim()
            let n = inner.[idx+1..].Trim()
            s, n
        else
            "Tests", rawName

    let result =
        let failure = el.Element(XName.Get "failure")
        let error = el.Element(XName.Get "error")
        let skipped = el.Element(XName.Get "skipped")
        if failure <> null then
            let msg = failure.Attribute(XName.Get "message") |> Option.ofObj |> Option.map (fun a -> a.Value) |> Option.defaultValue (failure.Value)
            Failed msg
        elif error <> null then
            let msg = error.Attribute(XName.Get "message") |> Option.ofObj |> Option.map (fun a -> a.Value) |> Option.defaultValue (error.Value)
            Errored msg
        elif skipped <> null then
            let msg = skipped.Attribute(XName.Get "message") |> Option.ofObj |> Option.map (fun a -> a.Value) |> Option.defaultValue ""
            Skipped msg
        else
            Passed

    { Suite = suite; Name = name; Time = time; Result = result }

let parseJUnit (path: string) : TestCase list =
    let doc = XDocument.Load(path)
    let testsuites =
        if doc.Root.Name.LocalName = "testsuites" then
            doc.Root.Elements(XName.Get "testsuite")
        elif doc.Root.Name.LocalName = "testsuite" then
            seq { doc.Root }
        else
            Seq.empty
    [ for suite in testsuites do
        for tc in suite.Elements(XName.Get "testcase") do
            yield parseTestCase tc ]

// ---- Summarize ----

let summarize (tests: TestCase list) : SuiteSummary list =
    tests
    |> List.groupBy (fun t -> t.Suite)
    |> List.map (fun (suite, cases) ->
        let sorted = cases |> List.sortBy (fun t -> t.Name)
        { Name = suite
          Tests = sorted
          Passed = sorted |> List.filter (fun t -> t.Result = Passed) |> List.length
          Failed = sorted |> List.filter (fun t -> match t.Result with Failed _ -> true | _ -> false) |> List.length
          Errored = sorted |> List.filter (fun t -> match t.Result with Errored _ -> true | _ -> false) |> List.length
          Skipped = sorted |> List.filter (fun t -> match t.Result with Skipped _ -> true | _ -> false) |> List.length
          TotalTime = sorted |> List.sumBy (fun t -> t.Time) })
    |> List.sortBy (fun s -> s.Name)

// ---- Render ----

let resultIcon (r: TestResult) =
    match r with
    | Passed -> "PASS"
    | Failed _ -> "FAIL"
    | Errored _ -> "ERR"
    | Skipped _ -> "SKIP"

let formatTime (t: float) =
    if t >= 1.0 then sprintf "%.2fs" t
    elif t >= 0.001 then sprintf "%.0fms" (t * 1000.0)
    elif t > 0.0 then sprintf "%.1fms" (t * 1000.0)
    else "<1ms"

let renderMarkdown (suites: SuiteSummary list) (totalTime: float) : string =
    let sb = System.Text.StringBuilder()
    let w (s: string) = sb.AppendLine(s) |> ignore
    let wf fmt = Printf.kprintf w fmt

    let totalTests = suites |> List.sumBy (fun s -> s.Tests.Length)
    let totalPassed = suites |> List.sumBy (fun s -> s.Passed)
    let totalFailed = suites |> List.sumBy (fun s -> s.Failed)
    let totalErrored = suites |> List.sumBy (fun s -> s.Errored)
    let totalSkipped = suites |> List.sumBy (fun s -> s.Skipped)
    let allPassed = totalFailed = 0 && totalErrored = 0

    // Header
    w "# Test Results"
    w ""

    // Summary badge
    if allPassed then
        wf "**%d tests passed** in %s" totalTests (formatTime totalTime)
    else
        wf "**%d passed, %d failed, %d errored** out of %d tests in %s"
            totalPassed totalFailed totalErrored totalTests (formatTime totalTime)
    if totalSkipped > 0 then
        wf " (%d skipped)" totalSkipped
    w ""

    // Summary table
    w "| Suite | Passed | Failed | Errored | Skipped | Time |"
    w "|-------|-------:|-------:|--------:|--------:|-----:|"
    for s in suites do
        let status = if s.Failed = 0 && s.Errored = 0 then "" else " **"
        wf "| %s%s | %d | %d | %d | %d | %s |"
            s.Name status s.Passed s.Failed s.Errored s.Skipped (formatTime s.TotalTime)
    w ""

    // Per-suite details
    for s in suites do
        wf "## %s" s.Name
        w ""
        w "| Status | Test | Time |"
        w "|--------|------|-----:|"
        for t in s.Tests do
            let icon = resultIcon t.Result
            wf "| %s | %s | %s |" icon t.Name (formatTime t.Time)
        w ""

        // Show failure details
        let failures = s.Tests |> List.filter (fun t -> match t.Result with Failed _ | Errored _ -> true | _ -> false)
        if not failures.IsEmpty then
            w "### Failures"
            w ""
            for t in failures do
                let msg = match t.Result with Failed m | Errored m -> m | _ -> ""
                wf "**%s**" t.Name
                w ""
                w "```"
                w msg
                w "```"
                w ""

    sb.ToString()

// ---- Main ----

let args = Environment.GetCommandLineArgs() |> Array.skip 1  // skip "dotnet" and script path
// fsi puts args after "--" or the script name; handle both
let realArgs =
    let idx = args |> Array.tryFindIndex (fun a -> a.EndsWith(".fsx"))
    match idx with
    | Some i -> args.[i+1..]
    | None -> args

if realArgs.Length = 0 then
    eprintfn "Usage: dotnet fsi junit-to-md.fsx <input.xml> [output.md]"
    exit 1

let inputPath = realArgs.[0]
let outputPath = if realArgs.Length > 1 then Some realArgs.[1] else None

if not (File.Exists inputPath) then
    eprintfn "Error: file not found: %s" inputPath
    exit 2

let tests = parseJUnit inputPath
let suites = summarize tests
let totalTime = tests |> List.sumBy (fun t -> t.Time)
let markdown = renderMarkdown suites totalTime

match outputPath with
| Some path ->
    File.WriteAllText(path, markdown)
    eprintfn "Wrote %s (%d tests, %d suites)" path tests.Length suites.Length
| None ->
    printf "%s" markdown
