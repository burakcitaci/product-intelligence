import * as React from "react";
import { PanelLeft } from "lucide-react";

import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

type SidebarContextValue = { open: boolean; setOpen: React.Dispatch<React.SetStateAction<boolean>> };
const SidebarContext = React.createContext<SidebarContextValue | null>(null);

function SidebarProvider({ children }: { children: React.ReactNode }) {
  const [open, setOpen] = React.useState(false);
  return <SidebarContext.Provider value={{ open, setOpen }}><div className="flex min-h-dvh w-full">{children}</div></SidebarContext.Provider>;
}

function useSidebar() {
  const value = React.useContext(SidebarContext);
  if (!value) throw new Error("useSidebar must be used inside SidebarProvider");
  return value;
}

function Sidebar({ className, ...props }: React.ComponentProps<"aside">) {
  const { open, setOpen } = useSidebar();
  return <><button aria-label="Close navigation" className={cn("fixed inset-0 z-30 bg-black/40 md:hidden", open ? "block" : "hidden")} onClick={() => setOpen(false)} /><aside className={cn("fixed inset-y-0 left-0 z-40 flex w-64 -translate-x-full flex-col border-r bg-[var(--sidebar)] text-[var(--sidebar-foreground)] transition-transform md:sticky md:translate-x-0", open && "translate-x-0", className)} {...props} /></>;
}

function SidebarHeader({ className, ...props }: React.ComponentProps<"div">) { return <div className={cn("flex h-16 items-center border-b border-[var(--sidebar-border)] px-4", className)} {...props} />; }
function SidebarContent({ className, ...props }: React.ComponentProps<"div">) { return <div className={cn("flex min-h-0 flex-1 flex-col gap-2 p-3", className)} {...props} />; }
function SidebarFooter({ className, ...props }: React.ComponentProps<"div">) { return <div className={cn("border-t border-[var(--sidebar-border)] p-3", className)} {...props} />; }
function SidebarMenu({ className, ...props }: React.ComponentProps<"ul">) { return <ul className={cn("grid gap-1", className)} {...props} />; }
function SidebarMenuItem(props: React.ComponentProps<"li">) { return <li {...props} />; }
function SidebarMenuButton({ className, active, ...props }: React.ComponentProps<"button"> & { active?: boolean }) { return <button className={cn("flex min-h-9 w-full items-center gap-2.5 rounded-md px-3 text-left text-sm font-medium hover:bg-[var(--sidebar-accent)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring", active && "bg-[var(--sidebar-accent)] text-[var(--sidebar-accent-foreground)]", className)} {...props} />; }
function SidebarInset({ className, ...props }: React.ComponentProps<"main">) { return <main className={cn("min-w-0 flex-1", className)} {...props} />; }
function SidebarTrigger({ className }: { className?: string }) { const { setOpen } = useSidebar(); return <Button type="button" variant="ghost" size="icon" className={cn("md:hidden", className)} aria-label="Open navigation" onClick={() => setOpen(true)}><PanelLeft /></Button>; }

export { Sidebar, SidebarContent, SidebarFooter, SidebarHeader, SidebarInset, SidebarMenu, SidebarMenuButton, SidebarMenuItem, SidebarProvider, SidebarTrigger, useSidebar };
