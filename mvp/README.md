# Music Structure for Windows 11

Music Structure listens to the Spotify desktop process tree through WASAPI and draws a listening score in a local browser. The timbre contour traces spectral brightness. Repeat links join passages that share an eight-second sequence of chord movement. Spotify stays in charge of playback and routing.

## Build and launch on Windows

Install the x64 [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and Node.js 22.12+ or 24 LTS. From the repository root in PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\mvp\scripts\Build-Windows.ps1
.\mvp\out\win-x64\MusicStructure.exe --verify
.\mvp\out\win-x64\MusicStructure.exe
```

The build checks TypeScript, bundles React and the fonts into the helper, and publishes a self-contained x64 executable. Once built, the helper needs no SDK, Node.js, Spotify credentials or internet access. It prints a local URL and opens it in the default browser. Use `--no-open` to paste the URL yourself. Use `--demo` to open the A–B–A replay immediately. The bundled viewer licenses are served at `/viewer-licenses.txt`.

1. Open Spotify desktop, play locally on the intended output and choose its process in **Source**. If Spotify opened after the helper page, use **Refresh playback sources**.
2. Choose **Start capture**. The status and dBFS meter distinguish arriving Spotify audio from waiting. Use **New listen** at a track boundary or after seeking.
3. Select a repeat link, texture marker or gap. Tab focus also selects a marker. Escape clears the selection. The time ruler uses elapsed capture time. Use **Timbre smoothing** to choose Off, 0.5, 1, 2 or 4 seconds. The live contour averages recent brightness measurements, with a one-second window by default. Longer windows tame spikes and take longer to settle after sharp changes. Choose **Off** to see each quarter-second measurement. In the song map, set **Timbre scale** to **Relative** to spread out the visible range after four seconds of audible features. **Fixed** keeps the same axis across listens.
4. Choose **Stop** to freeze the score, then open **Evaluation summary**. Add setup notes and save timestamped features, findings and counters as JSON. Closing the listening tab stops capture. Press Ctrl+C in the helper console to exit.
5. After a Spotify restart or capture error, **Refresh playback sources** and **Resume capture**. This preserves available history and places a gap before newly captured material. Use **New listen** to clear the map.

### Update, rebuild, and launch

Quit the running helper with Ctrl+C before updating. From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\mvp\scripts\Update-And-Run-Windows.ps1
```

The script requires Git and a clean working tree. It pulls the current branch from its configured upstream, or from the same branch on `origin` if no upstream is configured. It accepts only fast-forward updates and launches the helper after a successful build. Add `-Demo` to start with the A–B–A replay or `-NoOpen` to print the URL without opening a browser.

## Experimental ML texture and persistence A/B

For VLC process capture, model installation, and the listening comparison, see the [ML texture guide](ML-TEXTURE.md). It also records the generated control results and CPU measurements.

## Try the listening lab

Choose **Show listening lab** above the song map. Use the checkboxes to compare the four experimental lanes with timbre and repeats on the same ruler. Move **Inspect at** to read an earlier estimate while capture continues. Choose **Follow live** to return to the latest frame.

| Lane | What to listen for |
| --- | --- |
| Events | Click an orange star and listen around its timestamp. A hit or brief texture change should stand out from what came just before it. |
| Tempo | Tap along to a steady section and compare with the BPM. Pale ticks are observed onsets. Taller bright ticks are onsets that also fit the estimated beat. Half-time and double-time readings are possible. |
| Tonality | Give this lane roughly 12 seconds of harmonic context. Compare its estimated key with a known tonal center and note whether a real modulation produces a convincing change. |
| Chords | Compare a progression you know by ear or can play along with. The estimator fits major and minor triads and common seventh chords, including voicings with a different bass note. A question mark after a label means tentative. Unknown appears when the pitch evidence is too weak or ambiguous. |

For a known control, choose **Pulse and harmony** and **Open replay**. The study contains a 120 BPM C-major section, a 90 BPM E-flat-major section, accents around 10 and 38 seconds, silence at 48 seconds and a simulated gap from 52 to 56 seconds. The harmony and tempo estimates use recent context, so changes can follow the section boundary by several seconds. The generated control establishes basic behavior. Use familiar Spotify recordings to judge how much each lane helps.

For the chord-specific control, open **Chord voicings**. Its chord roots and qualities at 2, 6, 10, 14, 18 and 22 seconds are **C, Am7, Fmaj7, G7, D and Bm**. The C and D voicings have E and F-sharp in the bass, respectively. From 24 seconds the sound is silence, a lone bass note and an ambiguous cluster. Inspect those ranges for **Unknown**.

## Compare original and refined scores

Capture a song from the beginning, then choose **Stop** and **Refine listen**. Use **Original** and **Refined** above the score to switch between estimates on the same timeline. Choose a lettered repeat link to highlight every occurrence in its family. Use the texture markers to compare where each pass places a change. The JSON summary includes both sets of findings.

For the known control, select **Three returns** and choose **Open replay**, then **Refine listen**. Family A should cover passages near 0–12, 22.5–34.5 and 46.5–58.5 seconds. The refined markers should land at 12, 22.5, 34.5 and 46.5 seconds. There is a fourth copy of the phrase after the 58.5–62.5 second capture gap. The pass analyzes each continuous capture span independently.

The pass uses the retained feature history, up to ten minutes. It searches for sustained harmonic matches over the captured spans and measures texture change using spectral variation across the listen. The refined line centers the selected smoothing window around each point. Both versions keep the same scale bounds when you change smoothing on a stopped listen. Use **Fixed** to keep the same bounds across different songs. After the fixture, try a familiar Spotify song and compare the highlighted returns and boundary times by ear.

## Generate or replay without Spotify

The fixture menu has an A–B–A study, a constant-texture gain step, an interrupted listen, a pulse-and-harmony control, a repeat-family study and a chord-voicing study. **Run in real time** feeds synthetic PCM into the normal sample queue while the map advances. **Open replay** sends the completed feature map through the same versioned socket and viewer. Generated fixtures are silent to the listener.

On any platform with the SDK, build and run the fixture viewer from the repository root:

```sh
npm ci --prefix mvp/viewer
npm run check --prefix mvp/viewer
npm run build --prefix mvp/viewer
dotnet build mvp/helper -c Release
dotnet mvp/helper/bin/Release/net10.0/MusicStructure.dll --verify
dotnet mvp/helper/bin/Release/net10.0/MusicStructure.dll --demo
```

## Capture and analysis

- The helper uses Microsoft's process-loopback activation pattern with `INCLUDE_TARGET_PROCESS_TREE`. It requests shared, event-driven 44.1 kHz stereo PCM16 and releases each native packet after copying it to a bounded 24-packet analysis queue. It counts discarded frames and resets across sequence breaks, WASAPI discontinuities and packet timeouts. There is no mixed-output source. [Microsoft process-loopback sample](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)
- Analysis uses equal L/R weights, a low-pass filter and decimation to 22.05 kHz. It measures RMS, centroid, normalized log-mel energy and chroma with a 2,048-sample Hann window and a 512-sample hop, aggregating into quarter-second frames. Silence below −58 dBFS gates the detectors. A valid silent frame and a missing interval are represented separately.
- Texture compares adjacent two-second windows for a sustained 15.5 dB log-mel shape difference. Repeat search runs once a second over up to ten minutes of features, requires chroma similarity of at least .91 with aligned frames and local contrast, and extends a continuing pair by one second at a time. Gaps split analysis and prevent cross-gap matches. The thresholds were set using generated controls.
- The helper serves the embedded viewer from an ephemeral IPv4 loopback port. The same-origin WebSocket validates Origin and a helper-lifetime capability from the printed URL. One listening tab holds the capture lease. Batches carry a session ID, sequence, feature deltas and snapshot state. The viewer drops history older than ten minutes. Audio stays in memory. Only the explicit JSON export writes listening data.

See the [Windows smoke protocol](WINDOWS-SMOKE.md) for physical validation and the [evidence record](EVIDENCE.md) for checks already run.
