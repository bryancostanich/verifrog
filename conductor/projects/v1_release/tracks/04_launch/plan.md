# Track Plan: Launch & Socialization

Goal: get Verifrog in front of hardware engineers, FPGA devs, and open-source EDA folks. Drive early users, feedback, and stars.

## Audiences

1. **Hardware/FPGA engineers** tired of writing Verilog testbenches or wrestling with UVM — this is the primary audience
2. **Open-source EDA community** — people who use Verilator, Yosys, OpenROAD, SKY130 — they're hungry for better tooling
3. **F# developers** — niche but enthusiastic, love seeing F# used in unusual domains
4. **Verification engineers** — know the pain, skeptical of alternatives, but will engage if the pitch is right

## Channels

### High-signal communities (post to these)

| Channel | Audience | Format | Notes |
|---------|----------|--------|-------|
| **r/FPGA** | FPGA engineers, hobbyists | Show HN style post | ~180k members. Very active. They'll appreciate "no UVM, just F#" |
| **r/VLSI** | Chip designers, verification | Post + discussion | Smaller but more professional. Focus on the debug tools (checkpoint/fork) |
| **r/chipdesign** | IC design engineers | Post | Growing community, overlaps with r/VLSI |
| **r/fsharp** | F# developers | Post | They love seeing F# in non-web domains. "F# for hardware testing" angle |
| **Hacker News** | Developers, tool builders | Show HN post | "Show HN: Verifrog — test Verilog designs with F# instead of testbenches" |
| **LinkedIn** | Professional network | Article | Write a proper article about the problem and approach. Tag #FPGA #Verification #OpenSource |
| **Twitter/X** | EDA community, FPGA folks | Thread | EDA Twitter is active. Tag @antaboris, @kaboroevich, @fpaborist etc. Short thread with a demo GIF |
| **Mastodon** | Tech/FOSS community | Post | fosstodon.org has EDA people |

### Community-specific outreach

| Channel | How | Notes |
|---------|-----|-------|
| **FOSSi Foundation** | Submit to FOSSi Dial-Up newsletter, El Correo Libre | They aggregate open-source silicon news. High-value audience. https://www.fossi-foundation.org |
| **F# Weekly** | Submit link | Sergey Tihon's newsletter. Reaches most active F# devs. https://sergeytihon.com/fsharp-weekly/ |
| **Verilator GitHub Discussions** | Post in Show & Tell | Verilator users who'd benefit from a testing framework on top of it |
| **Lobsters** | Post | Smaller HN alternative, technical audience. Needs invite to post. |

### Content to create

| Asset | Purpose | Priority |
|-------|---------|----------|
| **Launch blog post** | Canonical reference for all channels. "Why we built Verifrog" — the problem, the approach, the debug tools, quick demo. Host on GitHub Pages or dev.to | High |
| **30-second demo GIF** | Shows: write test → run → see results. Embed in README and all posts | High |
| **2-minute walkthrough video** | Terminal recording: init, build, test, checkpoint, fork. YouTube or Asciinema | Medium |
| **Comparison table** | Verifrog vs UVM vs cocotb vs plain testbenches. Honest, not marketing. Put in blog post | Medium |

## Messaging

### One-liner
"Test Verilog designs with F# — checkpoints, fork/explore, VCD analysis, no UVM."

### Key angles by audience

**For hardware engineers**: "You know the pain of Verilog testbenches. Verifrog lets you checkpoint simulation state, fork to explore what-if scenarios, and sweep parameters — all from a real programming language. No UVM."

**For open-source EDA**: "Verifrog is an open-source testing framework that sits on top of Verilator. It adds the debug workflow that's missing: save state, restore, fork, compare. Apache 2.0."

**For F# devs**: "F# is great for DSLs and testing. Verifrog uses it to test hardware designs — driving Verilator-compiled Verilog with type-safe signal access, Expecto tests, and TOML-driven config."

**For verification engineers**: "Not a replacement for UVM on complex SoCs. But for block-level testing, unit verification, and debugging, Verifrog gives you things UVM can't: instant state restore, what-if exploration, and 10x less boilerplate."

## Timing

- [ ] Prepare all content before posting anywhere
- [ ] Post to smaller communities first (r/fsharp, F# Weekly, FOSSi) for early feedback
- [ ] Then hit the big ones (r/FPGA, HN, LinkedIn) once we've incorporated initial feedback
- [ ] Space posts out over 1-2 weeks, don't spam everything on day 1

## Phase 1: Content Prep

- [ ] Write launch blog post
- [ ] Create demo GIF (asciinema or screen recording)
- [ ] Draft posts tailored for each channel (don't copy-paste the same text everywhere)
- [ ] Build comparison table (Verifrog vs cocotb vs UVM vs plain TB)

## Phase 2: Soft Launch

- [ ] Submit to F# Weekly
- [ ] Post to r/fsharp
- [ ] Submit to FOSSi El Correo Libre
- [ ] Post in Verilator GitHub Discussions

## Phase 3: Main Launch

- [ ] Post to r/FPGA
- [ ] Post to r/VLSI and r/chipdesign
- [ ] Submit to Hacker News (Show HN)
- [ ] Publish LinkedIn article
- [ ] Twitter/X thread
- [ ] Mastodon post

## Phase 4: Follow-up

- [ ] Respond to every comment and question (critical for early traction)
- [ ] Track which channels drive GitHub stars and issues
- [ ] Incorporate feedback into next release
