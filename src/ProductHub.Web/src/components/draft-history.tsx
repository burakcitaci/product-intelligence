import { GitCompareArrows, History } from "lucide-react";
import { useEffect, useState } from "react";

import { StatusBadge } from "@/components/status-badge";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { NativeSelect } from "@/components/ui/native-select";

type DraftVersion = { id: string; title: string; shortDescription: string; longDescription: string; highlights: string[]; status: number | string; isCurrent: boolean; createdAt: string };

function status(status: number | string) { const value = typeof status === "string" ? status : (["Pending review", "Approved", "Rejected"][status] ?? "Pending review"); return value === "Pending review" || value === "PendingReview" ? "In review" : value as "Approved" | "Rejected"; }

export function DraftHistory({ drafts, selectedId, onSelect }: { drafts: DraftVersion[]; selectedId?: string; onSelect: (id: string) => void }) {
  const [compareOpen, setCompareOpen] = useState(false);
  const [leftId, setLeftId] = useState(drafts[0]?.id ?? "");
  const [rightId, setRightId] = useState(drafts[1]?.id ?? drafts[0]?.id ?? "");
  useEffect(() => { setLeftId(drafts[0]?.id ?? ""); setRightId(drafts[1]?.id ?? drafts[0]?.id ?? ""); }, [drafts]);
  const left = drafts.find((item) => item.id === leftId);
  const right = drafts.find((item) => item.id === rightId);
  return <Card className="mt-6 gap-0 p-0 shadow-none"><div className="flex flex-wrap items-center justify-between gap-3 border-b p-5"><div className="flex items-center gap-3"><span className="grid size-10 place-items-center rounded-lg bg-secondary text-secondary-foreground"><History className="size-5" /></span><div><h2 className="font-semibold">Draft history</h2><p className="text-xs text-muted-foreground">{drafts.length} {drafts.length === 1 ? "version" : "versions"} in this language</p></div></div><Button variant="outline" disabled={drafts.length < 2} onClick={() => setCompareOpen(true)}><GitCompareArrows />Compare versions</Button></div><div className="divide-y">{drafts.map((draft, index) => <button type="button" className={`flex min-h-16 w-full items-center gap-4 px-5 text-left hover:bg-muted/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring ${draft.id === selectedId ? "bg-primary/5" : ""}`} key={draft.id} onClick={() => onSelect(draft.id)}><span className="grid size-8 shrink-0 place-items-center rounded-full border bg-card text-xs font-semibold">{drafts.length - index}</span><span className="min-w-0 flex-1"><strong className="block truncate text-sm">{draft.title}</strong><span className="text-xs text-muted-foreground">{new Date(draft.createdAt).toLocaleString([], { dateStyle: "medium", timeStyle: "short" })}</span></span><StatusBadge status={status(draft.status)} />{draft.isCurrent && <StatusBadge status="Current" />}{draft.id === selectedId && <Badge variant="outline">Viewing</Badge>}</button>)}</div>
    <Dialog open={compareOpen} onOpenChange={setCompareOpen}><DialogContent className="max-h-[calc(100dvh-2rem)] max-w-5xl overflow-y-auto"><DialogHeader><DialogTitle>Compare draft versions</DialogTitle><DialogDescription>Review two versions side by side.</DialogDescription></DialogHeader><div className="grid gap-4 md:grid-cols-2">{[[left, leftId, setLeftId], [right, rightId, setRightId]].map(([item, id, setter], column) => <section className="grid content-start gap-4 rounded-xl border p-4" key={column}><NativeSelect value={id as string} onChange={(event) => (setter as (id: string) => void)(event.target.value)} aria-label={column ? "Right version" : "Left version"}>{drafts.map((draft, index) => <option value={draft.id} key={draft.id}>Version {drafts.length - index} · {new Date(draft.createdAt).toLocaleDateString()}</option>)}</NativeSelect>{item && <><div className="flex gap-2"><StatusBadge status={status((item as DraftVersion).status)} />{(item as DraftVersion).isCurrent && <StatusBadge status="Current" />}</div><h3 className="text-xl font-semibold">{(item as DraftVersion).title}</h3><p className="font-medium leading-7">{(item as DraftVersion).shortDescription}</p><p className="whitespace-pre-line text-sm leading-7 text-muted-foreground">{(item as DraftVersion).longDescription}</p><ul className="grid gap-2">{(item as DraftVersion).highlights.map((highlight) => <li className="rounded-lg bg-muted p-3 text-sm" key={highlight}>{highlight}</li>)}</ul></>}</section>)}</div></DialogContent></Dialog>
  </Card>;
}
