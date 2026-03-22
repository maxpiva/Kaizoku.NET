"use client";

import React, { useState } from 'react';
import { BookOpen, Send, Loader2 } from 'lucide-react';
import Image from 'next/image';
import { Button } from '@/components/ui/button';
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
  Drawer,
  DrawerContent,
  DrawerHeader,
  DrawerTitle,
} from '@/components/ui/drawer';
import { useCreateRequest } from '@/lib/api/hooks/useRequests';
import { useToast } from '@/hooks/use-toast';
import { useMediaQuery } from '@/hooks/use-media-query';
import { formatThumbnailUrl } from '@/lib/utils/thumbnail';
import type { LatestSeriesInfo, LinkedSeries } from '@/lib/api/types';

interface RequestSeriesDialogProps {
  item: Pick<LatestSeriesInfo, 'title' | 'thumbnailUrl' | 'description' | 'provider' | 'mihonId' | 'mihonProviderId' | 'language'>;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

function RequestForm({
  item,
  onClose,
}: {
  item: RequestSeriesDialogProps['item'];
  onClose: () => void;
}) {
  const createRequest = useCreateRequest();
  const { toast } = useToast();
  const [note, setNote] = useState('');

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    try {
      // Build a proper LinkedSeries[] array so admins have full provider data when reviewing
      const linkedSeries: LinkedSeries[] = [{
        mihonId: item.mihonId,
        mihonProviderId: item.mihonProviderId ?? '',
        providerId: item.mihonProviderId ?? item.mihonId,
        provider: item.provider,
        lang: item.language ?? 'en',
        thumbnailUrl: item.thumbnailUrl ?? undefined,
        title: item.title,
        linkedIds: [],
        useCover: true,
        isStorage: true,
        isLocal: false,
      }];

      await createRequest.mutateAsync({
        title: item.title,
        description: note || undefined,
        thumbnailUrl: item.thumbnailUrl ?? undefined,
        providerData: JSON.stringify(linkedSeries),
      });

      toast({ title: 'Request submitted', description: `"${item.title}" has been requested.`, variant: 'success' });
      onClose();
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Failed to submit request.';
      if (msg.toLowerCase().includes('already') || msg.toLowerCase().includes('exist')) {
        toast({ title: 'Already requested', description: 'This manga has already been requested.', variant: 'destructive' });
      } else {
        toast({ title: 'Failed to submit request', description: msg, variant: 'destructive' });
      }
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {/* Series preview */}
      <div className="flex gap-3 rounded-lg border bg-muted/30 p-3">
        <div className="w-10 h-14 rounded overflow-hidden bg-muted shrink-0 relative">
          {item.thumbnailUrl ? (
            <Image
              src={formatThumbnailUrl(item.thumbnailUrl)}
              alt={item.title}
              fill
              className="object-cover"
              onError={(e) => {
                (e.target as HTMLImageElement).style.display = 'none';
              }}
            />
          ) : (
            <div className="h-full w-full flex items-center justify-center">
              <BookOpen className="h-4 w-4 text-muted-foreground" />
            </div>
          )}
        </div>
        <div className="min-w-0">
          <p className="text-sm font-semibold text-foreground leading-tight truncate">{item.title}</p>
          <p className="text-xs text-muted-foreground mt-0.5">{item.provider}</p>
          {item.description && (
            <p className="text-xs text-muted-foreground line-clamp-2 mt-1">{item.description}</p>
          )}
        </div>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="request-note">Note (optional)</Label>
        <Textarea
          id="request-note"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Any additional context about this manga..."
          rows={2}
        />
      </div>

      <div className="flex justify-end gap-2 pt-1">
        <Button type="button" variant="ghost" onClick={onClose} size="sm">
          Cancel
        </Button>
        <Button type="submit" disabled={createRequest.isPending} size="sm" className="gap-2">
          {createRequest.isPending ? (
            <><Loader2 className="h-3.5 w-3.5 animate-spin" />Submitting...</>
          ) : (
            <><Send className="h-3.5 w-3.5" />Submit Request</>
          )}
        </Button>
      </div>
    </form>
  );
}

export function RequestSeriesDialog({ item, open, onOpenChange }: RequestSeriesDialogProps) {
  const isDesktop = useMediaQuery('(min-width: 768px)');

  if (isDesktop) {
    return (
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="w-[95vw] max-w-md p-0">
          <DialogHeader className="px-5 pt-5 pb-0">
            <DialogTitle>Request Series</DialogTitle>
            <DialogDescription>
              Submit a request for an admin to add this manga to the library.
            </DialogDescription>
          </DialogHeader>
          <div className="px-5 pb-5">
            <RequestForm item={item} onClose={() => onOpenChange(false)} />
          </div>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Drawer open={open} onOpenChange={onOpenChange}>
      <DrawerContent className="max-h-[85dvh]">
        <DrawerHeader className="text-left pb-2">
          <DrawerTitle>Request Series</DrawerTitle>
        </DrawerHeader>
        <div className="px-4 pb-[max(1.5rem,env(safe-area-inset-bottom))]">
          <RequestForm item={item} onClose={() => onOpenChange(false)} />
        </div>
      </DrawerContent>
    </Drawer>
  );
}
