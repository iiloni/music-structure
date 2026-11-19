import { useLayoutEffect, useMemo, useRef, useState } from "react";
import type { Frame, MapState, Mark, Passage } from "./wire";
import { drawLabLanes, laneDefinitions, type Lane, type LaneRow } from "./LabLanes";
import { brightnessHz, brightnessRange, centeredBrightness, displayBrightness, type TimbreScale, type TimbreWindow } from "./timbre";

export function time(seconds: number, precision = false) {
  const rounded = Math.max(0, precision ? Math.round(seconds * 10) / 10 : Math.floor(seconds + 0.001));
  const minutes = Math.floor(rounded / 60);
  const remainder = rounded % 60;
  return `${String(minutes).padStart(2, "0")}:${(precision ? remainder.toFixed(1) : String(remainder)).padStart(precision ? 4 : 2, "0")}`;
}

export function markName(mark: Mark) {
  if (mark.family) return `Repeat family ${mark.family}`;
  return { repeat: "Repeat", gap: "Gap", timbre: "Texture change", event: "Transient event" }[mark.kind];
}

const repeatColor = "#aeaeff", contourColor = "#9cebd6", markerColor = "#ffab86";
const ink = "#18264c", grid = "#36446e", faint = "#aab8d6";
const top = 5, repeatBaseline = 123, contourTop = 163, contourBottom = 283;

export function Score({ map, selected, select, timbreScale, smoothing, lanes, refined }: {
  map?: MapState; selected?: Mark; select: (id: number) => void; timbreScale: TimbreScale; smoothing: TimbreWindow; lanes: readonly Lane[]; refined: boolean;
}) {
  const wrapper = useRef<HTMLDivElement>(null);
  const canvas = useRef<HTMLCanvasElement>(null);
  const [width, setWidth] = useState(0);
  const [inspection, setInspection] = useState<{ session: string; seconds: number }>();
  let timelineBottom = 303;
  const rows: LaneRow[] = [];
  for (const lane of laneDefinitions) {
    if (!lanes.includes(lane.id)) continue;
    rows.push({ ...lane, top: timelineBottom });
    timelineBottom += lane.height;
  }
  const chartHeight = timelineBottom + 37;
  const eventLane = rows.find(row => row.id === "event");
  const from = map?.historyStart ?? 0;
  const last = map?.elapsed ?? 0;
  const plotWidth = Math.max(1, width - 30);
  const horizon = from + Math.max(48, last - from);
  const x = (value: number) => 14 + ((Math.max(from, value) - from) / (horizon - from)) * plotWidth;
  const frames = map?.frames;
  const rawContour = useMemo(() => displayBrightness(frames ?? [], 0), [frames]);
  const contour = useMemo(() => {
    if (!smoothing) return rawContour;
    return refined ? centeredBrightness(frames ?? [], smoothing) : displayBrightness(frames ?? [], smoothing);
  }, [frames, rawContour, refined, smoothing]);
  const [low, high] = useMemo(() => brightnessRange(rawContour, timbreScale), [rawContour, timbreScale]);
  const axisValues = [high, (low + high) / 2, low];
  const axisPositions = [contourTop, (contourTop + contourBottom) / 2, contourBottom];
  const marks = map?.marks;
  const repeats = (marks ?? []).flatMap(mark => {
    if (mark.otherStart === null || mark.otherEnd === null || mark.otherEnd < from) return [];
    const spans = mark.occurrences ?? [mark, { start: mark.otherStart, end: mark.otherEnd }];
    return spans.slice(1).map((next, index) => ({ mark, spans, first: spans[index], next }));
  });
  const events = marks?.filter(mark => mark.kind !== "repeat" && (mark.kind !== "event" || eventLane)) ?? [];
  const pinned = map && inspection?.session === map.sessionId ? inspection.seconds : undefined;
  const inspectedAt = Math.max(from, Math.min(last, pinned ?? last));
  const tail = frames?.at(-1);
  const currentGap = tail && events.some(mark => mark.kind === "gap" && mark.start >= tail.end - .001 && mark.end >= last - .001);
  const inspected = inspectedAt === last && tail && last - tail.end <= .26 && !currentGap ? tail :
    frames?.find(frame => frame.start <= inspectedAt && frame.end > inspectedAt);

  function arc(first: Passage, next: Passage) {
    const a = x((first.start + first.end) / 2);
    const b = x((next.start + next.end) / 2);
    const depth = Math.min(101, 28 + (b - a) * .18);
    return { a, b, depth, labelY: repeatBaseline - depth * .75 };
  }

  useLayoutEffect(() => {
    const element = wrapper.current!;
    const resize = new ResizeObserver(entries => setWidth(entries[0].contentRect.width));
    resize.observe(element);
    return () => resize.disconnect();
  }, []);

  useLayoutEffect(() => {
    if (!width) return;
    const element = canvas.current!;
    const context = element.getContext("2d");
    if (!context) return;
    const scale = window.devicePixelRatio || 1;
    element.width = Math.round(width * scale);
    element.height = Math.round(chartHeight * scale);
    context.scale(scale, scale);

    function line(x0: number, y0: number, x1: number, y1: number, color: string) {
      context!.strokeStyle = color;
      context!.beginPath();
      context!.moveTo(x0, y0);
      context!.lineTo(x1, y1);
      context!.stroke();
    }

    context.fillStyle = ink;
    context.fillRect(0, 0, width, chartHeight);
    context.lineWidth = 1;
    context.font = '11px "Instrument Sans Variable", sans-serif';
    context.textAlign = "center";
    context.fillStyle = faint;
    const candidates = [5, 10, 15, 20, 30, 60, 120];
    const tick = candidates.find(value => value * plotWidth / (horizon - from) >= 57) ?? 120;
    for (let second = Math.ceil(from / tick) * tick; second <= horizon; second += tick) {
      const at = x(second);
      line(at, 14, at, timelineBottom + 1, grid);
      context.textAlign = at < 22 ? "left" : at > width - 22 ? "right" : "center";
      context.fillText(time(second), at, timelineBottom + 18);
    }
    for (const y of [repeatBaseline, contourTop, (contourTop + contourBottom) / 2, contourBottom, 303, ...rows.map(row => row.top + row.height)])
      line(14, y, width - 16, y, grid);

    for (const mark of events.filter(event => event.kind === "gap")) {
      const a = x(mark.start), b = Math.max(a + 2, x(mark.end));
      context.save();
      context.beginPath();
      context.rect(a, top, b - a, timelineBottom - top);
      context.clip();
      context.fillStyle = selected?.id === mark.id ? "#5d597a" : "#2b395d";
      context.fillRect(a, top, b - a, timelineBottom - top);
      for (let stripe = a - timelineBottom; stripe < b; stripe += 7)
        line(stripe, timelineBottom, stripe + timelineBottom, 0, "#5c6989");
      context.restore();
    }

    for (const { mark, spans, first, next } of repeats) {
      const chosen = selected?.id === mark.id;
      const { a, b, depth } = arc(first, next);
      if (chosen && first === spans[0]) {
        context.fillStyle = "#aeaeff24";
        for (const { start, end } of spans)
          context.fillRect(x(start), top, x(end) - x(start), timelineBottom - top);
      }
      context.strokeStyle = repeatColor;
      context.globalAlpha = chosen || !selected ? 1 : .45;
      context.lineWidth = chosen ? 2 : 1.4;
      const midpoint = (a + b) / 2;
      context.beginPath();
      context.moveTo(a, repeatBaseline);
      context.bezierCurveTo(midpoint, repeatBaseline - depth, midpoint, repeatBaseline - depth, b, repeatBaseline);
      context.stroke();
      for (const { start, end } of [first, next])
        line(x(start), repeatBaseline, x(end), repeatBaseline, repeatColor);
      context.globalAlpha = 1;
    }

    let run: { x: number; y: number }[] = [];
    let previous: Frame | undefined;
    const flush = () => {
      if (!run.length) return;
      context.beginPath();
      context.moveTo(run[0].x, run[0].y);
      for (const point of run.slice(1)) context.lineTo(point.x, point.y);
      context.strokeStyle = contourColor;
      context.lineWidth = 1.7;
      context.stroke();
      context.lineTo(run.at(-1)!.x, contourBottom);
      context.lineTo(run[0].x, contourBottom);
      context.closePath();
      context.fillStyle = "#9cebd632";
      context.fill();
      run = [];
    };
    for (const [index, frame] of (frames ?? []).entries()) {
      const brightness = contour[index];
      if (frame.segment !== previous?.segment || frame.index !== previous.index + 1 || brightness === null) flush();
      previous = frame;
      if (brightness === null) {
        if (frame.valid) line(x(frame.start), contourBottom, x(frame.end), contourBottom, "#6d91a1");
        continue;
      }
      const value = Math.max(0, Math.min(1, (brightness - low) / (high - low)));
      const y = contourBottom - value * (contourBottom - contourTop);
      if (!run.length) run.push({ x: x(frame.start), y });
      run.push({ x: x(frame.end), y });
    }
    flush();

    drawLabLanes(context, rows, frames ?? [], x);
    if (eventLane) {
      for (const mark of events.filter(event => event.kind === "event")) {
        context.fillStyle = selected?.id === mark.id ? "#ffaf7970" : "#ffaf7935";
        context.fillRect(x(mark.start), eventLane.top + 5, Math.max(2, x(mark.end) - x(mark.start)), eventLane.height - 10);
      }
    }
    context.lineWidth = 1;
    if (rows.length && pinned !== undefined) {
      context.setLineDash([3, 4]);
      line(x(inspectedAt), top, x(inspectedAt), timelineBottom, "#e0e7f7");
      context.setLineDash([]);
    }
    for (const mark of events.filter(event => event.kind === "timbre")) {
      const chosen = selected?.id === mark.id;
      line(x(mark.start), contourTop, x(mark.start), 302, chosen ? markerColor : "#bd8c81");
    }
    if (last > 0) {
      line(x(last), top, x(last), timelineBottom, markerColor);
      context.fillStyle = markerColor;
      context.fillRect(x(last) - 2, top, 4, 10);
    }
  });

  return <><div className="plot" style={{ height: chartHeight }}>
    <div className="plot-lanes" aria-hidden="true">
      <span className="repeat-lane"><i />Repeats</span>
      <span className="timbre-lane"><i />Timbre</span>
      {axisValues.map((value, index) => {
        const hz = brightnessHz(value);
        return <span key={index} className="timbre-tick" style={{ top: axisPositions[index] }}>
          {hz >= 1000 ? `${(hz / 1000).toFixed(2).replace(/\.?0+$/, "")} kHz` : `${hz} Hz`}
        </span>;
      })}
      {rows.map(row => <span className="lab-lane-name" key={row.id} style={{ top: row.top + row.height / 2 }}>
        <i style={{ background: row.color }} />{row.label}
      </span>)}
      <span className="ruler-lane" style={{ top: timelineBottom + 6 }}>Elapsed</span>
    </div>
    <div className="plot-field" ref={wrapper}>
      <canvas ref={canvas} aria-hidden="true" style={{ height: chartHeight }} />
      <div className="plot-controls">
        {width > 0 && repeats.map(({ mark, first, next }) => {
          const { a, b, labelY } = arc(first, next);
          return <button key={`${mark.id}-${first.start}`} className="pin repeat-pin" style={{ left: (a + b) / 2, top: labelY }}
            aria-label={`${markName(mark)} ${time(first.start, Boolean(mark.family))} and ${time(next.start, Boolean(mark.family))}`}
            aria-pressed={selected?.id === mark.id} onFocus={() => select(mark.id)} onClick={() => select(mark.id)}>
            ↔ {mark.family ?? "Repeat"}
          </button>;
        })}
        {width > 0 && events.map(mark => <button key={mark.id} className={`pin ${mark.kind}-pin`}
          style={{ left: x(mark.start), top: mark.kind === "gap" ? 145 :
            mark.kind === "event" && eventLane ? eventLane.top + eventLane.height / 2 : 292 }}
          aria-label={`${markName(mark)} at ${time(mark.start)}`}
          aria-pressed={selected?.id === mark.id} onFocus={() => select(mark.id)} onClick={() => select(mark.id)}>
          {mark.kind === "gap" ? "Gap" : mark.kind === "event" ? "✦" : "◆"}
        </button>)}
      </div>
      {!frames?.length && <div className="plot-empty">
        <strong>The score is empty</strong>
        <span>Start Spotify capture or open a generated example.</span>
      </div>}
    </div>
  </div>
  {rows.length > 0 && <div className="lab-inspector">
    <label><span>Inspect at {time(inspectedAt, true)}</span>
      <input type="range" min={from} max={last} step={.25} value={inspectedAt} disabled={!frames?.length}
        aria-valuetext={time(inspectedAt, true)}
        onChange={event => map && setInspection({ session: map.sessionId, seconds: Number(event.target.value) })} />
    </label>
    {pinned !== undefined && <button type="button" onClick={() => setInspection(undefined)}>Follow live</button>}
    <dl>
      {lanes.includes("tempo") && <><dt>Tempo</dt><dd>{inspected?.lanes?.bpm == null ? "Unknown" :
        `${Math.round(inspected.lanes.bpm)} BPM${(inspected.lanes.tempoFit ?? 0) < .6 ? " (tentative)" : ""}`}</dd></>}
      {lanes.includes("key") && <><dt>Tonality</dt><dd>{inspected?.lanes?.key?.label ?? "Unknown"}</dd></>}
      {lanes.includes("chord") && <><dt>Chord</dt><dd>{inspected?.lanes?.chord?.label ?? "Unknown"}
        {inspected?.lanes?.chord?.tentative && " (tentative)"}</dd></>}
      {lanes.includes("event") && <><dt>Events</dt><dd>{events.filter(mark => mark.kind === "event").length} visible</dd></>}
    </dl>
  </div>}
  </>;
}
