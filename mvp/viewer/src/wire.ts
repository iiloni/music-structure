import { useEffect, useRef, useState } from "react";

export interface Frame {
  index: number; start: number; end: number; clockEndMs: number | null; packetReadMs?: number; segment: number;
  rmsDb: number; centroidHz: number; valid: boolean; audible: boolean; mel: number[]; chroma: number[];
}
export interface Mark {
  id: number; kind: "repeat" | "timbre" | "gap";
  start: number; end: number; detail: string;
  otherStart: number | null; otherEnd: number | null; score: number | null;
  deltaHz: number | null; detectedAt: number | null;
}
export interface Source { pid: number; started: string; label: string }
export interface Counters {
  packets: number; frames: number; queueDroppedPackets: number; queueDroppedFrames: number;
  discontinuities: number; timestampErrors: number; missingSeconds: number; maxGapSeconds: number;
  gapsOver250Ms: number; features: number; meanFeatureComputeMs: number;
}
export interface Batch {
  version: 1; analysisVersion: string; sessionId: string; sequence: number;
  state: "ready" | "waiting" | "capturing" | "loading" | "stopping" | "stopped" | "unavailable";
  status: string; mode: "spotify" | "fixture" | "replay" | null; source: string | null;
  elapsed: number; historyStart: number; inputDb: number | null; counters: Counters;
  frames: Frame[]; marks: Mark[] | null; sources: Source[] | null; snapshot: boolean; windowsCapture: boolean;
}
export type MapState = Batch & { marks: Mark[]; sources: Source[] };
type Command = { action: "start" | "stop" | "new" | "sources"; mode?: string; fixture?: string; pid?: number; started?: string };
type Clock = { t0: number; t1: number; t2: number };
type Alignment = { offset: number; uncertainty: number; received: number };

const fragment = location.hash.slice(1);
const [fragmentKey, demoFlag] = fragment.split("/");
if (/^[\da-f]{64}$/i.test(fragmentKey)) {
  sessionStorage.setItem("music-structure-key", fragmentKey);
  history.replaceState(null, "", location.pathname);
}
const key = sessionStorage.getItem("music-structure-key");
window.addEventListener("hashchange", () => {
  if (/^[\da-f]{64}(?:\/demo)?$/i.test(location.hash.slice(1))) location.reload();
});

export function useHelper() {
  const [map, setMap] = useState<MapState>();
  const [connection, setConnection] = useState(key ? "Connecting to the local helper…" : "Open the address printed by the helper.");
  const [online, setOnline] = useState(false);
  const socket = useRef<WebSocket>(null);
  const alignment = useRef<Alignment | undefined>(undefined);
  const demo = useRef(demoFlag === "demo");

  useEffect(() => {
    if (!key) return;
    let retry: number | undefined;
    let ping: number | undefined;
    let active = true;

    function connect() {
      if (!active || socket.current) return;
      const client = new WebSocket(`ws://${location.host}/session`);
      socket.current = client;
      let sequence = 0;
      client.onopen = () => client.send(JSON.stringify({ key }));
      client.onmessage = event => {
        const reply = JSON.parse(event.data) as Batch | Clock;
        if ("t2" in reply) {
          const now = performance.now();
          const uncertainty = Math.max(0, (now - reply.t0 - (reply.t2 - reply.t1)) / 2);
          if (!alignment.current || uncertainty < alignment.current.uncertainty + 1 ||
              now - alignment.current.received > 25_000) {
            alignment.current = { offset: (reply.t1 - reply.t0 + reply.t2 - now) / 2, uncertainty, received: now };
          }
          return;
        }
        if (reply.version !== 1 || reply.sequence !== sequence + 1) {
          client.close(1002, "Invalid batch sequence");
          return;
        }
        if (sequence === 0) {
          setOnline(true);
          setConnection("");
          const sync = () => {
            if (client.readyState === WebSocket.OPEN) client.send(JSON.stringify({ action: "clock", t0: performance.now() }));
          };
          sync();
          ping = window.setInterval(sync, 3000);
          if (demo.current) {
            client.send(JSON.stringify({ action: "start", mode: "replay", fixture: "return" }));
            demo.current = false;
          }
        }
        sequence = reply.sequence;
        setMap(previous => {
          const replace = reply.snapshot || previous?.sessionId !== reply.sessionId;
          return {
            ...reply,
            frames: (replace ? reply.frames : (previous?.frames ?? []).concat(reply.frames))
              .filter(frame => frame.end > reply.historyStart),
            marks: reply.marks ?? (replace ? [] : previous?.marks ?? []),
            sources: reply.sources ?? previous?.sources ?? [],
          };
        });
      };
      client.onclose = event => {
        if (socket.current !== client) return;
        socket.current = null;
        window.clearInterval(ping);
        alignment.current = undefined;
        setOnline(false);
        if (!active) return;
        if (event.code === 1008) {
          setConnection(event.reason || "Open the address printed by the helper.");
          return;
        }
        setConnection("Helper disconnected. Reconnecting…");
        retry = window.setTimeout(connect, 1200);
      };
    }

    function detach() {
      active = false;
      window.clearTimeout(retry);
      window.clearInterval(ping);
      socket.current?.close();
      socket.current = null;
      setOnline(false);
    }

    function restore() {
      active = true;
      connect();
    }

    connect();
    window.addEventListener("pagehide", detach);
    window.addEventListener("pageshow", restore);
    return () => {
      detach();
      window.removeEventListener("pagehide", detach);
      window.removeEventListener("pageshow", restore);
    };
  }, []);

  return {
    map, connection, online, alignment,
    send(command: Command) {
      if (socket.current?.readyState === WebSocket.OPEN) socket.current.send(JSON.stringify(command));
    },
  };
}
