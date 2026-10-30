import { LayoutGrid, LogOut, Package2 } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { Sidebar, SidebarContent, SidebarFooter, SidebarHeader, SidebarInset, SidebarMenu, SidebarMenuButton, SidebarMenuItem, SidebarProvider, SidebarTrigger } from "@/components/ui/sidebar";

const navigation = [{ label: "Catalogs", icon: LayoutGrid, route: "catalogs" }] as const;

type AppShellProps = {
  children: React.ReactNode;
  user: { displayName: string; email: string };
  activeRoute?: string;
  onNavigate: (route: string) => void;
  onLogout: () => void;
};

export function AppShell({ children, user, activeRoute = "catalogs", onNavigate, onLogout }: AppShellProps) {
  return <SidebarProvider>
    <Sidebar>
      <SidebarHeader><div className="flex items-center gap-3"><span className="grid size-9 place-items-center rounded-md bg-primary text-primary-foreground"><Package2 className="size-5" /></span><div><strong className="block text-sm">Product Hub</strong><span className="text-xs text-muted-foreground">Content operations</span></div></div></SidebarHeader>
      <SidebarContent><SidebarMenu>{navigation.map((item) => <SidebarMenuItem key={item.route}><SidebarMenuButton active={activeRoute === item.route} onClick={() => onNavigate(item.route)}><item.icon className="size-4" />{item.label}</SidebarMenuButton></SidebarMenuItem>)}</SidebarMenu></SidebarContent>
      <SidebarFooter><div className="grid gap-3"><div className="min-w-0 px-2"><strong className="block truncate text-sm">{user.displayName}</strong><span className="block truncate text-xs text-muted-foreground">{user.email}</span></div><Separator /><Button variant="ghost" className="justify-start" onClick={onLogout}><LogOut />Sign out</Button></div></SidebarFooter>
    </Sidebar>
    <SidebarInset className="app-shell-main"><div className="flex h-16 items-center border-b bg-card px-4 md:hidden"><SidebarTrigger /><strong className="ml-2 text-sm">Product Hub</strong></div>{children}</SidebarInset>
  </SidebarProvider>;
}

export { navigation };
