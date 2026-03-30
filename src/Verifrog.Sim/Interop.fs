module Verifrog.Sim.Interop

open System
open System.Runtime.InteropServices

// P/Invoke bindings to libverifrog_sim (.dylib on macOS, .so on Linux)
// Extracted from khalkulo's SimDebugger.Interop, retargeted to libverifrog_sim.
// Design-specific bindings (regfile, weight SRAM, activation SRAM, MAC) removed.

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern nativeint sim_create()

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern void sim_destroy(nativeint ctx)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern void sim_reset(nativeint ctx, int cycles)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern void sim_step(nativeint ctx, int n)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern uint64 sim_get_cycle(nativeint ctx)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern int sim_read(nativeint ctx, string name, int64& outValue)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern int sim_write(nativeint ctx, string name, int64 value)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern int sim_signal_bits(nativeint ctx, string name)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern int sim_signal_count(nativeint ctx)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern int sim_signal_name(nativeint ctx, int index, byte[] nameBuf, int bufLen)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern void sim_suppress_display(int suppress)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern nativeint sim_checkpoint(nativeint ctx)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern int sim_restore(nativeint ctx, nativeint cp)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern void sim_checkpoint_free(nativeint cp)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern uint64 sim_checkpoint_cycle(nativeint cp)

// Signal forcing

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern int sim_force(nativeint ctx, string name, int64 value)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern int sim_release(nativeint ctx, string name)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern void sim_release_all(nativeint ctx)

[<DllImport("libverifrog_sim", CallingConvention = CallingConvention.Cdecl)>]
extern int sim_force_count(nativeint ctx)
