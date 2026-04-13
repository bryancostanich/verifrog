module Verifrog.Runner.Iverilog

open System
open System.Diagnostics
open System.IO
open System.Threading
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

/// Build a .vvp filename that encodes parameter overrides so different
/// configurations get separate cached binaries.
let private vvpFileName (testbenchName: string) (overrides: (string * string) list) : string =
    if overrides.IsEmpty then
        $"{testbenchName}.vvp"
    else
        let suffix = overrides |> List.map (fun (k, v) -> $"{k}_{v}") |> String.concat "_"
        $"{testbenchName}__{suffix}.vvp"

/// Check if the .vvp file is up to date: exists and is newer than all source files.
let private isUpToDate (vvpFile: string) (sourceFiles: string list) : bool =
    if not (File.Exists(vvpFile)) then false
    else
        let vvpTime = File.GetLastWriteTimeUtc(vvpFile)
        sourceFiles |> List.forall (fun src ->
            if File.Exists(src) then File.GetLastWriteTimeUtc(src) <= vvpTime
            else true)  // missing source file will cause compile error anyway

/// Acquire a file lock for compilation dedup. Returns IDisposable to release.
/// If another process is already compiling, waits up to timeoutMs.
let private acquireCompileLock (vvpFile: string) (timeoutMs: int) : IDisposable option =
    let lockFile = vvpFile + ".lock"
    try
        // FileShare.None ensures exclusive access
        let fs = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)
        Some (fs :> IDisposable)
    with
    | :? IOException ->
        // Another process holds the lock — wait and poll
        let sw = Stopwatch.StartNew()
        let mutable acquired = false
        let mutable result : IDisposable option = None
        while not acquired && sw.ElapsedMilliseconds < int64 timeoutMs do
            Thread.Sleep(200)
            try
                let fs = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)
                result <- Some (fs :> IDisposable)
                acquired <- true
            with :? IOException -> ()
        result

/// Compile and run a Verilog testbench with iverilog/vvp.
///
/// projectRoot: root directory of the project
/// config: parsed verifrog.toml
/// testbenchName: name without .v extension (e.g., "counter_tb")
/// overrides: parameter overrides (e.g., [("TIMEOUT", "10000")])
/// extraSources: additional .v files beyond what's in TOML
let run (projectRoot: string) (config: VerifrogConfig) (testbenchName: string) (overrides: (string * string) list) (extraSources: string list) : IverilogResult =
    let scratchDir = config.Test.Output         // Already absolute from Config.parse
    let testOutputDir = config.Test.TestOutput   // Already absolute from Config.parse
    Directory.CreateDirectory(scratchDir) |> ignore
    Directory.CreateDirectory(testOutputDir) |> ignore

    let iverilogCfg = config.Iverilog |> Option.defaultValue { Testbenches = []; Models = [] }

    // Find the testbench file by searching testbench directories
    // Testbench patterns are already absolute paths from Config.parse
    let tbFile =
        iverilogCfg.Testbenches
        |> List.tryPick (fun pattern ->
            let dir = Path.GetDirectoryName(pattern)
            let candidate = Path.Combine(dir, $"{testbenchName}.v")
            if File.Exists(candidate) then Some candidate else None)
        |> Option.defaultWith (fun () ->
            // Fallback: look in scratch dir
            Path.Combine(scratchDir, $"{testbenchName}.v"))

    if not (File.Exists(tbFile)) then
        { ExitCode = -1; Stdout = ""; Stderr = $"Testbench not found: {tbFile}"; ElapsedMs = 0 }
    else

    let vvpFile = Path.Combine(scratchDir, vvpFileName testbenchName overrides)

    // All source files for cache invalidation
    let allSources = config.Design.Sources @ [tbFile] @ iverilogCfg.Models @ extraSources

    // Sources are already absolute paths from Config.parse
    let rtlSources = config.Design.Sources |> String.concat " "

    // Model sources are already absolute paths from Config.parse
    let modelSources = iverilogCfg.Models |> String.concat " "

    // Parameter overrides
    let paramFlags =
        overrides |> List.map (fun (name, value) -> $"-P {testbenchName}.{name}={value}") |> String.concat " "

    let extraSourceStr = extraSources |> String.concat " "
    let iverilogCmd = $"iverilog -o {vvpFile} {paramFlags} {rtlSources} {tbFile} {modelSources} {extraSourceStr}"

    // Compile with caching and dedup lock
    let compileError =
        if isUpToDate vvpFile allSources then
            None  // Cache hit — skip compilation
        else
            // Acquire lock to prevent parallel duplicate compilations
            let lockOpt = acquireCompileLock vvpFile 120_000
            try
                // Re-check after acquiring lock (another process may have compiled)
                if isUpToDate vvpFile allSources then
                    None
                else
                    let compileResult = runProcess "bash" $"-c \"{iverilogCmd}\"" projectRoot 60_000
                    if compileResult.ExitCode <> 0 then
                        Some { compileResult with Stderr = $"iverilog compilation failed:\n{compileResult.Stderr}" }
                    else
                        None
            finally
                lockOpt |> Option.iter (fun l -> l.Dispose())

    match compileError with
    | Some err -> err
    | None ->

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
    // Model paths are already absolute from Config.parse
    let extras =
        if testbenchName.Contains("i2c", StringComparison.OrdinalIgnoreCase) then
            iverilogCfg.Models |> List.filter (fun m -> m.Contains("i2c", StringComparison.OrdinalIgnoreCase))
        else
            []
    run projectRoot config testbenchName [] extras

/// Discover all testbench files matching TOML patterns
let discover (projectRoot: string) (config: VerifrogConfig) : string list =
    let iverilogCfg = config.Iverilog |> Option.defaultValue { Testbenches = []; Models = [] }
    // Testbench patterns are already absolute paths from Config.parse
    [ for pattern in iverilogCfg.Testbenches do
        let dir = Path.GetDirectoryName(pattern)
        let filePattern = Path.GetFileName(pattern)
        if Directory.Exists(dir) then
            yield! Directory.GetFiles(dir, filePattern)
                   |> Array.map Path.GetFileNameWithoutExtension
                   |> Array.sort ]

// ---- TOML-based convenience functions ----
// These read config from verifrog.toml and derive projectRoot automatically,
// eliminating the need for design-specific wrapper files.

/// Cached config holder to avoid re-parsing on every call
let mutable private cachedConfig: (string * string * VerifrogConfig) option = None

let private getConfig (tomlPath: string) =
    let fullPath = Path.GetFullPath(tomlPath)
    match cachedConfig with
    | Some (path, root, cfg) when path = fullPath -> (root, cfg)
    | _ ->
        let cfg = Verifrog.Sim.Config.parse fullPath
        let root = Path.GetDirectoryName(fullPath)
        cachedConfig <- Some (fullPath, root, cfg)
        (root, cfg)

/// Run a testbench using verifrog.toml for config and project root.
///
/// Usage:
///   let result = Iverilog.runFromToml "verifrog.toml" "dpc_roundtrip_tb" [] []
let runFromToml (tomlPath: string) (testbenchName: string) (overrides: (string * string) list) (extraSources: string list) : IverilogResult =
    let (root, cfg) = getConfig tomlPath
    run root cfg testbenchName overrides extraSources

/// Run a testbench with no overrides, using verifrog.toml.
///
/// Usage:
///   let result = Iverilog.runSimpleFromToml "verifrog.toml" "shift_reg_tb"
let runSimpleFromToml (tomlPath: string) (testbenchName: string) : IverilogResult =
    let (root, cfg) = getConfig tomlPath
    runSimple root cfg testbenchName

/// Auto-detect BFM dependencies, using verifrog.toml.
let runAutoFromToml (tomlPath: string) (testbenchName: string) : IverilogResult =
    let (root, cfg) = getConfig tomlPath
    runAuto root cfg testbenchName

/// Discover all testbenches matching TOML patterns, using verifrog.toml.
let discoverFromToml (tomlPath: string) : string list =
    let (root, cfg) = getConfig tomlPath
    discover root cfg

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
