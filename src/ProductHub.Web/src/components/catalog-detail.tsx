import { MoreHorizontal, PackageOpen, Plus, Search, Settings } from "lucide-react";
import { useState } from "react";

import { PageHeader } from "@/components/page-header";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";

type ProductRow = { id: string; sku: string; name: string; brand: string; attributes: unknown[]; updatedAt: string };
type CatalogDetailProps = { catalog: { name: string; description: string }; products: ProductRow[]; loading?: boolean; query: string; onQueryChange: (value: string) => void; onBack: () => void; onOpenProduct: (id: string) => void; onAddProduct: () => void };

export function CatalogDetail({ catalog, products, loading = false, query, onQueryChange, onBack, onOpenProduct, onAddProduct }: CatalogDetailProps) {
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const term = query.trim().toLowerCase();
  const visible = term ? products.filter((product) => `${product.name} ${product.sku} ${product.brand}`.toLowerCase().includes(term)) : products;
  const allVisibleSelected = visible.length > 0 && visible.every((product) => selectedIds.has(product.id));
  const toggle = (id: string) => setSelectedIds((current) => { const next = new Set(current); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const toggleAll = () => setSelectedIds((current) => { const next = new Set(current); if (allVisibleSelected) visible.forEach((product) => next.delete(product.id)); else visible.forEach((product) => next.add(product.id)); return next; });
  return <div className="mx-auto w-full max-w-7xl p-6 lg:p-8">
    <PageHeader breadcrumbs={["Catalogs", catalog.name]} title={catalog.name} subtitle={catalog.description || "Manage the products in this catalog."} action={{ label: "Add product", onClick: onAddProduct, icon: <Plus /> }} />
    <div className="mt-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"><div className="relative w-full sm:max-w-sm"><Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" /><Input className="h-11 pl-9" value={query} onChange={(event) => onQueryChange(event.target.value)} placeholder="Search products" aria-label="Search products" /></div><div className="flex gap-2"><Button variant="outline" onClick={onBack}>All catalogs</Button><Button variant="outline" disabled title="Catalog settings are not available in the current API"><Settings />Settings</Button></div></div>
    {selectedIds.size > 0 && <div className="mt-4 flex min-h-12 flex-wrap items-center gap-3 rounded-lg border bg-card px-4 py-2 text-sm"><strong>{selectedIds.size} selected</strong><span className="h-5 w-px bg-border" /><Button size="sm" variant="outline" disabled={selectedIds.size !== 1} onClick={() => onOpenProduct([...selectedIds][0])}>Open selected</Button><Button size="sm" variant="ghost" onClick={() => setSelectedIds(new Set())}>Clear</Button></div>}
    <Card className="mt-4 overflow-hidden p-0 shadow-none">
      {loading ? <div className="grid gap-3 p-5" aria-label="Loading products">{[1,2,3,4].map((item) => <Skeleton className="h-14 w-full" key={item} />)}</div> : visible.length === 0 ? <div className="grid min-h-72 place-items-center p-8 text-center"><div><PackageOpen className="mx-auto mb-4 size-10 text-muted-foreground" /><h2 className="font-semibold">{products.length ? "No matching products" : "No products yet"}</h2><p className="mt-1 text-sm text-muted-foreground">{products.length ? "Try a different search term." : "Add the first product to this catalog."}</p>{products.length === 0 && <Button className="mt-5" onClick={onAddProduct}><Plus />Add product</Button>}</div></div> : <Table><TableHeader><TableRow><TableHead className="w-12"><input type="checkbox" className="size-4 accent-primary" checked={allVisibleSelected} onChange={toggleAll} aria-label="Select all visible products" /></TableHead><TableHead>Product</TableHead><TableHead>SKU</TableHead><TableHead>Brand</TableHead><TableHead>Facts</TableHead><TableHead>Updated</TableHead><TableHead className="w-16"><span className="sr-only">Actions</span></TableHead></TableRow></TableHeader><TableBody>{visible.map((product) => <TableRow key={product.id} data-state={selectedIds.has(product.id) ? "selected" : undefined} className="data-[state=selected]:bg-muted/60"><TableCell><input type="checkbox" className="size-4 accent-primary" checked={selectedIds.has(product.id)} onChange={() => toggle(product.id)} aria-label={`Select ${product.name}`} /></TableCell><TableCell><button className="flex min-h-11 items-center gap-3 rounded-md text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" onClick={() => onOpenProduct(product.id)}><span className="grid size-9 place-items-center rounded-lg border bg-muted text-xs font-semibold">{product.name.slice(0, 2).toUpperCase()}</span><strong className="text-sm">{product.name}</strong></button></TableCell><TableCell className="font-mono text-xs">{product.sku}</TableCell><TableCell>{product.brand}</TableCell><TableCell>{product.attributes.length}</TableCell><TableCell>{new Date(product.updatedAt).toLocaleDateString()}</TableCell><TableCell><DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" aria-label={`Actions for ${product.name}`}><MoreHorizontal /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem onSelect={() => onOpenProduct(product.id)}>Open product</DropdownMenuItem></DropdownMenuContent></DropdownMenu></TableCell></TableRow>)}</TableBody></Table>}
    </Card>
  </div>;
}
