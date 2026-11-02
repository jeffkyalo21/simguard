"use client";

import { createContext, useCallback, useContext, useRef, useState } from "react";
import { LeadForm } from "@/components/landing/lead-form";
import { Button } from "@/components/ui/button";
import { Dialog, DialogClose, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { copy } from "@/lib/copy";

const text = copy.landing.demo;
const DemoContext = createContext(() => {});

/** Opens the demo dialog. Pass the element that should get focus back. */
export function useOpenDemo() {
  return useContext(DemoContext);
}

/**
 * Holds the single "Book a demo" dialog for the Landing page. Every Book a
 * demo control opens it, and focus returns to the control that did.
 * @param {{ children: React.ReactNode }} props
 */
export function DemoDialogProvider({ children }) {
  const [open, setOpen] = useState(false);
  const opener = useRef(null);

  const openDemo = useCallback((returnTo) => {
    opener.current = returnTo ?? document.activeElement;
    setOpen(true);
  }, []);

  function onCloseAutoFocus(event) {
    const element = opener.current;
    if (element?.isConnected && element !== document.body) {
      event.preventDefault();
      element.focus({ preventScroll: true });
    }
  }

  return (
    <DemoContext.Provider value={openDemo}>
      {children}
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent
          aria-describedby={undefined}
          onCloseAutoFocus={onCloseAutoFocus}
          className="max-h-[calc(100dvh-2rem)] gap-5 overflow-y-auto sm:max-w-md"
        >
          <DialogHeader>
            <DialogTitle className="text-lg font-semibold">{text.title}</DialogTitle>
          </DialogHeader>
          {/* The content unmounts on close, so the form starts empty each time. */}
          <LeadForm
            kind="demo"
            submitLabel={text.submit}
            doneAction={
              <DialogClose asChild>
                <Button type="button" className="h-11 px-5">
                  {copy.actions.close}
                </Button>
              </DialogClose>
            }
          />
        </DialogContent>
      </Dialog>
    </DemoContext.Provider>
  );
}

/**
 * A "Book a demo" control. It is a link to the contact form, so it still
 * works without JavaScript; with JavaScript it opens the demo dialog.
 * Spreads its props so it can sit inside <Button asChild>.
 * @param {{ children: React.ReactNode } & React.AnchorHTMLAttributes<HTMLAnchorElement>} props
 */
export function BookDemoButton({ children, onClick, ...props }) {
  const openDemo = useOpenDemo();

  return (
    <a
      href="#contact"
      aria-haspopup="dialog"
      {...props}
      onClick={(event) => {
        onClick?.(event);
        if (event.defaultPrevented || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        openDemo(event.currentTarget);
      }}
    >
      {children}
    </a>
  );
}
