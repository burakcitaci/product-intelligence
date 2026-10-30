import { ArrowRight, Folder, Plus } from "lucide-react";

import { PageHeader } from "@/components/page-header";
import { StatusBadge } from "@/components/status-badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

type CatalogSummary = { id: string; name: string; description: string; productCount: number };

export function CatalogOverview({ catalogs, loading = false, onCreate, onOpen }: { catalogs: CatalogSummary[]; loading?: boolean; onCreate: () => void; onOpen: (id: string) => void }) {
  return <div className="mx-auto w-full max-w-7xl p-5 lg:p-6">
    <PageHeader breadcrumbs={["Catalogs"]} title="Catalogs" subtitle="Organize products and manage their approved content." action={{ label: "New catalog", onClick: onCreate }} />
    {loading ? <div className="mt-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-3" aria-label="Loading catalogs">{[1,2,3].map((item) => <Card className="gap-3 p-4 shadow-none" key={item}><Skeleton className="h-16 w-full" /><Skeleton className="h-5 w-1/2" /><Skeleton className="h-8 w-full" /></Card>)}</div> : catalogs.length === 0 ? <Card className="mt-6 grid min-h-56 place-items-center border-dashed p-6 text-center shadow-none"><div className="max-w-sm"><span className="mx-auto mb-3 grid size-10 place-items-center rounded-md bg-secondary text-secondary-foreground"><Plus className="size-4" /></span><h2 className="text-base font-semibold">Create your first catalog</h2><p className="mt-1 text-sm text-muted-foreground">Catalogs keep related products together and make content work easier to manage.</p><Button className="mt-4" onClick={onCreate}><Plus />New catalog</Button></div></Card> : <div className="mt-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {catalogs.map((catalog) => <Card key={catalog.id} className="overflow-hidden p-0 shadow-none transition-colors hover:border-foreground/25"><div className="flex h-12 items-center border-b bg-muted/50 px-4" aria-hidden="true"><span className="grid size-8 place-items-center rounded-md border bg-card text-muted-foreground"><Folder className="size-4" /></span></div><div className="grid gap-2.5 p-4"><div className="flex items-start justify-between gap-3"><div className="min-w-0"><h2 className="truncate text-sm font-semibold">{catalog.name}</h2><p className="mt-1 line-clamp-2 min-h-9 text-xs leading-5 text-muted-foreground">{catalog.description || "No description provided."}</p></div><StatusBadge status={catalog.productCount > 0 ? "Published" : "Draft"} /></div><div className="flex items-center justify-between border-t pt-2.5"><span className="text-xs text-muted-foreground">{catalog.productCount} {catalog.productCount === 1 ? "product" : "products"}</span><Button size="sm" variant="ghost" onClick={() => onOpen(catalog.id)}>Open <ArrowRight /></Button></div></div></Card>)}
      <button type="button" onClick={onCreate} className="grid min-h-44 place-items-center rounded-md border border-dashed bg-card p-4 text-center text-muted-foreground transition-colors hover:border-foreground/30 hover:bg-muted/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"><span><Plus className="mx-auto mb-2 size-5" /><strong className="block text-sm text-foreground">Create catalog</strong><span className="mt-1 block text-xs">Start a new product collection</span></span></button>
    </div>}
  </div>;
}
