"use client";

import React from 'react';
import { useSearch } from "@/contexts/search-context";
import { ProviderManager } from "@/components/kzk/provider-manager";

export default function ProvidersPage() {
  const { searchTerm, setSearchTerm, clearSearch } = useSearch();

  return (
    <div className="space-y-6">
      <div>
        <p className="text-muted-foreground mb-4">Manage your installed and available sources. Use the search above to filter available sources.</p>
      </div>
      <ProviderManager
        searchTerm={searchTerm}
        setSearchTerm={setSearchTerm}
        clearSearch={clearSearch}
        isCompact={true}
        showSearch={false}
        showNsfwIndicator={true}
        installedGridCols="grid-cols-1 md:grid-cols-2 lg:grid-cols-3"
        availableGridCols="grid-cols-1 md:grid-cols-2 lg:grid-cols-3"
        installedTitle="Installed"
        availableTitle="Available"
      />
    </div>
  );
}
