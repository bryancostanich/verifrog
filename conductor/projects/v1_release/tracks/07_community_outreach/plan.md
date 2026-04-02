# Track Plan: Community Outreach & Publicizing

## Phase 1: Write the Article

- [ ] Write blog post / article covering:
  - The problem: Verilog TBs are tedious, UVM is heavyweight, nothing offers interactive debug
  - What verifrog does: F#/Expecto framework driving Verilator + iverilog from `dotnet test`
  - Key differentiators:
    - Checkpoint/restore — snapshot and rewind simulation state
    - Fork — explore what-if scenarios, auto-restore
    - Sweep — parameter exploration from identical starting state
    - Signal forcing — inject faults, override internals
    - VCD waveform analysis — programmatic query of simulation traces
    - Declarative `.verifrog` test files — no F# needed for simple tests
  - Comparison with alternatives (cocotb, UVM, raw Verilog TB)
  - Real-world usage: born from khalkulo chip project, 151 tests across a full inference accelerator
- [ ] Include code examples (declarative test, F# test, checkpoint/fork usage)
- [ ] Decide where to host (GitHub Pages, personal blog, Medium, etc.)

## Phase 2: Distribute

- [ ] Post to Reddit — target communities:
  - r/chipdesign
  - r/FPGA
  - r/ECE
  - r/opensourcehardware
  - r/fsharp
  - r/dotnet
- [ ] Submit to Hacker News
- [ ] Cross-post to FOSSi Foundation channels (mailing list, Discourse)
- [ ] Share on open-source silicon Slack/Matrix communities
- [ ] Post to F# community channels (F# Weekly, fsharp.org, Twitter/X)

## Phase 3: Conference Outreach (if timing works)

- [ ] Check ORConf / FOSSi Dial-Up / WOSET CFP dates
- [ ] Consider lightning talk or poster submission
- [ ] Prepare slides if accepted
