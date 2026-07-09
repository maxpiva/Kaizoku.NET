"use client";

import React, { useState } from 'react';
import { Activity, AlertTriangle, Radio } from 'lucide-react';
import { useSeriesStatus, useProviderStatus, useClearAlert } from '@/lib/api/hooks/useStatus';
import { ProviderStatusPanel } from '@/components/comp/status/provider-status-panel';
import { SeriesStatusPanel } from '@/components/comp/status/series-status-panel';
import { RibbonSlot } from '@/components/comp/layout/ribbon';
import { useAuth } from '@/contexts/auth-context';

type StatusTab = "providers" | "series";

interface StatTileProps {
  label: string;
  value: number;
  icon: React.ReactNode;
  tone: "warning" | "critical";
}

/** Glass summary tile — muted when the count is zero, accent-lit when there are alerts. */
function StatTile({ label, value, icon, tone }: StatTileProps) {
  const active = value > 0;
  const accentText =
    tone === "critical" ? "text-red-400" : "text-amber-400";
  const accentRing =
    tone === "critical"
      ? "border-red-500/30 shadow-[0_0_24px_-12px] shadow-red-500/40"
      : "border-amber-500/30 shadow-[0_0_24px_-12px] shadow-amber-500/40";

  return (
    <div
      className={`relative overflow-hidden rounded-xl border bg-white/[0.02] p-4 transition-colors ${
        active ? accentRing : "border-white/[0.06]"
      }`}
    >
      <div className="flex items-center justify-between">
        <span className="text-[10.5px] font-medium uppercase tracking-[0.16em] text-muted-foreground">
          {label}
        </span>
        <span className={active ? accentText : "text-muted-foreground/60"}>
          {icon}
        </span>
      </div>
      <div
        className={`mt-2 font-mono text-3xl font-semibold tabular-nums ${
          active ? accentText : "text-muted-foreground/50"
        }`}
      >
        {value}
      </div>
    </div>
  );
}

interface SegmentedTabsProps {
  value: StatusTab;
  onChange: (v: StatusTab) => void;
  providerCount: number;
  seriesCount: number;
}

/** Sources / Series segmented control — matches the queue page's filter pills. */
function SegmentedTabs({ value, onChange, providerCount, seriesCount }: SegmentedTabsProps) {
  const tabs: { id: StatusTab; label: string; count: number }[] = [
    { id: "series", label: "Series", count: seriesCount },
    { id: "providers", label: "Sources", count: providerCount },
  ];
  return (
    <div className="inline-flex items-center gap-0.5 rounded-full border border-white/[0.06] bg-white/[0.015] px-0.5 py-0.5">
      {tabs.map((tab) => {
        const isActive = value === tab.id;
        return (
          <button
            key={tab.id}
            type="button"
            onClick={() => onChange(tab.id)}
            className={`rounded-full px-3.5 py-[5px] text-[12.5px] font-medium transition-colors duration-[120ms] ${
              isActive
                ? "bg-primary text-primary-foreground"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
            {tab.count > 0 && (
              <span
                className={`ml-1 text-[11px] ${
                  isActive ? "text-primary-foreground/70" : "text-muted-foreground/60"
                }`}
              >
                {tab.count}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}

function PanelSkeleton({ rows }: { rows: number }) {
  return (
    <div className="space-y-3">
      {Array.from({ length: rows }).map((_, i) => (
        <div
          key={i}
          className="h-20 w-full animate-pulse rounded-xl border border-white/[0.06] bg-white/[0.02]"
        />
      ))}
    </div>
  );
}

export default function StatusPage() {
  const [activeTab, setActiveTab] = useState<StatusTab>("series");
  const { data: series, isLoading: seriesLoading } = useSeriesStatus();
  const { data: providers, isLoading: providersLoading } = useProviderStatus();
  const { mutate: clearAlert } = useClearAlert();
  const { canAdmin } = useAuth();

  const handleClearAlert = (targetType: number, targetId: string) => {
    clearAlert({ targetType, targetId });
  };

  const seriesWarnings = series?.filter((s) => s.level === 1).length ?? 0;
  const seriesCritical = series?.filter((s) => s.level === 2).length ?? 0;
  const sourceWarnings = providers?.filter((p) => p.level === 1).length ?? 0;
  const sourceCritical = providers?.filter((p) => p.level === 2).length ?? 0;

  return (
    <div className="space-y-6">
      <RibbonSlot>
        <div className="flex w-full items-center gap-2">
          <Activity className="h-4 w-4 shrink-0 text-muted-foreground" />
          <h2 className="truncate text-sm font-semibold text-foreground">Status</h2>
          <span className="hidden truncate text-xs text-muted-foreground sm:inline">
            · Health of your sources and series
          </span>
        </div>
      </RibbonSlot>

      {/* Summary tiles */}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <StatTile
          label="Series Warnings"
          value={seriesWarnings}
          tone="warning"
          icon={<AlertTriangle className="h-4 w-4" />}
        />
        <StatTile
          label="Series Critical"
          value={seriesCritical}
          tone="critical"
          icon={<Radio className="h-4 w-4" />}
        />
        <StatTile
          label="Source Warnings"
          value={sourceWarnings}
          tone="warning"
          icon={<AlertTriangle className="h-4 w-4" />}
        />
        <StatTile
          label="Source Critical"
          value={sourceCritical}
          tone="critical"
          icon={<Radio className="h-4 w-4" />}
        />
      </div>

      {/* Tab selector */}
      <SegmentedTabs
        value={activeTab}
        onChange={setActiveTab}
        providerCount={providers?.length ?? 0}
        seriesCount={series?.length ?? 0}
      />

      {/* Active panel */}
      <div>
        {activeTab === "providers" &&
          (providersLoading ? (
            <PanelSkeleton rows={2} />
          ) : (
            <ProviderStatusPanel
              providers={providers ?? []}
              onClearAlert={handleClearAlert}
              canAdmin={canAdmin}
            />
          ))}

        {activeTab === "series" &&
          (seriesLoading ? (
            <PanelSkeleton rows={3} />
          ) : (
            <SeriesStatusPanel
              series={series ?? []}
              onClearAlert={handleClearAlert}
              canAdmin={canAdmin}
            />
          ))}
      </div>
    </div>
  );
}
