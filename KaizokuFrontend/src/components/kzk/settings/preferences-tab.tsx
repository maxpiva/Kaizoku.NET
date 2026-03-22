"use client";

import React, { useState, useEffect } from 'react';
import { Loader2, Save, Sun, Moon, Monitor } from 'lucide-react';
import { useTheme } from 'next-themes';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import { useAuth } from '@/contexts/auth-context';
import { useUserPreferences, useUpdatePreferences, useUpdateProfile, useChangePassword } from '@/lib/api/hooks/useUsers';
import { useToast } from '@/hooks/use-toast';
import { Input } from '@/components/ui/input';
import { Eye, EyeOff } from 'lucide-react';

export function PreferencesTab() {
  const { user, updateUser } = useAuth();
  const { data: preferences, isLoading } = useUserPreferences();
  const updatePrefs = useUpdatePreferences();
  const updateProfile = useUpdateProfile();
  const changePassword = useChangePassword();
  const { setTheme } = useTheme();
  const { toast } = useToast();

  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showCurrentPw, setShowCurrentPw] = useState(false);
  const [showNewPw, setShowNewPw] = useState(false);
  const [selectedTheme, setSelectedTheme] = useState<string>('system');
  const [savingProfile, setSavingProfile] = useState(false);
  const [savingPassword, setSavingPassword] = useState(false);
  const [savingPrefs, setSavingPrefs] = useState(false);

  useEffect(() => {
    if (user) setDisplayName(user.displayName);
  }, [user]);

  useEffect(() => {
    if (preferences?.theme) setSelectedTheme(preferences.theme);
  }, [preferences]);

  const handleSaveProfile = async () => {
    if (!displayName.trim()) return;
    setSavingProfile(true);
    try {
      const updated = await updateProfile.mutateAsync({ displayName: displayName.trim() });
      updateUser(updated);
      toast({ title: 'Profile updated', variant: 'success' });
    } catch (err) {
      toast({ title: 'Failed to update profile', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    } finally {
      setSavingProfile(false);
    }
  };

  const handleChangePassword = async () => {
    if (!currentPassword || !newPassword || newPassword !== confirmPassword) return;
    setSavingPassword(true);
    try {
      await changePassword.mutateAsync({ currentPassword, newPassword });
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      toast({ title: 'Password changed', variant: 'success' });
    } catch (err) {
      toast({ title: 'Failed to change password', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    } finally {
      setSavingPassword(false);
    }
  };

  const handleSaveTheme = async () => {
    setSavingPrefs(true);
    try {
      setTheme(selectedTheme);
      await updatePrefs.mutateAsync({ theme: selectedTheme });
      toast({ title: 'Preferences saved', variant: 'success' });
    } catch (err) {
      toast({ title: 'Failed to save preferences', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    } finally {
      setSavingPrefs(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  const themes = [
    { value: 'light', label: 'Light', Icon: Sun },
    { value: 'dark', label: 'Dark', Icon: Moon },
    { value: 'system', label: 'System', Icon: Monitor },
  ];

  const passwordsMatch = newPassword === confirmPassword;

  return (
    <div className="space-y-8">
      <div>
        <h2 className="text-lg font-semibold text-foreground">Preferences</h2>
        <p className="text-sm text-muted-foreground">Manage your profile and application preferences.</p>
      </div>

      {/* Profile section */}
      <section className="space-y-4">
        <h3 className="text-sm font-semibold text-foreground border-b pb-2">Profile</h3>

        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="pref-username">Username</Label>
            <Input id="pref-username" value={user?.username ?? ''} readOnly className="bg-muted" />
            <p className="text-xs text-muted-foreground">Username cannot be changed.</p>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="pref-email">Email</Label>
            <Input id="pref-email" value={user?.email ?? ''} readOnly className="bg-muted" />
          </div>
        </div>

        <div className="space-y-1.5 max-w-sm">
          <Label htmlFor="pref-display-name">Display Name</Label>
          <Input
            id="pref-display-name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
          />
        </div>

        <div>
          <Button
            onClick={handleSaveProfile}
            disabled={savingProfile || !displayName.trim() || displayName.trim() === user?.displayName}
            size="sm"
          >
            {savingProfile ? (
              <><Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" />Saving...</>
            ) : (
              <><Save className="mr-2 h-3.5 w-3.5" />Save Profile</>
            )}
          </Button>
        </div>
      </section>

      {/* Theme section */}
      <section className="space-y-4">
        <h3 className="text-sm font-semibold text-foreground border-b pb-2">Theme</h3>
        <div className="flex gap-2 flex-wrap">
          {themes.map(({ value, label, Icon }) => (
            <button
              key={value}
              onClick={() => setSelectedTheme(value)}
              className={`flex items-center gap-2 rounded-lg border px-4 py-2.5 text-sm font-medium transition-all ${
                selectedTheme === value
                  ? 'border-primary bg-primary/10 text-primary'
                  : 'border-border bg-card text-muted-foreground hover:text-foreground hover:border-border'
              }`}
            >
              <Icon className="h-4 w-4" />
              {label}
            </button>
          ))}
        </div>
        <Button
          onClick={handleSaveTheme}
          disabled={savingPrefs}
          size="sm"
        >
          {savingPrefs ? (
            <><Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" />Saving...</>
          ) : (
            <><Save className="mr-2 h-3.5 w-3.5" />Save Preferences</>
          )}
        </Button>
      </section>

      {/* Change Password section */}
      <section className="space-y-4">
        <h3 className="text-sm font-semibold text-foreground border-b pb-2">Change Password</h3>

        <div className="space-y-3 max-w-sm">
          <div className="space-y-1.5">
            <Label htmlFor="current-password">Current Password</Label>
            <div className="relative">
              <Input
                id="current-password"
                type={showCurrentPw ? 'text' : 'password'}
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                className="pr-10"
                placeholder="Enter current password"
              />
              <button
                type="button"
                onClick={() => setShowCurrentPw((v) => !v)}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                tabIndex={-1}
              >
                {showCurrentPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="new-password">New Password</Label>
            <div className="relative">
              <Input
                id="new-password"
                type={showNewPw ? 'text' : 'password'}
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                className="pr-10"
                placeholder="Enter new password (8+ chars)"
              />
              <button
                type="button"
                onClick={() => setShowNewPw((v) => !v)}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                tabIndex={-1}
              >
                {showNewPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="confirm-new-password">Confirm New Password</Label>
            <Input
              id="confirm-new-password"
              type="password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              placeholder="Repeat new password"
              className={confirmPassword && !passwordsMatch ? 'border-destructive' : ''}
            />
            {confirmPassword && !passwordsMatch && (
              <p className="text-xs text-destructive">Passwords do not match.</p>
            )}
          </div>
        </div>

        <Button
          onClick={handleChangePassword}
          disabled={
            savingPassword ||
            !currentPassword ||
            !newPassword ||
            newPassword.length < 8 ||
            !passwordsMatch
          }
          size="sm"
          variant="outline"
        >
          {savingPassword ? (
            <><Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" />Changing...</>
          ) : (
            'Change Password'
          )}
        </Button>
      </section>
    </div>
  );
}
