"use client";

import { createContext, useCallback, useContext, useRef, useState } from "react";
import { RegisterForm } from "@/components/landing/register-form";
import { Button } from "@/components/ui/button";
import { Dialog, DialogClose, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { copy } from "@/lib/copy";

const text = copy.landing.register;
const RegisterContext = createContext(() => {});

/** Opens the Register dialog. Pass the element that should get focus back. */
export function useOpenRegister() {
  return useContext(RegisterContext);
}

/**
 * Holds the single Register dialog for the Landing page. Every Register
 * control opens it, and focus returns to the control that did.
 * @param {{ children: React.ReactNode }} props
 */
export function RegisterDialogProvider({ children }) {
  const [open, setOpen] = useState(false);
  const opener = useRef(null);

  const openRegister = useCallback((returnTo) => {
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
    <RegisterContext.Provider value={openRegister}>
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
          <RegisterForm
            idPrefix="dialog"
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
    </RegisterContext.Provider>
  );
}

/**
 * A Register control. It is a link to the /register page, so it still
 * works without JavaScript; with JavaScript it opens the Register dialog.
 * Spreads its props so it can sit inside <Button asChild>.
 * @param {{ children: React.ReactNode } & React.AnchorHTMLAttributes<HTMLAnchorElement>} props
 */
export function RegisterButton({ children, onClick, ...props }) {
  const openRegister = useOpenRegister();

  return (
    <a
      href="/register"
      aria-haspopup="dialog"
      {...props}
      onClick={(event) => {
        onClick?.(event);
        if (event.defaultPrevented || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        openRegister(event.currentTarget);
      }}
    >
      {children}
    </a>
  );
}
