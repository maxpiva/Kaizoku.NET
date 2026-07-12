'use client';

import React, { useState, useEffect, useRef } from 'react';
import { useClaimUser, useCreateFirstUser } from '@/lib/api/hooks/useAuth';
import { useAuth } from '@/contexts/auth-context';
import { UserLevel } from '@/lib/api/types';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { UserIcon, Check, Loader2 } from 'lucide-react';

interface IdentifyUserStepProps {
  setError: (error: string | null) => void;
  setIsLoading: (loading: boolean) => void;
  setCanProgress: (canProgress: boolean) => void;
  /** List of usernames that were auto-created during import */
  autoCreatedUsers: string[];
}

export function IdentifyUserStep({
  setError,
  setIsLoading,
  setCanProgress,
  autoCreatedUsers,
}: IdentifyUserStepProps) {
  const { availableUsers, refreshAuth } = useAuth();
  const claimUser = useClaimUser();
  const createFirstUser = useCreateFirstUser();
  const [claimed, setClaimed] = useState(false);
  const [selectedUsername, setSelectedUsername] = useState('');
  const [localError, setLocalError] = useState('');
  const [refreshing, setRefreshing] = useState(true);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [newUsername, setNewUsername] = useState('');
  const [created, setCreated] = useState(false);
  const hasRefreshedRef = useRef(false);

  // Refresh auth context on mount so availableUsers is up-to-date
  // (users may have been created during the previous step)
  useEffect(() => {
    if (!hasRefreshedRef.current) {
      hasRefreshedRef.current = true;
      setRefreshing(true);
      refreshAuth().finally(() => {
        setRefreshing(false);
      });
    }
  }, [refreshAuth]);

  // All users from the database (after refresh)
  const allUsers = availableUsers ?? [];

  // Detect if any user already has Owner level — this covers the case where the
  // user re-navigates to this step after already creating/claiming an owner,
  // or where an owner was established earlier in the wizard flow.
  // `allUsers` includes the full `User` object with `level` (see AuthController.GetStatus).
  const existingOwner = allUsers.find((u) => u.level >= UserLevel.Owner);

  // Sync local state when an existing owner is detected from server data
  // so that canProgress is set and the UI transitions to the completed state
  // without showing the claim/create UI again.
  useEffect(() => {
    if (existingOwner && !claimed && !created) {
      setSelectedUsername(existingOwner.username);
      setCanProgress(true);
    }
  }, [existingOwner, claimed, created, setCanProgress]);

  // Prioritise showing auto-created users first, then all other available users
  const autoCreatedMatch = allUsers.filter(
    (u) => autoCreatedUsers.includes(u.username)
  );
  const otherUsers = allUsers.filter(
    (u) => !autoCreatedUsers.includes(u.username)
  );
  const displayUsers = autoCreatedMatch.length > 0
    ? [...autoCreatedMatch, ...otherUsers]
    : allUsers;

  const handleClaim = async (userId: string, username: string) => {
    setLocalError('');
    setError(null);
    setIsLoading(true);
    setSelectedUsername(username);

    try {
      await claimUser.mutateAsync(userId);
      await refreshAuth();
      setClaimed(true);
      setCanProgress(true);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to claim user';
      setLocalError(msg);
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  };

  const handleCreateOwner = async (e: React.FormEvent) => {
    e.preventDefault();
    setLocalError('');
    setError(null);
    setIsLoading(true);

    if (newUsername.length < 3 || newUsername.length > 32) {
      setLocalError('Username must be between 3 and 32 characters');
      setIsLoading(false);
      return;
    }

    try {
      await createFirstUser.mutateAsync({ username: newUsername });
      await refreshAuth();
      setCreated(true);
      setSelectedUsername(newUsername);
      setCanProgress(true);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to create owner user';
      setLocalError(msg);
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  };

  // Show loading spinner while refreshing available users
  if (refreshing) {
    return (
      <Card className="mt-12">
        <CardContent className="flex items-center justify-center py-8">
          <div className="flex items-center gap-3">
            <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
            <span className="text-sm text-muted-foreground">Loading users...</span>
          </div>
        </CardContent>
      </Card>
    );
  }

  // Completed state: user has claimed, created, or an existing owner was detected
  const isComplete = claimed || created || !!existingOwner;
  const displayUsername = existingOwner?.username ?? selectedUsername;

  if (isComplete) {
    return (
      <Card className="mt-12">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Check className="h-5 w-5 text-green-500" />
            User Identified
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="bg-secondary border border-green-200 rounded-lg p-4">
            <p className="text-sm">
              You are now identified as <strong>{displayUsername}</strong>!
            </p>
            <p className="text-sm text-muted-foreground mt-2">
              You have been promoted to owner. No password was set - authentication
              is disabled by default. You can enable authentication later in Settings.
            </p>
          </div>
        </CardContent>
      </Card>
    );
  }

  // No users exist at all — offer to create the owner user
  if (allUsers.length === 0 || showCreateForm) {
    return (
      <Card className="mt-12">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <UserIcon className="h-5 w-5" />
            Create Owner User
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="text-sm text-muted-foreground mb-4">
            No users were found in the system. Create your owner account to get started.
            You can set a password later when you enable authentication.
          </div>

          {localError && (
            <div className="p-3 mb-4 text-sm text-red-500 bg-red-50 dark:bg-red-950 rounded-md">
              {localError}
            </div>
          )}

          <form onSubmit={handleCreateOwner} className="space-y-4 max-w-md">
            <div className="ml-1 space-y-2">
              <Label htmlFor="identify-owner-username">Owner Username</Label>
              <Input
                id="identify-owner-username"
                value={newUsername}
                onChange={(e) => setNewUsername(e.target.value)}
                placeholder="Enter your username"
                required
                minLength={3}
                maxLength={32}
                autoFocus
                className="focus-visible:ring-1 focus-visible:ring-offset-2"
              />
            </div>

            <Button type="submit" disabled={createFirstUser.isPending} className="flex ml-1 items-center gap-2">
              <UserIcon className="h-4 w-4" />
              {createFirstUser.isPending ? 'Creating...' : 'Create Owner User'}
            </Button>
          </form>
        </CardContent>
      </Card>
    );
  }

  // Users exist but no owner has been established — show the list of users to select/claim
  return (
    <div className="space-y-4">
      <div className="text-sm text-muted-foreground pt-12">
        {autoCreatedMatch.length > 0
          ? 'Users were found from your imported series data. Select which user you are to be promoted to owner.'
          : 'Existing users were found in the system. Select which user you are to be promoted to owner.'}
      </div>

      {localError && (
        <div className="p-3 text-sm text-red-500 bg-red-50 dark:bg-red-950 rounded-md">
          {localError}
        </div>
      )}

      <div className="space-y-2">
        {displayUsers.map((u) => (
          <Button
            key={u.id}
            variant="outline"
            className="w-full justify-start gap-3 h-14"
            onClick={() => handleClaim(u.id, u.username)}
            disabled={claimUser.isPending}
          >
            <div className="w-8 h-8 rounded-full bg-muted flex items-center justify-center overflow-hidden">
              {u.avatarBase64 ? (
                <img
                  src={`data:${u.avatarContentType || 'image/png'};base64,${u.avatarBase64}`}
                  alt={u.username}
                  className="w-full h-full object-cover"
                />
              ) : (
                <UserIcon className="w-4 h-4" />
              )}
            </div>
            <span className="font-medium">{u.username}</span>
          </Button>
        ))}
      </div>
    </div>
  );
}