"use client";

import React from "react";
import { PageLayout } from "@/components/kzk/layout/page-layout";

export default function RequestsLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <PageLayout>{children}</PageLayout>;
}
