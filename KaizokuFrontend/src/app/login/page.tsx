"use client";

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import Image from 'next/image';
import Link from 'next/link';
import { Eye, EyeOff, Loader2, AlertCircle } from 'lucide-react';
import { motion } from 'framer-motion';
import { useAuth } from '@/contexts/auth-context';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

export default function LoginPage() {
  const { login, isAuthenticated, isLoading, needsSetup } = useAuth();
  const router = useRouter();

  const [usernameOrEmail, setUsernameOrEmail] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (isLoading) return;
    // No users exist yet — redirect to initial admin setup
    if (needsSetup) {
      router.replace('/setup');
      return;
    }
    if (isAuthenticated) {
      router.replace('/library');
    }
  }, [isAuthenticated, isLoading, needsSetup, router]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!usernameOrEmail.trim() || !password) return;

    setError(null);
    setIsSubmitting(true);
    try {
      await login(usernameOrEmail.trim(), password, rememberMe);
      router.push('/library');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Login failed. Please try again.';
      if (msg.includes('401') || msg.includes('401')) {
        setError('Invalid username or password.');
      } else if (msg.toLowerCase().includes('disabled') || msg.toLowerCase().includes('inactive')) {
        setError('Your account has been disabled. Contact an administrator.');
      } else if (msg.toLowerCase().includes('rate')) {
        setError('Too many attempts. Please wait a moment before trying again.');
      } else {
        setError(msg);
      }
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

  return (
    <div className="min-h-screen flex items-center justify-center bg-background px-4">
      {/* Subtle background decoration */}
      <div
        className="pointer-events-none fixed inset-0 overflow-hidden"
        aria-hidden="true"
      >
        <div
          className="absolute -top-40 -right-40 h-96 w-96 rounded-full opacity-[0.04]"
          style={{
            background:
              'radial-gradient(circle, hsl(var(--primary)) 0%, transparent 70%)',
          }}
        />
        <div
          className="absolute -bottom-40 -left-40 h-96 w-96 rounded-full opacity-[0.04]"
          style={{
            background:
              'radial-gradient(circle, hsl(var(--primary)) 0%, transparent 70%)',
          }}
        />
      </div>

      <motion.div
        initial={{ opacity: 0, y: 16 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.35, ease: [0.4, 0, 0.2, 1] }}
        className="relative w-full max-w-sm"
      >
        {/* Card */}
        <div className="rounded-xl border bg-card shadow-lg p-8 space-y-6">
          {/* Logo + Title */}
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
              <h1 className="text-xl font-semibold text-foreground">Sign in</h1>
              <p className="text-sm text-muted-foreground mt-0.5">
                Welcome back to Kaizoku.NET
              </p>
            </div>
          </div>

          {/* Error message */}
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

          {/* Form */}
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="usernameOrEmail">Username or Email</Label>
              <Input
                id="usernameOrEmail"
                type="text"
                autoComplete="username"
                placeholder="Enter your username or email"
                value={usernameOrEmail}
                onChange={(e) => setUsernameOrEmail(e.target.value)}
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
                  autoComplete="current-password"
                  placeholder="Enter your password"
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
                  aria-label={showPassword ? 'Hide password' : 'Show password'}
                  tabIndex={-1}
                >
                  {showPassword ? (
                    <EyeOff className="h-4 w-4" />
                  ) : (
                    <Eye className="h-4 w-4" />
                  )}
                </button>
              </div>
            </div>

            <div className="flex items-center gap-2">
              <input
                id="rememberMe"
                type="checkbox"
                checked={rememberMe}
                onChange={(e) => setRememberMe(e.target.checked)}
                disabled={isSubmitting}
                className="h-4 w-4 rounded border-border accent-primary cursor-pointer"
              />
              <Label
                htmlFor="rememberMe"
                className="text-sm font-normal cursor-pointer select-none"
              >
                Remember me
              </Label>
            </div>

            <Button
              type="submit"
              className="w-full"
              disabled={isSubmitting || !usernameOrEmail.trim() || !password}
            >
              {isSubmitting ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Signing in...
                </>
              ) : (
                'Sign in'
              )}
            </Button>
          </form>

          {/* Register link */}
          <p className="text-center text-sm text-muted-foreground">
            Have an invite code?{' '}
            <Link
              href="/register"
              className="text-primary hover:underline font-medium transition-colors"
            >
              Create account
            </Link>
          </p>
        </div>
      </motion.div>
    </div>
  );
}
