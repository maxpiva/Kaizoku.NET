"use client";

import React from "react";
import { PageLayout } from "@/components/kzk/layout/page-layout";

export default function SettingsLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <PageLayout mainClassName="p-0 overflow-hidden flex flex-col">{children}</PageLayout>
  );
}
