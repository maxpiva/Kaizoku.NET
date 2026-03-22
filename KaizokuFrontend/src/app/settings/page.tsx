"use client";

import React, { Suspense } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import {
  Settings,
  Users,
  ShieldCheck,
  Sliders,
  Info,
} from 'lucide-react';
import { useIsAdmin } from '@/hooks/use-permission';

// Tab content components
import { GeneralTab } from '@/components/kzk/settings/general-tab';
import { UsersTab } from '@/components/kzk/settings/users-tab';
import { PermissionsTab } from '@/components/kzk/settings/permissions-tab';
import { PreferencesTab } from '@/components/kzk/settings/preferences-tab';
import { AboutTab } from '@/components/kzk/settings/about-tab';

const ADMIN_TABS = [
  { id: 'general', label: 'General', icon: Settings },
  { id: 'users', label: 'Users', icon: Users },
  { id: 'permissions', label: 'Permissions', icon: ShieldCheck },
  { id: 'preferences', label: 'Preferences', icon: Sliders },
  { id: 'about', label: 'About', icon: Info },
];

const USER_TABS = [
  { id: 'preferences', label: 'Preferences', icon: Sliders },
  { id: 'about', label: 'About', icon: Info },
];

function SettingsContent() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const isAdmin = useIsAdmin();

  const tabs = isAdmin ? ADMIN_TABS : USER_TABS;
  const defaultTab = tabs[0]?.id ?? 'preferences';
  const activeTab = searchParams.get('tab') ?? defaultTab;

  const setTab = (id: string) => {
    const params = new URLSearchParams(searchParams.toString());
    params.set('tab', id);
    router.push(`/settings?${params.toString()}`);
  };

  return (
    <div className="flex flex-col h-full min-h-0">
      {/* Top tab bar */}
      <nav className="shrink-0 border-b bg-background/50">
        <div className="flex items-center gap-1 px-4 overflow-x-auto scrollbar-hide max-w-4xl mx-auto w-full">
          {tabs.map((tab) => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => setTab(tab.id)}
                className={`relative flex items-center gap-2 px-3 py-3 text-sm font-medium whitespace-nowrap transition-colors ${
                  isActive
                    ? 'text-primary'
                    : 'text-muted-foreground hover:text-foreground'
                }`}
              >
                <Icon className="h-4 w-4 shrink-0" />
                <span>{tab.label}</span>
                {isActive && (
                  <div className="absolute bottom-0 left-2 right-2 h-0.5 rounded-full bg-primary" />
                )}
              </button>
            );
          })}
        </div>
      </nav>

      {/* Content panel */}
      <div className="flex-1 min-w-0 overflow-y-auto">
        <div className="px-4 sm:px-6 py-6 max-w-4xl mx-auto pb-24">
          {activeTab === 'general' && isAdmin && <GeneralTab />}
          {activeTab === 'users' && isAdmin && <UsersTab />}
          {activeTab === 'permissions' && isAdmin && <PermissionsTab />}
          {activeTab === 'preferences' && <PreferencesTab />}
          {activeTab === 'about' && <AboutTab />}
        </div>
      </div>
    </div>
  );
}

export default function SettingsPage() {
  return (
    <Suspense fallback={<div className="p-6 text-muted-foreground">Loading settings...</div>}>
      <SettingsContent />
    </Suspense>
  );
}
