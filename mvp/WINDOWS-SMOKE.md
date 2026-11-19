# Windows 11 smoke and listening protocol

Run on the listener's Windows 11 x64 machine using normal Spotify desktop playback. Run the [build and launch commands](README.md#build-and-launch-on-windows) and save the `--verify` output. This protocol requires a listener, the intended output device and Boom 3D for the EQ rows.

## Setup log

Record Windows build (from `winver`), CPU model and logical cores, Spotify version and installation source, browser version, Boom 3D version and preset, physical output and selected Windows route. Record Spotify volume, output volume, normalization, crossfade and any spatial audio settings. Choose a clearly audible 30–45 second Spotify excerpt and note the track/version and Spotify start time. Use the same excerpt and settings across both EQ passes. If Boom uses a virtual device, record its route and keep the physical output the same.

## Shared playback and isolation

Run each pass with the ordinary listening route. Save one summary per row with the setup log, relevant elapsed times and the Windows task observations.

| Pass | Run | Passing observation |
| --- | --- | --- |
| Boom 3D off | Start local Spotify playback, select its process and start capture. At the agreed excerpt start, choose **New listen**. Listen through the excerpt. Pause Spotify and wait at least three seconds for the meter to settle. Play the isolation tone below. Resume Spotify, then stop the helper capture while still listening. | Spotify is audible and its meter and score respond. The independent tone is clearly audible on the listening device. The isolated meter stays at its paused baseline or shows waiting during that tone and no tone-shaped brightness trace appears. Starting and stopping capture causes no double playback, dropout or output change. |
| Boom 3D on | Enable a strong, clearly audible EQ preset. Repeat the same excerpt, pause, independent tone, resume and stop sequence at the same volumes. | Repeat the off-pass observations with Boom enabled. Record whether the tonal change is audible and whether the captured centroid and log-mel values change across the corresponding excerpt. A difference describes where the tap sits in this setup. |

From a separate PowerShell window, after Spotify has paused and the input has settled:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\mvp\scripts\Play-IsolationTone.ps1 -Seconds 5
```

The command plays five 880 Hz pulses in the normal render route from a separate process and reports its PID and start wall time. Record the score's elapsed time before and after the tone. Listen for all five pulses. If the tone is inaudible or on another physical route, mark isolation unverified and fix routing before repeating. Use the saved quarter-second RMS values to verify that the paused baseline stays below −58 dBFS where valid frames exist. Inspect the score for new structure in that interval and record any false event.

Compare the median centroid and per-band mean log-mel energy for the same clean 20-second window in your two JSON exports. Use only frames with `valid: true` and `audible: true`, leaving margin at each edge for the manual start. Report the actual frequencies and band differences, plus any uncertainty in timing, rather than inferring EQ order from a visual color. If testing a setting changes route, record both routes.

## Waiting and recovery

| Case | Procedure | Observe |
| --- | --- | --- |
| No local sound | Leave the app open with Spotify paused. Try playback on another Spotify Connect device, then return to local playback. | Waiting or silence during remote playback. Local audio resumes on the chosen source. A silent packet can advance elapsed capture time. |
| Source interruption | While Spotify is playing, quit Spotify fully. Reopen it, refresh sources and resume capture. | Capture becomes unavailable, old coverage is retained, and the resumed map has a gap. No repeat spans the gap. |
| Output change | During playback, remove the active output or change to a second output in Windows. | If samples stop or WASAPI reports a discontinuity, mark the missing interval and reset the analysis. If capture becomes unavailable, reselect Spotify and resume. Record what remained audible through the change. |
| Exclusive playback | If Spotify and the target device expose an exclusive playback option, enable it and repeat the capture attempt. | Record whether isolated samples arrive or the source waits or becomes unavailable. Mark unverified if exclusive playback is unavailable. |
| Close | Start capture, then close the listening tab while the helper remains open. Reopen the printed URL after a moment. | The score returns frozen. No new frames arrive until capture is started explicitly. Spotify remains audible throughout. |

## Thirty-minute run

Use a dedicated browser instance with only the listening tab and leave it in the foreground. Record a five-minute idle baseline for the same helper and browser, then use a continuous 30-minute Spotify playlist run. Leave the same route in place and avoid seeks or manual resets during this measurement. The visible score retains the latest ten minutes while capture counters accumulate for the full listen.

- Export the stopped run. Record `gapsOver250Ms`, `maxGapSeconds`, total missing time, discontinuities, timestamp errors and dropped PCM frames. Account for every gap over 250 ms using the operator log.
- Record `paint.p95Ms`, clock uncertainty and sample count. The viewer estimates an upper bound on first paint with a second animation frame and aligns clocks using timestamp exchanges. It excludes replay snapshots, hidden-tab observations and observations with greater than 25 ms clock uncertainty. Compare the p95 with 500 ms. Inspect `detectedAt` separately for detector lookback.
- Sample the helper and dedicated browser process tree in Windows Performance Monitor. Compare combined mean CPU with idle, expressed as a percentage of one logical core, and record combined private working set at minutes five and thirty. The proposed budgets are below 15% above idle and under 50 MiB of growth. For Task Manager CPU percentages, multiply by the logical core count to express them as a percentage of one logical core.
- Save a screenshot with one selected repeat and one with a texture change or gap. Keep setup, counter export, timing measurements and listener notes with the evidence.

## Listening score

Choose two calibration tracks before scoring six distinct held-out Spotify tracks. Follow the [MVP evaluation protocol](../MVP-SPEC.md#evaluation-protocol) for track mix, independent annotations, duration overlap and counterbalanced baseline listening. Keep this trial separate from the synthetic controls.

For each track, log the annotated timbre times, predicted times and one-to-one matches within ±3 seconds. Record both spans of each expected or observed return, seconds to the first link, duplicate links and why any false link was misleading. Pool timbre precision and recall. Report correct returns for the four annotated tracks and false repeats per track, including the negative example. Record where the listener located a change or return unaided and which view they chose for the next song.

Use the [decision criteria](../MVP-SPEC.md#decision-record) to make the go, revise or stop decision after capturing device and listener evidence.
