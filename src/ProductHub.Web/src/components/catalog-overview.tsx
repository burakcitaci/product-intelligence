import { useState } from "react";
import { ArrowRight, Folder, FolderOpen, Package2, Plus, Search } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { EmptyState, PageLayout, SearchField } from "@/components/workspace";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";

type CatalogSummary = { id: string; name: string; description: string; productCount: number };

function CatalogCard({ catalog, onOpen }: { catalog: CatalogSummary; onOpen: (id: string) => void }) {
  return <button type="button" className="catalog-card" onClick={() => onOpen(catalog.id)}>
    <span className="catalog-card-top"><span className="catalog-icon"><Folder size={19} /></span><ArrowRight className="catalog-arrow" size={16} /></span>
    <strong>{catalog.name}</strong>
    <p>{catalog.description || "Add products to start building this collection."}</p>
    <span className="catalog-card-footer"><span><Package2 size={13} />{catalog.productCount} {catalog.productCount === 1 ? "product" : "products"}</span><span>View catalog</span></span>
  </button>;
}
export function CatalogOverview({ catalogs, loading = false, onCreate, onOpen }: { catalogs: CatalogSummary[]; loading?: boolean; onCreate: () => void; onOpen: (id: string) => void }) {
  const [query, setQuery] = useState("");
  const term = query.trim().toLowerCase();
  const visible = catalogs.filter((catalog) => `${catalog.name} ${catalog.description}`.toLowerCase().includes(term));
  const productCount = catalogs.reduce((sum, catalog) => sum + catalog.productCount, 0);
  return <PageLayout>
    <PageHeader breadcrumbs={["Workspace", "Catalogs"]} title="Catalogs" subtitle="Your products, organized. Your content, under control." action={{ label: "New catalog", onClick: onCreate }} />
    <div className="catalog-summary" aria-live="polite"><span><FolderOpen size={15} />{loading ? "—" : catalogs.length} catalogs</span><span><Package2 size={15} />{loading ? "—" : productCount} products</span></div>
    <div className="collection-toolbar"><SearchField value={query} onChange={setQuery} placeholder="Search catalogs…" /><span className="text-xs text-muted-foreground">{term ? `${visible.length} of ${catalogs.length} catalogs` : "All collections"}</span></div>
    {loading ? <div className="catalog-grid" aria-label="Loading catalogs" aria-busy="true">{[1, 2, 3].map((item) => <div className="catalog-card" key={item}><Skeleton className="mb-5 size-10" /><Skeleton className="mb-3 h-4 w-1/2" /><Skeleton className="h-10 w-full" /></div>)}</div>
      : catalogs.length === 0 ? <EmptyState icon={<FolderOpen />} title="A home for your products" description="Create a catalog to organize products and start preparing their content." action={<Button onClick={onCreate}><Plus />New catalog</Button>} />
      : visible.length === 0 ? <EmptyState icon={<Search />} title="No catalogs found" description="Try a different name or description." action={<Button variant="outline" onClick={() => setQuery("")}>Clear search</Button>} />
      : <div className="catalog-grid">{visible.map((catalog) => <CatalogCard key={catalog.id} catalog={catalog} onOpen={onOpen} />)}</div>}
  </PageLayout>;
}
