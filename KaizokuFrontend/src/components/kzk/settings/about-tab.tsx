"use client";

import React from 'react';
import Image from 'next/image';
import { ExternalLink } from 'lucide-react';

export function AboutTab() {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-lg font-semibold text-foreground">About</h2>
        <p className="text-sm text-muted-foreground">Application information and credits.</p>
      </div>

      <div className="flex items-center gap-4 rounded-xl border bg-card p-6">
        <div className="h-16 w-16 flex items-center justify-center rounded-xl bg-primary/10 border border-primary/20 shrink-0">
          <Image
            src="/kaizoku.net.png"
            alt="Kaizoku.NET"
            width={40}
            height={40}
            className="h-10 w-10 object-contain"
          />
        </div>
        <div>
          <h3 className="text-xl font-bold text-foreground">Kaizoku.NET</h3>
          <p className="text-sm text-muted-foreground mt-0.5">
            A self-hosted manga series management and downloader
          </p>
        </div>
      </div>

      <div className="rounded-xl border bg-card divide-y">
        <div className="px-4 py-3 flex items-center justify-between">
          <span className="text-sm text-muted-foreground">Application</span>
          <span className="text-sm font-medium text-foreground">Kaizoku.NET</span>
        </div>
        <div className="px-4 py-3 flex items-center justify-between">
          <span className="text-sm text-muted-foreground">Frontend</span>
          <span className="text-sm font-medium text-foreground">Next.js 16 / React 19</span>
        </div>
        <div className="px-4 py-3 flex items-center justify-between">
          <span className="text-sm text-muted-foreground">Backend</span>
          <span className="text-sm font-medium text-foreground">ASP.NET Core</span>
        </div>
        <div className="px-4 py-3 flex items-center justify-between">
          <span className="text-sm text-muted-foreground">UI Framework</span>
          <span className="text-sm font-medium text-foreground">Tailwind CSS v4 + Radix UI</span>
        </div>
      </div>

      <div className="rounded-xl border bg-card p-4 space-y-3">
        <h4 className="text-sm font-semibold text-foreground">Links</h4>
        <a
          href="https://github.com/oae/kaizoku"
          target="_blank"
          rel="noopener noreferrer"
          className="flex items-center gap-2 text-sm text-primary hover:underline"
        >
          <ExternalLink className="h-3.5 w-3.5" />
          GitHub Repository
        </a>
      </div>

      <p className="text-xs text-muted-foreground">
        Kaizoku.NET is an open-source project. All manga content belongs to their respective
        creators and publishers.
      </p>
    </div>
  );
}
