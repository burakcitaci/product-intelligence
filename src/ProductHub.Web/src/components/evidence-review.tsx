export type ClaimReviewReport = {
  version: string;
  context?: { includeFacts: boolean; attributes: { name: string; value: string; unit?: string | null }[]; grounding: { citation: string; text: string; documentRevision?: string | null; chunkRevision?: string | null }[] };
  requiresAcknowledgement?: boolean;
  warnings?: string[] | null;
  warningsAcknowledged?: boolean;
  acknowledgedBy?: string | null;
  acknowledgedAt?: string | null;
  claims: { id: string; field: string; text: string; status: string; reason: string;
    evidence: { id: string; kind: string; label: string; text: string }[] }[];
};

export function EvidenceReview({ review }: { review?: ClaimReviewReport | null }) {
  if (!review) return <p className="mt-5 border-t pt-3 text-xs text-muted-foreground">No saved evidence review for this older draft.</p>;
  const flagged = review.claims.filter(claim => claim.status !== "Supported").length;
  return <section aria-label="Claim and evidence review" className="mt-5 border-t pt-4">
    <h3 className="text-sm font-semibold">Claim review <span className="font-normal text-muted-foreground">· {flagged} to inspect</span></h3>
    <p className="mt-1 text-xs leading-5 text-muted-foreground">Deterministic checks are limited. “Supported” means an exact supplied match, not a complete semantic guarantee. Reference guidance is advisory.</p>
    {review.warnings?.map(warning => <p key={warning} className="mt-2 rounded border border-amber-300 bg-amber-50 p-2 text-xs text-amber-950">{warning}</p>)}
    {review.warningsAcknowledged && <p className="mt-2 text-xs text-muted-foreground">Review warnings acknowledged by {review.acknowledgedBy} at {review.acknowledgedAt ? new Date(review.acknowledgedAt).toLocaleString() : "unknown time"}.</p>}
    {review.context && <details className="mt-3 rounded-md border p-3 text-xs"><summary className="cursor-pointer font-medium">All saved generation evidence</summary><p className="mt-2 text-muted-foreground">This is the evidence snapshot at generation time. Document revisions may have changed since then.</p><ul className="mt-2 grid gap-2">{review.context.attributes.map((fact, index) => <li key={index}>{fact.name}: {fact.value} {fact.unit}</li>)}{review.context.grounding.map((source, index) => <li key={index} className="rounded border p-2"><strong>{source.citation}</strong><p className="mt-1 whitespace-pre-wrap">{source.text}</p><p className="mt-1 break-all text-muted-foreground">Document revision: {source.documentRevision ?? "legacy"}<br />Chunk revision: {source.chunkRevision ?? "legacy"}</p></li>)}</ul></details>}
    <div className="mt-3 grid gap-2">{review.claims.map(claim =>
      <details key={claim.id} className="rounded-md border bg-card p-3 text-xs">
        <summary className="cursor-pointer leading-5"><span className={claim.status === "Conflict" ? "font-semibold text-destructive" : claim.status === "Supported" ? "font-semibold text-primary" : "font-semibold"}>{claim.status}</span><span className="ml-2">{claim.text}</span></summary>
        <p className="mt-2 text-muted-foreground">{claim.reason}</p>
        <p className="mt-1 text-muted-foreground">Field: {claim.field}</p>
        {claim.evidence.length ? <ul className="mt-2 grid gap-2">{claim.evidence.map(evidence =>
          <li key={evidence.id} className="rounded border bg-muted/40 p-2"><strong>{evidence.kind} · {evidence.label}</strong><p className="mt-1 whitespace-pre-wrap break-words">{evidence.text}</p><small className="text-muted-foreground">Saved evidence snapshot</small></li>
        )}</ul> : <p className="mt-2">No exact supporting passage identified. Verify manually before approval.</p>}
      </details>)}</div>
  </section>;
}

