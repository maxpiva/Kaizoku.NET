"use client";

import { useState, useEffect, Suspense } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Image from 'next/image';
import Link from 'next/link';
import { Eye, EyeOff, Loader2, AlertCircle, CheckCircle2, XCircle } from 'lucide-react';
import { motion } from 'framer-motion';
import { useAuth } from '@/contexts/auth-context';
import { useValidateInvite } from '@/lib/api/hooks/useInvites';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

function PasswordStrengthBar({ password }: { password: string }) {
  const checks = {
    length: password.length >= 8,
    letter: /[a-zA-Z]/.test(password),
    number: /[0-9]/.test(password),
  };
  const score = Object.values(checks).filter(Boolean).length;
  const levels = [
    { label: 'Weak', color: 'bg-destructive' },
    { label: 'Fair', color: 'bg-amber-500' },
    { label: 'Good', color: 'bg-emerald-500' },
  ];
  const level = levels[Math.max(0, score - 1)] ?? levels[0];

  if (!password) return null;

  return (
    <div className="space-y-1.5 mt-1.5">
      <div className="flex gap-1">
        {[0, 1, 2].map((i) => (
          <div
            key={i}
            className={`h-1 flex-1 rounded-full transition-all duration-300 ${
              i < score ? level!.color : 'bg-muted'
            }`}
          />
        ))}
      </div>
      <div className="flex gap-3">
        {[
          { key: 'length', label: '8+ chars' },
          { key: 'letter', label: 'Letter' },
          { key: 'number', label: 'Number' },
        ].map(({ key, label }) => (
          <span
            key={key}
            className={`flex items-center gap-1 text-xs transition-colors ${
              checks[key as keyof typeof checks]
                ? 'text-emerald-600 dark:text-emerald-400'
                : 'text-muted-foreground'
            }`}
          >
            {checks[key as keyof typeof checks] ? (
              <CheckCircle2 className="h-3 w-3" />
            ) : (
              <XCircle className="h-3 w-3" />
            )}
            {label}
          </span>
        ))}
      </div>
    </div>
  );
}

function RegisterForm() {
  const { register, isAuthenticated, isLoading, needsSetup } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();

  const inviteCodeParam = searchParams.get('invite') ?? '';
  const [inviteCode, setInviteCode] = useState(inviteCodeParam);
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const { data: inviteValidation, isLoading: validatingInvite } = useValidateInvite(
    inviteCode.trim()
  );

  useEffect(() => {
    if (isLoading) return;
    if (needsSetup) {
      router.replace('/setup');
      return;
    }
    if (isAuthenticated) {
      router.replace('/library');
    }
  }, [isAuthenticated, isLoading, needsSetup, router]);

  const isPasswordValid =
    password.length >= 8 && /[a-zA-Z]/.test(password) && /[0-9]/.test(password);
  const passwordsMatch = password === confirmPassword;

  const canSubmit =
    inviteCode.trim() &&
    inviteValidation?.isValid &&
    username.trim() &&
    email.trim() &&
    displayName.trim() &&
    isPasswordValid &&
    passwordsMatch &&
    !isSubmitting;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;

    setError(null);
    setIsSubmitting(true);
    try {
      await register(
        username.trim(),
        email.trim(),
        password,
        displayName.trim(),
        inviteCode.trim()
      );
      router.push('/library');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Registration failed. Please try again.';
      setError(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex h-screen items-center justify-center bg-background">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    );
  }

  const showInvalidInvite =
    inviteCode.trim() && !validatingInvite && inviteValidation && !inviteValidation.isValid;
  const showValidInvite =
    inviteCode.trim() && !validatingInvite && inviteValidation?.isValid;

  return (
    <div className="min-h-screen flex items-center justify-center bg-background px-4 py-8">
      <div
        className="pointer-events-none fixed inset-0 overflow-hidden"
        aria-hidden="true"
      >
        <div
          className="absolute -top-40 -right-40 h-96 w-96 rounded-full opacity-[0.04]"
          style={{
            background: 'radial-gradient(circle, hsl(var(--primary)) 0%, transparent 70%)',
          }}
        />
        <div
          className="absolute -bottom-40 -left-40 h-96 w-96 rounded-full opacity-[0.04]"
          style={{
            background: 'radial-gradient(circle, hsl(var(--primary)) 0%, transparent 70%)',
          }}
        />
      </div>

      <motion.div
        initial={{ opacity: 0, y: 16 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.35, ease: [0.4, 0, 0.2, 1] }}
        className="relative w-full max-w-sm"
      >
        <div className="rounded-xl border bg-card shadow-lg p-8 space-y-6">
          {/* Logo */}
          <div className="flex flex-col items-center gap-3">
            <div className="h-12 w-12 flex items-center justify-center rounded-xl bg-primary/10 border border-primary/20">
              <Image
                src="/kaizoku.net.png"
                alt="Kaizoku.NET"
                width={32}
                height={32}
                className="h-8 w-8 object-contain"
                priority
              />
            </div>
            <div className="text-center">
              <h1 className="text-xl font-semibold text-foreground">Create account</h1>
              <p className="text-sm text-muted-foreground mt-0.5">
                You need an invite code to register
              </p>
            </div>
          </div>

          {/* Error */}
          {error && (
            <motion.div
              initial={{ opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: 'auto' }}
              className="flex items-start gap-2.5 rounded-lg border border-destructive/20 bg-destructive/10 px-3 py-2.5"
            >
              <AlertCircle className="h-4 w-4 text-destructive shrink-0 mt-0.5" />
              <p className="text-sm text-destructive">{error}</p>
            </motion.div>
          )}

          <form onSubmit={handleSubmit} className="space-y-4">
            {/* Invite code */}
            <div className="space-y-1.5">
              <Label htmlFor="inviteCode">Invite Code</Label>
              <div className="relative">
                <Input
                  id="inviteCode"
                  type="text"
                  placeholder="Enter your invite code"
                  value={inviteCode}
                  onChange={(e) => setInviteCode(e.target.value)}
                  disabled={isSubmitting}
                  required
                  className={
                    showValidInvite
                      ? 'border-emerald-500 pr-9'
                      : showInvalidInvite
                      ? 'border-destructive pr-9'
                      : ''
                  }
                />
                {validatingInvite && inviteCode.trim() && (
                  <Loader2 className="absolute right-3 top-1/2 -translate-y-1/2 h-4 w-4 animate-spin text-muted-foreground" />
                )}
                {showValidInvite && (
                  <CheckCircle2 className="absolute right-3 top-1/2 -translate-y-1/2 h-4 w-4 text-emerald-500" />
                )}
                {showInvalidInvite && (
                  <XCircle className="absolute right-3 top-1/2 -translate-y-1/2 h-4 w-4 text-destructive" />
                )}
              </div>
              {showInvalidInvite && (
                <p className="text-xs text-destructive">
                  {inviteValidation?.reason ?? 'This invite code is invalid or expired.'}
                </p>
              )}
              {showValidInvite && inviteValidation?.permissionPresetName && (
                <p className="text-xs text-emerald-600 dark:text-emerald-400">
                  Valid invite — permissions preset: {inviteValidation.permissionPresetName}
                </p>
              )}
              {showValidInvite && !inviteValidation?.permissionPresetName && (
                <p className="text-xs text-emerald-600 dark:text-emerald-400">
                  Valid invite code
                </p>
              )}
            </div>

            {/* Only show the rest when invite is valid */}
            {showValidInvite && (
              <motion.div
                initial={{ opacity: 0, height: 0 }}
                animate={{ opacity: 1, height: 'auto' }}
                className="space-y-4"
              >
                <div className="space-y-1.5">
                  <Label htmlFor="username">Username</Label>
                  <Input
                    id="username"
                    type="text"
                    autoComplete="username"
                    placeholder="Choose a username"
                    value={username}
                    onChange={(e) => setUsername(e.target.value)}
                    disabled={isSubmitting}
                    required
                  />
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="email">Email</Label>
                  <Input
                    id="email"
                    type="email"
                    autoComplete="email"
                    placeholder="Enter your email address"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    disabled={isSubmitting}
                    required
                  />
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="displayName">Display Name</Label>
                  <Input
                    id="displayName"
                    type="text"
                    autoComplete="name"
                    placeholder="How should we call you?"
                    value={displayName}
                    onChange={(e) => setDisplayName(e.target.value)}
                    disabled={isSubmitting}
                    required
                  />
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="password">Password</Label>
                  <div className="relative">
                    <Input
                      id="password"
                      type={showPassword ? 'text' : 'password'}
                      autoComplete="new-password"
                      placeholder="Choose a strong password"
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      disabled={isSubmitting}
                      required
                      className="pr-10"
                    />
                    <button
                      type="button"
                      onClick={() => setShowPassword((v) => !v)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                      tabIndex={-1}
                      aria-label={showPassword ? 'Hide password' : 'Show password'}
                    >
                      {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                  <PasswordStrengthBar password={password} />
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="confirmPassword">Confirm Password</Label>
                  <div className="relative">
                    <Input
                      id="confirmPassword"
                      type={showConfirm ? 'text' : 'password'}
                      autoComplete="new-password"
                      placeholder="Repeat your password"
                      value={confirmPassword}
                      onChange={(e) => setConfirmPassword(e.target.value)}
                      disabled={isSubmitting}
                      required
                      className={`pr-10 ${
                        confirmPassword && !passwordsMatch ? 'border-destructive' : ''
                      }`}
                    />
                    <button
                      type="button"
                      onClick={() => setShowConfirm((v) => !v)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                      tabIndex={-1}
                      aria-label={showConfirm ? 'Hide password' : 'Show password'}
                    >
                      {showConfirm ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                    </button>
                  </div>
                  {confirmPassword && !passwordsMatch && (
                    <p className="text-xs text-destructive">Passwords do not match.</p>
                  )}
                </div>
              </motion.div>
            )}

            <Button
              type="submit"
              className="w-full"
              disabled={!canSubmit}
            >
              {isSubmitting ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Creating account...
                </>
              ) : (
                'Create account'
              )}
            </Button>
          </form>

          <p className="text-center text-sm text-muted-foreground">
            Already have an account?{' '}
            <Link
              href="/login"
              className="text-primary hover:underline font-medium transition-colors"
            >
              Sign in
            </Link>
          </p>
        </div>
      </motion.div>
    </div>
  );
}

export default function RegisterPage() {
  return (
    <Suspense
      fallback={
        <div className="flex h-screen items-center justify-center bg-background">
          <Loader2 className="h-8 w-8 animate-spin text-primary" />
        </div>
      }
    >
      <RegisterForm />
    </Suspense>
  );
}
