import type { ComponentProps, ReactNode } from "react";
import { LogOut, Package2, Search, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

export function PageLayout({ className, ...props }: ComponentProps<"div">) {
  return <div className={cn("workspace-page", className)} {...props} />;
}
export function Brand({ subtitle = "Content workspace" }: { subtitle?: string }) {
  return <div className="workspace-brand"><span className="workspace-brand-icon"><Package2 aria-hidden="true" size={19} /></span><div><strong>Product Hub</strong><span>{subtitle}</span></div></div>;
}
export function AccountMenu({ user, onLogout, label = "Sign out" }: { user: { displayName: string; email: string }; onLogout: () => void; label?: string }) {
  return <div className="workspace-account"><span className="workspace-avatar" aria-hidden="true">{user.displayName.trim().split(/\s+/).map((part) => part[0]).slice(0, 2).join("").toUpperCase()}</span><div className="min-w-0 flex-1"><strong className="block truncate text-xs">{user.displayName}</strong><span className="block truncate text-xs text-muted-foreground">{user.email}</span></div><Button size="icon" variant="ghost" onClick={onLogout} aria-label={label} title={label}><LogOut size={16} /></Button></div>;
}
export function SearchField({ value, onChange, placeholder = "Search…", className }: { value: string; onChange: (value: string) => void; placeholder?: string; className?: string }) {
  return <div className={cn("workspace-search", className)}><Search size={15} aria-hidden="true" /><Input value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} aria-label={placeholder} className="pl-9 pr-9" />{value && <button type="button" onClick={() => onChange("")} aria-label="Clear search"><X size={14} /></button>}</div>;
}
export function EmptyState({ icon, title, description, action }: { icon: ReactNode; title: string; description?: string; action?: ReactNode }) {
  return <div className="workspace-empty"><span className="workspace-empty-icon" aria-hidden="true">{icon}</span><h2>{title}</h2>{description && <p>{description}</p>}{action && <div className="mt-4">{action}</div>}</div>;
}
export function SectionHeader({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return <div className="workspace-section-header"><div><h2>{title}</h2>{description && <p>{description}</p>}</div>{action}</div>;
}
