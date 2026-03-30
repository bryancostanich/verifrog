# Extension Guide

Verifrog provides generic structured access to signals, memories, and registers. For design-specific convenience APIs, build an extension layer in your own repo on top of Verifrog.

## Pattern

```
Your Tests
  |
Your Extension Layer (KhalkuloSim, custom Expect helpers, Stimulus)
  |
Verifrog (Sim, Memory, Register, Expect, SimFixture)
```

## Example: khalkulo extension layer

khalkulo is a neural network accelerator. Its extension layer wraps Verifrog with domain-specific APIs.

### Design-specific Sim wrapper

```fsharp
// khalkulo/verifrog/KhalkuloSim.fs

type KhalkuloSim(sim: Verifrog.Sim.Sim) =

    /// Write a weight to a specific SRAM bank/address
    member _.WgtWrite(bank: int, addr: int, value: uint32) =
        sim.Memory("weight_sram").Write(bank, addr, int64 value)

    /// Write an activation to a specific SRAM bank/address
    member _.ActWrite(bank: int, addr: int, value: uint64) =
        sim.Memory("act_sram").Write(bank, addr, int64 value)

    /// Configure a layer via register writes
    member _.ConfigureLayer(layerType: int, inCh: int, outCh: int) =
        sim.Register("LAYER_TYPE").Write(int64 layerType) |> ignore
        sim.Register("IN_CHANNELS").Write(int64 inCh) |> ignore
        sim.Register("OUT_CHANNELS").Write(int64 outCh) |> ignore

    /// Start inference
    member _.StartInference() =
        sim.Register("CTRL").Write(0x01L) |> ignore

    /// Wait for inference to complete
    member _.WaitForDone(?maxCycles) =
        let max = defaultArg maxCycles 100000
        sim.RunUntilSignal("fsm_done_w", 1L, max)

    /// Read the underlying Sim for generic access
    member _.Sim = sim
```

### Design-specific Expect helpers

```fsharp
// khalkulo/verifrog/KhalkuloExpect.fs

module KhalkuloExpect =

    open Expecto
    open Verifrog.Sim

    /// Expect a weight SRAM word to have a specific value
    let weightSram (sim: Sim) (bank: int) (addr: int) (expected: uint32) (msg: string) =
        match sim.Memory("weight_sram").Read(bank, addr) with
        | SimResult.Error e -> failtest e
        | SimResult.Ok actual ->
            if actual <> int64 expected then
                failtest $"{msg}\n  weight SRAM: bank={bank} addr={addr}\n  expected: 0x{expected:X8}\n  actual:   0x{actual:X8}"

    /// Expect a MAC accumulator value (reads from named signal)
    let macAcc (sim: Sim) (group: int) (mac: int) (expected: int32) (msg: string) =
        let name = $"u_mac_array.gen_group[{group}].u_group.gen_mac[{mac}].u_mac.acc_bank0"
        let actual = sim.ReadOrFail(name)
        if actual <> int64 expected then
            failtest $"{msg}\n  MAC[{group}][{mac}].acc_bank0\n  expected: {expected}\n  actual:   {actual}"
```

### Stimulus module

```fsharp
// khalkulo/verifrog/Stimulus.fs

module Stimulus =

    /// Register addresses (mirrors RTL register map)
    module Reg =
        let CTRL = 0x00
        let STATUS = 0x01
        let MODE = 0x02
        let NUM_LAYERS = 0x03

    /// Layer types
    module LayerType =
        let CONV = 0x01
        let DEPTHWISE = 0x02
        let POOL = 0x03

    /// Load a standard 3-layer CNN configuration
    let loadStandardConfig (sim: KhalkuloSim) =
        sim.ConfigureLayer(LayerType.CONV, 3, 16)
        // ... etc
```

## Project structure

```
khalkulo/
  verifrog/
    verifrog.toml          # Memory/register config for khalkulo
    KhalkuloSim.fs         # Design-specific Sim wrapper
    KhalkuloExpect.fs      # Design-specific Expect helpers
    Stimulus.fs            # Config helpers, register addresses
  tests/
    ...existing 150 tests, now importing from Verifrog + extension layer
```

## Guidelines

1. **Keep Verifrog generic.** If a helper is only useful for your design, put it in your extension layer.
2. **Use TOML for paths.** Don't hardcode signal paths in your extension code. Put them in `verifrog.toml` and use `sim.Memory()` / `sim.Register()`.
3. **Wrap, don't fork.** Your `MyDesignSim` type should contain a `Sim` member, not inherit from it.
4. **Share common patterns.** If your extension patterns are useful to others, consider contributing them back to Verifrog as optional modules.
