import { Fragment } from "react";
import { Plus } from "lucide-react";
import { Breadcrumb, BreadcrumbItem, BreadcrumbList, BreadcrumbPage, BreadcrumbSeparator } from "@/components/ui/breadcrumb";
import { Button } from "@/components/ui/button";

type Crumb = string | { label: string; onClick: () => void };
type PageHeaderProps = {
  breadcrumbs: Crumb[];
  title: string;
  subtitle?: string;
  action?: { label: string; onClick: () => void; icon?: React.ReactNode };
};

export function PageHeader({ breadcrumbs, title, subtitle, action }: PageHeaderProps) {
  return <header className="page-header">
    <div className="min-w-0">
      <Breadcrumb><BreadcrumbList>{breadcrumbs.filter(Boolean).map((item, index) => <Fragment key={index}>{index > 0 && <BreadcrumbSeparator />}<BreadcrumbItem>{typeof item === "string" ? index === breadcrumbs.length - 1 ? <BreadcrumbPage>{item}</BreadcrumbPage> : <span>{item}</span> : <button type="button" className="breadcrumb-button" onClick={item.onClick}>{item.label}</button>}</BreadcrumbItem></Fragment>)}</BreadcrumbList></Breadcrumb>
      <h1>{title}</h1>{subtitle && <p className="page-subtitle">{subtitle}</p>}
    </div>
    {action && <Button type="button" className="shrink-0" onClick={action.onClick}>{action.icon ?? <Plus />}{action.label}</Button>}
  </header>;
}
