import * as React from "react";
import * as DialogPrimitive from "@radix-ui/react-dialog";
import { X } from "lucide-react";
import { cn } from "@/lib/utils";

const Sheet = DialogPrimitive.Root;
const SheetTrigger = DialogPrimitive.Trigger;
const SheetClose = DialogPrimitive.Close;

function SheetContent({ className, children, ...props }: React.ComponentProps<typeof DialogPrimitive.Content>) {
  return <DialogPrimitive.Portal><DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/45" /><DialogPrimitive.Content className={cn("fixed inset-y-0 right-0 z-50 flex w-full max-w-lg flex-col border-l bg-background shadow-xl", className)} {...props}>{children}<DialogPrimitive.Close className="absolute right-4 top-4 grid size-9 place-items-center rounded-md hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"><X className="size-4" /><span className="sr-only">Close</span></DialogPrimitive.Close></DialogPrimitive.Content></DialogPrimitive.Portal>;
}
function SheetHeader({ className, ...props }: React.ComponentProps<"div">) { return <div className={cn("grid gap-1.5 border-b p-5 pr-16", className)} {...props} />; }
function SheetTitle({ className, ...props }: React.ComponentProps<typeof DialogPrimitive.Title>) { return <DialogPrimitive.Title className={cn("text-xl font-semibold", className)} {...props} />; }
function SheetDescription({ className, ...props }: React.ComponentProps<typeof DialogPrimitive.Description>) { return <DialogPrimitive.Description className={cn("text-sm text-muted-foreground", className)} {...props} />; }
function SheetFooter({ className, ...props }: React.ComponentProps<"div">) { return <div className={cn("mt-auto flex flex-col-reverse gap-2 border-t p-5 sm:flex-row sm:justify-end", className)} {...props} />; }

export { Sheet, SheetClose, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle, SheetTrigger };
