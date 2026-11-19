# Proof of concept · can you read a song as it plays?

Decision proposal, November 2026. This experiment tests the premise behind the [full product spec](PROJECT-SPEC.md). All budgets and thresholds below are proposed targets. No measurements have been collected.

## Decision and scope

Would the listener keep a second window open while using Spotify desktop because it reveals a change of texture or a returning passage they care about? Two things must work together: passive capture must fit the listener’s ordinary playback setup, and the map must tell them something they can verify by ear.

Build one local browser page, a Windows capture path, a Linux capture smoke test, two musical lanes and a small evidence report. Windows 11 is the proposed first end-to-end target. Linux gets an early test on a real PipeWire desktop so an apparently successful Windows trial cannot hide the cost of the second platform. Use stock Spotify playing on the test computer. Reserve Android, other players, playback control, persistent song libraries, installers, account integration and the other four analytical lanes for decisions outside this trial. Use generated audio only as a repeatable engineering control.

**Proposed time box:** five focused engineering days plus two ordinary listening sessions. Treat a missing device or ambiguous result as unknown. Log the resulting platform or product risk explicitly. The time box is a spending decision, not a delivery estimate for the rebuild.

## What the tester sees

1. Launch the helper and open its local page. Pick Spotify, start capture, and leave the normal speaker, headphones and EQ route in place. The page shows the selected source and whether frames are arriving.
2. Press “New listen” at the start of a song. The page measures elapsed captured time. The first lane shows a continuous timbre contour and sustained change markers. The second draws a labeled link back to an earlier passage when similar harmony recurs. Until a match exists the lane stays quiet.
3. Click a link or focus it with the keyboard to highlight both spans and read their timestamps. Click a timbre boundary to see its time and the measured change, such as a sustained rise in brightness. Reserve one marker and short label for gaps. The live edge keeps advancing when inspecting history.
4. Stop and review the frozen map. Save an evaluation summary containing timestamped features, detected intervals, setup and performance counters. Keep music samples in memory only. Stop capture when the page closes.

The experiment uses a session clock and manual track boundaries. Display “elapsed since capture” so a late start or pause cannot masquerade as Spotify position. For controlled song trials play continuously. For a known seek or track switch the tester starts a new listen. When a device disappears or the frame sequence breaks, open a gap and reset the analysis window. Cached maps, seek reconciliation and automatic track metadata belong to the full product.

## Implementation slice

| Increment | Deliverable and observable exit condition |
| --- | --- |
| 1 · Capture | Use Microsoft’s process-loopback sample as a reference to read Spotify and its child processes into memory. Log timestamps, sample rate and dropped frames. Play a loud notification from another process. The listener hears it and the isolated source meter excludes it. Run an early equivalent capture check on Linux using a Spotify sink-input monitor. |
| 2 · Features | Downmix with explicit channel weights and resample to a fixed analysis rate, initially 22.05 kHz. Start with overlapping 2,048-sample windows and a 512-sample hop. Aggregate to 4 feature frames/s. Compute log-mel energies/MFCCs for texture, centroid for a legible contour, chroma for harmony and RMS for silence gating. Carry each frame’s start/end time and validity. |
| 3 · Musical evidence | For timbre, compare several seconds of recent normalized spectral features to the preceding window. Require a sustained change, suppress adjacent duplicates and treat pure gain steps separately. For repeats, compare an 8-second chroma sequence to earlier nonoverlapping windows. Require sustained similarity and local contrast over neighboring candidates. Extend adjacent matches into one pair and suppress matches on silence, a held chord or a featureless loop. Tune only on calibration material. |
| 4 · Viewer | Use React, TypeScript, Vite and one Canvas or SVG plot with shared time coordinates. Serve the built viewer and versioned feature messages from the helper on loopback. A bounded queue keeps capture responsive and exposes overruns. A fixture event stream drives the same UI. A small native executable per target is sufficient; the product’s proposed Rust implementation is optional for the spike. |
| 5 · Trial | Run the capture matrix and held-out listening tasks below. Save the two-lane map, counts, timings, false matches and listener notes for each case. Record a go, revise or stop decision. |

Use a fixed or calibration-derived scale for the timbre contour so later passages do not recolor history. Keep the evaluation implementation modest: one prior feature buffer capped at ten minutes, one repeat search per second, one UI update per feature batch. Cap the history explicitly and mark its visible extent. A fixed phrase length will miss tempo-shifted or short repeats; log those misses for the decision. [Windows loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/) · [Linux per-stream monitor](https://www.freedesktop.org/software/pulseaudio/doxygen/stream_8h.html)

## Evaluation protocol

**Controls.** Generate an A–B–A fixture with contrasting textures and a known returning chord sequence, a constant-texture gain step, and a non-repeating phrase with silence and a simulated device interruption. Feed both real-time and replayed fixture frames through the viewer. Require a correct A–A link, a texture mark on the A–B change, zero links across the gap, and no texture boundary on the pure gain step once it settles. Keep generation parameters so regressions can be replayed.

**Listening set.** Choose two calibration tracks, then freeze thresholds. Select six separate held-out recordings from the listener’s usual Spotify rotation: include a repeated chorus with changed instrumentation, a gradual build, a sparse arrangement, a sustained groove, and a through-composed or weakly repeating piece. Before viewing predictions, listen and annotate all clearly audible texture boundaries in the scored interval (the full track by default), plus one target returning pair on each of four tracks and at least one track with no salient return. For each, record title/version, approximate time ranges and why it was chosen. Keep human annotations independent from the detector. Ambiguous examples stay in the listening notes and outside the accuracy denominator.

**Capture matrix.**

| Setup | Procedure | Passing observation |
| --- | --- | --- |
| Windows shared playback | Spotify, Boom 3D off and on, and a second app playing a distinctive sound during a Spotify pause. Repeat the same excerpt at both EQ settings. | Music arrives with both settings; the other app is audible to the listener and absent from the isolated capture. Note whether the measured spectrum follows the EQ. Starting/stopping the helper causes no audible double playback, dropout or output switch. |
| Linux PipeWire | Repeat the isolation check with JamesDSP off/on. Restart Spotify and move playback between available outputs. | Obtain frames from the selected Spotify stream in both EQ conditions, keep existing playback audible, and recover after reselecting the source. Record distro, sound server, Spotify package and routing. |
| No local audio | Try a stopped player, remote Spotify Connect playback, and Windows exclusive mode if the device offers it. Interrupt a live source and reconnect. | Show waiting or unavailable, retain existing coverage and resume with a fresh analysis window. Silence alone cannot identify a specific cause. Never silently broaden capture to the desktop mix. |

Compare EQ on/off using the same passage at the same volume. A changed spectrum establishes tap behavior; it need not fail the trial. Score false structural events during normal listening separately. Where a named EQ or target host is unavailable, mark the row unverified. A whole-output capture can help diagnose a source-selection error but cannot pass an isolation check.

**Musical scoring.** Match predicted boundaries one-to-one to the nearest unused reference within ±3 seconds. Report precision (matched/predicted) and recall (matched/reference) pooled across the held-out set, along with counts. Count a returning pair as correct when both highlighted spans overlap their annotated passages by at least half the shorter span, occur in the correct order and last at least six seconds each. Review other emitted links by ear against their timestamps and count unsupported ones as false positives. Count duplicate links to the same pair once for recall and flag the extras as clutter. Report time between the return’s onset and the first visible link. Keep a short explanation for every conspicuously wrong link.

**Listener value.** Use one session with a baseline (CaYaDev if available, otherwise a level/spectrum view) and one with the prototype. Counterbalance the order across songs, using the same short excerpts and recording the baseline choice. Ask the listener to point to where the arrangement changed, locate what an indicated passage resembles and say what they learned, without coaching them on the colors. At the end, ask which view they would leave open for the next song and why. This is a personal build decision; one listener’s preference is evidence about that listener. [Existing visualizer](https://github.com/CaYatur/SoundVisualizer#audio-capture-and-analysis)

## Decision record

| Question | Proposed pass condition |
| --- | --- |
| Does desktop capture fit ordinary listening? | The Windows shared-playback row passes and a 30-minute continuous run produces zero unexplained missing intervals over 250 ms. Measure on the intended listening machine and record its specs. For two-platform approval the Linux isolation and EQ row also passes. |
| Does it feel live? | 95th percentile feature-window-end to first paint ≤500 ms over the run, measured with aligned monotonic clocks. Measure algorithmic lookback separately. Combined helper and active browser CPU averages under 15% of one logical core above idle. Combined memory growth from minute five to minute thirty stays under 50 MiB. |
| Does the map correspond to the music? | Held-out timbre precision and recall each ≥0.65. Correct repeat links on at least three of the four annotated return tracks, visible within 15 seconds of the return starting. At most one false repeat link per track, including the negative control track. All generated controls pass. |
| Is the result worth returning to? | The listener can locate a meaningful change or return unaided on at least four of six held-out songs, names two specific things the map helped them hear, and chooses it over the baseline for a subsequent ordinary session. |

**Go** when capture, responsiveness, musical evidence and listener value all pass. Carry the evidence packet and both specs into the proposed new repo. **Revise** when the listening sessions show value but there is a specific fixable miss. Give one bounded follow-up experiment a named hypothesis and a fresh held-out check. If Windows passes and Linux is unverified, record a Windows-only result and keep Linux feasibility open. **Stop or narrow** if realistic capture disrupts the normal listening setup, repeated links remain misleading after the revision, or the listener prefers the existing tool. Document which individual lane, if any, earned continued work.

The evidence packet consists of a one-page verdict, both platform rows with versions and status, calibration settings, a per-song expected/observed table, two representative map screenshots, performance measurements and anonymized listener notes. Deliver it with prototype reproduction steps. Record the decision before starting the full rebuild.
