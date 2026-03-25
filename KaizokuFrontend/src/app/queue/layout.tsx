"use client";

import React from "react";
import { PageLayout } from "@/components/kzk/layout/page-layout";

export default function QueueLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <PageLayout mainClassName="p-4 pb-16 sm:px-6 sm:py-4 sm:pb-4 overflow-y-auto">{children}</PageLayout>
  );
}
