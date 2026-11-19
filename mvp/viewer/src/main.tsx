import { useLayoutEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import "@fontsource/instrument-serif/latin-400.css";
import "@fontsource-variable/instrument-sans/wght.css";
import { Score, markName, time } from "./Score";
import { laneDefinitions, type Lane } from "./LabLanes";
import type { TimbreScale } from "./timbre";
import { useHelper, type Mark } from "./wire";
import "./style.css";

const fixtures = { return: "A–B–A study", gain: "Gain step", gap: "Interrupted listen", lanes: "Pulse and harmony", refinement: "Three returns" };
type Fixture = keyof typeof fixtures;
type PaintReading = { index: number; ms: number | null; timestampToPacketReadMs: number | null; afterPacketReadMs: number | null; error: number };
const active = new Set(["waiting", "capturing", "loading", "stopping"]);

function markTimes(mark: Mark) {
  if (mark.occurrences) return mark.occurrences.map(span => `${time(span.start, true)}–${time(span.end, true)}`).join(", ");
  const first = time(mark.start, true);
  const range = `${first}–${time(mark.end, true)}`;
  if (mark.otherStart !== null && mark.otherEnd !== null)
    return `${range} and ${time(mark.otherStart, true)}–${time(mark.otherEnd, true)}`;
  return mark.kind === "gap" || mark.kind === "event" ? range : first;
}

function App() {
  const { map, online, connection, send, alignment } = useHelper();
  const [chosenSource, chooseSource] = useState<number>();
  const [timbreScale, setTimbreScale] = useState<TimbreScale>("fixed");
  const [smooth, setSmooth] = useState(true);
  const [refinedSession, setRefinedSession] = useState<string>();
  const [labOpen, setLabOpen] = useState(false);
  const [lanes, setLanes] = useState<Lane[]>(laneDefinitions.map(lane => lane.id));
  const [showTiming, setShowTiming] = useState(false);
  const [timing, setTiming] = useState<{ session: string; reading: PaintReading }>();
  const [fixture, chooseFixture] = useState<Fixture>("return");
  const [selection, setSelection] = useState<{ session?: string; id: number }>();
  const [setup, setSetup] = useState("");
  const latency = useRef({ session: "", index: 0, readings: [] as PaintReading[] });
  const visibleLanes = labOpen ? lanes : [];
  const viewingRefined = Boolean(map?.refined && refinedSession === map.sessionId);
  const scoreMap = map && viewingRefined && map.refined ? {
    ...map,
    marks: [...map.marks.filter(mark => mark.kind !== "repeat" && mark.kind !== "timbre"), ...map.refined.marks]
      .sort((a, b) => a.start - b.start),
  } : map;
  const visibleMarks = scoreMap?.marks.filter(mark => mark.kind !== "event" || visibleLanes.includes("event"));
  const selected = visibleMarks?.find(mark => selection?.session === map?.sessionId && mark.id === selection?.id);
  const source = map?.sources.find(item => item.pid === chosenSource) ?? map?.sources[0];
  const running = online && active.has(map?.state ?? "");
  const canRefine = online && Boolean(map?.frames.length) && (map?.state === "stopped" || map?.state === "unavailable");
  const capturingSpotify = running && map?.mode === "spotify" && map.state !== "stopping";
  const title = map?.mode && map.mode !== "spotify" && map.source
    ? fixtures[map.source as Fixture] : "Listening score";
  const currentState = !online ? "Offline" : map?.state === "capturing" ? "Receiving audio" :
    map?.state === "waiting" ? "Waiting for audio" : map?.state === "loading" ? "Opening replay" :
    map?.state === "stopping" ? "Stopping" : map?.state === "unavailable" ? "Unavailable" :
    map?.state === "stopped" ? "Review" : "Ready";
  const rms = running ? map?.inputDb : null;
  const meter = rms == null ? 0 : Math.max(0, Math.min(1, (rms + 60) / 60));

  useLayoutEffect(() => {
    if (!map?.sessionId) return;
    if (latency.current.session !== map.sessionId)
      latency.current = { session: map.sessionId, index: 0, readings: [] };

    const state = latency.current;
    const newFrames = map.frames.filter(frame => frame.index > state.index);
    if (!newFrames.length) return;
    state.index = newFrames.at(-1)!.index;
    const sync = alignment.current;
    if (!sync || sync.uncertainty > 25 || document.hidden || map.snapshot) return;
    // A second animation frame supplies an upper bound on the preceding paint.
    requestAnimationFrame(() => requestAnimationFrame(now => {
      if (document.hidden || state !== latency.current) return;
      for (const frame of newFrames) {
        const age = (stamp: number) => {
          const elapsed = now + sync.offset - stamp;
          return elapsed >= -sync.uncertainty ? Math.max(0, elapsed) : null;
        };
        const ms = frame.clockEndMs === null ? null : age(frame.clockEndMs);
        const afterPacketReadMs = frame.packetReadMs === undefined ? null : age(frame.packetReadMs);
        if (ms === null && afterPacketReadMs === null) continue;
        state.readings.push({
          index: frame.index, ms, afterPacketReadMs, error: sync.uncertainty,
          timestampToPacketReadMs: frame.clockEndMs === null || frame.packetReadMs === undefined
            ? null : frame.packetReadMs - frame.clockEndMs,
        });
      }
      const reading = state.readings.at(-1);
      if (showTiming && reading?.index === newFrames.at(-1)!.index)
        setTiming({ session: state.session, reading });
      if (state.readings.length > 7200) state.readings.splice(0, state.readings.length - 7200);
    }));
  }, [map, alignment, showTiming]);

  function exportSummary() {
    if (!map) return;
    const readings = latency.current.readings;
    const sorted = readings.map(reading => reading.ms).filter(ms => ms !== null).sort((a, b) => a - b);
    const summary = {
      version: map.version, analysisVersion: map.analysisVersion, sessionId: map.sessionId, refinement: map.refined,
      exportedAt: new Date().toISOString(),
      setup: {
        notes: setup, browser: navigator.userAgent, logicalCores: navigator.hardwareConcurrency,
        source: map.source, mode: map.mode, format: "44.1 kHz stereo PCM16", analysisRateHz: 22050,
        timbreScale, smooth, clock: "elapsed since capture", isolation: map.mode === "spotify" ? "process tree" : null,
      },
      state: map.state, status: map.status, elapsed: map.elapsed, historyStart: map.historyStart,
      counters: map.counters, frames: map.frames, marks: map.marks,
      paint: { method: "aligned monotonic clocks, second animation frame upper bound",
        count: sorted.length, p95Ms: sorted[Math.ceil(sorted.length * .95) - 1] ?? null, readings },
    };
    const url = URL.createObjectURL(new Blob([JSON.stringify(summary, null, 2)], { type: "application/json" }));
    const download = document.createElement("a");
    download.href = url;
    download.download = `music-structure-${map.sessionId.slice(0, 8)}.json`;
    download.click();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  const reading = timing?.session === map?.sessionId ? timing?.reading : undefined;
  const latest = map?.frames.at(-1);

  function openFixture(mode: "replay" | "fixture") {
    if (fixture === "lanes") setLabOpen(true);
    send({ action: "start", mode, fixture });
  }

  function compare(useRefined: boolean) {
    setRefinedSession(useRefined ? map?.sessionId : undefined);
    setSelection(undefined);
  }

  function select(id: number) {
    setSelection({ session: map?.sessionId, id });
  }

  return <div className="shell" onKeyDown={event => {
    if (event.key === "Escape") setSelection(undefined);
  }}>
    <header className="topbar">
      <div className="brand"><span className="brand-mark" aria-hidden="true"><i/><i/><i/></span> Music Structure</div>
      <span className="state" role="status"><i className={`state-light ${online ? map?.state ?? "" : ""}`}/>{currentState}</span>
    </header>

    <main>
      <section className="heading" aria-labelledby="title">
        <div>
          <div className="mode">{map?.mode === "spotify" ? "Isolated Spotify audio" :
            map?.mode === "fixture" ? "Generated audio running in real time" :
            map?.mode === "replay" ? "Generated fixture replay" : "Local listening companion"}</div>
          <h1 id="title">{title}</h1>
        </div>
        <div className="clock"><strong>{time(map?.elapsed ?? 0)}</strong><span>Elapsed since capture</span></div>
      </section>

      <div className="transport">
        <div className="source-controls">
          <label className="field"><span>Source</span>
            <select aria-label="Playback source" value={source?.pid ?? ""} disabled={!source || running}
              onChange={event => chooseSource(Number(event.target.value))}>
              {!source && <option value="">{map?.windowsCapture ? "No Spotify process found" : "Spotify capture requires Windows 11"}</option>}
              {map?.sources.map(item => <option key={item.pid} value={item.pid}>{item.label}</option>)}
            </select>
          </label>
          <button className="quiet icon-button" type="button" disabled={!online || running || !map?.windowsCapture}
            aria-label="Refresh Spotify sources" title="Refresh Spotify sources" onClick={() => send({ action: "sources" })}>↻</button>
        </div>
        <div className="capture-controls">
          <button type="button" className="quiet" disabled={!capturingSpotify} onClick={() => send({ action: "new" })}>New listen</button>
          {running ? <button className="primary" type="button" disabled={map?.state === "stopping"}
            onClick={() => send({ action: "stop" })}><i className="stop-glyph"/>Stop</button> :
            <button className="primary" type="button" disabled={!online || !source || map?.refining}
              onClick={() => source && send({ action: "start", mode: "spotify", pid: source.pid, started: source.started })}>
              <i className="record-glyph"/>{map?.mode === "spotify" ? "Resume capture" : "Start capture"}
            </button>}
        </div>
        <div className="meter">
          <div><span>Input level</span><strong>{rms == null ? "—" : `${rms.toFixed(0)} dBFS`}</strong></div>
          <meter min="0" max="1" value={meter} aria-label="Input level"/>
        </div>
      </div>

      <section className="score" aria-labelledby="map-heading">
        <header className="score-heading">
          <h2 id="map-heading">Song map</h2>
          <label className="scale-control">
            Timbre scale
            <select value={timbreScale} onChange={event => setTimbreScale(event.target.value as TimbreScale)}>
              <option value="fixed">Fixed</option>
              <option value="relative">Relative</option>
            </select>
          </label>
          <label className="scale-control">
            <input type="checkbox" checked={smooth} onChange={event => setSmooth(event.target.checked)} />
            Smooth timbre
          </label>
          <div className="coverage">{time(map?.historyStart ?? 0)}–{time(map?.elapsed ?? 0)}
            <span> / {map?.historyStart ? "Latest 10 min" : "Captured range"}</span>
          </div>
        </header>
        <div className="lab-toolbar">
          <button type="button" aria-expanded={labOpen} aria-controls="lab-options"
            onClick={() => setLabOpen(open => !open)}>{labOpen ? "Hide listening lab" : "Show listening lab"}</button>
          <div id="lab-options" className="lab-options" hidden={!labOpen} role="group" aria-label="Listening lab lanes">
            {laneDefinitions.map(lane => <label key={lane.id} style={{ "--lane-color": lane.color } as React.CSSProperties}>
              <input type="checkbox" checked={lanes.includes(lane.id)}
                onChange={event => setLanes(current => event.target.checked
                  ? [...current, lane.id] : current.filter(id => id !== lane.id))} />
              {lane.label}
            </label>)}
          </div>
          <div className="comparison">
            {map?.refined ? <div role="group" aria-label="Score version">
              <button type="button" aria-pressed={!viewingRefined} onClick={() => compare(false)}>Original</button>
              <button type="button" aria-pressed={viewingRefined} onClick={() => compare(true)}>Refined</button>
            </div> : canRefine && <button type="button" disabled={map?.refining} onClick={() => {
              compare(true);
              send({ action: "refine", sessionId: map?.sessionId });
            }}>{map?.refining ? "Refining…" : "Refine listen"}</button>}
          </div>
        </div>
        <Score map={scoreMap} selected={selected} select={select} timbreScale={timbreScale}
          smooth={smooth} lanes={visibleLanes} refined={viewingRefined}/>
        <div className="score-footer">
          <span title={map?.status}>{connection || map?.status}</span>
          <span>{map?.counters.features ?? 0} feature frames</span>
        </div>
      </section>

      <section className="review" aria-label="Inspect and replay">
        <div className="annotations">
          <h2>Annotations</h2>
          {visibleMarks?.length ? <ul className="annotation-list">
            {visibleMarks.map(mark => <li key={mark.id}><button type="button" className={`annotation ${mark.kind}`}
              aria-pressed={selected?.id === mark.id} onFocus={() => select(mark.id)} onClick={() => select(mark.id)}>
              <span>{mark.kind === "repeat" ? "↔" : mark.kind === "gap" ? "▧" : mark.kind === "event" ? "✦" : "◆"} <b>{markName(mark)}</b></span>
              <span>{markTimes(mark)}</span>
            </button></li>)}
          </ul> : <p>Detected changes, returning passages and gaps appear here.</p>}
        </div>
        <div className="inspector">
          <h2>Inspect</h2>
          <span className="sr-only" aria-live="polite">
            {selected ? `${markName(selected)} at ${time(selected.start)}` : "No annotation selected"}
          </span>
          <div>
            {selected ? <>
              <strong className="inspection-title">{selected.detail}</strong>
              <p className="inspection-time">{markTimes(selected)}</p>
              {selected.occurrences && <p>{selected.occurrences.length} occurrences. Lowest linked chroma similarity {Math.round(selected.score! * 100)}%.</p>}
              {selected.kind === "repeat" && !selected.occurrences && <p>{Math.round(selected.score! * 100)}% chroma similarity.
                First linked {((selected.detectedAt ?? selected.otherEnd!) - selected.otherStart!).toFixed(1)} s into the return.</p>}
              {selected.kind === "timbre" && <p>Brightness {selected.deltaHz! >= 0 ? "+" : ""}{Math.round(selected.deltaHz!)} Hz.
                Spectral change {selected.score!.toFixed(1)} dB.</p>}
              {selected.kind === "event" && <p>Peak spectral deviation {selected.score!.toFixed(1)} dB from the preceding texture.</p>}
              {selected.kind === "gap" && <p>{(selected.end - selected.start).toFixed(2)} s of missing coverage.</p>}
            </> : <p>Select a link or marker to see what changed and when.</p>}
          </div>
        </div>
      </section>

      <section className="utility" aria-label="Fixtures and diagnostics">
        <div className="fixture-controls">
          <label className="field"><span>Generated fixture</span>
            <select value={fixture} onChange={event => chooseFixture(event.target.value as Fixture)} disabled={running}>
              {Object.entries(fixtures).map(([key, label]) => <option value={key} key={key}>{label}</option>)}
            </select>
          </label>
          <button type="button" className="quiet" disabled={!online || running || map?.refining}
            onClick={() => openFixture("replay")}>Open replay</button>
          <button type="button" className="quiet" disabled={!online || running || map?.refining}
            onClick={() => openFixture("fixture")}>Run in real time</button>
        </div>
        <details onToggle={event => {
          setShowTiming(event.currentTarget.open);
          const state = latency.current;
          const reading = state.readings.at(-1);
          if (event.currentTarget.open && reading) setTiming({ session: state.session, reading });
        }}>
          <summary>Evaluation summary</summary>
          <div className="export">
            {map?.mode && map.mode !== "replay" && <dl className="timing">
              <dt>Latest measured brightness</dt><dd>{latest?.audible && map.inputDb !== null ? `${Math.round(latest.centroidHz)} Hz` : "—"}</dd>
              <dt>Audio timestamp to packet read</dt><dd>{reading?.timestampToPacketReadMs == null ? "—" : `${Math.round(reading.timestampToPacketReadMs)} ms`}</dd>
              <dt>Packet read to chart paint</dt><dd>{reading?.afterPacketReadMs == null ? "—" : `${Math.round(reading.afterPacketReadMs)} ms`}</dd>
            </dl>}
            <label className="field"><span>Setup notes</span>
              <textarea value={setup} onChange={event => setSetup(event.target.value)} maxLength={4000}
                placeholder="Windows build, Spotify version, output device, Boom 3D preset, excerpt and observations"/>
            </label>
            <p>{map?.counters.packets ?? 0} packets · {map?.counters.queueDroppedFrames ?? 0} dropped PCM frames · {(map?.counters.missingSeconds ?? 0).toFixed(2)} s missing</p>
            <button type="button" className="quiet" disabled={!map?.sessionId || running} onClick={exportSummary}>Save JSON summary</button>
          </div>
        </details>
      </section>
    </main>
  </div>;
}

createRoot(document.getElementById("root")!).render(<App/>);
