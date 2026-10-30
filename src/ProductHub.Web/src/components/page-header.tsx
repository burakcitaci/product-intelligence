import { Plus } from "lucide-react";

import { Breadcrumb, BreadcrumbItem, BreadcrumbList, BreadcrumbPage, BreadcrumbSeparator } from "@/components/ui/breadcrumb";
import { Button } from "@/components/ui/button";

type PageHeaderProps = {
  breadcrumbs: string[];
  title: string;
  subtitle?: string;
  action?: { label: string; onClick: () => void; icon?: React.ReactNode };
};

export function PageHeader({ breadcrumbs, title, subtitle, action }: PageHeaderProps) {
  return <header className="flex flex-col gap-5 border-b pb-6 sm:flex-row sm:items-end sm:justify-between">
    <div className="grid gap-3">
      <Breadcrumb><BreadcrumbList>{breadcrumbs.map((item, index) => <BreadcrumbItem key={`${item}-${index}`}><BreadcrumbPage className={index < breadcrumbs.length - 1 ? "font-normal text-muted-foreground" : undefined}>{item}</BreadcrumbPage>{index < breadcrumbs.length - 1 && <BreadcrumbSeparator />}</BreadcrumbItem>)}</BreadcrumbList></Breadcrumb>
      <div><h1 className="text-3xl font-semibold tracking-tight">{title}</h1>{subtitle && <p className="mt-1 max-w-2xl text-sm text-muted-foreground">{subtitle}</p>}</div>
    </div>
    {action && <Button type="button" onClick={action.onClick}>{action.icon ?? <Plus />}{action.label}</Button>}
  </header>;
}
