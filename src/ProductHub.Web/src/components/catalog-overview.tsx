import { ArrowRight, Plus } from "lucide-react";

import { PageHeader } from "@/components/page-header";
import { StatusBadge } from "@/components/status-badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";

type CatalogSummary = { id: string; name: string; description: string; productCount: number };

const covers = ["bg-indigo-600", "bg-amber-600", "bg-emerald-700", "bg-rose-700", "bg-sky-700", "bg-violet-700"];

export function CatalogOverview({ catalogs, onCreate, onOpen }: { catalogs: CatalogSummary[]; onCreate: () => void; onOpen: (id: string) => void }) {
  return <div className="mx-auto w-full max-w-7xl p-6 lg:p-10">
    <PageHeader breadcrumbs={["Catalogs"]} title="Catalogs" subtitle="Organize products and manage their approved content." action={{ label: "New catalog", onClick: onCreate }} />
    {catalogs.length === 0 ? <Card className="mt-8 grid min-h-72 place-items-center border-dashed p-8 text-center shadow-none"><div className="max-w-sm"><span className="mx-auto mb-4 grid size-12 place-items-center rounded-full bg-secondary text-secondary-foreground"><Plus /></span><h2 className="text-lg font-semibold">Create your first catalog</h2><p className="mt-2 text-sm text-muted-foreground">Catalogs keep related products together and make content work easier to manage.</p><Button className="mt-5" onClick={onCreate}><Plus />New catalog</Button></div></Card> : <div className="mt-8 grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
      {catalogs.map((catalog, index) => <Card key={catalog.id} className="overflow-hidden p-0 shadow-none transition-colors hover:border-primary/50"><div className={`h-28 ${covers[index % covers.length]}`} aria-hidden="true" /><div className="grid gap-4 p-5"><div className="flex items-start justify-between gap-4"><div className="min-w-0"><h2 className="truncate text-lg font-semibold">{catalog.name}</h2><p className="mt-1 line-clamp-2 min-h-10 text-sm text-muted-foreground">{catalog.description || "No description provided."}</p></div><StatusBadge status={catalog.productCount > 0 ? "Published" : "Draft"} /></div><div className="flex items-center justify-between border-t pt-4"><span className="text-sm text-muted-foreground">{catalog.productCount} {catalog.productCount === 1 ? "product" : "products"}</span><Button variant="ghost" onClick={() => onOpen(catalog.id)}>Open <ArrowRight /></Button></div></div></Card>)}
      <button type="button" onClick={onCreate} className="grid min-h-72 place-items-center rounded-xl border border-dashed bg-card p-6 text-center text-muted-foreground transition-colors hover:border-primary hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"><span><Plus className="mx-auto mb-3 size-6" /><strong className="block text-sm text-foreground">Create catalog</strong><span className="mt-1 block text-sm">Start a new product collection</span></span></button>
    </div>}
  </div>;
}
