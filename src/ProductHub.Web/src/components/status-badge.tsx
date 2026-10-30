import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

const styles = {
  Draft: "border-slate-500 bg-slate-100 text-slate-900 dark:bg-slate-800 dark:text-slate-100",
  "In review": "border-amber-600 bg-amber-100 text-amber-950 dark:bg-amber-950 dark:text-amber-100",
  Approved: "border-emerald-700 bg-emerald-100 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100",
  Rejected: "border-red-700 bg-red-100 text-red-950 dark:bg-red-950 dark:text-red-100",
  Current: "border-indigo-700 bg-indigo-100 text-indigo-950 dark:bg-indigo-950 dark:text-indigo-100",
  Published: "border-emerald-700 bg-emerald-100 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100",
} as const;

export type StatusLabel = keyof typeof styles;

export function StatusBadge({ status, className }: { status: StatusLabel; className?: string }) {
  return <Badge variant="outline" className={cn(styles[status], className)}>{status}</Badge>;
}
