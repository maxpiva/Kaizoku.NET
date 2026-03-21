"use client";

import React, { useState } from 'react';
import {
  Check,
  X,
  Clock,
  CheckCircle2,
  XCircle,
  AlertCircle,
  Loader2,
  BookOpen,
  Filter,
} from 'lucide-react';
import Image from 'next/image';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
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
import { useRequests, useDenyRequest, useCancelRequest } from '@/lib/api/hooks/useRequests';
import { useToast } from '@/hooks/use-toast';
import { useIsAdmin } from '@/hooks/use-permission';
import { useAuth } from '@/contexts/auth-context';
import type { MangaRequest } from '@/lib/api/auth-types';
import type { LinkedSeries } from '@/lib/api/types';
import { formatThumbnailUrl } from '@/lib/utils/thumbnail';
import { AddSeries } from '@/components/kzk/series/add-series';

type StatusFilter = 'all' | 'Pending' | 'Approved' | 'Denied' | 'Cancelled';

const STATUS_CONFIG: Record<MangaRequest['status'], { label: string; icon: React.FC<{ className?: string }>; badgeClass: string }> = {
  Pending: { label: 'Pending', icon: Clock, badgeClass: 'bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-500/20' },
  Approved: { label: 'Approved', icon: CheckCircle2, badgeClass: 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/20' },
  Denied: { label: 'Denied', icon: XCircle, badgeClass: 'bg-destructive/10 text-destructive border-destructive/20' },
  Cancelled: { label: 'Cancelled', icon: AlertCircle, badgeClass: 'bg-muted/60 text-muted-foreground border-border' },
};

function DenyDialog({ open, requestId, onOpenChange }: { open: boolean; requestId: string; onOpenChange: (v: boolean) => void }) {
  const denyRequest = useDenyRequest();
  const { toast } = useToast();
  const [note, setNote] = useState('');

  const handleDeny = async () => {
    try {
      await denyRequest.mutateAsync({ id: requestId, data: { reviewNote: note || undefined } });
      toast({ title: 'Request denied' });
      setNote('');
      onOpenChange(false);
    } catch (err) {
      toast({ title: 'Failed to deny request', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Deny Request</DialogTitle>
          <DialogDescription>Optionally provide a reason for denying this request.</DialogDescription>
        </DialogHeader>
        <div className="space-y-4 mt-2">
          <div className="space-y-1.5">
            <Label htmlFor="deny-note">Note (optional)</Label>
            <Textarea
              id="deny-note"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Reason for denying..."
              rows={3}
            />
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button variant="destructive" onClick={handleDeny} disabled={denyRequest.isPending}>
              {denyRequest.isPending ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Denying...</> : 'Deny Request'}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function RequestCard({ request, isAdmin, currentUserId }: { request: MangaRequest; isAdmin: boolean; currentUserId: string }) {
  const cancelRequest = useCancelRequest();
  const { toast } = useToast();
  const [showDenyDialog, setShowDenyDialog] = useState(false);
  const [showApproveDialog, setShowApproveDialog] = useState(false);

  const statusConfig = STATUS_CONFIG[request.status];
  const StatusIcon = statusConfig.icon;

  const canApprove = isAdmin && request.status === 'Pending';
  const canDeny = isAdmin && request.status === 'Pending';
  const canCancel = request.requestedByUserId === currentUserId && request.status === 'Pending';

  // Parse the stored LinkedSeries[] from providerData
  const preloadedLinkedSeries = React.useMemo((): LinkedSeries[] | undefined => {
    if (!request.providerData) return undefined;
    try {
      const parsed = JSON.parse(request.providerData);
      if (Array.isArray(parsed) && parsed.length > 0 && (parsed[0] as Record<string, unknown>).title) {
        return parsed as LinkedSeries[];
      }
    } catch {
      // providerData may be in the old format — can't approve with configure
    }
    return undefined;
  }, [request.providerData]);

  const handleCancel = async () => {
    try {
      await cancelRequest.mutateAsync(request.id);
      toast({ title: 'Request cancelled' });
    } catch (err) {
      toast({ title: 'Failed to cancel', description: err instanceof Error ? err.message : undefined, variant: 'destructive' });
    }
  };

  return (
    <>
      <div className={`rounded-xl border bg-card overflow-hidden transition-all ${
        request.status === 'Pending' ? 'border-amber-500/30 bg-amber-500/5' : ''
      }`}>
        <div className="flex gap-3 p-4">
          {/* Thumbnail */}
          <div className="w-12 h-16 rounded-md overflow-hidden bg-muted shrink-0 relative">
            {request.thumbnailUrl ? (
              <Image
                src={formatThumbnailUrl(request.thumbnailUrl)}
                alt={request.title}
                fill
                className="object-cover"
                onError={(e) => {
                  const t = e.target as HTMLImageElement;
                  t.style.display = 'none';
                }}
              />
            ) : (
              <div className="h-full w-full flex items-center justify-center">
                <BookOpen className="h-5 w-5 text-muted-foreground" />
              </div>
            )}
          </div>

          {/* Content */}
          <div className="flex-1 min-w-0 space-y-1">
            <div className="flex items-start justify-between gap-2">
              <h4 className="text-sm font-semibold text-foreground leading-tight">{request.title}</h4>
              <span className={`inline-flex items-center gap-1 text-[10px] font-medium px-1.5 py-0.5 rounded-full border shrink-0 ${statusConfig.badgeClass}`}>
                <StatusIcon className="h-2.5 w-2.5" />
                {statusConfig.label}
              </span>
            </div>

            {request.description && (
              <p className="text-xs text-muted-foreground line-clamp-2">{request.description}</p>
            )}

            <p className="text-[10px] text-muted-foreground">
              Requested by {request.requestedByUsername} · {new Date(request.createdAt).toLocaleDateString()}
              {request.reviewedByUsername && ` · Reviewed by ${request.reviewedByUsername}`}
            </p>

            {request.reviewNote && (
              <div className="rounded-md bg-muted/50 border px-2.5 py-1.5 mt-1">
                <p className="text-xs text-muted-foreground italic">"{request.reviewNote}"</p>
              </div>
            )}
          </div>
        </div>

        {/* Actions */}
        {(canApprove || canDeny || canCancel) && (
          <div className="border-t px-4 py-2.5 flex items-center gap-2 bg-background/50">
            {canApprove && preloadedLinkedSeries && (
              <Button
                size="sm"
                onClick={() => setShowApproveDialog(true)}
                className="h-7 text-xs gap-1.5"
              >
                <Check className="h-3 w-3" />
                Approve
              </Button>
            )}
            {canDeny && (
              <Button
                size="sm"
                variant="outline"
                onClick={() => setShowDenyDialog(true)}
                className="h-7 text-xs gap-1.5 text-destructive border-destructive/30 hover:bg-destructive/10"
              >
                <X className="h-3 w-3" />
                Deny
              </Button>
            )}
            {canCancel && (
              <Button
                size="sm"
                variant="ghost"
                onClick={handleCancel}
                disabled={cancelRequest.isPending}
                className="h-7 text-xs gap-1.5 text-muted-foreground"
              >
                Cancel
              </Button>
            )}
          </div>
        )}
      </div>

      <DenyDialog
        open={showDenyDialog}
        requestId={request.id}
        onOpenChange={setShowDenyDialog}
      />

      {preloadedLinkedSeries && (
        <AddSeries
          open={showApproveDialog}
          onOpenChange={setShowApproveDialog}
          approveRequestId={request.id}
          preloadedLinkedSeries={preloadedLinkedSeries}
          triggerButton={<span />}
        />
      )}
    </>
  );
}

export function RequestsTab() {
  const { data: requests = [], isLoading } = useRequests();
  const isAdmin = useIsAdmin();
  const { user } = useAuth();
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');

  const currentUserId = user?.id ?? '';

  const visibleRequests = isAdmin
    ? requests
    : requests.filter((r) => r.requestedByUserId === currentUserId);

  const pendingRequests = visibleRequests.filter((r) => r.status === 'Pending');
  const historyRequests = visibleRequests.filter((r) => r.status !== 'Pending');

  const filteredHistory =
    statusFilter === 'all'
      ? historyRequests
      : historyRequests.filter((r) => r.status === statusFilter);

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-lg font-semibold text-foreground">
          {isAdmin ? 'Manga Requests' : 'My Requests'}
        </h2>
        <p className="text-sm text-muted-foreground">
          {isAdmin
            ? 'Review and manage manga requests from all users.'
            : 'Track the status of your manga requests.'}
        </p>
      </div>

      {isLoading ? (
        <div className="flex justify-center py-8">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : (
        <>
          {/* Pending section */}
          {pendingRequests.length > 0 && (
            <div className="space-y-3">
              <div className="flex items-center gap-2">
                <h3 className="text-sm font-semibold text-foreground">Pending</h3>
                <Badge className="bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-500/20 text-[9px] px-1.5 h-4">
                  {pendingRequests.length}
                </Badge>
              </div>
              <div className="space-y-3">
                {pendingRequests.map((req) => (
                  <RequestCard
                    key={req.id}
                    request={req}
                    isAdmin={isAdmin}
                    currentUserId={currentUserId}
                  />
                ))}
              </div>
            </div>
          )}

          {/* History section */}
          <div className="space-y-3">
            <div className="flex items-center justify-between flex-wrap gap-2">
              <h3 className="text-sm font-semibold text-foreground">History</h3>
              <div className="flex items-center gap-2">
                <Filter className="h-3.5 w-3.5 text-muted-foreground" />
                <Select value={statusFilter} onValueChange={(v) => setStatusFilter(v as StatusFilter)}>
                  <SelectTrigger className="h-7 text-xs w-28">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All</SelectItem>
                    <SelectItem value="Approved">Approved</SelectItem>
                    <SelectItem value="Denied">Denied</SelectItem>
                    <SelectItem value="Cancelled">Cancelled</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>

            {filteredHistory.length === 0 ? (
              <div className="text-center py-10 rounded-xl border border-dashed">
                <p className="text-sm text-muted-foreground">
                  {pendingRequests.length === 0
                    ? 'No requests yet.'
                    : 'No history to show.'}
                </p>
              </div>
            ) : (
              <div className="space-y-3">
                {filteredHistory.map((req) => (
                  <RequestCard
                    key={req.id}
                    request={req}
                    isAdmin={isAdmin}
                    currentUserId={currentUserId}
                  />
                ))}
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
