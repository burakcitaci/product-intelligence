import { useEffect, useState } from "react";
import { ArrowLeft, PackageOpen, Plus, RefreshCw } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { EmptyState, PageLayout, SearchField } from "@/components/workspace";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { NativeSelect } from "@/components/ui/native-select";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

export type QueueProduct = { id: string; sku: string; name: string; brand: string; catalogId?: string; categoryId?: string; attributes: { name: string; value: string; unit?: string }[]; updatedAt: string; rowVersion: string };
type Filter = "all" | "pending" | "missing-language" | "missing-facts" | "stale";
type Preferences = { filter: Filter; language: "de" | "en"; search: string; page: number };
type Queue = { items: { product: QueueProduct; pendingCount: number; missingLanguage: boolean; missingFacts: boolean; staleContent: boolean }[]; counts: { all: number; pending: number; missingLanguage: number; missingFacts: number; stale: number }; totalCount: number; totalPages: number; page: number };
const filters: { id: Filter; label: string; count: keyof Queue["counts"] }[] = [{ id: "all", label: "All products", count: "all" }, { id: "pending", label: "Pending review", count: "pending" }, { id: "missing-language", label: "Missing language", count: "missingLanguage" }, { id: "missing-facts", label: "Missing facts", count: "missingFacts" }, { id: "stale", label: "Stale content", count: "stale" }];
function readPreferences(key: string): Preferences {
  const fallback: Preferences = { filter: "all", language: "de", search: "", page: 1 };
  try {
    const value = JSON.parse(sessionStorage.getItem(key) ?? "null");
    if (!value || !filters.some(filter => filter.id === value.filter) || !["de", "en"].includes(value.language)) return fallback;
    return { filter: value.filter, language: value.language, search: typeof value.search === "string" ? value.search.slice(0, 200) : "", page: Number.isInteger(value.page) && value.page > 0 ? value.page : 1 };
  } catch { return fallback; }
}
export function CatalogDetail({ api, userId, catalog, refreshKey, onBack, onOpenProduct, onAddProduct }: { api: string; userId: string; catalog: { id: string; name: string; description: string }; refreshKey: string; onBack: () => void; onOpenProduct: (product: QueueProduct, language: "de" | "en") => void; onAddProduct: () => void }) {
  const storageKey = `producthub:review-queue:${userId}:${catalog.id}`;
  const [preferences, setPreferences] = useState<Preferences>(() => readPreferences(storageKey));
  const [data, setData] = useState<Queue>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const { filter, language, search, page } = preferences;
  useEffect(() => { try { sessionStorage.setItem(storageKey, JSON.stringify(preferences)); } catch { /* Preferences still work for this visit. */ } }, [storageKey, preferences]);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError("");
    const timer = setTimeout(async () => {
      try {
        const query = new URLSearchParams({ language, filter, search, page: String(page), pageSize: "25" });
        const response = await fetch(`${api}/api/catalogs/${catalog.id}/review-queue?${query}`, { credentials: "include", signal: controller.signal });
        if (!response.ok) throw new Error("Could not load the review queue. Please retry.");
        const next: Queue = await response.json();
        if (controller.signal.aborted) return;
        setData(next);
        if (next.page !== page) setPreferences(value => ({ ...value, page: next.page }));
      } catch (error) { if (!controller.signal.aborted) setError((error as Error).message); }
      finally { if (!controller.signal.aborted) setLoading(false); }
    }, 250);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [api, catalog.id, language, filter, search, page, refresh, refreshKey]);
  return <PageLayout>
    <PageHeader breadcrumbs={[{ label: "Catalogs", onClick: onBack }, catalog.name]} title={catalog.name} subtitle={catalog.description || "Review product content and source facts."} action={{ label: "Add product", onClick: onAddProduct }} />
    <div className="collection-toolbar"><SearchField value={search} onChange={value => setPreferences(p => ({ ...p, search: value.slice(0, 200), page: 1 }))} placeholder="Search products…" /><label className="flex items-center gap-2 text-xs">Review language<NativeSelect value={language} onChange={event => setPreferences(p => ({ ...p, language: event.target.value as "de" | "en", page: 1 }))}><option value="de">Deutsch</option><option value="en">English</option></NativeSelect></label><Button variant="ghost" size="sm" onClick={onBack}><ArrowLeft />All catalogs</Button></div>
    <div className="mb-3 flex flex-wrap gap-2" role="group" aria-label="Review queue filters">{filters.map(item => <Button size="sm" variant={filter === item.id ? "secondary" : "outline"} aria-pressed={filter === item.id} key={item.id} onClick={() => setPreferences(p => ({ ...p, filter: item.id, page: 1 }))}>{item.label}<Badge variant="outline">{loading || error ? "—" : data?.counts[item.count] ?? 0}</Badge></Button>)}<Button size="sm" variant="ghost" aria-label="Refresh queue" disabled={loading} onClick={() => setRefresh(value => value + 1)}><RefreshCw /></Button></div>
    <p className="mb-3 text-xs text-muted-foreground">Counts cover all matching products in this catalog for {language.toUpperCase()}. Missing language means no published description. Stale means product facts changed after the current description started generating.</p>
    <Card className="overflow-hidden gap-0 p-0 shadow-none">
      <div className="collection-table-heading"><strong>Review queue <span>{loading || error ? "—" : data?.totalCount ?? 0}</span></strong><span>{filters.find(item => item.id === filter)?.label}</span></div>
      {error ? <div className="p-5" role="alert"><p className="text-sm text-destructive">{error}</p><Button className="mt-3" variant="outline" onClick={() => setRefresh(value => value + 1)}>Retry</Button></div> : loading ? <div className="grid gap-2 p-4" aria-label="Loading review queue" aria-busy="true">{[1, 2, 3, 4].map(item => <Skeleton className="h-12 w-full" key={item} />)}</div> : !data?.items.length ? <EmptyState icon={<PackageOpen />} title="No products in this queue" description="Choose another filter or search, or add a product." action={<><Button variant="outline" onClick={() => setPreferences({ filter: "all", language, search: "", page: 1 })}>Clear filters</Button><Button onClick={onAddProduct}><Plus />Add product</Button></>} /> : <Table><TableHeader><TableRow><TableHead>Product</TableHead><TableHead>SKU</TableHead><TableHead>Review status</TableHead><TableHead>Facts</TableHead><TableHead>Updated</TableHead></TableRow></TableHeader><TableBody>{data.items.map(row => <TableRow key={row.product.id}>
        <TableCell><button className="product-name-button" onClick={() => onOpenProduct(row.product, language)}><span className="product-initials">{row.product.name.slice(0, 2).toUpperCase()}</span><span><strong>{row.product.name}</strong><small className="block text-muted-foreground">{row.product.brand}</small></span></button></TableCell><TableCell className="font-mono text-xs text-muted-foreground">{row.product.sku}</TableCell><TableCell><div className="flex flex-wrap gap-1">{row.pendingCount > 0 && <Badge variant="outline">{row.pendingCount} pending</Badge>}{row.missingLanguage && <Badge variant="outline">Missing {language.toUpperCase()}</Badge>}{row.staleContent && <Badge variant="outline">Stale {language.toUpperCase()}</Badge>}{!row.pendingCount && !row.missingLanguage && !row.staleContent && <span className="text-xs text-muted-foreground">Published</span>}</div></TableCell><TableCell><span className="fact-total">{row.product.attributes.length} facts</span></TableCell><TableCell className="whitespace-nowrap text-xs text-muted-foreground">{new Date(row.product.updatedAt).toLocaleDateString()}</TableCell>
      </TableRow>)}</TableBody></Table>}
      {!loading && !error && data && <div className="collection-table-footer flex items-center justify-between gap-3"><span>{data.totalCount} products · Page {data.page} of {Math.max(1, data.totalPages)}</span><div className="flex gap-2"><Button size="sm" variant="outline" disabled={data.page <= 1} onClick={() => setPreferences(p => ({ ...p, page: p.page - 1 }))}>Previous</Button><Button size="sm" variant="outline" disabled={data.page >= data.totalPages} onClick={() => setPreferences(p => ({ ...p, page: p.page + 1 }))}>Next</Button></div></div>}
    </Card>
  </PageLayout>;
}
