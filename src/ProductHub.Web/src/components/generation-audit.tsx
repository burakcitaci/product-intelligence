import { useEffect, useState } from "react";
import { RefreshCw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { NativeSelect } from "@/components/ui/native-select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

type Source = { citation: string; score: number; graphHops?: number | null; graphPath?: string[] | null; documentRevision?: string | null; chunkRevision?: string | null; retrievalVersion?: string | null; denseCandidateCount?: number; lexicalCandidateCount?: number; graphCacheHit?: boolean | null; graphRevision?: string | null; graphConcepts?: string[] | null };
type Attempt = { id: string; generationId?: string | null; jobId?: string | null; draftId?: string | null; language: string; model?: string | null; retrievalMode: string; includeFacts?: boolean | null; requestedTopK?: number | null; status: string; stage: string; startedAt: string; completedAt?: string | null; durationMs?: number | null; errorCode?: string | null; errorMessage?: string | null; metadata?: { provider: string; promptVersion: string; correlationId: string; inputTokens?: number | null; outputTokens?: number | null; retrievalStatus: string; retrievalDurationMs?: number | null; retrievedChunkCount: number; retrievedDocumentCount: number; retrievalSources: Source[]; parameters?: { temperature: number; seed?: number | null; maxOutputTokens: number; schemaVersion: string } | null } | null };
type AuditPage = { items: Attempt[]; page: number; pageSize: number; totalCount: number; totalPages: number };

export function GenerationAudit({ api, productId, refreshKey, selectedDraftId, onViewDraft }: { api: string; productId: string; refreshKey: string; selectedDraftId?: string; onViewDraft: (id: string, language: string) => void }) {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("all");
  const [language, setLanguage] = useState("all");
  const [data, setData] = useState<AuditPage>();
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [refresh, setRefresh] = useState(0);
  useEffect(() => {
    const abort = new AbortController(); let timer: ReturnType<typeof setTimeout> | undefined;
    setLoading(true); setError("");
    async function load() {
      try {
        const query = new URLSearchParams({ page: String(page), pageSize: "25", status, language });
        const response = await fetch(`${api}/api/products/${productId}/generations?${query}`, { credentials: "include", signal: abort.signal });
        if (!response.ok) throw new Error("Could not load model audit. Refresh to retry.");
        const next: AuditPage = await response.json();
        if (abort.signal.aborted) return;
        setData(next); setError("");
        if (next.page !== page) setPage(next.page);
        if (next.items.some(item => item.status === "Queued" || item.status === "InProgress")) timer = setTimeout(load, 2000);
      } catch (error) { if (!abort.signal.aborted) setError((error as Error).message); }
      finally { if (!abort.signal.aborted) setLoading(false); }
    }
    void load();
    return () => { abort.abort(); clearTimeout(timer); };
  }, [api, productId, page, status, language, refreshKey, refresh]);
  return <section aria-label="Generation attempt audit" className="grid gap-3">
    <div className="flex flex-wrap items-end justify-between gap-3"><div><h3 className="text-sm font-semibold">All generation attempts</h3><p className="mt-1 text-xs text-muted-foreground">Successful, failed and cancelled attempts, including requests that created no draft.</p></div><div className="flex flex-wrap gap-2"><label className="grid gap-1 text-xs">Audit status<NativeSelect aria-label="Audit status" value={status} onChange={event => { setStatus(event.target.value); setPage(1); }}>{[["all", "All statuses"], ["failed", "Failed"], ["succeeded", "Succeeded"], ["cancelled", "Cancelled"], ["inprogress", "In progress"], ["queued", "Queued"]].map(([value, label]) => <option value={value} key={value}>{label}</option>)}</NativeSelect></label><label className="grid gap-1 text-xs">Audit language<NativeSelect aria-label="Audit language" value={language} onChange={event => { setLanguage(event.target.value); setPage(1); }}><option value="all">All languages</option><option value="de">Deutsch</option><option value="en">English</option></NativeSelect></label><Button size="sm" variant="outline" disabled={loading} onClick={() => setRefresh(value => value + 1)}><RefreshCw />Refresh audit</Button></div></div>
    {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
    <div className="overflow-x-auto rounded-md border"><Table><TableHeader><TableRow>{["Started", "Attempt", "Language", "Model", "RAG", "Facts", "Status", "Duration", "Tokens", "Result / error", "Draft"].map(label => <TableHead key={label}>{label}</TableHead>)}</TableRow></TableHeader><TableBody>
      {loading && !data ? <TableRow><TableCell colSpan={11} role="status">Loading generation attempts…</TableCell></TableRow> : !data?.items.length ? <TableRow><TableCell colSpan={11}>No generation attempts match these filters.</TableCell></TableRow> : data.items.map(item => <TableRow key={item.id}>
        <TableCell className="whitespace-nowrap text-xs">{new Date(item.startedAt).toLocaleString([], { dateStyle: "medium", timeStyle: "short" })}</TableCell><TableCell><code title={item.id}>{item.id.slice(0, 8)}</code></TableCell><TableCell>{item.language.toUpperCase()}</TableCell><TableCell className="max-w-48 whitespace-normal text-xs">{item.model ?? "Unavailable"}</TableCell><TableCell>{item.retrievalMode}</TableCell><TableCell>{item.includeFacts == null ? "Unknown" : item.includeFacts ? "Included" : "Excluded"}</TableCell><TableCell><Badge variant={item.status === "Failed" ? "destructive" : item.status === "Succeeded" ? "secondary" : "outline"}>{item.status === "InProgress" ? "In progress" : item.status}</Badge><p className="mt-1 text-xs text-muted-foreground">{item.stage}</p></TableCell><TableCell>{item.durationMs == null ? "—" : `${(item.durationMs / 1000).toFixed(1)}s`}</TableCell><TableCell>{item.metadata?.inputTokens == null && item.metadata?.outputTokens == null ? "—" : `${item.metadata?.inputTokens ?? "?"} in / ${item.metadata?.outputTokens ?? "?"} out`}</TableCell>
        <TableCell className="min-w-56 max-w-md whitespace-normal text-xs"><p className={item.status === "Failed" ? "font-medium text-destructive" : ""}>{item.errorMessage ?? (item.status === "Succeeded" ? "Draft saved" : item.status === "Cancelled" ? "Request cancelled" : "No saved draft yet")}</p>{item.errorCode && <code className="mt-1 block">{item.errorCode}</code>}<details className="mt-2"><summary className="cursor-pointer text-primary">Attempt details</summary><dl className="mt-2 grid gap-1 break-all text-muted-foreground"><div><dt className="inline font-medium">Generation: </dt><dd className="inline">{item.generationId ?? "Not started"}</dd></div><div><dt className="inline font-medium">Job: </dt><dd className="inline">{item.jobId ?? "Synchronous request"}</dd></div><div><dt className="inline font-medium">Provider: </dt><dd className="inline">{item.metadata?.provider ?? "Not recorded"}</dd></div><div><dt className="inline font-medium">Prompt: </dt><dd className="inline">{item.metadata?.promptVersion ?? "Not recorded"}</dd></div><div><dt className="inline font-medium">Trace: </dt><dd className="inline">{item.metadata?.correlationId ?? "Not recorded"}</dd></div><div><dt className="inline font-medium">Completed: </dt><dd className="inline">{item.completedAt ? new Date(item.completedAt).toLocaleString() : "—"}</dd></div><div><dt className="inline font-medium">Top-K: </dt><dd className="inline">{item.requestedTopK ?? "Not requested"}</dd></div>{item.metadata?.parameters && <div>Temperature {item.metadata.parameters.temperature} · Seed {item.metadata.parameters.seed ?? "unset"} · Max tokens {item.metadata.parameters.maxOutputTokens} · {item.metadata.parameters.schemaVersion}</div>}{item.metadata && <div>Retrieval: {item.metadata.retrievalStatus} · {item.metadata.retrievalDurationMs ?? "—"} ms · {item.metadata.retrievedChunkCount} chunks / {item.metadata.retrievedDocumentCount} documents</div>}</dl>{item.metadata?.retrievalSources.map((source, index) => <div key={index} className="mt-2 rounded border p-2 break-words"><strong>{source.citation} · {source.score.toFixed(3)}</strong><p>{source.retrievalVersion} · {source.denseCandidateCount ?? 0} dense / {source.lexicalCandidateCount ?? 0} lexical candidates</p>{source.graphPath && <p>{source.graphPath.join(" → ")}</p>}{source.graphRevision && <p className="break-all">Graph: {source.graphRevision}<br />Cache: {source.graphCacheHit == null ? "Unknown" : source.graphCacheHit ? "Hit" : "Miss"} · Hops: {source.graphHops ?? 0}<br />Concepts: {source.graphConcepts?.join(", ")}</p>}<p className="break-all text-muted-foreground">Document: {source.documentRevision ?? "legacy"}<br />Chunk: {source.chunkRevision ?? "legacy"}</p></div>)}</details></TableCell>
        <TableCell>{item.draftId ? <Button size="sm" variant="ghost" disabled={item.draftId === selectedDraftId} onClick={() => onViewDraft(item.draftId!, item.language)}>{item.draftId === selectedDraftId ? "Viewing" : "View draft"}</Button> : <span className="text-xs text-muted-foreground">No draft</span>}</TableCell>
      </TableRow>)}
    </TableBody></Table></div>
    {data && <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground"><span>{data.totalCount} attempts · Page {data.page} of {Math.max(1, data.totalPages)}</span><div className="flex gap-2"><Button size="sm" variant="outline" disabled={loading || data.page <= 1} onClick={() => setPage(page - 1)}>Previous attempts</Button><Button size="sm" variant="outline" disabled={loading || data.page >= data.totalPages} onClick={() => setPage(page + 1)}>Next attempts</Button></div></div>}
  </section>;
}
