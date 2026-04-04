module Verifrog.Cli.Program

open System
open System.Diagnostics
open System.IO
open Verifrog.Sim
open Verifrog.Sim.Config
open Verifrog.Cli.Debugger

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
    "tests = \"tests\""
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
    "    <Compile Include=\"DeclarativeLoader.fs\" />"
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

let private declarativeLoaderTemplate = String.concat "\n" [
    "module DeclarativeTests"
    ""
    "open Expecto"
    "open Verifrog.Runner.Declarative"
    ""
    "[<Tests>]"
    "let declarativeTests = discoverFromToml (System.IO.Path.Combine(__SOURCE_DIRECTORY__, \"..\", \"verifrog.toml\"))"
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

let private launchJsonTemplate = String.concat "\n" [
    "{"
    "    \"version\": \"0.2.0\","
    "    \"configurations\": ["
    "        {"
    "            \"name\": \"Debug Tests\","
    "            \"type\": \"coreclr\","
    "            \"request\": \"launch\","
    "            \"program\": \"dotnet\","
    "            \"args\": ["
    "                \"run\","
    "                \"--project\", \"${workspaceFolder}/tests/Tests.fsproj\","
    "                \"--\","
    "                \"--sequenced\""
    "            ],"
    "            \"cwd\": \"${workspaceFolder}\","
    "            \"env\": {"
    "                \"DYLD_LIBRARY_PATH\": \"${workspaceFolder}/build\","
    "                \"LD_LIBRARY_PATH\": \"${workspaceFolder}/build\""
    "            },"
    "            \"console\": \"integratedTerminal\","
    "            \"stopAtEntry\": false"
    "        },"
    "        {"
    "            \"name\": \"Debug Single Test\","
    "            \"type\": \"coreclr\","
    "            \"request\": \"launch\","
    "            \"program\": \"dotnet\","
    "            \"args\": ["
    "                \"run\","
    "                \"--project\", \"${workspaceFolder}/tests/Tests.fsproj\","
    "                \"--\","
    "                \"--sequenced\","
    "                \"--filter\", \"${input:testName}\""
    "            ],"
    "            \"cwd\": \"${workspaceFolder}\","
    "            \"env\": {"
    "                \"DYLD_LIBRARY_PATH\": \"${workspaceFolder}/build\","
    "                \"LD_LIBRARY_PATH\": \"${workspaceFolder}/build\""
    "            },"
    "            \"console\": \"integratedTerminal\","
    "            \"stopAtEntry\": false"
    "        }"
    "    ],"
    "    \"inputs\": ["
    "        {"
    "            \"id\": \"testName\","
    "            \"type\": \"promptString\","
    "            \"description\": \"Test name (substring match)\""
    "        }"
    "    ]"
    "}"
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
    let vscodeDir = Path.Combine(dir, ".vscode")
    Directory.CreateDirectory(testDir) |> ignore
    Directory.CreateDirectory(vscodeDir) |> ignore
    File.WriteAllText(tomlPath, tomlTemplate)
    File.WriteAllText(Path.Combine(testDir, "Tests.fsproj"), fsprojTemplate)
    File.WriteAllText(Path.Combine(testDir, "Tests.fs"), testsTemplate)
    File.WriteAllText(Path.Combine(testDir, "DeclarativeLoader.fs"), declarativeLoaderTemplate)
    File.WriteAllText(Path.Combine(testDir, "Program.fs"), programTemplate)
    File.WriteAllText(Path.Combine(vscodeDir, "launch.json"), launchJsonTemplate)
    printfn "Created verifrog project in %s" dir
    printfn "  verifrog.toml  — edit design config"
    printfn "  tests/         — sample test project"
    printfn "  .vscode/       — VS Code debug config"
    printfn ""
    printfn "Next steps:"
    printfn "  1. Edit verifrog.toml with your design info"
    printfn "  2. Run: verifrog build"
    printfn "  3. Run: dotnet test tests/"
    printfn "  4. Open in VS Code and press F5 to debug"
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
    let projectRoot = Path.GetDirectoryName(Path.GetFullPath(tomlPath))
    let buildDir = config.Test.Output  // Already absolute from Config.parse

    printfn "Building %s (top=%s)" tomlPath config.Design.Top

    // Find shim Makefile
    match findMake() with
    | None ->
        eprintfn "Could not find Verifrog shim Makefile."
        eprintfn "Set VERIFROG_ROOT to the Verifrog repo root."
        1
    | Some makefile ->
        // Sources are already absolute paths from Config.parse
        let rtlSources =
            config.Design.Sources
            |> String.concat " "

        let verilatorFlags =
            config.Verilator.Flags |> String.concat " "

        let makeArgs =
            $"-f {makefile} sim-lib TOP={config.Design.Top} RTL_SOURCES=\"{rtlSources}\" BUILD_DIR={buildDir} VFLAGS_EXTRA=\"{verilatorFlags}\""

        printfn "  Verilating %s..." config.Design.Top
        let (rc, stdout, stderr) = runCmd "make" makeArgs projectRoot

        if rc = 0 then
            let libExt = if OperatingSystem.IsMacOS() then ".dylib" else ".so"
            printfn "  Built: %s/libverifrog_sim%s" buildDir libExt

            // Write .env file so IDEs and manual dotnet run can find the library
            let envPath = Path.Combine(projectRoot, ".verifrog.env")
            let pathVar = if OperatingSystem.IsMacOS() then "DYLD_LIBRARY_PATH" else "LD_LIBRARY_PATH"
            File.WriteAllText(envPath, $"{pathVar}={buildDir}\n")

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
    let buildDir = config.Test.Output  // Already absolute from Config.parse

    if Directory.Exists(buildDir) then
        Directory.Delete(buildDir, true)
        printfn "Cleaned %s" buildDir
    else
        printfn "Nothing to clean (build dir does not exist)"
    0

// ---- Debug-server command ----

let private doDebugServer (projectDir: string) =
    let dir = Path.GetFullPath(projectDir)
    let tomlPath =
        match findToml dir with
        | Some p -> p
        | None ->
            eprintfn "verifrog.toml not found (searched up from %s)" dir
            exit 1

    let config = parse tomlPath
    let buildDir = config.Test.Output
    let libName = if OperatingSystem.IsMacOS() then "libverifrog_sim.dylib" else "libverifrog_sim.so"
    let libPath = Path.Combine(buildDir, libName)

    if not (File.Exists(libPath)) then
        eprintfn "Sim library not found: %s" libPath
        eprintfn "Run 'verifrog build' first."
        exit 1

    Environment.SetEnvironmentVariable("VERIFROG_SIM_LIB", libPath)

    use sim = Sim.Create(config)
    sim.Reset()
    DebugServer.runServer sim
    0

// ---- MCP server command ----

let private doMcpServer (projectDir: string option) =
    match projectDir with
    | Some dir ->
        let dir = Path.GetFullPath(dir)
        let tomlPath =
            match findToml dir with
            | Some p -> p
            | None ->
                eprintfn "verifrog.toml not found (searched up from %s)" dir
                exit 1

        let config = parse tomlPath
        let buildDir = config.Test.Output
        let libName = if OperatingSystem.IsMacOS() then "libverifrog_sim.dylib" else "libverifrog_sim.so"
        let libPath = Path.Combine(buildDir, libName)

        if not (File.Exists(libPath)) then
            eprintfn "Sim library not found: %s" libPath
            eprintfn "Run 'verifrog build' first."
            exit 1

        Environment.SetEnvironmentVariable("VERIFROG_SIM_LIB", libPath)

        let sim = Sim.Create(config)
        sim.Reset()
        McpServer.runMcpServer (Some sim)
        0
    | None ->
        McpServer.runMcpServer None
        0

// ---- Debug command ----

let private doDebug (projectDir: string) (scriptPath: string option) =
    let dir = Path.GetFullPath(projectDir)
    let tomlPath =
        match findToml dir with
        | Some p -> p
        | None ->
            eprintfn "verifrog.toml not found (searched up from %s)" dir
            eprintfn "Run 'verifrog init' to create one, then 'verifrog build'."
            exit 1

    let config = parse tomlPath
    let buildDir = config.Test.Output  // Already absolute from Config.parse
    let libName = if OperatingSystem.IsMacOS() then "libverifrog_sim.dylib" else "libverifrog_sim.so"
    let libPath = Path.Combine(buildDir, libName)

    if not (File.Exists(libPath)) then
        eprintfn "Sim library not found: %s" libPath
        eprintfn "Run 'verifrog build' first."
        exit 1

    // Set the native library path so the sim can find it
    Environment.SetEnvironmentVariable("VERIFROG_SIM_LIB", libPath)

    use sim = Sim.Create(config)
    sim.Reset()

    match scriptPath with
    | Some path -> runScript sim [] path
    | None -> runInteractive sim []
    0

// ---- Entry point ----

[<EntryPoint>]
let main argv =
    if argv.Length = 0 then
        printfn "verifrog -- Verilog testing framework"
        printfn ""
        printfn "Commands:"
        printfn "  init [dir]          Scaffold a new verifrog project"
        printfn "  build [dir]         Build Verilator model from verifrog.toml"
        printfn "  clean [dir]         Remove build artifacts"
        printfn "  debug [dir]         Interactive simulation debugger"
        printfn "  debug --script <f>  Run debugger script"
        printfn "  debug-server [dir]  JSON debug server (stdin/stdout)"
        printfn "  mcp-server [dir]    MCP debug server (for Claude)"
        0
    else
        let cmd = argv.[0]
        let rest = argv.[1..]
        match cmd with
        | "init"  ->
            let dir = if rest.Length > 0 then rest.[0] else "."
            doInit dir
        | "build" ->
            let dir = if rest.Length > 0 then rest.[0] else "."
            doBuild dir
        | "clean" ->
            let dir = if rest.Length > 0 then rest.[0] else "."
            doClean dir
        | "debug-server" ->
            let dir = if rest.Length > 0 then rest.[0] else "."
            doDebugServer dir
        | "mcp-server" ->
            let dir = if rest.Length > 0 then Some rest.[0] else None
            doMcpServer dir
        | "debug" ->
            // Parse debug args: [dir] [--script <path>]
            let mutable dir = "."
            let mutable script = None
            let mutable i = 0
            while i < rest.Length do
                match rest.[i] with
                | "--script" when i + 1 < rest.Length ->
                    script <- Some rest.[i + 1]
                    i <- i + 2
                | arg when not (arg.StartsWith("--")) && i = 0 ->
                    dir <- arg
                    i <- i + 1
                | _ ->
                    i <- i + 1
            doDebug dir script
        | _ ->
            eprintfn "Unknown command: %s" cmd
            1
