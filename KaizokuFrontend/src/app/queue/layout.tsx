"use client";

import React from 'react';
import KzkHeader from "@/components/kzk/layout/header";
import KzkSidebar from "@/components/kzk/layout/sidebar";

export default function QueueLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="flex min-h-screen w-full flex-col bg-muted/40">
      <KzkSidebar />
      <div className="flex flex-col sm:gap-4 sm:py-4 sm:pl-14">
        <KzkHeader />
        <main className="grid flex-1 items-start gap-4 p-4 sm:px-6 sm:py-0 md:gap-8">
          {children}
        </main>
      </div>
    </div>
  );
}
