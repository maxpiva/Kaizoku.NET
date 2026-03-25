"use client";

import * as React from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Drawer,
  DrawerContent,
  DrawerDescription,
  DrawerFooter,
  DrawerHeader,
  DrawerTitle,
} from "@/components/ui/drawer";
import { useMediaQuery } from "@/hooks/use-media-query";
import { cn } from "@/lib/utils";

// ─── Responsive Modal ────────────────────────────────────────────────
// Desktop: centered Dialog with backdrop blur
// Mobile:  bottom sheet Drawer with drag handle
// ──────────────────────────────────────────────────────────────────────

interface ResponsiveModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  children: React.ReactNode;
  /** Extra className applied to DialogContent / DrawerContent */
  className?: string;
  /** Prevent closing when clicking outside */
  preventOutsideClose?: boolean;
  /** Title displayed in header */
  title?: React.ReactNode;
  /** Description displayed below title */
  description?: React.ReactNode;
  /** Footer content (buttons etc) */
  footer?: React.ReactNode;
  /** Desktop dialog max-width class (default "max-w-lg") */
  desktopMaxWidth?: string;
  /** Additional drawer content className for mobile */
  drawerClassName?: string;
  /** Whether to show header — defaults to true */
  showHeader?: boolean;
  /** Header icon element */
  headerIcon?: React.ReactNode;
}

export function ResponsiveModal({
  open,
  onOpenChange,
  children,
  className,
  preventOutsideClose = false,
  title,
  description,
  footer,
  desktopMaxWidth = "max-w-lg",
  drawerClassName,
  showHeader = true,
  headerIcon,
}: ResponsiveModalProps) {
  const isDesktop = useMediaQuery("(min-width: 768px)");

  if (isDesktop) {
    return (
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent
          className={cn(desktopMaxWidth, className)}
          onInteractOutside={
            preventOutsideClose ? (e) => e.preventDefault() : undefined
          }
        >
          {showHeader && (title || description) && (
            <DialogHeader>
              {title && (
                <DialogTitle className="flex items-center gap-2">
                  {headerIcon}
                  {title}
                </DialogTitle>
              )}
              {description && (
                <DialogDescription>{description}</DialogDescription>
              )}
            </DialogHeader>
          )}
          {children}
          {footer && <DialogFooter>{footer}</DialogFooter>}
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Drawer open={open} onOpenChange={onOpenChange}>
      <DrawerContent className={cn("max-h-[92dvh]", drawerClassName)}>
        {showHeader && (title || description) && (
          <DrawerHeader className="text-left pb-2">
            {title && (
              <DrawerTitle className="flex items-center gap-2">
                {headerIcon}
                {title}
              </DrawerTitle>
            )}
            {description && (
              <DrawerDescription>{description}</DrawerDescription>
            )}
          </DrawerHeader>
        )}
        <div className="flex-1 overflow-y-auto px-4 pb-2">{children}</div>
        {footer && (
          <DrawerFooter className="pt-2 pb-4 border-t border-border">
            {footer}
          </DrawerFooter>
        )}
      </DrawerContent>
    </Drawer>
  );
}

// ─── Convenience sub-components for custom layouts ─────────────────

export function ResponsiveModalBody({
  children,
  className,
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return <div className={cn("flex-1 min-h-0", className)}>{children}</div>;
}
