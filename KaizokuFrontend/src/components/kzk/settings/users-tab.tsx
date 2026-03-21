"use client";

import React, { useState } from 'react';
import {
  Users,
  Search,
  Plus,
  Link2,
  Trash2,
  ShieldOff,
  Shield,
  Key,
  Copy,
  Check,
  X,
  ChevronRight,
  Loader2,
} from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Switch } from '@/components/ui/switch';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useUsers, useUser, useCreateUser, useUpdateUser, useDeleteUser, useDisableUser, useEnableUser, useResetPassword, useUpdatePermissions } from '@/lib/api/hooks/useUsers';
import { useInvites, useCreateInvite, useRevokeInvite } from '@/lib/api/hooks/useInvites';
import { usePermissionPresets } from '@/lib/api/hooks/usePermissionPresets';
import { useToast } from '@/hooks/use-toast';
import type { User, UserPermissions } from '@/lib/api/auth-types';

const PERMISSION_LABELS: { key: keyof UserPermissions; label: string; description: string }[] = [
  { key: 'canViewLibrary', label: 'View Library', description: 'Access the manga library' },
  { key: 'canBrowseSources', label: 'Browse Sources', description: 'Browse cloud-latest / sources' },
  { key: 'canViewQueue', label: 'View Queue', description: 'Access the download queue' },
  { key: 'canRequestSeries', label: 'Request Series', description: 'Submit manga requests for admin approval' },
  { key: 'canAddSeries', label: 'Add Series', description: 'Add series directly to library without approval' },
  { key: 'canEditSeries', label: 'Edit Series', description: 'Edit existing series metadata' },
  { key: 'canDeleteSeries', label: 'Delete Series', description: 'Delete series from library' },
  { key: 'canManageDownloads', label: 'Manage Downloads', description: 'Control download queue' },
  { key: 'canViewNSFW', label: 'View NSFW', description: 'View adult content' },
  { key: 'canManageRequests', label: 'Manage Requests', description: 'Approve / deny requests' },
  { key: 'canManageJobs', label: 'Manage Jobs', description: 'Run and cancel background jobs' },
  { key: 'canViewStatistics', label: 'View Statistics', description: 'Access statistics dashboard' },
];

// ─── User Detail Panel ────────────────────────────────────────────────────────

function UserDetailPanel({ userId, onClose }: { userId: string; onClose: () => void }) {
  const { data: userDetail, isLoading } = useUser(userId);
  const updatePermissions = useUpdatePermissions();
  const resetPassword = useResetPassword();
  const disableUser = useDisableUser();
  const enableUser = useEnableUser();
  const deleteUser = useDeleteUser();
  const { data: presets = [] } = usePermissionPresets();
  const { toast } = useToast();

  const [newPassword, setNewPassword] = useState('');
  const [showResetPw, setShowResetPw] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [permissions, setPermissions] = useState<UserPermissions | null>(null);
  const [savingPerms, setSavingPerms] = useState(false);

  // Initialize permissions from loaded user
  React.useEffect(() => {
    if (userDetail?.permissions) {
      setPermissions(userDetail.permissions);
    }
  }, [userDetail]);

  if (isLoading || !userDetail) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  const handleSavePermissions = async () => {
    if (!permissions) return;
    setSavingPerms(true);
    try {
      await updatePermissions.mutateAsync({ id: userId, data: permissions });
      toast({ title: 'Permissions updated', variant: 'success' });
    } catch (err) {
      toast({ title: 'Failed to update permissions', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    } finally {
      setSavingPerms(false);
    }
  };

  const handleResetPassword = async () => {
    if (!newPassword || newPassword.length < 8) return;
    try {
      await resetPassword.mutateAsync({ id: userId, data: { newPassword } });
      setNewPassword('');
      setShowResetPw(false);
      toast({ title: 'Password reset successfully', variant: 'success' });
    } catch (err) {
      toast({ title: 'Failed to reset password', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  const handleToggleActive = async () => {
    try {
      if (userDetail.isActive) {
        await disableUser.mutateAsync(userId);
        toast({ title: `${userDetail.displayName} disabled` });
      } else {
        await enableUser.mutateAsync(userId);
        toast({ title: `${userDetail.displayName} enabled`, variant: 'success' });
      }
    } catch (err) {
      toast({ title: 'Action failed', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  const handleDelete = async () => {
    try {
      await deleteUser.mutateAsync(userId);
      toast({ title: `${userDetail.displayName} deleted` });
      onClose();
    } catch (err) {
      toast({ title: 'Failed to delete user', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  const applyPreset = (presetId: string) => {
    const preset = presets.find((p) => p.id === presetId);
    if (!preset) return;
    setPermissions({ ...preset.permissions });
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <h3 className="text-base font-semibold text-foreground">{userDetail.displayName}</h3>
          <p className="text-sm text-muted-foreground">@{userDetail.username} · {userDetail.email}</p>
          <div className="flex items-center gap-2 mt-1.5">
            <Badge variant={userDetail.role === 'Admin' ? 'default' : 'secondary'}>
              {userDetail.role}
            </Badge>
            <Badge variant={userDetail.isActive ? 'outline' : 'destructive'}>
              {userDetail.isActive ? 'Active' : 'Disabled'}
            </Badge>
          </div>
        </div>
        <button onClick={onClose} className="text-muted-foreground hover:text-foreground transition-colors">
          <X className="h-5 w-5" />
        </button>
      </div>

      {/* Permissions */}
      {userDetail.role !== 'Admin' && permissions && (
        <div className="space-y-3">
          <div className="flex items-center justify-between">
            <h4 className="text-sm font-semibold text-foreground">Permissions</h4>
            {presets.length > 0 && (
              <div className="flex items-center gap-2">
                <span className="text-xs text-muted-foreground">Apply preset:</span>
                <Select onValueChange={applyPreset}>
                  <SelectTrigger className="h-7 text-xs w-36">
                    <SelectValue placeholder="Choose preset" />
                  </SelectTrigger>
                  <SelectContent>
                    {presets.map((p) => (
                      <SelectItem key={p.id} value={p.id} className="text-xs">
                        {p.name}{p.isDefault ? ' (default)' : ''}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            {PERMISSION_LABELS.map(({ key, label, description }) => (
              <div key={key} className="flex items-center justify-between rounded-lg border bg-card px-3 py-2">
                <div className="min-w-0">
                  <p className="text-xs font-medium text-foreground">{label}</p>
                  <p className="text-[10px] text-muted-foreground truncate">{description}</p>
                </div>
                <Switch
                  checked={permissions[key]}
                  onCheckedChange={(v) =>
                    setPermissions((prev) => prev ? { ...prev, [key]: v } : prev)
                  }
                  className="ml-3 shrink-0"
                />
              </div>
            ))}
          </div>

          <Button onClick={handleSavePermissions} disabled={savingPerms} size="sm">
            {savingPerms ? (
              <><Loader2 className="mr-2 h-3.5 w-3.5 animate-spin" />Saving...</>
            ) : (
              'Save Permissions'
            )}
          </Button>
        </div>
      )}

      {userDetail.role === 'Admin' && (
        <div className="rounded-lg border bg-primary/5 border-primary/20 px-3 py-2.5 text-sm text-muted-foreground">
          Admin users have all permissions.
        </div>
      )}

      {/* Actions */}
      <div className="space-y-3 pt-2 border-t">
        <h4 className="text-sm font-semibold text-foreground">Actions</h4>

        {/* Reset Password */}
        <div className="space-y-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setShowResetPw((v) => !v)}
            className="gap-2"
          >
            <Key className="h-3.5 w-3.5" />
            Reset Password
          </Button>

          <AnimatePresence>
            {showResetPw && (
              <motion.div
                initial={{ opacity: 0, height: 0 }}
                animate={{ opacity: 1, height: 'auto' }}
                exit={{ opacity: 0, height: 0 }}
                className="flex gap-2"
              >
                <Input
                  type="password"
                  placeholder="New password (8+ chars)"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  className="h-8 text-sm max-w-xs"
                />
                <Button
                  size="sm"
                  onClick={handleResetPassword}
                  disabled={!newPassword || newPassword.length < 8}
                >
                  Set
                </Button>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        {/* Disable / Enable */}
        <Button
          variant="outline"
          size="sm"
          onClick={handleToggleActive}
          className="gap-2"
        >
          {userDetail.isActive ? (
            <><ShieldOff className="h-3.5 w-3.5" />Disable User</>
          ) : (
            <><Shield className="h-3.5 w-3.5" />Enable User</>
          )}
        </Button>

        {/* Delete */}
        {!showDeleteConfirm ? (
          <Button
            variant="outline"
            size="sm"
            onClick={() => setShowDeleteConfirm(true)}
            className="gap-2 text-destructive hover:text-destructive border-destructive/30 hover:border-destructive/50"
          >
            <Trash2 className="h-3.5 w-3.5" />
            Delete User
          </Button>
        ) : (
          <div className="flex items-center gap-2">
            <p className="text-xs text-destructive">Are you sure? This cannot be undone.</p>
            <Button size="sm" variant="destructive" onClick={handleDelete} className="h-7 text-xs">
              Delete
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setShowDeleteConfirm(false)} className="h-7 text-xs">
              Cancel
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Create User Dialog ───────────────────────────────────────────────────────

function CreateUserDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (v: boolean) => void }) {
  const { data: presets = [] } = usePermissionPresets();
  const createUser = useCreateUser();
  const { toast } = useToast();

  const [form, setForm] = useState({
    username: '',
    email: '',
    displayName: '',
    password: '',
    permissionPresetId: '',
  });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      await createUser.mutateAsync({
        username: form.username.trim(),
        email: form.email.trim(),
        displayName: form.displayName.trim(),
        password: form.password,
        role: 'User',
        permissionPresetId: form.permissionPresetId || undefined,
      });
      toast({ title: 'User created', variant: 'success' });
      onOpenChange(false);
      setForm({ username: '', email: '', displayName: '', password: '', permissionPresetId: '' });
    } catch (err) {
      toast({ title: 'Failed to create user', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Create User</DialogTitle>
          <DialogDescription>Add a new user account to the system.</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4 mt-2">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="cu-username">Username</Label>
              <Input id="cu-username" value={form.username} onChange={(e) => setForm((f) => ({ ...f, username: e.target.value }))} required />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="cu-display-name">Display Name</Label>
              <Input id="cu-display-name" value={form.displayName} onChange={(e) => setForm((f) => ({ ...f, displayName: e.target.value }))} required />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="cu-email">Email</Label>
            <Input id="cu-email" type="email" value={form.email} onChange={(e) => setForm((f) => ({ ...f, email: e.target.value }))} required />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="cu-password">Password</Label>
            <Input id="cu-password" type="password" value={form.password} onChange={(e) => setForm((f) => ({ ...f, password: e.target.value }))} placeholder="Min. 8 characters" required />
          </div>

          {presets.length > 0 && (
            <div className="space-y-1.5">
              <Label>Permission Preset</Label>
              <Select value={form.permissionPresetId} onValueChange={(v) => setForm((f) => ({ ...f, permissionPresetId: v }))}>
                <SelectTrigger><SelectValue placeholder="None" /></SelectTrigger>
                <SelectContent>
                  {presets.map((p) => (
                    <SelectItem key={p.id} value={p.id}>
                      {p.name}{p.isDefault ? ' (default)' : ''}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={createUser.isPending}>
              {createUser.isPending ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Creating...</> : 'Create User'}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ─── Create Invite Dialog ─────────────────────────────────────────────────────

function getInviteUrl(code: string): string {
  const origin = typeof window !== 'undefined' ? window.location.origin : '';
  return `${origin}/register?invite=${code}`;
}

function CreateInviteDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (v: boolean) => void }) {
  const { data: presets = [] } = usePermissionPresets();
  const createInvite = useCreateInvite();
  const { toast } = useToast();
  const [expiresInDays, setExpiresInDays] = useState('7');
  const [maxUses, setMaxUses] = useState('1');
  const [presetId, setPresetId] = useState('');

  const handleCreate = async () => {
    try {
      const invite = await createInvite.mutateAsync({
        expiresInDays: parseInt(expiresInDays),
        maxUses: parseInt(maxUses),
        permissionPresetId: presetId || undefined,
      });
      const inviteUrl = getInviteUrl(invite.code);
      void navigator.clipboard.writeText(inviteUrl);
      toast({ title: 'Invite link created & copied to clipboard', description: inviteUrl });
      handleClose();
    } catch (err) {
      toast({ title: 'Failed to create invite', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  const handleClose = () => {
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Generate Invite Link</DialogTitle>
          <DialogDescription>Create a one-time invite link to share with new users.</DialogDescription>
        </DialogHeader>

        <div className="space-y-4 mt-2">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label>Expires In</Label>
              <Select value={expiresInDays} onValueChange={setExpiresInDays}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="1">1 day</SelectItem>
                  <SelectItem value="3">3 days</SelectItem>
                  <SelectItem value="7">7 days</SelectItem>
                  <SelectItem value="30">30 days</SelectItem>
                  <SelectItem value="365">1 year</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label>Max Uses</Label>
              <Select value={maxUses} onValueChange={setMaxUses}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="1">1 use</SelectItem>
                  <SelectItem value="5">5 uses</SelectItem>
                  <SelectItem value="10">10 uses</SelectItem>
                  <SelectItem value="25">25 uses</SelectItem>
                  <SelectItem value="0">Unlimited</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          {presets.length > 0 && (() => {
            const defaultPreset = presets.find((p) => p.isDefault);
            return (
              <div className="space-y-1.5">
                <Label>Permission Preset</Label>
                <Select value={presetId} onValueChange={setPresetId}>
                  <SelectTrigger>
                    <SelectValue placeholder={defaultPreset ? `Default: ${defaultPreset.name}` : 'View Library & Request only'} />
                  </SelectTrigger>
                  <SelectContent>
                    {presets.map((p) => (
                      <SelectItem key={p.id} value={p.id}>
                        {p.name}{p.isDefault ? ' (default)' : ''}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-[10px] text-muted-foreground">
                  {defaultPreset
                    ? `No selection uses the default preset "${defaultPreset.name}".`
                    : 'No default preset set. New users will only get View Library and Request Series permissions.'}
                </p>
              </div>
            );
          })()}

          <div className="flex justify-end gap-2 pt-2">
            <Button variant="ghost" onClick={handleClose}>Cancel</Button>
            <Button onClick={handleCreate} disabled={createInvite.isPending}>
              {createInvite.isPending ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Generating...</> : <><Link2 className="mr-2 h-4 w-4" />Generate Link</>}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

// ─── Main Users Tab ───────────────────────────────────────────────────────────

export function UsersTab() {
  const { data: users = [], isLoading } = useUsers();
  const { data: invites = [] } = useInvites();
  const revokeInvite = useRevokeInvite();
  const { toast } = useToast();

  const [searchTerm, setSearchTerm] = useState('');
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
  const [showCreateUser, setShowCreateUser] = useState(false);
  const [showCreateInvite, setShowCreateInvite] = useState(false);
  const [copiedInviteId, setCopiedInviteId] = useState<string | null>(null);

  const filteredUsers = users.filter((u) => {
    const term = searchTerm.toLowerCase();
    return (
      u.username.toLowerCase().includes(term) ||
      u.displayName.toLowerCase().includes(term) ||
      u.email.toLowerCase().includes(term)
    );
  });

  const handleCopyInvite = (url: string, id: string) => {
    void navigator.clipboard.writeText(url);
    setCopiedInviteId(id);
    setTimeout(() => setCopiedInviteId(null), 2000);
  };

  const handleRevokeInvite = async (id: string) => {
    try {
      await revokeInvite.mutateAsync(id);
      toast({ title: 'Invite revoked' });
    } catch {
      toast({ title: 'Failed to revoke invite', variant: 'destructive' });
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between flex-wrap gap-3">
        <div>
          <h2 className="text-lg font-semibold text-foreground">Users</h2>
          <p className="text-sm text-muted-foreground">Manage user accounts and access.</p>
        </div>
        <div className="flex gap-2">
          <Button size="sm" variant="outline" onClick={() => setShowCreateInvite(true)} className="gap-2">
            <Link2 className="h-3.5 w-3.5" />
            Invite Link
          </Button>
          <Button size="sm" onClick={() => setShowCreateUser(true)} className="gap-2">
            <Plus className="h-3.5 w-3.5" />
            Create User
          </Button>
        </div>
      </div>

      {/* Search */}
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
        <Input
          placeholder="Search users..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          className="pl-9"
        />
      </div>

      {/* User list with inline detail panel */}
      <div className="space-y-1">
        {isLoading ? (
          <div className="flex justify-center py-8">
            <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
          </div>
        ) : filteredUsers.length === 0 ? (
          <p className="text-sm text-muted-foreground py-8 text-center">
            {searchTerm ? 'No users match your search.' : 'No users yet.'}
          </p>
        ) : (
          filteredUsers.map((u) => (
            <React.Fragment key={u.id}>
              <UserRow
                user={u}
                isSelected={selectedUserId === u.id}
                onClick={() => setSelectedUserId(selectedUserId === u.id ? null : u.id)}
              />
              <AnimatePresence>
                {selectedUserId === u.id && (
                  <motion.div
                    initial={{ opacity: 0, height: 0 }}
                    animate={{ opacity: 1, height: 'auto' }}
                    exit={{ opacity: 0, height: 0 }}
                    transition={{ duration: 0.2, ease: 'easeInOut' }}
                    className="overflow-hidden"
                  >
                    <div className="rounded-xl border bg-card p-4 mt-1 mb-2">
                      <UserDetailPanel
                        userId={u.id}
                        onClose={() => setSelectedUserId(null)}
                      />
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>
            </React.Fragment>
          ))
        )}
      </div>

      {/* Active invite links */}
      {invites.length > 0 && (
        <div className="space-y-3">
          <h3 className="text-sm font-semibold text-foreground border-b pb-2">Active Invite Links</h3>
          <div className="space-y-2">
            {invites.map((invite) => (
              <div
                key={invite.id}
                className="flex items-center gap-3 rounded-lg border bg-card px-3 py-2.5"
              >
                <div className="flex-1 min-w-0">
                  <p className="text-xs font-mono text-foreground truncate">{invite.code}</p>
                  <p className="text-[10px] text-muted-foreground mt-0.5">
                    By {invite.createdByUsername} · {invite.usedCount}/{invite.maxUses || '∞'} uses
                    {invite.permissionPresetName ? ` · ${invite.permissionPresetName}` : ''}
                    {' · Expires '}{new Date(invite.expiresAt).toLocaleDateString()}
                  </p>
                </div>
                <div className="flex items-center gap-1.5 shrink-0">
                  <button
                    onClick={() => handleCopyInvite(getInviteUrl(invite.code), invite.id)}
                    className="h-7 w-7 flex items-center justify-center rounded-md text-muted-foreground hover:text-foreground hover:bg-accent/50 transition-colors"
                    aria-label="Copy invite link"
                  >
                    {copiedInviteId === invite.id ? (
                      <Check className="h-3.5 w-3.5 text-emerald-500" />
                    ) : (
                      <Copy className="h-3.5 w-3.5" />
                    )}
                  </button>
                  <button
                    onClick={() => handleRevokeInvite(invite.id)}
                    className="h-7 w-7 flex items-center justify-center rounded-md text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors"
                    aria-label="Revoke invite"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <CreateUserDialog open={showCreateUser} onOpenChange={setShowCreateUser} />
      <CreateInviteDialog open={showCreateInvite} onOpenChange={setShowCreateInvite} />
    </div>
  );
}

function UserRow({ user, isSelected, onClick }: { user: User; isSelected: boolean; onClick: () => void }) {
  const initials = user.displayName
    ? user.displayName.split(' ').map((w) => w[0]).join('').toUpperCase().slice(0, 2)
    : user.username.slice(0, 2).toUpperCase();

  return (
    <button
      onClick={onClick}
      className={`w-full flex items-center gap-3 rounded-lg px-3 py-2.5 text-left transition-all ${
        isSelected
          ? 'bg-primary/10 border border-primary/30'
          : 'bg-card border border-transparent hover:border-border'
      }`}
    >
      <div className="h-8 w-8 rounded-full bg-primary/20 border border-primary/30 flex items-center justify-center shrink-0">
        <span className="text-xs font-semibold text-primary">{initials}</span>
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <p className="text-sm font-medium text-foreground truncate">{user.displayName}</p>
          <Badge variant={user.role === 'Admin' ? 'default' : 'outline'} className="text-[9px] px-1.5 py-0 h-4 shrink-0">
            {user.role}
          </Badge>
          {!user.isActive && (
            <Badge variant="destructive" className="text-[9px] px-1.5 py-0 h-4 shrink-0">
              Disabled
            </Badge>
          )}
        </div>
        <p className="text-xs text-muted-foreground truncate">@{user.username} · {user.email}</p>
      </div>
      <ChevronRight className={`h-4 w-4 text-muted-foreground shrink-0 transition-transform ${isSelected ? 'rotate-90' : ''}`} />
    </button>
  );
}
