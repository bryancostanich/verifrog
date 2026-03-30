module Verifrog.Sim.Config

open System
open System.IO
open System.Collections.Generic
open Tomlyn
open Tomlyn.Model

/// Memory region declaration from verifrog.toml [memories.*]
type MemoryConfig = {
    Name: string
    /// Signal path template, may contain {bank} placeholder
    Path: string
    Banks: int
    Depth: int
    Width: int
}

/// Register map entry from verifrog.toml [registers.map]
type RegisterEntry = {
    Name: string
    Address: int
}

/// Register file declaration from verifrog.toml [registers]
type RegisterConfig = {
    /// Signal path to the register file memory array
    Path: string
    Width: int
    Map: RegisterEntry list
}

/// Design configuration from verifrog.toml [design]
type DesignConfig = {
    Top: string
    Sources: string list
}

/// Iverilog backend configuration from verifrog.toml [iverilog]
type IverilogConfig = {
    Testbenches: string list
    Models: string list
}

/// Verilator configuration from verifrog.toml [verilator]
type VerilatorConfig = {
    Flags: string list
}

/// Test configuration from verifrog.toml [test]
type TestConfig = {
    Output: string
}

/// Full parsed verifrog.toml
type VerifrogConfig = {
    Design: DesignConfig
    Verilator: VerilatorConfig
    Iverilog: IverilogConfig option
    Test: TestConfig
    Memories: MemoryConfig list
    Registers: RegisterConfig option
}

// ---- TOML parsing helpers ----

let private tryGetString (table: TomlTable) (key: string) : string option =
    match table.TryGetValue(key) with
    | true, (:? string as v) -> Some v
    | _ -> None

let private getString (table: TomlTable) (key: string) : string =
    match tryGetString table key with
    | Some v -> v
    | None -> failwith $"Missing required key: {key}"

let private getInt (table: TomlTable) (key: string) (defaultValue: int) : int =
    match table.TryGetValue(key) with
    | true, (:? int64 as v) -> int v
    | _ -> defaultValue

let private getStringList (table: TomlTable) (key: string) : string list =
    match table.TryGetValue(key) with
    | true, (:? TomlArray as arr) ->
        [ for item in arr do
            match item with
            | :? string as s -> yield s
            | _ -> () ]
    | _ -> []

let private getTable (table: TomlTable) (key: string) : TomlTable option =
    match table.TryGetValue(key) with
    | true, (:? TomlTable as t) -> Some t
    | _ -> None

// ---- Parsing ----

let private parseDesign (root: TomlTable) : DesignConfig =
    match getTable root "design" with
    | None -> failwith "Missing [design] section in verifrog.toml"
    | Some t ->
        { Top = getString t "top"
          Sources = getStringList t "sources" }

let private parseVerilator (root: TomlTable) : VerilatorConfig =
    match getTable root "verilator" with
    | None -> { Flags = [] }
    | Some t -> { Flags = getStringList t "flags" }

let private parseIverilog (root: TomlTable) : IverilogConfig option =
    match getTable root "iverilog" with
    | None -> None
    | Some t ->
        Some { Testbenches = getStringList t "testbenches"
               Models = getStringList t "models" }

let private parseTest (root: TomlTable) : TestConfig =
    match getTable root "test" with
    | None -> { Output = "build" }
    | Some t -> { Output = tryGetString t "output" |> Option.defaultValue "build" }

let private parseMemories (root: TomlTable) : MemoryConfig list =
    match getTable root "memories" with
    | None -> []
    | Some memTable ->
        [ for kv in memTable do
            match kv.Value with
            | :? TomlTable as t ->
                yield {
                    Name = kv.Key
                    Path = getString t "path"
                    Banks = getInt t "banks" 1
                    Depth = getInt t "depth" 0
                    Width = getInt t "width" 0
                }
            | _ -> () ]

let private parseRegisters (root: TomlTable) : RegisterConfig option =
    match getTable root "registers" with
    | None -> None
    | Some regTable ->
        let path = getString regTable "path"
        let width = getInt regTable "width" 8
        let map =
            match getTable regTable "map" with
            | None -> []
            | Some mapTable ->
                [ for kv in mapTable do
                    match kv.Value with
                    | :? int64 as v -> yield { Name = kv.Key; Address = int v }
                    | _ -> () ]
        Some { Path = path; Width = width; Map = map }

/// Parse a verifrog.toml file
let parse (tomlPath: string) : VerifrogConfig =
    let content = File.ReadAllText(tomlPath)
    let root = Toml.ToModel(content)
    { Design = parseDesign root
      Verilator = parseVerilator root
      Iverilog = parseIverilog root
      Test = parseTest root
      Memories = parseMemories root
      Registers = parseRegisters root }

/// Find verifrog.toml by walking up from a starting directory
let findToml (startDir: string) : string option =
    let rec walk (dir: string) =
        let candidate = Path.Combine(dir, "verifrog.toml")
        if File.Exists(candidate) then Some candidate
        else
            let parent = Path.GetDirectoryName(dir)
            if parent = null || parent = dir then None
            else walk parent
    walk (Path.GetFullPath(startDir))
