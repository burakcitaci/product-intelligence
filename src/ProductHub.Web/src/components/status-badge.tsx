import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

const styles = {
  Draft: "border-border bg-muted text-foreground",
  "In review": "border-amber-600 bg-amber-100 text-amber-950 dark:bg-amber-950 dark:text-amber-100",
  Approved: "border-emerald-300 bg-emerald-50 text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-200",
  Rejected: "border-red-700 bg-red-100 text-red-950 dark:bg-red-950 dark:text-red-100",
  Current: "border-primary/40 bg-primary/10 text-foreground",
  Published: "border-emerald-300 bg-emerald-50 text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-200",
} as const;

export type StatusLabel = keyof typeof styles;

export function StatusBadge({ status, className }: { status: StatusLabel; className?: string }) {
  return <Badge variant="outline" className={cn(styles[status], className)}>{status}</Badge>;
}
