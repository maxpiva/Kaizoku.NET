"use client";

import React, { createContext, useContext, useState } from 'react';

interface SeriesContextType {
  seriesTitle: string;
  setSeriesTitle: (title: string) => void;
}

const SeriesContext = createContext<SeriesContextType | undefined>(undefined);

export function SeriesProvider({ children }: { children: React.ReactNode }) {
  const [seriesTitle, setSeriesTitle] = useState('');

  return (
    <SeriesContext.Provider
      value={{
        seriesTitle,
        setSeriesTitle,
      }}
    >
      {children}
    </SeriesContext.Provider>
  );
}

export function useSeriesContext() {
  const context = useContext(SeriesContext);
  if (context === undefined) {
    throw new Error('useSeriesContext must be used within a SeriesProvider');
  }
  return context;
}
