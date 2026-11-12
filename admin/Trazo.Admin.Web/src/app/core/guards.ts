import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { SetupApi } from './setup.api';

/** While the installation has no setup/superadmin, everything redirects to the wizard. */
export const setupGuard: CanActivateFn = async () => {
  const router = inject(Router);
  try {
    const status = await inject(SetupApi).status();
    return status.setupRequired ? router.createUrlTree(['/setup']) : true;
  } catch {
    return true; // API unreachable: let the login page show its own error.
  }
};

/** The wizard is only reachable while setup is required. */
export const setupPageGuard: CanActivateFn = async () => {
  const router = inject(Router);
  try {
    const status = await inject(SetupApi).status();
    return status.setupRequired ? true : router.createUrlTree(['/login']);
  } catch {
    return true;
  }
};

export const adminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.user() && auth.isSuperadmin) return true;
  auth.clear();
  return router.createUrlTree(['/login']);
};

export const loginGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.user() && auth.isSuperadmin ? inject(Router).createUrlTree(['/']) : true;
};
