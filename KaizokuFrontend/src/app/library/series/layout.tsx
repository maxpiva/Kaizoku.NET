"use client";

import React from 'react';
import KzkHeader from "@/components/kzk/layout/header";
import KzkSidebar from "@/components/kzk/layout/sidebar";
import { SeriesProvider, useSeriesContext } from "@/contexts/series-context";

function SeriesLayoutContent({ children }: { children: React.ReactNode }) {
  const { seriesTitle } = useSeriesContext();
  
  return (
    <div className="flex min-h-screen w-full flex-col bg-muted/40">
      <KzkSidebar />
      <div className="flex flex-col sm:gap-4 sm:py-4 sm:pl-14">
        <KzkHeader seriesTitle={seriesTitle} />
        <main className="grid flex-1 items-start gap-4 p-4 sm:px-6 sm:py-0 md:gap-8">
          {children}
        </main>
      </div>
    </div>
  );
}

export default function SeriesLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <SeriesProvider>
      <SeriesLayoutContent>{children}</SeriesLayoutContent>
    </SeriesProvider>
  );
}
