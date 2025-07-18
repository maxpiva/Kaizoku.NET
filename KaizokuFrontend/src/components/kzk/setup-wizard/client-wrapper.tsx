"use client";

import { useEffect, useState } from 'react';
import { SetupWizard } from '@/components/kzk/setup-wizard';

export function ClientSideSetupWizard() {
  const [isClient, setIsClient] = useState(false);

  useEffect(() => {
    setIsClient(true);
  }, []);

  // Always render the same JSX structure to maintain hook consistency
  return isClient ? <SetupWizard /> : <div style={{ display: 'none' }} />;
}
