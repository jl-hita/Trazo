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

export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.user() && auth.canUseConsole && await auth.ensureFreshSession()) return true;
  auth.clear();
  return router.createUrlTree(['/login']);
};

/** Pages only the superadmin may see; an administrator is sent to the plan reviews. The API enforces the same rule. */
export const superadminGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isSuperadmin ? true : inject(Router).createUrlTree([auth.home]);
};

export const loginGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.user() && auth.canUseConsole ? inject(Router).createUrlTree([auth.home]) : true;
};

/** Plan reviews: administrators and the superadmin, not the accountant. */
export const staffGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isStaff ? true : inject(Router).createUrlTree([auth.home]);
};

/** Tax management: the superadmin and the accountant. */
export const fiscalGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.canSeeFiscal ? true : inject(Router).createUrlTree([auth.home]);
};

/** Marketing panel: the superadmin and the commercial role. */
export const marketingGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.canSeeMarketing ? true : inject(Router).createUrlTree([auth.home]);
};
