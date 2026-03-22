import { useAuth } from '@/contexts/auth-context';
import type { UserPermissions } from '@/lib/api/auth-types';

/**
 * Returns true if the current user has the given permission,
 * or is an Admin (admins have all permissions).
 */
export function usePermission(permission: keyof UserPermissions): boolean {
  const { user, isAuthenticated } = useAuth();
  if (!isAuthenticated || !user) return false;
  if (user.role === 'Admin') return true;
  return user.permissions?.[permission] ?? false;
}

/**
 * Returns true if the current user is an Admin.
 */
export function useIsAdmin(): boolean {
  const { user, isAuthenticated } = useAuth();
  if (!isAuthenticated || !user) return false;
  return user.role === 'Admin';
}
