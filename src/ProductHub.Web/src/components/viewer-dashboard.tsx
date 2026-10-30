import React from "react";
import { BookOpen, LogOut, Package2 } from "lucide-react";

import { PageHeader } from "@/components/page-header";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { Sidebar, SidebarContent, SidebarFooter, SidebarHeader, SidebarInset, SidebarMenu, SidebarMenuButton, SidebarMenuItem, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";

type CatalogView = { id: string; name: string; description: string; productCount: number };
type ProductView = { id: string; sku: string; name: string; brand: string; attributes: Array<{ name: string; value: string; unit?: string }>; approvedContent?: { title: string; shortDescription: string; highlights: string[] } };
type Copy = { catalog: string; viewer: string; signout: string; products: string; waiting: string; badge: string; facts: string };

export function ViewerDashboard({ user, catalogs, catalogId, activeCatalog, products, language, error, text, onCatalogChange, onLanguageChange, onLogout }: { user: { displayName: string; email: string }; catalogs: CatalogView[]; catalogId: string; activeCatalog?: CatalogView; products: ProductView[]; language: "de" | "en"; error: string; text: Copy; onCatalogChange: (id: string) => void; onLanguageChange: (language: "de" | "en") => void; onLogout: () => void }) {
  return <SidebarProvider>
    <Sidebar>
      <SidebarHeader><div className="flex items-center gap-3"><span className="grid size-8 place-items-center rounded-md bg-primary text-primary-foreground"><BookOpen className="size-4" /></span><div><strong className="block text-sm">Product Hub</strong><span className="text-xs text-muted-foreground">{text.viewer}</span></div></div></SidebarHeader>
      <SidebarContent><p className="px-3 pb-1 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{text.catalog}</p><SidebarMenu>{catalogs.map((catalog) => <SidebarMenuItem key={catalog.id}><SidebarMenuButton active={catalog.id === catalogId} onClick={() => onCatalogChange(catalog.id)}><Package2 className="size-4" /><span className="min-w-0 flex-1 truncate">{catalog.name}</span><span className="text-xs text-muted-foreground">{catalog.productCount}</span></SidebarMenuButton></SidebarMenuItem>)}</SidebarMenu></SidebarContent>
      <SidebarFooter><div className="grid gap-2"><div className="min-w-0 px-2"><strong className="block truncate text-sm">{user.displayName}</strong><span className="block truncate text-xs text-muted-foreground">{user.email}</span></div><Separator /><Button variant="ghost" className="justify-start" onClick={onLogout}><LogOut />{text.signout}</Button></div></SidebarFooter>
    </Sidebar>
    <SidebarInset className="app-shell-main"><div className="flex h-12 items-center border-b bg-card px-3 md:hidden"><SidebarTrigger /><strong className="ml-2 text-sm">Product Hub</strong></div><div className="mx-auto w-full max-w-7xl p-5 lg:p-6">
      <PageHeader breadcrumbs={[text.catalog, activeCatalog?.name ?? ""]} title={activeCatalog?.name ?? text.catalog} subtitle={activeCatalog?.description} />
      <div className="mt-4 flex items-center justify-between gap-3"><span className="text-xs text-muted-foreground">{products.length} {text.products}</span><div className="flex rounded-md border bg-card p-0.5" role="group" aria-label="Content language"><Button size="sm" variant={language === "de" ? "secondary" : "ghost"} onClick={() => onLanguageChange("de")}>DE</Button><Button size="sm" variant={language === "en" ? "secondary" : "ghost"} onClick={() => onLanguageChange("en")}>EN</Button></div></div>
      {error && <Alert className="mt-4 border-destructive/40 text-destructive"><AlertDescription>{error}</AlertDescription></Alert>}
      {products.length === 0 ? <Card className="mt-4 grid min-h-48 place-items-center border-dashed p-5 text-center shadow-none"><div><Package2 className="mx-auto mb-2 size-6 text-muted-foreground" /><h2 className="text-sm font-semibold">No products available</h2></div></Card> : <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">{products.map((product) => <Card className="gap-3 p-4 shadow-none" key={product.id}><div className="flex items-start gap-3"><span className="grid size-9 shrink-0 place-items-center rounded-md border bg-muted text-xs font-semibold">{product.name.slice(0, 2).toUpperCase()}</span><div className="min-w-0"><p className="truncate text-xs text-muted-foreground">{product.brand} · {product.sku}</p><h2 className="mt-0.5 text-sm font-semibold">{product.approvedContent?.title ?? product.name}</h2></div></div><p className="text-sm leading-5 text-muted-foreground">{product.approvedContent?.shortDescription ?? text.waiting}</p>{product.approvedContent ? <ul className="grid gap-1.5 border-t pt-3">{product.approvedContent.highlights.map((highlight) => <li className="text-xs leading-5" key={highlight}>• {highlight}</li>)}</ul> : <Badge variant="outline" className="w-fit border-amber-300 bg-amber-50 text-amber-800">{text.badge}</Badge>}<details className="border-t pt-3 text-sm"><summary className="cursor-pointer text-xs font-medium">{text.facts}</summary><dl className="mt-2 grid grid-cols-2 gap-x-3 gap-y-1.5 text-xs">{product.attributes.map((attribute) => <React.Fragment key={attribute.name}><dt className="text-muted-foreground">{attribute.name}</dt><dd className="m-0 text-right font-medium">{attribute.value}{attribute.unit ? ` ${attribute.unit}` : ""}</dd></React.Fragment>)}</dl></details></Card>)}</div>}
    </div></SidebarInset>
  </SidebarProvider>;
}
