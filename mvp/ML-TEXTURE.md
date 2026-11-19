# Live ML texture and persistence A/B

## Run on Windows

Build from the repository root with the [usual Windows build script](README.md#build-and-launch-on-windows). Place the model beside the executable, including when using the cross-built package. Run these commands in PowerShell.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\mvp\scripts\Install-MLModel.ps1
.\mvp\out\win-x64\MusicStructure.exe --verify-ml
.\mvp\out\win-x64\MusicStructure.exe
```

The installer downloads and checks the SHA-256 of the original weights. If you copied the executable to another directory, pass that directory as `-Destination`. The model is loaded on demand. Windows uses the x64 CPU build of ONNX Runtime 1.24.4. If its native libraries fail to load, check the [runtime prerequisites](https://onnxruntime.ai/docs/install/#requirements). The helper serves the runtime license and notices at `/onnx-runtime-license.txt` and `/onnx-runtime-notices.txt`.

1. Open Spotify desktop, or play an original or appropriately licensed local recording in VLC. In Music Structure, choose **Refresh playback sources** and select the player.
2. Check **Experimental ML texture** and choose **Start capture**. Allow about four seconds of audible audio for a pair of embeddings. The amber texture lane appears beneath the timbre contour. The experiment is off by default. Opt-in applies to the selected player process and clears when you switch sources. Starting Spotify with ML enabled, or after an ML listen, begins a fresh map so the model worker and baseline start cleanly.
3. Switch between **A · Spectrum** and **B · MusiCNN** to change both the lane and the candidate verdict. Both analyses continue during capture. Bars show the change at each trailing window end in multiples of the selected analysis's threshold. The dotted line marks 1× and the lane tops out at 3×. Short grey ticks mark hops without a comparison. Open circles show provisional candidates and filled circles show resolved candidates. Either analysis can nominate a candidate. The shaded band after each circle marks the check window, during which additional tall bars do not start candidates.
4. Choose a **Candidate** below the score to pin its time and reference while switching views. Candidates from the first fresh window after a break are labeled **After silence** or **After a gap**. Compare the provisional score or final verdict with what you heard. Note useful calls, missed changes and false alarms in **Evaluation summary**. Stop and export the JSON to save both lane scores, verdicts and timing counters.

Use **Generated fixture → ML texture control → Run ML control** for the repeatable 72-second in-memory test. The fixture plays silently and runs the same worker as VLC. It has a short fill at 8 s, a brightness- and loudness-matched instrument entrance at 16 s, a hard gain drop at 26 s, gradual layering from 36–45 s, silence from 45–49 s and a simulated gap from 60–63 s. The audio returns with a different texture after each break. A change at 58 s leaves a candidate pending when the gap starts.

## Model and comparison

- The original **MSD MusiCNN v1** ONNX package is 3.1 MiB. The helper uses its 200-dimensional `embeddings` output, with one CPU inference per hop. Attribution goes to the Music Technology Group, Universitat Pompeu Fabra, and the MusiCNN authors. The publisher's models page states CC BY-NC-SA 4.0. Its linked notice names NoDerivatives in the heading and links to BY-NC-SA in the legal code. This pilot downloads the unchanged publisher weights for noncommercial evaluation. The repository and executable contain no model weights. [Publisher model catalog](https://essentia.upf.edu/models.html) · [Official ONNX weights](https://essentia.upf.edu/models/autotagging/msd/msd-musicnn-1.onnx) · [Publisher license notice](https://essentia.upf.edu/models/LICENSE)
- The worker takes PCM before production feature reduction. It averages stereo to mono, resamples to 16 kHz with an anti-aliasing filter, and feeds 187 frames of 96 log-mel bands into MusiCNN. Each frame uses an unnormalized 512-point Hann window, a 256-sample stride, power, Slaney spacing, area-normalized linear triangles, and `log10(1 + 10000 × energy)`. The transform matches reference bins from Essentia 2.1b6.dev1389. Short-frame RMS conditioning to 0.1, with gain capped from 0.25× to 4×, is an extra experimental step to reduce sensitivity to level. Each patch is 3.008 seconds, trailing only, with a one-second hop. [Publisher preprocessing implementation](https://github.com/MTG/essentia/blob/master/src/algorithms/spectral/tensorflowinputmusicnn.cpp) · [Model dimensions and embedding documentation](https://essentia.upf.edu/tutorial_tensorflow_auto-tagging_classification_embeddings.html)
- The PCM queue holds at most 40 packets and discards the oldest on overrun. Inference stays on the ML worker. Gaps, sequence loss and sustained silence reset model windows. The first comparable window after a break uses the last two comparable pre-break hops and their thresholds, when available. A score at or above 1× can start a new candidate about three seconds after audio returns. A new listen clears the reference.
- A averages complete quarter-second production mel frames inside each live patch. B uses the embedding already produced for that patch. Distance is RMS mel difference for A and cosine distance for B. Both thresholds use three times the recent median step with separate fixed minimums of 1.5 dB and 0.015. During continuous playback, either representation can nominate a candidate if its distance from the previous hop reaches its threshold. For confirmation, both use the same two reference hop times and freeze their thresholds at nomination. After five fresh one-second hops, return toward the reference means **Brief event**, persistent departure means **Lasting change**, and inconclusive or interrupted evidence means **Uncertain**. Candidate time and verdict time are recorded separately.

## What the generated test found

| Control | A · Spectrum | B · MusiCNN | Candidate and decision |
| --- | --- | --- | --- |
| Short fill | Brief event | Brief event | 9.008 s, then 14.008 s |
| Sustained instrument entrance | Lasting change | Lasting change | 17.008 s, then 22.008 s |
| Hard gain-only drop | Uncertain | Brief event, a false alarm | 27.008 s, then 32.008 s |
| Gradual layering | No shared candidate | Small novelty peak of 0.00562 | Below threshold |
| Changed texture after silence | Lasting change | Lasting change | 52.008 s, then 57.008 s |
| Change interrupted by the gap | Uncertain | Uncertain | 59.008 s, resolved at the gap notice |
| Changed texture after the gap | Lasting change | Lasting change | 66.008 s, then 71.008 s |

The sustained swap measured −0.001 dB RMS and +0.005% centroid difference in the production features. The first above-threshold ML bar ended **1.008 s after** the fill, instrument entrance and hard gain drop in audio time. Ordinary decisions took another **5.0 s**. The hard gain transient can fool the model even with level conditioning. A 60 ms gain ramp in a preliminary control yielded a much smaller score, so an abrupt gain edge is worth testing separately from a gradual volume change.

On Fedora 44, a headless Chromium run of the 72-second control produced 56 embeddings with zero drops in either queue. Excluding the first patch, CPU inference was **55.8 ms median / 109.6 ms p95**, preprocessing **11.5 / 23.0 ms**, and trailing window end to helper result **108.9 / 177.5 ms**. The A/B comparison used another **25.0 ms total** across 56 paired hops. The gap callback resolved the pending candidate as uncertain at 60.25 s. A separate detector check confirmed an unchanged return stayed below threshold and a new listen cleared the old reference.

These timings cover generated streaming and helper results, before browser paint. Chromium verified that toggling A/B during capture sent no capture command and kept the candidate pinned. The stopped fixture also verified the bar changes, the return labels and the JSON export. The Windows x64 binary was cross-published. Physical VLC and Spotify ML capture, Windows latency, and a GPU comparison remain unmeasured.
