import { useEffect, useRef, useState } from "react";

export type GenerationJob = { id: string; productId: string; status: string; stage: string; cancellationRequested: boolean; draftId?: string | null; errorCode?: string | null; errorMessage?: string | null; idempotencyKey?: string | null };
export const jobIsActive = (job: GenerationJob) => job.status === "Queued" || job.status === "Running";

export function useGenerationJobs(api: string, productId: string, onReady: (job: GenerationJob) => void) {
  const [jobs, setJobs] = useState<GenerationJob[]>([]);
  const [connectionError, setConnectionError] = useState("");
  const onReadyRef = useRef(onReady);
  useEffect(() => { onReadyRef.current = onReady; });
  const known = useRef(new Map<string, string>());
  const pending = useRef<{ productId: string; key: string; payload: string } | null>(null);
  const currentProduct = useRef(productId);
  useEffect(() => { currentProduct.current = productId; }, [productId]);
  function readPending(id: string) {
    if (pending.current?.productId === id) return pending.current;
    try {
      const saved = JSON.parse(sessionStorage.getItem(`producthub:submission:${id}`) ?? "null");
      if (saved?.productId === id && typeof saved.key === "string" && typeof saved.payload === "string") return saved as { productId: string; key: string; payload: string };
    } catch { /* Fall back to the in-memory submission identity. */ }
    return null;
  }
  function clearPending(id: string) {
    if (pending.current?.productId === id) pending.current = null;
    try { sessionStorage.removeItem(`producthub:submission:${id}`); } catch { /* Storage may be disabled. */ }
  }
  useEffect(() => {
    let disposed = false;
    let timer: ReturnType<typeof setTimeout>;
    const abort = new AbortController();
    known.current.clear(); setJobs([]); setConnectionError("");
    if (!productId) return;
    async function poll() {
      try {
        const response = await fetch(`${api}/api/products/${productId}/generation-jobs`, { credentials: "include", signal: abort.signal });
        if (!response.ok) throw new Error("Progress connection unavailable. Retrying; queued work continues on the server.");
        const next: GenerationJob[] = await response.json();
        if (disposed) return;
        for (const job of next) {
          const submission = readPending(productId);
          const recovered = Boolean(submission && job.idempotencyKey === submission.key);
          if (recovered) clearPending(productId);
          const previous = known.current.get(job.id);
          if ((recovered || previous === "Queued" || previous === "Running") && job.status === "Succeeded") onReadyRef.current(job);
          known.current.set(job.id, job.status);
        }
        setJobs(next); setConnectionError("");
      } catch (error) { if (!disposed) setConnectionError((error as Error).message); }
      finally { if (!disposed) timer = setTimeout(poll, 1500); }
    }
    void poll();
    return () => { disposed = true; clearTimeout(timer); abort.abort(); };
  }, [api, productId]);
  async function enqueue(settings: object) {
    const payload = JSON.stringify(settings);
    const previous = readPending(productId);
    const key = previous?.payload === payload ? previous.key : crypto.randomUUID();
    const submission = { productId, key, payload };
    pending.current = submission;
    try { sessionStorage.setItem(`producthub:submission:${productId}`, JSON.stringify(submission)); } catch { /* In-memory identity still protects retries in this view. */ }
    let response: Response;
    try {
      response = await fetch(`${api}/api/products/${productId}/generation-jobs`, { method: "POST", credentials: "include", headers: { "Content-Type": "application/json", "Idempotency-Key": key }, body: payload });
    } catch {
      throw new Error("Could not confirm submission. Retry unchanged settings to reuse the same request; queued work may already be running.");
    }
    if (!response.ok) { const error = await response.json().catch(() => ({})); throw new Error(error.detail ?? error.title ?? "Could not queue generation."); }
    const job: GenerationJob = await response.json();
    clearPending(productId);
    if (currentProduct.current !== productId) return job;
    known.current.set(job.id, job.status);
    setJobs(items => [job, ...items.filter(item => item.id !== job.id)]);
    return job;
  }
  async function cancel(id: string) {
    const response = await fetch(`${api}/api/generation-jobs/${id}/cancel`, { method: "POST", credentials: "include" });
    if (!response.ok) throw new Error("Could not cancel generation.");
    const job: GenerationJob = await response.json();
    setJobs(items => items.map(item => item.id === id ? job : item));
  }
  return { jobs, connectionError, enqueue, cancel };
}
