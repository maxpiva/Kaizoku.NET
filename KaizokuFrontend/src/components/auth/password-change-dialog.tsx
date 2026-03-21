"use client";

import { useState } from 'react';
import { motion } from 'framer-motion';
import {
  Shield,
  Eye,
  EyeOff,
  Loader2,
  CheckCircle2,
  XCircle,
  AlertTriangle,
} from 'lucide-react';
import { toast } from 'sonner';
import * as DialogPrimitive from '@radix-ui/react-dialog';

import { useAuth } from '@/contexts/auth-context';
import { userService } from '@/lib/api/services/userService';
import type { ChangePasswordRequest } from '@/lib/api/auth-types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { DialogOverlay, DialogPortal } from '@/components/ui/dialog';

// ─── Sub-components ────────────────────────────────────────────────────────────

interface PasswordChangeDialogProps {
  open: boolean;
  onPasswordChanged: () => void;
}

function PasswordCheck({ ok, label }: { ok: boolean; label: string }) {
  return (
    <span
      className={`flex items-center gap-1 text-xs transition-colors ${
        ok ? 'text-emerald-600 dark:text-emerald-400' : 'text-muted-foreground'
      }`}
    >
      {ok ? <CheckCircle2 className="h-3 w-3" /> : <XCircle className="h-3 w-3" />}
      {label}
    </span>
  );
}

// ─── Animation variants ────────────────────────────────────────────────────────

const containerVariants = {
  hidden: { opacity: 0, y: 16 },
  visible: {
    opacity: 1,
    y: 0,
    transition: {
      duration: 0.35,
      ease: [0.4, 0, 0.2, 1] as [number, number, number, number],
      staggerChildren: 0.07,
    },
  },
};

const childVariants = {
  hidden: { opacity: 0, y: 8 },
  visible: {
    opacity: 1,
    y: 0,
    transition: { duration: 0.28, ease: [0.4, 0, 0.2, 1] as [number, number, number, number] },
  },
};

// ─── Main component ────────────────────────────────────────────────────────────

export function PasswordChangeDialog({ open, onPasswordChanged }: PasswordChangeDialogProps) {
  const { dismissPasswordChange } = useAuth();

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');

  const [showCurrent, setShowCurrent] = useState(false);
  const [showNew, setShowNew] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // ─── Validation ──────────────────────────────────────────────────────────────

  const passwordChecks = {
    length: newPassword.length >= 8,
    letter: /[a-zA-Z]/.test(newPassword),
    number: /[0-9]/.test(newPassword),
  };
  const isNewPasswordValid = Object.values(passwordChecks).every(Boolean);
  const passwordsMatch = newPassword.length > 0 && newPassword === confirmPassword;

  const canSubmit =
    currentPassword.trim().length > 0 &&
    isNewPasswordValid &&
    passwordsMatch &&
    !isSubmitting;

  // ─── Submit ──────────────────────────────────────────────────────────────────

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;

    setError(null);
    setIsSubmitting(true);

    try {
      const payload: ChangePasswordRequest = {
        currentPassword: currentPassword.trim(),
        newPassword,
      };
      await userService.changePassword(payload);

      toast.success('Password updated successfully.');
      dismissPasswordChange();
      onPasswordChanged();
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to change password. Please try again.';
      if (msg.includes('401') || msg.toLowerCase().includes('incorrect') || msg.toLowerCase().includes('invalid')) {
        setError('Current password is incorrect. Please try again.');
      } else {
        setError(msg);
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  // ─── Render ──────────────────────────────────────────────────────────────────

  return (
    <DialogPrimitive.Root open={open}>
      <DialogPortal>
        <DialogOverlay />
        {/* Use primitive Content directly to suppress the built-in Close button */}
        <DialogPrimitive.Content
          aria-describedby="password-change-description"
          // Block all dismissal paths — this is a forced action
          onInteractOutside={(e) => e.preventDefault()}
          onEscapeKeyDown={(e) => e.preventDefault()}
          onPointerDownOutside={(e) => e.preventDefault()}
          onFocusOutside={(e) => e.preventDefault()}
          className="fixed left-[50%] top-[50%] z-50 w-[95vw] sm:w-full max-w-md translate-x-[-50%] translate-y-[-50%] border bg-card shadow-lg rounded-xl max-h-[95vh] sm:max-h-[90vh] overflow-y-auto overflow-x-hidden focus:outline-none data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95 data-[state=open]:slide-in-from-left-1/2 data-[state=open]:slide-in-from-top-[48%] duration-200"
        >
          <motion.div
            variants={containerVariants}
            initial="hidden"
            animate="visible"
            className="p-6 sm:p-8 space-y-5"
          >
            {/* ── Header ───────────────────────────────────────────────────── */}
            <motion.div variants={childVariants} className="flex flex-col items-center gap-3 text-center">
              <div className="h-12 w-12 flex items-center justify-center rounded-xl bg-primary/10 border border-primary/20">
                <Shield className="h-6 w-6 text-primary" />
              </div>
              <div>
                <DialogPrimitive.Title className="text-lg font-semibold text-foreground">
                  Password Update Required
                </DialogPrimitive.Title>
                <DialogPrimitive.Description
                  id="password-change-description"
                  className="text-sm text-muted-foreground mt-1"
                >
                  Choose a new password to continue
                </DialogPrimitive.Description>
              </div>
            </motion.div>

            {/* ── Warning banner ────────────────────────────────────────────── */}
            <motion.div
              variants={childVariants}
              className="flex items-start gap-2.5 rounded-lg border border-amber-500/20 bg-amber-500/10 px-3 py-2.5"
            >
              <AlertTriangle className="h-4 w-4 text-amber-500 shrink-0 mt-0.5" />
              <p className="text-sm text-amber-600 dark:text-amber-400 leading-snug">
                Your current password doesn&apos;t meet the updated security policy. You must
                set a new password before you can use the app.
              </p>
            </motion.div>

            {/* ── Inline error ──────────────────────────────────────────────── */}
            {error && (
              <motion.div
                initial={{ opacity: 0, height: 0 }}
                animate={{ opacity: 1, height: 'auto' }}
                transition={{ duration: 0.2, ease: 'easeOut' }}
                className="flex items-start gap-2.5 rounded-lg border border-destructive/20 bg-destructive/10 px-3 py-2.5"
              >
                <XCircle className="h-4 w-4 text-destructive shrink-0 mt-0.5" />
                <p className="text-sm text-destructive">{error}</p>
              </motion.div>
            )}

            {/* ── Form ─────────────────────────────────────────────────────── */}
            <motion.form
              variants={childVariants}
              onSubmit={handleSubmit}
              className="space-y-4"
            >
              {/* Current password */}
              <div className="space-y-1.5">
                <Label htmlFor="currentPassword">Current Password</Label>
                <div className="relative">
                  <Input
                    id="currentPassword"
                    type={showCurrent ? 'text' : 'password'}
                    autoComplete="current-password"
                    placeholder="Enter your current password"
                    value={currentPassword}
                    onChange={(e) => setCurrentPassword(e.target.value)}
                    disabled={isSubmitting}
                    required
                    className="pr-10"
                  />
                  <button
                    type="button"
                    onClick={() => setShowCurrent((v) => !v)}
                    className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                    aria-label={showCurrent ? 'Hide current password' : 'Show current password'}
                    tabIndex={-1}
                  >
                    {showCurrent ? (
                      <EyeOff className="h-4 w-4" />
                    ) : (
                      <Eye className="h-4 w-4" />
                    )}
                  </button>
                </div>
              </div>

              {/* New password */}
              <div className="space-y-1.5">
                <Label htmlFor="newPassword">New Password</Label>
                <div className="relative">
                  <Input
                    id="newPassword"
                    type={showNew ? 'text' : 'password'}
                    autoComplete="new-password"
                    placeholder="Choose a new password"
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    disabled={isSubmitting}
                    required
                    className="pr-10"
                  />
                  <button
                    type="button"
                    onClick={() => setShowNew((v) => !v)}
                    className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                    aria-label={showNew ? 'Hide new password' : 'Show new password'}
                    tabIndex={-1}
                  >
                    {showNew ? (
                      <EyeOff className="h-4 w-4" />
                    ) : (
                      <Eye className="h-4 w-4" />
                    )}
                  </button>
                </div>

                {/* Strength indicators — only appear once typing starts */}
                {newPassword && (
                  <div className="flex gap-3 flex-wrap mt-1.5">
                    <PasswordCheck ok={passwordChecks.length} label="8+ chars" />
                    <PasswordCheck ok={passwordChecks.letter} label="Letter" />
                    <PasswordCheck ok={passwordChecks.number} label="Number" />
                  </div>
                )}
              </div>

              {/* Confirm new password */}
              <div className="space-y-1.5">
                <Label htmlFor="confirmPassword">Confirm New Password</Label>
                <div className="relative">
                  <Input
                    id="confirmPassword"
                    type={showConfirm ? 'text' : 'password'}
                    autoComplete="new-password"
                    placeholder="Re-enter your new password"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    disabled={isSubmitting}
                    required
                    className="pr-10"
                  />
                  <button
                    type="button"
                    onClick={() => setShowConfirm((v) => !v)}
                    className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                    aria-label={showConfirm ? 'Hide confirm password' : 'Show confirm password'}
                    tabIndex={-1}
                  >
                    {showConfirm ? (
                      <EyeOff className="h-4 w-4" />
                    ) : (
                      <Eye className="h-4 w-4" />
                    )}
                  </button>
                </div>

                {/* Match indicator — only when confirm field has input */}
                {confirmPassword && (
                  <div className="mt-1.5">
                    <PasswordCheck
                      ok={passwordsMatch}
                      label={passwordsMatch ? 'Passwords match' : 'Passwords do not match'}
                    />
                  </div>
                )}
              </div>

              <Button
                type="submit"
                className="w-full"
                disabled={!canSubmit}
              >
                {isSubmitting ? (
                  <>
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    Updating password...
                  </>
                ) : (
                  'Update password'
                )}
              </Button>
            </motion.form>
          </motion.div>
        </DialogPrimitive.Content>
      </DialogPortal>
    </DialogPrimitive.Root>
  );
}
