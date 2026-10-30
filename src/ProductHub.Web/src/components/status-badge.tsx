import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

const styles = {
  Draft: "border-slate-300 bg-slate-50 text-slate-700 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200",
  "In review": "border-amber-300 bg-amber-50 text-amber-800 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200",
  Approved: "border-emerald-300 bg-emerald-50 text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-200",
  Rejected: "border-red-300 bg-red-50 text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200",
  Current: "border-indigo-300 bg-indigo-50 text-indigo-800 dark:border-indigo-800 dark:bg-indigo-950 dark:text-indigo-200",
  Published: "border-emerald-300 bg-emerald-50 text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950 dark:text-emerald-200",
} as const;

export type StatusLabel = keyof typeof styles;

export function StatusBadge({ status, className }: { status: StatusLabel; className?: string }) {
  return <Badge variant="outline" className={cn(styles[status], className)}>{status}</Badge>;
}
