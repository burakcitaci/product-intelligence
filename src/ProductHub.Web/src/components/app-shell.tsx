import { LayoutGrid } from "lucide-react";
import { AccountMenu, Brand } from "@/components/workspace";
import { Sidebar, SidebarContent, SidebarFooter, SidebarHeader, SidebarInset, SidebarMenu, SidebarMenuButton, SidebarMenuItem, SidebarProvider, SidebarTrigger, useSidebar } from "@/components/ui/sidebar";

const navigation = [{ label: "Catalogs", icon: LayoutGrid, route: "catalogs" }] as const;
type AppShellProps = {
  children: React.ReactNode;
  user: { displayName: string; email: string };
  activeRoute?: string;
  onNavigate: (route: string) => void;
  onLogout: () => void;
};
function WorkspaceNavigation({ activeRoute, onNavigate }: Pick<AppShellProps, "activeRoute" | "onNavigate">) {
  const { setOpen } = useSidebar();
  return <SidebarMenu>{navigation.map((item) => <SidebarMenuItem key={item.route}><SidebarMenuButton active={activeRoute === item.route} onClick={() => { onNavigate(item.route); setOpen(false); }}><item.icon className="size-4" />{item.label}</SidebarMenuButton></SidebarMenuItem>)}</SidebarMenu>;
}
export function AppShell({ children, user, activeRoute = "catalogs", onNavigate, onLogout }: AppShellProps) {
  return <SidebarProvider>
    <Sidebar aria-label="Main navigation">
      <SidebarHeader><Brand /></SidebarHeader>
      <SidebarContent><p className="nav-section-label">Workspace</p><WorkspaceNavigation activeRoute={activeRoute} onNavigate={onNavigate} /><div className="sidebar-note"><span className="status-indicator" />Content operations<p>From source facts to approved product content.</p></div></SidebarContent>
      <SidebarFooter><AccountMenu user={user} onLogout={onLogout} /></SidebarFooter>
    </Sidebar>
    <SidebarInset className="app-shell-main"><div className="workspace-mobile-bar"><SidebarTrigger /><Brand /></div>{children}</SidebarInset>
  </SidebarProvider>;
}
export { navigation };
