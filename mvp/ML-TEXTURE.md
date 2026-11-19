# Live ML texture and persistence A/B

## Run on Windows

Build from the repository root with the [usual Windows build script](README.md#build-and-launch-on-windows). Place the model beside the executable, including when using the cross-built package. Run these commands in PowerShell.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\mvp\scripts\Install-MLModel.ps1
.\mvp\out\win-x64\MusicStructure.exe --verify-ml
.\mvp\out\win-x64\MusicStructure.exe
```

The installer downloads and checks the SHA-256 of the original weights. If you copied the executable to another directory, pass that directory as `-Destination`. The model is loaded on demand. Windows uses the x64 CPU build of ONNX Runtime 1.24.4. If its native libraries fail to load, check the [runtime prerequisites](https://onnxruntime.ai/docs/install/#requirements). The helper serves the runtime license and notices at `/onnx-runtime-license.txt` and `/onnx-runtime-notices.txt`.

1. Open VLC desktop and play an original or appropriately licensed local recording you know. In Music Structure, choose **Refresh playback sources** and select **VLC**.
2. Check **Experimental ML texture** and choose **Start capture**. Allow about four seconds of audible audio for a pair of embeddings. The amber **ML texture** lane appears beneath the original timbre contour. The experiment is off by default and the helper rejects ML requests for Spotify.
3. Watch the same moment in the original timbre lane and the ML lane. Amber bars show successive-embedding novelty at the trailing window end. The dotted line is the 0.015 prototype threshold. Open circles show provisional A/B candidates and filled circles show resolved candidates.
4. Choose a **Candidate** below the score, then switch between **A · Spectrum** and **B · MusiCNN**. Its time and reference stay pinned. Compare the provisional score or final verdict with what you heard. Try one short fill, one instrument entrance that stays, and one steady passage. Note useful calls, missed changes and false alarms in **Evaluation summary**. Stop and export the JSON to save both judgments and timing counters.

Use **Generated fixture → ML texture control → Run ML control** for the repeatable 68-second in-memory test. The fixture plays silently and runs the same worker as VLC. Its controls are a short fill at 8 s, a brightness- and loudness-matched instrument entrance at 16 s, a hard gain drop at 26 s, a gradual layer from 36–45 s, silence at 45 s and a simulated gap from 57–60 s. An additional change at 55 s leaves a pending candidate at the gap.

## Model and comparison

- The original **MSD MusiCNN v1** ONNX package is 3.1 MiB. The helper uses its 200-dimensional `embeddings` output, with one CPU inference per hop. Attribution goes to the Music Technology Group, Universitat Pompeu Fabra, and the MusiCNN authors. The publisher's models page states CC BY-NC-SA 4.0. Its linked notice names NoDerivatives in the heading and links to BY-NC-SA in the legal code. This pilot downloads the unchanged publisher weights for noncommercial evaluation. The repository and executable contain no model weights. [Publisher model catalog](https://essentia.upf.edu/models.html) · [Official ONNX weights](https://essentia.upf.edu/models/autotagging/msd/msd-musicnn-1.onnx) · [Publisher license notice](https://essentia.upf.edu/models/LICENSE)
- The worker takes PCM before production feature reduction. It averages stereo to mono, resamples to 16 kHz with an anti-aliasing filter, and feeds 187 frames of 96 log-mel bands into MusiCNN. Each frame uses an unnormalized 512-point Hann window, a 256-sample stride, power, Slaney spacing, area-normalized linear triangles, and `log10(1 + 10000 × energy)`. The transform matches reference bins from Essentia 2.1b6.dev1389. Short-frame RMS conditioning to 0.1, with gain capped from 0.25× to 4×, is an extra experimental step to reduce sensitivity to level. Each patch is 3.008 seconds, trailing only, with a one-second hop. [Publisher preprocessing implementation](https://github.com/MTG/essentia/blob/master/src/algorithms/spectral/tensorflowinputmusicnn.cpp) · [Model dimensions and embedding documentation](https://essentia.upf.edu/tutorial_tensorflow_auto-tagging_classification_embeddings.html)
- The PCM queue holds at most 40 packets and discards the oldest on overrun. Inference stays on the ML worker. Capture gaps, sequence loss, source changes and sustained silence reset the model context and the persistence reference. Scores, timestamps and CPU timings can be exported. The helper holds samples and embeddings in memory.
- A averages complete quarter-second production mel frames inside each live patch. B uses the embedding already produced for that patch. Either can nominate a shared timestamp when its distance from the previous hop exceeds its normalized threshold. Distance is RMS mel difference for A and cosine distance for B. Both scales use three times the recent median step with separate fixed minimums of 1.5 dB and 0.015. Both references are the same two preceding hop times. The reference and scale are frozen at nomination. After five fresh one-second hops, return toward the reference means **Brief event**, persistent departure means **Lasting change**, and inconclusive or interrupted evidence means **Uncertain**. Candidate time is preserved separately from verdict time.

## What the generated test found

| Control | A · Spectrum | B · MusiCNN | Candidate and decision |
| --- | --- | --- | --- |
| Short fill | Brief event | Brief event | 9.008 s, then 14.008 s |
| Sustained instrument entrance | Lasting change | Lasting change | 17.008 s, then 22.008 s |
| Hard gain-only drop | Uncertain | Brief event, a false alarm | 27.008 s, then 32.008 s |
| Gradual layering | No shared candidate | Small novelty peak of 0.00562 | Below threshold |
| Silence and capture gap | Uncertain on interrupted candidate | Uncertain on interrupted candidate | 56.008 s, stopped as uncertain at the first gap notice; fresh baseline afterward |

The sustained swap measured −0.001 dB RMS and +0.005% centroid difference in the production features. The first above-threshold ML bar ended **1.008 s after** the fill, instrument entrance and hard gain drop in audio time. Ordinary decisions took another **5.0 s**. The hard gain transient can fool the model even with level conditioning. A 60 ms gain ramp in a preliminary control yielded a much smaller score, so an abrupt gain edge is worth testing separately from a gradual volume change.

On Fedora 44 with an Intel Core i7-1065G7, a headless Chromium run of the real-time control produced 52 embeddings and dropped zero packets in either queue. Excluding the first patch, CPU inference was **42.1 ms median / 49.0 ms p95**, preprocessing **8.8 / 10.5 ms**, and trailing window end to helper result **92.7 / 100.7 ms**. The first fill result was ready about **1.10 s** after its programmed onset. The A/B comparison used another **17.7 ms total** across 52 paired hops, about **0.34 ms per hop**, with no separate inference. The gap callback resolved the pending candidate as uncertain at 57.25 s, before its scheduled check.

These timings cover generated streaming and helper results, before browser paint. Chromium also verified that the original score still runs when weights are missing, and that a pinned candidate can be inspected in both views. The Windows x64 binary was cross-published. Physical VLC capture, Windows latency, and a GPU comparison remain unmeasured.
