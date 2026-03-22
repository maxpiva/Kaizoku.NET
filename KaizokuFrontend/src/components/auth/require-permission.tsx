"use client";

import { usePermission, useIsAdmin } from '@/hooks/use-permission';
import type { UserPermissions } from '@/lib/api/auth-types';

interface RequirePermissionProps {
  permission: keyof UserPermissions;
  children: React.ReactNode;
  fallback?: React.ReactNode;
}

/**
 * Renders children only if the current user has the given permission (or is Admin).
 */
export function RequirePermission({
  permission,
  children,
  fallback = null,
}: RequirePermissionProps) {
  const hasPermission = usePermission(permission);
  return hasPermission ? <>{children}</> : <>{fallback}</>;
}

interface RequireAdminProps {
  children: React.ReactNode;
  fallback?: React.ReactNode;
}

/**
 * Renders children only if the current user is an Admin.
 */
export function RequireAdmin({ children, fallback = null }: RequireAdminProps) {
  const isAdmin = useIsAdmin();
  return isAdmin ? <>{children}</> : <>{fallback}</>;
}
