import { ArrowLeft, MoreHorizontal, PackageOpen, Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { EmptyState, PageLayout, SearchField } from "@/components/workspace";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

type ProductRow = { id: string; sku: string; name: string; brand: string; attributes: unknown[]; updatedAt: string };
type CatalogDetailProps = { catalog: { name: string; description: string }; products: ProductRow[]; loading?: boolean; query: string; onQueryChange: (value: string) => void; onBack: () => void; onOpenProduct: (id: string) => void; onAddProduct: () => void };

export function CatalogDetail({ catalog, products, loading = false, query, onQueryChange, onBack, onOpenProduct, onAddProduct }: CatalogDetailProps) {
  const term = query.trim().toLowerCase();
  const visible = term ? products.filter((product) => `${product.name} ${product.sku} ${product.brand}`.toLowerCase().includes(term)) : products;
  return <PageLayout>
    <PageHeader breadcrumbs={[{ label: "Catalogs", onClick: onBack }, catalog.name]} title={catalog.name} subtitle={catalog.description || "Manage the products in this catalog."} action={{ label: "Add product", onClick: onAddProduct }} />
    <div className="collection-toolbar"><SearchField value={query} onChange={onQueryChange} placeholder="Search products…" /><Button variant="ghost" size="sm" onClick={onBack}><ArrowLeft />All catalogs</Button></div>
    <Card className="overflow-hidden gap-0 p-0 shadow-none">
      <div className="collection-table-heading"><strong>Products <span>{loading ? "—" : products.length}</span></strong><span>{term ? `${visible.length} matching products` : "Product information"}</span></div>
      {loading ? <div className="grid gap-2 p-4" aria-label="Loading products" aria-busy="true">{[1, 2, 3, 4].map((item) => <Skeleton className="h-12 w-full" key={item} />)}</div>
        : visible.length === 0 ? <EmptyState icon={<PackageOpen />} title={products.length ? "No matching products" : "No products yet"} description={products.length ? "Try searching by name, SKU, or brand." : "Add a product and its verified source facts to get started."} action={products.length ? <Button variant="outline" onClick={() => onQueryChange("")}>Clear search</Button> : <Button onClick={onAddProduct}><Plus />Add product</Button>} />
        : <Table><TableHeader><TableRow><TableHead>Product</TableHead><TableHead>SKU</TableHead><TableHead>Brand</TableHead><TableHead>Source facts</TableHead><TableHead>Updated</TableHead><TableHead className="w-12"><span className="sr-only">Actions</span></TableHead></TableRow></TableHeader><TableBody>{visible.map((product) => <TableRow key={product.id}>
          <TableCell><button className="product-name-button" onClick={() => onOpenProduct(product.id)}><span className="product-initials">{product.name.slice(0, 2).toUpperCase()}</span><strong>{product.name}</strong></button></TableCell>
          <TableCell className="font-mono text-xs text-muted-foreground">{product.sku}</TableCell><TableCell>{product.brand}</TableCell><TableCell><span className="fact-total">{product.attributes.length} facts</span></TableCell><TableCell className="whitespace-nowrap text-muted-foreground">{new Date(product.updatedAt).toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" })}</TableCell>
          <TableCell><DropdownMenu><DropdownMenuTrigger asChild><Button variant="ghost" size="icon" aria-label={`Actions for ${product.name}`}><MoreHorizontal /></Button></DropdownMenuTrigger><DropdownMenuContent align="end"><DropdownMenuItem onSelect={() => onOpenProduct(product.id)}>Open product</DropdownMenuItem></DropdownMenuContent></DropdownMenu></TableCell>
        </TableRow>)}</TableBody></Table>}
      {!loading && visible.length > 0 && <div className="collection-table-footer">{visible.length} of {products.length} products</div>}
    </Card>
  </PageLayout>;
}
