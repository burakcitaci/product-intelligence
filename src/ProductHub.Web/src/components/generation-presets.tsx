import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { NativeSelect } from "@/components/ui/native-select";
import type { RetrievalMode } from "@/components/generation-settings";

type Settings = { model: string; retrievalMode: RetrievalMode; includeFacts: boolean; topK: number };
type Preset = Settings & { name: string };
export function GenerationPresets({ userId, settings, onApply }: { userId: string; settings: Settings; onApply: (value: Settings) => void }) {
  const storageKey = `producthub:generation-presets:${userId}`;
  const [saved, setSaved] = useState<Preset[]>([]);
  const [name, setName] = useState("");
  const [selected, setSelected] = useState("");
  const [error, setError] = useState("");
  useEffect(() => {
    try {
      const value: unknown = JSON.parse(localStorage.getItem(storageKey) ?? "[]");
      setSaved(Array.isArray(value) ? value.filter((p): p is Preset => typeof p?.name === "string" && p.name.length <= 60 && typeof p.model === "string" && ["None", "Naive", "Advanced", "Graph"].includes(p.retrievalMode) && typeof p.includeFacts === "boolean" && Number.isInteger(p.topK) && p.topK >= 1 && p.topK <= 20).slice(0, 8) : []);
    } catch { setError("Saved presets could not be loaded."); }
  }, [storageKey]);
  const builtins: Preset[] = [
    { name: "Facts only", ...settings, retrievalMode: "None", includeFacts: true },
    { name: "Hybrid with facts", ...settings, retrievalMode: "Advanced", includeFacts: true, topK: 5 },
    { name: "Graph with facts", ...settings, retrievalMode: "Graph", includeFacts: true, topK: 5 },
  ];
  const presets = [...builtins, ...saved];
  function persist(next: Preset[]) {
    try { localStorage.setItem(storageKey, JSON.stringify(next)); setSaved(next); setError(""); }
    catch { setError("Presets cannot be saved in this browser."); }
  }
  return <details className="rounded-md border p-3 text-xs"><summary className="cursor-pointer font-medium">Generation presets</summary><div className="mt-3 grid gap-3"><label className="grid gap-2">Apply a preset<NativeSelect value={selected} onChange={event => { const key = event.target.value; setSelected(key); const preset = presets[Number(key)]; if (key && preset) onApply(preset); }}><option value="">Choose settings…</option>{presets.map((preset, index) => <option key={index} value={index}>{preset.name}</option>)}</NativeSelect></label><div className="flex gap-2"><Input aria-label="New preset name" placeholder="Name these settings" maxLength={60} value={name} onChange={event => setName(event.target.value)} /><Button type="button" size="sm" variant="outline" disabled={!name.trim()} onClick={() => { persist([...saved.filter(p => p.name !== name.trim()), { ...settings, name: name.trim() }].slice(-8)); setName(""); setSelected(""); }}>Save</Button>{Number(selected) >= builtins.length && selected !== "" && <Button type="button" size="sm" variant="ghost" onClick={() => { persist(saved.filter((_, index) => index !== Number(selected) - builtins.length)); setSelected(""); }}>Remove</Button>}</div><p className="text-muted-foreground">Up to eight personal presets, stored in this browser. Built-in presets keep your selected model.</p>{error && <p role="status">{error}</p>}</div></details>;
}
