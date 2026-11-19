import type { Frame } from "./wire";

export const laneDefinitions = [
  { id: "event", label: "Events", height: 36, color: "#ffaf79" },
  { id: "tempo", label: "Tempo", height: 52, color: "#8fd7f6" },
  { id: "key", label: "Tonality", height: 36, color: "#f6d68a" },
  { id: "chord", label: "Chords", height: 36, color: "#d4b4ff" },
] as const;

export type Lane = typeof laneDefinitions[number]["id"];
export type LaneRow = typeof laneDefinitions[number] & { top: number };

export function drawLabLanes(context: CanvasRenderingContext2D, rows: LaneRow[],
    frames: readonly Frame[], x: (value: number) => number) {
  for (const lane of rows) {
    switch (lane.id) {
      case "tempo":
        drawTempo(context, lane, frames, x);
        break;
      case "key":
      case "chord":
        drawHarmony(context, lane, lane.id, frames, x);
        break;
    }
  }
}

function drawTempo(context: CanvasRenderingContext2D, row: LaneRow,
    frames: readonly Frame[], x: (value: number) => number) {
  let previous: Frame | undefined;
  // A log BPM axis gives equal space to equal tempo ratios.
  const y = (bpm: number) => row.top + row.height - 12 -
    Math.min(1, Math.max(0, Math.log(bpm / 50) / Math.log(200 / 50))) * (row.height - 19);

  for (const frame of frames) {
    if (frame.valid) {
      context.fillStyle = "#8fd7f670";
      for (const onset of frame.lanes?.onsets ?? [])
        context.fillRect(x(onset), row.top + row.height - 7, 1, 5);
      context.fillStyle = row.color;
      for (const beat of frame.lanes?.beats ?? [])
        context.fillRect(x(beat), row.top + row.height - 10, 1.5, 8);
    }

    const bpm = frame.lanes?.bpm;
    if (bpm != null) {
      const earlier = previous?.segment === frame.segment && previous.index + 1 === frame.index
        ? previous.lanes?.bpm : null;
      context.strokeStyle = row.color;
      context.globalAlpha = .45 + .55 * (frame.lanes?.tempoFit ?? 0);
      context.lineWidth = 1.5;
      context.beginPath();
      context.moveTo(x(frame.start), y(earlier ?? bpm));
      context.lineTo(x(frame.end), y(bpm));
      context.stroke();
      context.globalAlpha = 1;
    }
    previous = frame;
  }
}

function drawHarmony(context: CanvasRenderingContext2D, row: LaneRow, kind: "key" | "chord",
    frames: readonly Frame[], x: (value: number) => number) {
  let run: { label: string; start: number; end: number; segment: number } | undefined;
  function finish() {
    if (!run) return;
    const left = x(run.start);
    const width = x(run.end) - left;
    if (width < 1) return;
    context.fillStyle = run.label === "?" ? "#95a3c224" : row.color + "2e";
    context.fillRect(left, row.top + 7, Math.max(1, width - 1), row.height - 14);
    context.fillStyle = run.label === "?" ? "#95a3c275" : row.color;
    context.fillRect(left, row.top + 7, 1, row.height - 14);
    if (context.measureText(run.label).width + 12 <= width) {
      context.textAlign = "left";
      context.fillText(run.label, left + 6, row.top + row.height / 2 + 4);
    }
  }

  for (const frame of frames) {
    const estimate = frame.lanes?.[kind];
    const label = frame.valid && frame.audible
      ? estimate ? estimate.label + (estimate.tentative ? "?" : "") : "?"
      : null;
    if (label && run?.label === label && run.segment === frame.segment && Math.abs(run.end - frame.start) < .001) {
      run.end = frame.end;
      continue;
    }
    finish();
    run = label ? { label, start: frame.start, end: frame.end, segment: frame.segment } : undefined;
  }
  finish();
}
