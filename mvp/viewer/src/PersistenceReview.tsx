import { time } from "./Score";
import type { PersistenceReport, PersistenceVerdict, TextureView } from "./wire";

const labels: Record<PersistenceVerdict, string> = {
  provisional: "Provisional", brief: "Brief event", lasting: "Lasting change", uncertain: "Uncertain",
};

export function PersistenceReview({ report, selection, choose, view, setView }: {
  report: PersistenceReport; selection?: number; choose: (id?: number) => void;
  view: TextureView; setView: (view: TextureView) => void;
}) {
  const selected = report.cues.find(cue => cue.id === selection) ?? report.cues.at(-1);

  return <section className="persistence" aria-label="Texture persistence comparison">
    <div role="group" aria-label="Texture representation" className="persistence-views">
      <button type="button" aria-pressed={view === "spectrum"} onClick={() => setView("spectrum")}>A · Spectrum</button>
      <button type="button" aria-pressed={view === "embedding"} onClick={() => setView("embedding")}>B · MusiCNN</button>
    </div>
    {selected ? <>
      <label>Candidate
        <select aria-label="Texture candidate" value={report.cues.some(cue => cue.id === selection) ? selection : ""} onChange={event =>
          choose(event.target.value ? Number(event.target.value) : undefined)}>
          <option value="">Latest</option>
          {report.cues.map(cue => <option key={cue.id} value={cue.id}>
            {time(cue.time, true)}{cue.afterBreak && ` · After ${cue.afterBreak === "silence" ? "silence" : "a gap"}`}
          </option>)}
        </select>
      </label>
      <span aria-live="polite" className="persistence-verdict">
        <strong>{labels[selected[view]]}</strong> {time(selected.time, true)}
        {selected.confirmedAt === null ? ` · Check at ${time(selected.dueAt, true)}`
          : ` · ${(selected.confirmedAt - selected.time).toFixed(1)} s to decision`}
      </span>
      <span className="persistence-reference">
        {selected.afterBreak && (selected.afterBreak === "silence" ? "After silence · " : "After a gap · ")}
        {(view === "spectrum" ? selected.spectrumDistance : selected.embeddingDistance).toFixed(2)}× vs. reference
        {" · "}{`Reference hops ${time(selected.referenceStart, true)}–${time(selected.referenceEnd, true)}`}
        {selected.reason && ` · ${selected.reason}`}
      </span>
    </> : <span>Waiting for a shared texture candidate.</span>}
  </section>;
}
