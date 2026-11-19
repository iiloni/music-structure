# Music Structure for Windows 11

Music Structure listens to the Spotify desktop process tree through WASAPI and draws two lanes in a local browser. The timbre contour traces spectral brightness. Repeat links join passages that share an eight-second sequence of chord movement. Spotify stays in charge of playback and routing.

## Build and launch on Windows

Install the x64 [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and Node.js 22.12+ or 24 LTS. From the repository root in PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\mvp\scripts\Build-Windows.ps1
.\mvp\out\win-x64\MusicStructure.exe --verify
.\mvp\out\win-x64\MusicStructure.exe
```

The build checks TypeScript, bundles React and the fonts into the helper, and publishes a self-contained x64 executable. Once built, the helper needs no SDK, Node.js, Spotify credentials or internet access. It prints a local URL and opens it in the default browser. Use `--no-open` to paste the URL yourself. Use `--demo` to open the A–B–A replay immediately. The bundled viewer licenses are served at `/viewer-licenses.txt`.

1. Open Spotify desktop, play locally on the intended output and choose its process in **Source**. If Spotify opened after the helper page, use **Refresh Spotify sources**.
2. Choose **Start capture**. The status and dBFS meter distinguish arriving Spotify audio from waiting. Use **New listen** at a track boundary or after seeking.
3. Select a repeat link, texture marker or gap. Tab focus also selects a marker. Escape clears the selection. The time ruler uses elapsed capture time.
4. Choose **Stop** to freeze the score, then open **Evaluation summary**. Add setup notes and save timestamped features, findings and counters as JSON. Closing the listening tab stops capture. Press Ctrl+C in the helper console to exit.
5. After a Spotify restart or capture error, **Refresh Spotify sources** and **Resume capture**. This preserves available history and places a gap before newly captured material. Use **New listen** to clear the map.

## Generate or replay without Spotify

The fixture menu has an A–B–A study, a constant-texture gain step and a listen interrupted by silence and a four-second capture gap. **Run in real time** feeds synthetic PCM into the normal sample queue while the map advances. **Open replay** sends the completed feature map through the same versioned socket and viewer. Generated fixtures are silent to the listener.

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
- Texture compares adjacent two-second windows for a sustained 15.5 dB log-mel shape difference. The contour uses a fixed logarithmic centroid scale. Repeat search runs once a second over up to ten minutes of features, requires chroma similarity of at least .91 with aligned frames and local contrast, and extends a continuing pair by one second at a time. Gaps split analysis and prevent cross-gap matches. The thresholds were set using generated controls.
- The helper serves the embedded viewer from an ephemeral IPv4 loopback port. The same-origin WebSocket validates Origin and a helper-lifetime capability from the printed URL. One listening tab holds the capture lease. Batches carry a session ID, sequence, feature deltas and snapshot state. The viewer drops history older than ten minutes. Audio stays in memory. Only the explicit JSON export writes listening data.

See the [Windows smoke protocol](WINDOWS-SMOKE.md) for physical validation and the [evidence record](EVIDENCE.md) for checks already run.
