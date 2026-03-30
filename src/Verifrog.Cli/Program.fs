module Verifrog.Cli.Program

open System
open System.Diagnostics
open System.IO
open Verifrog.Sim.Config

// ---- Helpers ----

let private runCmd (cmd: string) (args: string) (workDir: string) =
    let psi = ProcessStartInfo(cmd, args)
    psi.WorkingDirectory <- workDir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    use proc = Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    if proc.ExitCode <> 0 then
        eprintfn "%s" stderr
    (proc.ExitCode, stdout, stderr)

let private findVerilator () =
    let (rc, stdout, _) = runCmd "which" "verilator" "."
    if rc = 0 then stdout.Trim() else "verilator"

let private findMake () =
    let shimMakefile =
        // Look for the shim Makefile relative to the tool's location or in known paths
        let candidates = [
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "src", "shim", "Makefile")
            Path.Combine(Environment.CurrentDirectory, "src", "shim", "Makefile")
        ]
        // Also check VERIFROG_ROOT environment variable
        let envRoot = Environment.GetEnvironmentVariable("VERIFROG_ROOT")
        let allCandidates =
            if envRoot <> null then
                Path.Combine(envRoot, "src", "shim", "Makefile") :: candidates
            else
                candidates
        allCandidates |> List.tryFind File.Exists
    shimMakefile

// ---- Init command ----

let private tomlTemplate = String.concat "\n" [
    "[design]"
    "top = \"my_module\""
    "sources = [\"src/rtl/*.v\"]"
    ""
    "[verilator]"
    "flags = [\"--trace\"]"
    ""
    "# [iverilog]"
    "# testbenches = [\"src/sim/*_tb.v\"]"
    "# models = [\"src/sim/*.v\"]"
    ""
    "[test]"
    "output = \"build\""
    ""
    "# [memories.data_ram]"
    "# path = \"u_ram.mem\""
    "# banks = 1"
    "# depth = 1024"
    "# width = 32"
    ""
    "# [registers]"
    "# path = \"u_regfile.mem\""
    "# width = 8"
    "#"
    "# [registers.map]"
    "# CTRL = 0x00"
    "# STATUS = 0x01"
]

let private fsprojTemplate = String.concat "\n" [
    "<Project Sdk=\"Microsoft.NET.Sdk\">"
    "  <PropertyGroup>"
    "    <TargetFramework>net8.0</TargetFramework>"
    "    <OutputType>Exe</OutputType>"
    "  </PropertyGroup>"
    "  <ItemGroup>"
    "    <Compile Include=\"Tests.fs\" />"
    "    <Compile Include=\"Program.fs\" />"
    "  </ItemGroup>"
    "  <ItemGroup>"
    "    <PackageReference Include=\"Expecto\" Version=\"10.2.1\" />"
    "    <PackageReference Include=\"YoloDev.Expecto.TestSdk\" Version=\"0.14.3\" />"
    "  </ItemGroup>"
    "</Project>"
]

let private testsTemplate = String.concat "\n" [
    "module Tests"
    ""
    "open Expecto"
    ""
    "[<Tests>]"
    "let tests = testList \"my_module\" ["
    "    test \"placeholder\" {"
    "        Expect.equal 1 1 \"placeholder passes\""
    "    }"
    "]"
]

let private programTemplate = String.concat "\n" [
    "module Program"
    ""
    "open Expecto"
    ""
    "[<EntryPoint>]"
    "let main argv ="
    "    runTestsInAssemblyWithCLIArgs [] argv"
]

let private doInit (targetDir: string) =
    let dir = Path.GetFullPath(targetDir)
    Directory.CreateDirectory(dir) |> ignore
    let tomlPath = Path.Combine(dir, "verifrog.toml")
    if File.Exists(tomlPath) then
        eprintfn "verifrog.toml already exists in %s" dir
        1
    else
    let testDir = Path.Combine(dir, "tests")
    Directory.CreateDirectory(testDir) |> ignore
    File.WriteAllText(tomlPath, tomlTemplate)
    File.WriteAllText(Path.Combine(testDir, "Tests.fsproj"), fsprojTemplate)
    File.WriteAllText(Path.Combine(testDir, "Tests.fs"), testsTemplate)
    File.WriteAllText(Path.Combine(testDir, "Program.fs"), programTemplate)
    printfn "Created verifrog project in %s" dir
    printfn "  verifrog.toml  — edit design config"
    printfn "  tests/         — sample test project"
    printfn ""
    printfn "Next steps:"
    printfn "  1. Edit verifrog.toml with your design info"
    printfn "  2. Run: verifrog build"
    printfn "  3. Run: dotnet test tests/"
    0

// ---- Build command ----

let private doBuild (projectDir: string) =
    let dir = Path.GetFullPath(projectDir)

    // Find verifrog.toml
    let tomlPath =
        match findToml dir with
        | Some p -> p
        | None ->
            eprintfn "verifrog.toml not found (searched up from %s)" dir
            eprintfn "Run 'verifrog init' to create one."
            exit 1

    let config = parse tomlPath
    let projectRoot = Path.GetDirectoryName(tomlPath)
    let buildDir = Path.Combine(projectRoot, config.Test.Output)

    printfn "Building %s (top=%s)" tomlPath config.Design.Top

    // Find shim Makefile
    match findMake() with
    | None ->
        eprintfn "Could not find Verifrog shim Makefile."
        eprintfn "Set VERIFROG_ROOT to the Verifrog repo root."
        1
    | Some makefile ->
        // Resolve RTL sources
        let rtlSources =
            config.Design.Sources
            |> List.map (fun s -> Path.Combine(projectRoot, s))
            |> String.concat " "

        let verilatorFlags =
            config.Verilator.Flags |> String.concat " "

        let makeArgs =
            $"-f {makefile} sim-lib TOP={config.Design.Top} RTL_SOURCES=\"{rtlSources}\" BUILD_DIR={buildDir}"

        printfn "  Verilating %s..." config.Design.Top
        let (rc, stdout, stderr) = runCmd "make" makeArgs projectRoot

        if rc = 0 then
            printfn "  Built: %s/libverifrog_sim%s" buildDir (if OperatingSystem.IsMacOS() then ".dylib" else ".so")
            0
        else
            eprintfn "Build failed:"
            eprintfn "%s" stderr
            1

// ---- Clean command ----

let private doClean (projectDir: string) =
    let dir = Path.GetFullPath(projectDir)
    let tomlPath =
        match findToml dir with
        | Some p -> p
        | None ->
            eprintfn "verifrog.toml not found"
            exit 1

    let config = parse tomlPath
    let projectRoot = Path.GetDirectoryName(tomlPath)
    let buildDir = Path.Combine(projectRoot, config.Test.Output)

    if Directory.Exists(buildDir) then
        Directory.Delete(buildDir, true)
        printfn "Cleaned %s" buildDir
    else
        printfn "Nothing to clean (build dir does not exist)"
    0

// ---- Entry point ----

[<EntryPoint>]
let main argv =
    if argv.Length = 0 then
        printfn "verifrog — Verilog testing framework"
        printfn ""
        printfn "Commands:"
        printfn "  init [dir]   Scaffold a new verifrog project"
        printfn "  build [dir]  Build Verilator model from verifrog.toml"
        printfn "  clean [dir]  Remove build artifacts"
        0
    else
        let cmd = argv.[0]
        let dir = if argv.Length > 1 then argv.[1] else "."
        match cmd with
        | "init"  -> doInit dir
        | "build" -> doBuild dir
        | "clean" -> doClean dir
        | _ ->
            eprintfn "Unknown command: %s" cmd
            1
