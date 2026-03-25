/**
 * Returns the responsive default card width based on viewport size.
 * Mobile (<1024px) defaults to "w-32" (Small), desktop defaults to "w-45" (Medium).
 *
 * Uses synchronous matchMedia check so it can be called during useState initialization
 * without causing a flash of wrong size.
 */
export function getResponsiveCardDefault(): string {
  if (typeof window === "undefined") return "w-45";
  return window.matchMedia("(min-width: 1024px)").matches ? "w-45" : "w-32";
}
