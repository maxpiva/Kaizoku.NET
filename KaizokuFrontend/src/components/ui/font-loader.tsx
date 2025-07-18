"use client";

import { useEffect } from 'react';

export function FontLoader() {
  useEffect(() => {
    // Check if fonts are already loaded
    if (typeof document !== 'undefined') {
      const checkFontLoad = async () => {
        try {
          // Use the Font Loading API if available
          if ('fonts' in document) {
            // Wait for fonts to be ready
            await document.fonts.ready;
            document.body.classList.add('font-loaded');
            document.body.classList.remove('font-loading');
          } else {
            // Fallback for browsers without Font Loading API
            setTimeout(() => {
              document.body.classList.add('font-loaded');
              document.body.classList.remove('font-loading');
            }, 1000); // Reduced from 2000ms to 1000ms
          }
        } catch (error) {
          console.warn('Font loading check failed:', error);
          // Use fallback fonts
          document.body.classList.add('font-loading-fallback');
          document.body.classList.remove('font-loading');
        }
      };

      // Start with loading state only if not already loaded
      if (!document.body.classList.contains('font-loaded')) {
        document.body.classList.add('font-loading');
        void checkFontLoad();
      }
    }
  }, []);

  return null; // This component doesn't render anything
}

export default FontLoader;
