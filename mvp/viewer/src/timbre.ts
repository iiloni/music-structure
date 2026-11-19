import type { Frame } from "./wire";

export type TimbreScale = "fixed" | "relative";

const offsetHz = 200;
const fixedRange: [number, number] = [0, Math.log1p(6000 / offsetHz)];
const smoothingFrames = 4; // One second at the feature stream's 4 Hz cadence.
const minimumRelativeFrames = 16;

export function displayBrightness(frames: readonly Frame[], smooth: boolean) {
  const window: number[] = [];
  let previous: Frame | undefined;
  return frames.map(frame => {
    if (frame.segment !== previous?.segment || frame.index !== previous.index + 1) window.length = 0;
    previous = frame;
    if (!frame.audible || !frame.valid) {
      window.length = 0;
      return null;
    }

    const brightness = Math.log1p(frame.centroidHz / offsetHz);
    if (!smooth) return brightness;
    window.push(brightness);
    if (window.length > smoothingFrames) window.shift();
    return window.reduce((sum, sample) => sum + sample, 0) / window.length;
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
