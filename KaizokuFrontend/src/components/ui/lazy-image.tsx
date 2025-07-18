"use client";

import React, { useState, useRef, useEffect } from 'react';

interface LazyImageProps extends React.ImgHTMLAttributes<HTMLImageElement> {
  src: string;
  alt: string;
  fallbackSrc?: string;
  className?: string;
  loading?: 'lazy' | 'eager';
  threshold?: number;
}

export function LazyImage({ 
  src, 
  alt, 
  fallbackSrc = '/kaizoku.net.png',
  className = '',
  loading = 'lazy',
  threshold = 0.1,
  ...props 
}: LazyImageProps) {  const [imageSrc, setImageSrc] = useState<string | null>(null);
  const [isLoaded, setIsLoaded] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const observerRef = useRef<IntersectionObserver | null>(null);

  useEffect(() => {
    // If loading is eager, load immediately
    if (loading === 'eager') {
      setImageSrc(src);
      return;
    }

    // Create intersection observer for lazy loading
    if (typeof window !== 'undefined' && 'IntersectionObserver' in window) {      observerRef.current = new IntersectionObserver(        (entries) => {          entries.forEach((entry) => {
            if (entry.isIntersecting && !imageSrc) {
              setImageSrc(src);
              // Disconnect observer after loading starts
              if (observerRef.current && containerRef.current) {
                observerRef.current.unobserve(containerRef.current);
              }
            }
          });
        },
        {
          threshold,
          rootMargin: '50px', // Start loading 50px before the image enters viewport
        }
      );

      if (containerRef.current) {
        observerRef.current.observe(containerRef.current);
      }
    } else {
      // Fallback for browsers without IntersectionObserver
      setImageSrc(src);
    }

    return () => {
      if (observerRef.current) {
        observerRef.current.disconnect();
      }
    };
  }, [src, loading, threshold, imageSrc]);  const handleLoad = () => {
    setIsLoaded(true);
  };

  const handleError = () => {
    setIsLoaded(true);
    if (imageSrc !== fallbackSrc) {
      setImageSrc(fallbackSrc);
    }
  };return (
    <div ref={containerRef} className={`relative overflow-hidden ${className}`}>
      {/* Placeholder while loading */}
      {!isLoaded && (
        <div className={`absolute inset-0 bg-muted animate-pulse rounded-lg`} />
      )}
      
      {/* Actual image */}
      {imageSrc && (
        <img
          src={imageSrc}
          alt={alt}
          className={`transition-opacity duration-200 ${
            isLoaded ? 'opacity-100' : 'opacity-0'
          } ${className}`}
          onLoad={handleLoad}
          onError={handleError}
          {...props}
        />
      )}
    </div>
  );
}
