import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog";
import { buttonVariants } from "@/components/ui/button";

type Confirmation = { title: string; description: string; actionLabel: string; cancelLabel?: string; destructive?: boolean };
type Confirm = (options: Confirmation) => Promise<boolean>;
const ConfirmationContext = createContext<Confirm | null>(null);

export function ConfirmationProvider({ children }: { children: ReactNode }) {
  const [options, setOptions] = useState<Confirmation | null>(null);
  const [open, setOpen] = useState(false);
  const resolve = useRef<((accepted: boolean) => void) | null>(null);
  const trigger = useRef<HTMLElement | null>(null);
  const confirm = useCallback<Confirm>(next => new Promise<boolean>(settle => {
    resolve.current?.(false);
    resolve.current = settle;
    trigger.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setOptions(next); setOpen(true);
  }), []);
  const finish = useCallback((accepted: boolean) => {
    const settle = resolve.current; resolve.current = null;
    setOpen(false); settle?.(accepted);
  }, []);
  useEffect(() => () => { resolve.current?.(false); resolve.current = null; }, []);
  return <ConfirmationContext.Provider value={confirm}>{children}<AlertDialog open={open} onOpenChange={open => { if (!open) finish(false); }}><AlertDialogContent onCloseAutoFocus={event => { event.preventDefault(); trigger.current?.focus(); }}><AlertDialogHeader><AlertDialogTitle>{options?.title}</AlertDialogTitle><AlertDialogDescription>{options?.description}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel onClick={() => finish(false)}>{options?.cancelLabel ?? "Cancel"}</AlertDialogCancel><AlertDialogAction className={options?.destructive ? buttonVariants({ variant: "destructive" }) : undefined} onClick={() => finish(true)}>{options?.actionLabel ?? "Confirm"}</AlertDialogAction></AlertDialogFooter></AlertDialogContent></AlertDialog></ConfirmationContext.Provider>;
}
export function useConfirmation() {
  const confirm = useContext(ConfirmationContext);
  if (!confirm) throw new Error("ConfirmationProvider is required.");
  return confirm;
}
