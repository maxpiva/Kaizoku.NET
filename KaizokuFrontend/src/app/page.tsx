"use client";

import { useEffect } from "react";

export default function RootPage() {
  useEffect(() => {
    // Use window.location.replace for static exports to avoid history issues
    // and ensure proper routing to the library page
    window.location.replace("/library/");
  }, []);

  // Return a minimal loading state while redirect happens
  return (
    <div className="flex items-center justify-center min-h-screen">
      <div className="text-lg">Redirecting to library...</div>
    </div>
  );
}
