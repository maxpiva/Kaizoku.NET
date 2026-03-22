"use client";

import React, { useState } from 'react';
import {
  Plus,
  Star,
  Trash2,
  Edit2,
  Loader2,
  Check,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import { Badge } from '@/components/ui/badge';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  usePermissionPresets,
  useCreatePreset,
  useUpdatePreset,
  useDeletePreset,
  useSetDefaultPreset,
  useClearDefaultPreset,
} from '@/lib/api/hooks/usePermissionPresets';
import { useToast } from '@/hooks/use-toast';
import type { PermissionPreset, UserPermissions } from '@/lib/api/auth-types';

const PERMISSION_FIELDS: { key: keyof UserPermissions; label: string; group: string }[] = [
  { key: 'canViewLibrary', label: 'View Library', group: 'Access' },
  { key: 'canBrowseSources', label: 'Browse Sources', group: 'Access' },
  { key: 'canViewQueue', label: 'View Queue', group: 'Access' },
  { key: 'canViewStatistics', label: 'View Statistics', group: 'Access' },
  { key: 'canViewNSFW', label: 'View NSFW', group: 'Content' },
  { key: 'canRequestSeries', label: 'Request Series', group: 'Content' },
  { key: 'canAddSeries', label: 'Add Series', group: 'Library' },
  { key: 'canEditSeries', label: 'Edit Series', group: 'Library' },
  { key: 'canDeleteSeries', label: 'Delete Series', group: 'Library' },
  { key: 'canManageDownloads', label: 'Manage Downloads', group: 'Management' },
  { key: 'canManageRequests', label: 'Manage Requests', group: 'Management' },
  { key: 'canManageJobs', label: 'Manage Jobs', group: 'Management' },
];

const BLANK_PERMS: UserPermissions = {
  canViewLibrary: false,
  canRequestSeries: false,
  canAddSeries: false,
  canEditSeries: false,
  canDeleteSeries: false,
  canManageDownloads: false,
  canViewQueue: false,
  canBrowseSources: false,
  canViewNSFW: false,
  canManageRequests: false,
  canManageJobs: false,
  canViewStatistics: false,
};

function PresetDialog({
  open,
  onOpenChange,
  editing,
}: {
  open: boolean;
  onOpenChange: (v: boolean) => void;
  editing: PermissionPreset | null;
}) {
  const createPreset = useCreatePreset();
  const updatePreset = useUpdatePreset();
  const { toast } = useToast();

  const [name, setName] = useState('');
  const [perms, setPerms] = useState<UserPermissions>(BLANK_PERMS);

  React.useEffect(() => {
    if (editing) {
      setName(editing.name);
      setPerms({ ...editing.permissions });
    } else {
      setName('');
      setPerms(BLANK_PERMS);
    }
  }, [editing, open]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;

    try {
      if (editing) {
        await updatePreset.mutateAsync({ id: editing.id, data: { name: name.trim(), permissions: perms } });
        toast({ title: 'Preset updated', variant: 'success' });
      } else {
        await createPreset.mutateAsync({ name: name.trim(), permissions: perms });
        toast({ title: 'Preset created', variant: 'success' });
      }
      onOpenChange(false);
    } catch (err) {
      toast({ title: 'Failed to save preset', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  const isPending = createPreset.isPending || updatePreset.isPending;

  // Group fields
  const groups = [...new Set(PERMISSION_FIELDS.map((f) => f.group))];

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg max-h-[85dvh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>{editing ? 'Edit Preset' : 'Create Permission Preset'}</DialogTitle>
          <DialogDescription>
            Define a set of permissions that can be applied to users.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-5 mt-2">
          <div className="space-y-1.5">
            <Label htmlFor="preset-name">Preset Name</Label>
            <Input
              id="preset-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Viewer, Editor, Moderator"
              required
            />
          </div>

          <div className="space-y-4">
            {groups.map((group) => (
              <div key={group}>
                <p className="text-xs font-semibold uppercase tracking-widest text-muted-foreground/60 mb-2">{group}</p>
                <div className="space-y-2">
                  {PERMISSION_FIELDS.filter((f) => f.group === group).map(({ key, label }) => (
                    <div key={key} className="flex items-center justify-between rounded-lg border bg-card px-3 py-2">
                      <span className="text-sm text-foreground">{label}</span>
                      <Switch
                        checked={!!perms[key as keyof typeof perms]}
                        onCheckedChange={(v) =>
                          setPerms((prev) => ({ ...prev, [key]: v }))
                        }
                      />
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>

          <div className="flex justify-end gap-2 pt-2 border-t">
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button type="submit" disabled={isPending || !name.trim()}>
              {isPending ? (
                <><Loader2 className="mr-2 h-4 w-4 animate-spin" />{editing ? 'Saving...' : 'Creating...'}</>
              ) : editing ? 'Save Preset' : 'Create Preset'}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}

export function PermissionsTab() {
  const { data: presets = [], isLoading } = usePermissionPresets();
  const deletePreset = useDeletePreset();
  const setDefault = useSetDefaultPreset();
  const clearDefault = useClearDefaultPreset();
  const { toast } = useToast();

  const [showDialog, setShowDialog] = useState(false);
  const [editingPreset, setEditingPreset] = useState<PermissionPreset | null>(null);

  const handleDelete = async (id: string, name: string) => {
    if (!window.confirm(`Delete preset "${name}"? This cannot be undone.`)) return;
    try {
      await deletePreset.mutateAsync(id);
      toast({ title: 'Preset deleted' });
    } catch (err) {
      toast({ title: 'Failed to delete preset', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  const handleToggleDefault = async (preset: PermissionPreset) => {
    try {
      if (preset.isDefault) {
        await clearDefault.mutateAsync();
        toast({ title: 'Default preset cleared', description: 'New users will get minimal permissions (View Library & Request only).' });
      } else {
        await setDefault.mutateAsync(preset.id);
        toast({ title: 'Default preset updated', description: `"${preset.name}" will be applied to new users.`, variant: 'success' });
      }
    } catch (err) {
      toast({ title: 'Failed to update default', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  const openCreate = () => {
    setEditingPreset(null);
    setShowDialog(true);
  };

  const openEdit = (preset: PermissionPreset) => {
    setEditingPreset(preset);
    setShowDialog(true);
  };

  const enabledCount = (preset: PermissionPreset) =>
    PERMISSION_FIELDS.filter(({ key }) => preset.permissions[key]).length;

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between flex-wrap gap-3">
        <div>
          <h2 className="text-lg font-semibold text-foreground">Permission Presets</h2>
          <p className="text-sm text-muted-foreground">
            Create permission presets to quickly assign roles to new users. Mark a preset as default to automatically apply it when users join via invite link.
          </p>
        </div>
        <Button size="sm" onClick={openCreate} className="gap-2">
          <Plus className="h-3.5 w-3.5" />
          New Preset
        </Button>
      </div>

      {isLoading ? (
        <div className="flex justify-center py-8">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : presets.length === 0 ? (
        <div className="text-center py-12 rounded-xl border border-dashed">
          <p className="text-sm text-muted-foreground">No permission presets yet.</p>
          <Button size="sm" variant="outline" onClick={openCreate} className="mt-3 gap-2">
            <Plus className="h-3.5 w-3.5" />
            Create first preset
          </Button>
        </div>
      ) : (
        <div className="space-y-3">
          {presets.map((preset) => (
            <div key={preset.id} className="rounded-xl border bg-card p-4 flex items-start gap-4">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h4 className="text-sm font-semibold text-foreground">{preset.name}</h4>
                  {preset.isDefault && (
                    <Badge variant="default" className="text-[9px] px-1.5 py-0 h-4 gap-1">
                      <Star className="h-2.5 w-2.5" />
                      Default
                    </Badge>
                  )}
                </div>
                <p className="text-xs text-muted-foreground mt-1">
                  {enabledCount(preset)}/{PERMISSION_FIELDS.length} permissions enabled
                </p>
                <div className="flex flex-wrap gap-1 mt-2">
                  {PERMISSION_FIELDS.filter(({ key }) => preset.permissions[key]).map(({ key, label }) => (
                    <span key={key} className="inline-flex items-center gap-0.5 text-[9px] bg-primary/10 text-primary rounded px-1.5 py-0.5 font-medium">
                      <Check className="h-2 w-2" />
                      {label}
                    </span>
                  ))}
                </div>
              </div>

              <div className="flex items-center gap-1 shrink-0">
                <button
                  onClick={() => handleToggleDefault(preset)}
                  className={`h-7 w-7 flex items-center justify-center rounded-md transition-colors ${
                    preset.isDefault
                      ? 'text-amber-500 hover:text-muted-foreground hover:bg-accent/50'
                      : 'text-muted-foreground hover:text-amber-500 hover:bg-amber-500/10'
                  }`}
                  aria-label={preset.isDefault ? 'Remove as default' : 'Set as default'}
                  title={preset.isDefault ? 'Remove as default' : 'Set as default for new users'}
                >
                  <Star className={`h-3.5 w-3.5 ${preset.isDefault ? 'fill-current' : ''}`} />
                </button>
                <button
                  onClick={() => openEdit(preset)}
                  className="h-7 w-7 flex items-center justify-center rounded-md text-muted-foreground hover:text-foreground hover:bg-accent/50 transition-colors"
                  aria-label="Edit preset"
                >
                  <Edit2 className="h-3.5 w-3.5" />
                </button>
                <button
                  onClick={() => handleDelete(preset.id, preset.name)}
                  className="h-7 w-7 flex items-center justify-center rounded-md text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors"
                  aria-label="Delete preset"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Default behavior info */}
      {!isLoading && (() => {
        const defaultPreset = presets.find((p) => p.isDefault);
        return (
          <div className="rounded-lg border bg-muted/30 px-4 py-3">
            <p className="text-xs font-medium text-foreground">New user defaults</p>
            <p className="text-[11px] text-muted-foreground mt-0.5">
              {defaultPreset
                ? <>Users joining via invite link (without a specific preset) will receive the <span className="font-medium text-foreground">{defaultPreset.name}</span> preset.</>
                : <>No default preset is set. New users joining via invite link will only receive <span className="font-medium text-foreground">View Library</span> and <span className="font-medium text-foreground">Request Series</span> permissions.</>}
            </p>
          </div>
        );
      })()}

      <PresetDialog
        open={showDialog}
        onOpenChange={(v) => {
          setShowDialog(v);
          if (!v) setEditingPreset(null);
        }}
        editing={editingPreset}
      />
    </div>
  );
}
