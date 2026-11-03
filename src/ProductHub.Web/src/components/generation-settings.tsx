import { BookOpen, Layers3, ScanText } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export type RetrievalMode = "None" | "Naive" | "Advanced";

const modes = [
  { value: "None", label: "No RAG", description: "Generate without uploaded documents.", icon: ScanText },
  { value: "Naive", label: "Naive RAG", description: "One semantic search for matching excerpts.", icon: BookOpen },
  { value: "Advanced", label: "Advanced RAG", description: "Multiple searches, keyword reranking, and diverse excerpts.", icon: Layers3 },
] as const;

type GenerationSettingsProps = {
  includeFacts: boolean;
  onIncludeFactsChange: (value: boolean) => void;
  retrievalMode: RetrievalMode;
  onRetrievalModeChange: (value: RetrievalMode) => void;
  topK: number;
  onTopKChange: (value: number) => void;
  hasKnowledge: boolean;
  disabled?: boolean;
};

export function GenerationSettings({ includeFacts, onIncludeFactsChange, retrievalMode, onRetrievalModeChange, topK, onTopKChange, hasKnowledge, disabled = false }: GenerationSettingsProps) {
  return <div className="grid gap-4">
    <Label className="flex cursor-pointer items-start gap-3 rounded-lg border bg-muted/30 p-3">
      <input type="checkbox" className="mt-0.5 size-4 accent-primary" checked={includeFacts} disabled={disabled} onChange={(event) => onIncludeFactsChange(event.target.checked)} />
      <span className="grid gap-1"><strong className="text-sm">Include product facts</strong><span className="text-xs font-normal leading-5 text-muted-foreground">Use verified attributes as authoritative specifications. Product name, brand, and SKU are always included.</span></span>
    </Label>
    <fieldset disabled={disabled} className="min-w-0">
      <legend className="mb-2 text-xs font-medium">Document retrieval</legend>
      <div className="grid gap-2 sm:grid-cols-3">{modes.map(({ value, label, description, icon: Icon }) =>
        <label key={value} className={`flex cursor-pointer items-start gap-2 rounded-lg border p-3 transition-colors ${retrievalMode === value ? "border-primary bg-primary/5 ring-1 ring-primary" : "bg-card hover:bg-muted/40"}`}>
          <input type="radio" name="retrieval-mode" className="mt-0.5 size-3.5 shrink-0 accent-primary" value={value} checked={retrievalMode === value} onChange={() => onRetrievalModeChange(value)} />
          <span className="grid gap-1"><Icon className="mb-1 size-4 text-primary" aria-hidden="true" /><strong className="text-xs">{label}</strong><span className="text-xs leading-5 text-muted-foreground">{description}</span></span>
        </label>)}</div>
    </fieldset>
    {retrievalMode !== "None" && <div className="grid gap-2">
      <Label className="flex flex-wrap items-center justify-between gap-3 text-xs"><span>Maximum excerpts <span className="font-normal text-muted-foreground">(Top-K, 1–20)</span></span><Input aria-label="Maximum retrieved excerpts" type="number" min={1} max={20} className="w-20" disabled={disabled} value={topK} onChange={(event) => onTopKChange(Math.min(20, Math.max(1, Number(event.target.value) || 1)))} /></Label>
      {retrievalMode === "Advanced" && <p className="text-xs leading-5 text-muted-foreground">Advanced retrieval may return fewer excerpts after removing repetition and applying the context limit.</p>}
      {!hasKnowledge && <p role="status" className="rounded-md border border-amber-200 bg-amber-50 p-2.5 text-xs leading-5 text-amber-900">Upload a document and wait for indexing to complete before using RAG.</p>}
    </div>}
    {!includeFacts && <p className="rounded-md border bg-muted/40 p-3 text-xs leading-5 text-muted-foreground">{retrievalMode === "None" ? "Identity-only generation: the model receives the product name, brand, and SKU. It must not invent missing specifications." : "Structured attributes are excluded from both the search query and generation prompt. Retrieved instructions remain reference guidance, not verified specifications."}</p>}
  </div>;
}
