module Verifrog.Runner.Iverilog

open System
open System.Diagnostics
open System.IO
open Verifrog.Sim.Config

/// Result of running an iverilog simulation
type IverilogResult = {
    ExitCode: int
    Stdout: string
    Stderr: string
    ElapsedMs: int64
}

/// Run a shell process and capture output
let private runProcess (cmd: string) (args: string) (workDir: string) (timeoutMs: int) : IverilogResult =
    let psi = ProcessStartInfo(cmd, args)
    psi.WorkingDirectory <- workDir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.CreateNoWindow <- true

    let sw = Stopwatch.StartNew()
    use proc = Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    let exited = proc.WaitForExit(timeoutMs)
    sw.Stop()

    if not exited then
        proc.Kill()
        { ExitCode = -1; Stdout = stdout; Stderr = "TIMEOUT"; ElapsedMs = sw.ElapsedMilliseconds }
    else
        { ExitCode = proc.ExitCode; Stdout = stdout; Stderr = stderr; ElapsedMs = sw.ElapsedMilliseconds }

/// Compile and run a Verilog testbench with iverilog/vvp.
///
/// projectRoot: root directory of the project
/// config: parsed verifrog.toml
/// testbenchName: name without .v extension (e.g., "counter_tb")
/// overrides: parameter overrides (e.g., [("TIMEOUT", "10000")])
/// extraSources: additional .v files beyond what's in TOML
let run (projectRoot: string) (config: VerifrogConfig) (testbenchName: string) (overrides: (string * string) list) (extraSources: string list) : IverilogResult =
    let scratchDir = Path.Combine(projectRoot, config.Test.Output)
    let testOutputDir = Path.Combine(projectRoot, config.Test.TestOutput)
    Directory.CreateDirectory(scratchDir) |> ignore
    Directory.CreateDirectory(testOutputDir) |> ignore

    let iverilogCfg = config.Iverilog |> Option.defaultValue { Testbenches = []; Models = [] }

    // Find the testbench file by searching testbench directories
    let tbFile =
        iverilogCfg.Testbenches
        |> List.tryPick (fun pattern ->
            let dir = Path.GetDirectoryName(Path.Combine(projectRoot, pattern))
            let candidate = Path.Combine(dir, $"{testbenchName}.v")
            if File.Exists(candidate) then Some candidate else None)
        |> Option.defaultWith (fun () ->
            // Fallback: look in scratch dir
            Path.Combine(scratchDir, $"{testbenchName}.v"))

    if not (File.Exists(tbFile)) then
        { ExitCode = -1; Stdout = ""; Stderr = $"Testbench not found: {tbFile}"; ElapsedMs = 0 }
    else

    let vvpFile = Path.Combine(scratchDir, $"{testbenchName}.vvp")

    // Build source list from TOML design.sources
    let rtlSources = config.Design.Sources |> List.map (fun s -> Path.Combine(projectRoot, s)) |> String.concat " "

    // Model sources from TOML
    let modelSources = iverilogCfg.Models |> List.map (fun s -> Path.Combine(projectRoot, s)) |> String.concat " "

    // Parameter overrides
    let paramFlags =
        overrides |> List.map (fun (name, value) -> $"-P {testbenchName}.{name}={value}") |> String.concat " "

    let extraSourceStr = extraSources |> String.concat " "
    let iverilogCmd = $"iverilog -o {vvpFile} {paramFlags} {rtlSources} {tbFile} {modelSources} {extraSourceStr}"

    // Compile
    let compileResult = runProcess "bash" $"-c \"{iverilogCmd}\"" projectRoot 60_000
    if compileResult.ExitCode <> 0 then
        { compileResult with Stderr = $"iverilog compilation failed:\n{compileResult.Stderr}" }
    else

    // Run (use testOutputDir as cwd so $dumpfile VCDs land there)
    let sw = Stopwatch.StartNew()
    let runResult = runProcess "vvp" vvpFile testOutputDir 300_000
    sw.Stop()
    { runResult with ElapsedMs = sw.ElapsedMilliseconds }

/// Run with no overrides or extra sources
let runSimple (projectRoot: string) (config: VerifrogConfig) (testbenchName: string) : IverilogResult =
    run projectRoot config testbenchName [] []

/// Auto-detect if a testbench needs I2C BFM (by name heuristic)
let runAuto (projectRoot: string) (config: VerifrogConfig) (testbenchName: string) : IverilogResult =
    let iverilogCfg = config.Iverilog |> Option.defaultValue { Testbenches = []; Models = [] }
    let extras =
        if testbenchName.Contains("i2c", StringComparison.OrdinalIgnoreCase) then
            iverilogCfg.Models |> List.filter (fun m -> m.Contains("i2c", StringComparison.OrdinalIgnoreCase))
                               |> List.map (fun m -> Path.Combine(projectRoot, m))
        else
            []
    run projectRoot config testbenchName [] extras

/// Discover all testbench files matching TOML patterns
let discover (projectRoot: string) (config: VerifrogConfig) : string list =
    let iverilogCfg = config.Iverilog |> Option.defaultValue { Testbenches = []; Models = [] }
    [ for pattern in iverilogCfg.Testbenches do
        let dir = Path.GetDirectoryName(Path.Combine(projectRoot, pattern))
        let filePattern = Path.GetFileName(pattern)
        if Directory.Exists(dir) then
            yield! Directory.GetFiles(dir, filePattern)
                   |> Array.map Path.GetFileNameWithoutExtension
                   |> Array.sort ]

/// Check if stdout indicates all tests passed
let passed (result: IverilogResult) : bool =
    result.ExitCode = 0 &&
    (result.Stdout.Contains("ALL TESTS PASSED") ||
     result.Stdout.Contains("PASSED"))

/// Parse pass/fail counts from stdout
let parseSummary (stdout: string) : (int * int) option =
    let lines = stdout.Split('\n')
    lines
    |> Array.tryPick (fun line ->
        let m = System.Text.RegularExpressions.Regex.Match(line, @"(\d+)\s+passed.*?(\d+)\s+failed")
        if m.Success then
            Some (int m.Groups.[1].Value, int m.Groups.[2].Value)
        else None)
