import type { Frame } from "./wire";

export type TimbreScale = "fixed" | "relative";
export const timbreWindows = [0, .5, 1, 2, 4] as const;
export type TimbreWindow = typeof timbreWindows[number];

const offsetHz = 200;
const fixedRange: [number, number] = [0, Math.log1p(6000 / offsetHz)];
const framesPerSecond = 4;
const minimumRelativeFrames = 16;

export function displayBrightness(frames: readonly Frame[], seconds: TimbreWindow) {
  const window: number[] = [];
  const smoothingFrames = seconds * framesPerSecond;
  let previous: Frame | undefined;
  return frames.map(frame => {
    if (frame.segment !== previous?.segment || frame.index !== previous.index + 1) window.length = 0;
    previous = frame;
    if (!frame.audible || !frame.valid) {
      window.length = 0;
      return null;
    }

    const brightness = Math.log1p(frame.centroidHz / offsetHz);
    if (!smoothingFrames) return brightness;
    window.push(brightness);
    if (window.length > smoothingFrames) window.shift();
    return window.reduce((sum, sample) => sum + sample, 0) / window.length;
  });
}

export function centeredBrightness(frames: readonly Frame[], seconds: TimbreWindow) {
  const radius = seconds * framesPerSecond / 2;
  return frames.map((frame, index) => {
    if (!frame.audible || !frame.valid) return null;
    let total = Math.log1p(frame.centroidHz / offsetHz), count = 1;
    for (const direction of [-1, 1]) {
      for (let offset = 1; offset <= radius; offset++) {
        const other = frames[index + direction * offset];
        if (!other?.audible || !other.valid || other.segment !== frame.segment ||
            other.index !== frame.index + direction * offset) break;
        total += Math.log1p(other.centroidHz / offsetHz);
        count++;
      }
    }
    return total / count;
  });
}

export function brightnessRange(contour: readonly (number | null)[], scale: TimbreScale): [number, number] {
  if (scale === "fixed") return fixedRange;
  const audible = contour.filter(point => point !== null);
  if (audible.length < minimumRelativeFrames) return fixedRange;

  audible.sort((a, b) => a - b);
  const low = audible[Math.floor((audible.length - 1) * .1)];
  const high = audible[Math.ceil((audible.length - 1) * .9)];
  const center = (low + high) / 2;
  // Leave headroom for peaks and keep a near-constant passage from filling the lane.
  const halfSpan = Math.max((high - low) * .7, Math.log(1.25) / 2);
  return [Math.max(0, center - halfSpan), center + halfSpan];
}

export function brightnessHz(value: number) {
  return Math.round(Math.expm1(value) * offsetHz);
}
