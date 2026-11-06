import { useMemo, useState } from "react";
import { diffText } from "@/lib/content-diff";
import { NativeSelect } from "@/components/ui/native-select";

type Draft = { id: string; language: string; title: string; shortDescription: string; longDescription: string; highlights: string[]; createdAt: string; isCurrent: boolean };
type Audit = { model: string; promptVersion: string; retrievalMode?: string; groundingMode: string; includeFacts?: boolean; requestedTopK?: number };
export function DraftComparison({ selected, drafts, audits = {} }: { selected: Draft; drafts: Draft[]; audits?: Record<string, Audit> }) {
  const choices = drafts.filter(item => item.language.toLowerCase() === selected.language.toLowerCase() && item.id !== selected.id);
  const [baselineId, setBaselineId] = useState("");
  const baseline = choices.find(item => item.id === baselineId) ?? choices.find(item => item.isCurrent) ?? choices[0];
  const fields = useMemo(() => baseline ? [
    { name: "Title", before: baseline.title, after: selected.title },
    { name: "Short description", before: baseline.shortDescription, after: selected.shortDescription },
    { name: "Long description", before: baseline.longDescription, after: selected.longDescription },
    { name: "Highlights", before: baseline.highlights.join("\n"), after: selected.highlights.join("\n") },
  ].map(field => ({ ...field, parts: diffText(field.before, field.after) })) : [], [baseline, selected]);
  if (!baseline) return <p className="mt-4 border-t pt-3 text-xs text-muted-foreground">Save another {selected.language.toUpperCase()} draft to compare versions.</p>;
  function describe(item: Draft) {
    const audit = audits[item.id];
    return `Version ${drafts.length - drafts.findIndex(draft => draft.id === item.id)} · ${item.id.slice(0, 8)} · ${item.isCurrent ? "Published · " : ""}${new Date(item.createdAt).toLocaleString()} · ${audit ? `${audit.model} · ${audit.retrievalMode ?? audit.groundingMode} · ${audit.includeFacts === false ? "without facts" : "with facts"} · ${audit.promptVersion}${audit.requestedTopK ? ` · Top-K ${audit.requestedTopK}` : ""}` : "Settings unavailable"}`;
  }
  return <details className="mt-4 rounded-md border p-3 text-sm"><summary className="cursor-pointer font-semibold">Compare saved versions <span className="font-normal text-muted-foreground">· {fields.filter(field => field.before !== field.after).length} fields changed</span></summary>
    <div className="mt-3 grid gap-3"><label className="grid gap-2 text-xs font-medium">Compare against<NativeSelect className="w-full" value={baseline.id} onChange={event => setBaselineId(event.target.value)}>{choices.map(item => <option key={item.id} value={item.id}>{describe(item)}</option>)}</NativeSelect></label><p className="break-words text-xs text-muted-foreground">Selected {selected.language.toUpperCase()} version: {describe(selected)}</p><p className="text-xs"><del className="rounded bg-red-100 px-1 text-red-950">Removed</del> <ins className="rounded bg-emerald-100 px-1 text-emerald-950">Added</ins> · Saved content only; comparison does not publish changes.</p>
      {fields.map(field => <section className="rounded border p-3" key={field.name} aria-label={`${field.name} changes`}><h4 className="mb-2 text-xs font-semibold">{field.name}{field.before === field.after ? " · Unchanged" : ""}</h4><p className="whitespace-pre-wrap break-words text-xs leading-6">{field.parts.length ? field.parts.map((part, index) => part.kind === "added" ? <ins key={index} aria-label="Added" className="rounded bg-emerald-100 text-emerald-950">{part.text}</ins> : part.kind === "removed" ? <del key={index} aria-label="Removed" className="rounded bg-red-100 text-red-950">{part.text}</del> : <span key={index}>{part.text}</span>) : "No highlights"}</p></section>)}
    </div>
  </details>;
}
