import { MoreHorizontal, PackageOpen, Plus, Search, Settings } from "lucide-react";

import { PageHeader } from "@/components/page-header";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

type ProductRow = { id: string; sku: string; name: string; brand: string; attributes: unknown[]; updatedAt: string };
type CatalogDetailProps = { catalog: { name: string; description: string }; products: ProductRow[]; loading?: boolean; query: string; onQueryChange: (value: string) => void; onBack: () => void; onOpenProduct: (id: string) => void; onAddProduct: () => void };

export function CatalogDetail({ catalog, products, loading = false, query, onQueryChange, onBack, onOpenProduct, onAddProduct }: CatalogDetailProps) {
  const term = query.trim().toLowerCase();
  const visible = term ? products.filter((product) => `${product.name} ${product.sku} ${product.brand}`.toLowerCase().includes(term)) : products;
  return <div className="mx-auto w-full max-w-7xl p-5 lg:p-6">
    <PageHeader breadcrumbs={["Catalogs", catalog.name]} title={catalog.name} subtitle={catalog.description || "Manage the products in this catalog."} action={{ label: "Add product", onClick: onAddProduct, icon: <Plus /> }} />
    <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"><div className="relative w-full sm:max-w-sm"><Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" /><Input className="pl-9" value={query} onChange={(event) => onQueryChange(event.target.value)} placeholder="Search products" aria-label="Search products" /></div><div className="flex gap-2"><Button variant="outline" onClick={onBack}>All catalogs</Button><Button variant="outline" disabled title="Catalog settings are not available in the current API"><Settings />Settings</Button></div></div>
    <Card className="mt-3 overflow-hidden rounded-sm p-0 shadow-none">
      {loading ? <div className="grid gap-2 p-4" aria-label="Loading products">{[1,2,3,4].map((item) => <Skeleton className="h-10 w-full" key={item} />)}</div> : visible.length === 0 ? <div className="grid min-h-56 place-items-center p-6 text-center"><div><PackageOpen className="mx-auto mb-4 size-10 text-muted-foreground" /><h2 className="font-semibold">{products.length ? "No matching products" : "No products yet"}</h2><p className="mt-1 text-sm text-muted-foreground">{products.length ? "Try a different search term." : "Add the first product to this catalog."}</p>{products.length === 0 && <Button className="mt-5" onClick={onAddProduct}><Plus />Add product</Button>}</div></div> : <Table><TableHeader><TableRow><TableHead>Product</TableHead><TableHead>SKU</TableHead><TableHead>Brand</TableHead><TableHead>Facts</TableHead><TableHead>Updated</TableHead><TableHead className="w-16"><span className="sr-only">Actions</span></TableHead></TableRow></TableHeader><TableBody>{visible.map((product) => <TableRow key={product.id}><TableCell><button className="flex min-h-9 items-center gap-2.5 rounded-md text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" onClick={() => onOpenProduct(product.id)}><span className="grid size-8 place-items-center rounded-md border bg-muted text-xs font-semibold">{product.name.slice(0, 2).toUpperCase()}</span><strong className="text-sm">{product.name}</strong></button></TableCell><TableCell className="font-mono text-xs">{product.sku}</TableCell><TableCell>{product.brand}</TableCell><TableCell>{product.attributes.length}</TableCell><TableCell>{new Date(product.updatedAt).toLocaleDateString()}</TableCell><TableCell><DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" aria-label={`Actions for ${product.name}`}><MoreHorizontal /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem onSelect={() => onOpenProduct(product.id)}>Open product</DropdownMenuItem></DropdownMenuContent></DropdownMenu></TableCell></TableRow>)}</TableBody></Table>}
    </Card>
  </div>;
}
