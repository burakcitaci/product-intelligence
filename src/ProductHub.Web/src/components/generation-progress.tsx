import { type GenerationJob, jobIsActive } from "@/hooks/use-generation-jobs";
import { Button } from "@/components/ui/button";

export function GenerationProgress({ jobs, error, onCancel, onOpenAudit }: { jobs: GenerationJob[]; error: string; onCancel: (id: string) => void; onOpenAudit?: () => void }) {
  const active = jobs.filter(jobIsActive);
  const terminal = jobs.filter(job => !jobIsActive(job)).slice(0, 3);
  return <section aria-label="Generation progress" className="mb-3 grid gap-2">
    {error && <p role="status" className="text-xs text-muted-foreground">{error}</p>}
    {active.map(job => <div key={job.id} className="flex items-center justify-between rounded-md border bg-muted/40 p-3"><div role="status" aria-live="polite"><strong className="text-sm">{job.cancellationRequested ? "Cancelling…" : job.stage}</strong><p className="text-xs text-muted-foreground">{job.id.slice(0, 8)} · You can leave this page and reconnect later.</p></div><Button size="sm" variant="outline" disabled={job.cancellationRequested} onClick={() => onCancel(job.id)}>Cancel generation</Button></div>)}
    {terminal.some(job => job.status === "Failed" || job.status === "Cancelled") && onOpenAudit && <Button className="justify-self-start" size="sm" variant="outline" onClick={onOpenAudit}>View model audit</Button>}
    {terminal.length > 0 && <details className="rounded-md border p-2 text-xs"><summary className="cursor-pointer">Recent generations</summary><ul className="mt-2 grid gap-2">{terminal.map(job => <li key={job.id}><strong>{job.status}</strong> · {job.id.slice(0, 8)}{job.errorMessage && <p className="mt-1 text-muted-foreground">{job.errorMessage}</p>}</li>)}</ul></details>}
  </section>;
}
