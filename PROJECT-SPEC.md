# Music Structure · desktop listening companion

Product and engineering proposal, November 2026. Working title. The product decision follows the [proof of concept](MVP-SPEC.md). These documents live in the legacy repository so a future implementation can start in its own repository.

## The listening experience

Play a song in the ordinary Spotify desktop app. Open the companion and select Spotify. During the first verse, a strip traces changes in texture. When the same phrase returns with heavier drums, a link joins the two passages. The listener can see both the harmonic resemblance and the difference in arrangement. On a later listen to the same recording, its saved map is available from the start.

The primary audience is someone listening for pleasure who wonders what changed, where they heard a passage before, or how a track builds. A musician can inspect timing and harmony in more detail. The principal success measure is whether people notice and locate audible relationships during normal listening. An existing tool already offers live chord, pitch-class and tempo displays for system audio. That makes a readable history of the song the reason to build this one. [CaYaDev SoundVisualizer](https://github.com/CaYatur/SoundVisualizer#audio-capture-and-analysis)

**Scope agreed in discussion.** Support locally playing Spotify desktop on Windows and Linux, derive musical features from audio, and keep the interface accessible in a desktop browser. Android is out of scope. Spotify retains playback controls. The proposal assumes local-only operation, an installable capture helper, and a possible new repository after the trial. The visual identity is original to this project.

## Product surfaces

**Listen.** The helper opens the local web app. First launch offers available playback sources, defaults to a candidate Spotify process and requires a deliberate start. Show the selected source, whether it is isolated or a whole-output mix, current capture state and a stop control throughout listening. A moving input meter aids setup. Once capture begins, the map takes the space previously used for setup.

**Song map.** Build the interface around one horizontal time ruler. The top lane draws thin arcs between matched passages, with matching letter labels at either end. Immediately beneath it, a continuous timbre contour shows where the sound changes character. A quieter strip carries event markers. Chords, tonal regions and tempo share the ruler and can be individually expanded. At narrow desktop widths preserve alignment and collapse the inspector. Wheel or buttons zoom around the cursor or selection. Panning pauses automatic following and a visible “Follow live” action restores it. Inspecting a point only moves the inspection cursor. The playhead continues to show what is audible.

**Inspect.** Selecting either end of a repeat highlights both ranges and gives their start times, lengths and the basis for the match, such as “similar chord movement; brighter on return.” Generate such descriptions only from measured differences. Show uncertainty on ambiguous estimates. Selecting an event describes the measured change. Keyboard users can step by event or passage, open details with Enter and return with Escape.

**History.** List locally analyzed recordings with title when available, duration, covered intervals and analysis version. Open a saved map while capture is stopped. A partial map shows where another listen could fill holes. Provide per-record deletion, clear-all with confirmation, storage usage and an adjustable size cap. On cap exhaustion evict least recently used unpinned records or offer cleanup if all remaining records are pinned.

## Visual and interaction direction

Use a contemporary annotated musical score as the design reference. The repeated shape of a passage should be recognizable across the width of the piece. Reserve vertical space for the timbre contour, put time labels directly along it, and keep controls on a compact edge toolbar. Use a matte ink or paper field, fine rules and tabular timestamps. Audition high-contrast section hues against both field colors. Use a neutral color for uncertain evidence and hatching for missing coverage. Give each section family a letter and each event a shape. Color alone cannot carry meaning.

Prototype the map with real evidence from one listen before choosing final typography and palette. Distinguish three things at a glance: the live edge, the time being inspected and the boundaries of a selected passage. New repeat evidence can add a link back to an earlier passage while preserving existing positions and family labels. Support reduced motion, visible focus, scalable text and a synchronized HTML list of passages and events for screen readers. Announce capture-state transitions and user selections, never each incoming frame.

## The six musical views

| View | Evidence and live behavior | Whole-track refinement |
| --- | --- | --- |
| Repeated structure | Compare sequences of normalized pitch-class vectors over several seconds. Report two time ranges and a match score once the return is sustained. Assign neutral family letters. | Group further occurrences and refine endpoints with the full captured context. |
| Timbre | Aggregate spectral shape, brightness and MFCCs (mel-frequency cepstral coefficients). Show stable regions and gradual transitions. Loudness has its own measure. | Normalize against the recording and refine boundaries. Keep a consistent orientation across replays. |
| Events | Flag short-lived deviations from recent texture. Merge adjacent peaks into one inspectable event. | Rank distinctive moments relative to the track, keeping gaps outside the comparison. |
| Tempo | Estimate onsets, beat times and a locally smoothed BPM. Display uncertainty in sparse or rubato passages. | Reconcile half/double-time ambiguities across neighboring regions. |
| Tonality | Aggregate chroma over longer windows to estimate local tonal center and mode. | Smooth region boundaries while retaining sustained modulations. |
| Chords | Fit chord templates to short harmonic windows. Show a new label only on sustained evidence, with an unknown state for weak matches. | Reconcile fleeting labels with surrounding harmony. |

A first listen exposes only observed intervals. Track duration, when available, defines the extent of the ruler. Silence is a valid observation. Dropped frames, a disconnected capture source and skipped time are gaps. A completed pass may revise estimates and is labeled as refined. Reference labels like “chorus” are appropriate in test annotations; an acoustically similar passage earns a link even when its lyrical role is unknown.

## Capture and timing

| Target | First-choice signal | Metadata and failure behavior |
| --- | --- | --- |
| Windows 11 x64 | WASAPI process loopback including Spotify’s child processes. Resolve the process again after restart. | Read the matching Windows media session when available. Test normal shared playback with and without Boom 3D. Exclusive output and remote playback can yield no usable signal. |
| Linux x64 with PipeWire and its PulseAudio compatibility service | Identify Spotify’s playback stream and monitor that sink input. Following a stream that moves or is recreated requires re-enumeration. | Match the Spotify MPRIS session when available. Validate JamesDSP and a device change. Offer a clearly labeled whole-output monitor if isolation is unavailable. |

Use passive observation and leave routing under the listener’s control. A switch to mixed output requires an explicit choice. The app must never start microphone capture as a recovery action. Whether an EQ is audible to the analyzer depends on the tap. Record the selected route with the session, test a strong EQ toggle, and describe the measured behavior for supported configurations. [Windows process loopback](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/) · [Linux stream monitoring](https://www.freedesktop.org/software/pulseaudio/doxygen/stream_8h.html) · [Spotify exclusive mode](https://support.spotify.com/uk/article/exclusive-mode/)

Anchor a monotonic sample counter to reported media position and playback rate. Reconcile positions on track changes, pause/resume and seeks. Split intervals on discontinuities, rebuild the live analysis window and prohibit matches across a gap. A seek back may fill missing coverage. Overlapping observations replace or corroborate a range once; they never extend duration twice. If identity, timing or crossfade ownership is ambiguous, keep the observation in a session-relative partial map. When Spotify Connect plays elsewhere, show a waiting state. Metadata alone is insufficient evidence that sound is playing locally.

## Implementation proposal

Ship one user-level helper that serves bundled web assets on loopback, opens the default browser, captures samples and owns the local database. Proposed implementation is Rust with platform capture adapters, a worker for feature extraction, and SQLite. Use React, TypeScript, Vite, pnpm workspaces, Biome, Vitest and Playwright for the web layer. Use Canvas 2D for dense curves and HTML or SVG for selectable annotations. Measure before adding a GPU renderer. Keep the native transport replaceable so a desktop shell remains a packaging choice.

**Engineering references inspected in the sibling checkouts**

| Project and source within that checkout | Practice to carry forward |
| --- | --- |
| Catch · `apps/web/package.json`, `docs/decisions/0003-design-system-and-motion.md` | Typed React/Vite client, bundled offline assets, documented interaction decisions and explicit reduced-motion behavior. |
| FGRM · `fgrm-main/frontend/package.json`, `fgrm-main/frontend/src/index.css` | Define semantic state and typography tokens centrally so dense views can be tuned consistently. |
| Throw · `docs/spec.md`, `README.md`, `apps/web/package.json` | State assumptions alongside decisions and expose a single repeatable check for types, tests and build. |

These references inform tooling and implementation conventions. Develop the score layout, typography, palette, icon treatment and motion specifically for music.

The new repository would contain a web app, native helper, versioned message schemas, deterministic audio fixtures and platform smoke tests. Build native targets on their own operating systems. Host-based development must exercise the actual desktop audio session. The web developer can replay fixture events on any machine.

**Data flow.** Capture thread → bounded sample queue → channel conversion/resampling → short-time features → timeline analysis → local WebSocket → render buffer. Carry source format, capture timestamps and discontinuities through conversion. Process capture off the UI thread. Batches carry session ID, sequence number, covered time range, analysis version and quality flags. On overflow mark missing time and continue at the live edge. On reconnect send a snapshot and subsequent deltas. A late message for the previous track cannot overwrite the current map.

**Storage.** Keep session, recording identity, coverage intervals, compact feature blocks and versioned annotations. Store raw audio only in the transient working buffer. Prefer a stable player-provided recording identifier with duration and feature agreement. Title and artist alone are insufficient to merge remasters, live takes or uncertain sessions. Write feature blocks and coverage atomically. Keep enough blocks for offline refinement and rebuild annotations when their algorithm changes. Closing the last listening UI stops capture by default. A clean stop flushes the last complete block. A restart recovers only committed coverage.

**Trust boundary.** Serve assets and commands from the same loopback origin, validate Host and Origin for HTTP and WebSocket, and bootstrap the browser with a short-lived local capability before accepting capture commands. Bind to loopback only. Package fonts and analysis assets locally. Diagnostics are opt-in exports of format, routing, overruns, timing and build versions. A user can review them before sharing.

**Algorithm lineage.** The legacy `client/src/app/Features.js` constructs features using Spotify segments, known duration and track-wide extrema. Its harmonic worker builds a pitch self-similarity matrix; the timbre worker uses track-wide smoothing, downsampling and multidimensional scaling. Use those routines as reference points for offline refinement. Incremental extraction, stable live coloring and gap-aware time alignment need explicit implementations. Avoid rebuilding an unbounded all-pairs matrix on each frame. Keep bounded live windows and use coarser summaries for long histories. Preserve provenance and applicable notices for code carried to a new repository. [Legacy project](https://github.com/JobSavelsberg/music-structure)

## Delivery and acceptance

1. **Feasibility.** Run the companion experiment in the [MVP spec](MVP-SPEC.md). Its result determines scope and whether to open a new implementation repository.
2. **Desktop alpha.** Deliver isolated capture and the two primary lanes on both target systems. Add track alignment, session recovery, a readable setup flow, and saved partial maps. Verify a replay can fill a skipped interval without shifting earlier marks.
3. **Musical analysis beta.** Add events, chords, tempo and tonal regions. Use a held-out collection across genres and review musical failures per lane. Gate each lane on a listener being able to interpret its output, with low-confidence passages visible as such.
4. **First release.** Publish versioned Windows and Linux user-installable artifacts, a documented support matrix, checksums, and install, upgrade and removal instructions. Exercise upgrade with an existing cache and offer a rebuild if the analysis format changed.

Release checks include deterministic DSP and timestamp tests, contract replay with out-of-order and missing batches, keyboard-only map exploration, and manual listening on physical Windows and Linux systems. Record device, CPU and browser versions alongside latency, memory and CPU measurements. Adopt the MVP budgets for live capture and rendering, then repeat under a 60-minute continuous session. Exercise source loss, pause, seeks, crossfade, track replacement, audio-device change, cache deletion and helper restart. A synthetic pass alone cannot establish Spotify or EQ compatibility.

**Decisions left to evidence.** Choose a product name and final visual treatment after the listening trial. Select the capture libraries and feature extractor after measuring the proposed paths. Decide whether older Windows versions, other Linux audio stacks, or additional players warrant support from actual use. The first release targets the two desktop configurations above.
