"use client";

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { Loader2 } from 'lucide-react';
import { PasswordChangeDialog } from './password-change-dialog';

interface RequireAuthProps {
  children: React.ReactNode;
}

/**
 * Wraps children — redirects to /login if not authenticated.
 * Shows a spinner while auth state is loading.
 * Shows a blocking password-change dialog if the user's password doesn't meet the current policy.
 */
export function RequireAuth({ children }: RequireAuthProps) {
  const { isAuthenticated, isLoading, needsSetup, requiresPasswordChange, dismissPasswordChange } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (isLoading) return;
    if (needsSetup) {
      router.replace('/setup');
      return;
    }
    if (!isAuthenticated) {
      router.replace('/login');
    }
  }, [isAuthenticated, isLoading, needsSetup, router]);

  if (isLoading) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-background">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-background">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  return (
    <>
      <PasswordChangeDialog
        open={requiresPasswordChange}
        onPasswordChanged={dismissPasswordChange}
      />
      {children}
    </>
  );
}
