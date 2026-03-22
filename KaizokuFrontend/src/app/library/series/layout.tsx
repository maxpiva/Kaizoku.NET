"use client";

import React from "react";
import { SeriesProvider, useSeriesContext } from "@/contexts/series-context";
import { PageLayout } from "@/components/kzk/layout/page-layout";

function SeriesLayoutContent({ children }: { children: React.ReactNode }) {
  const { seriesTitle } = useSeriesContext();

  return (
    <PageLayout seriesTitle={seriesTitle} mainClassName="p-4 pb-16 sm:px-6 sm:py-4 sm:pb-4 overflow-y-auto">
      {children}
    </PageLayout>
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
