import { Routes } from '@angular/router';
import { adminGuard, loginGuard, setupGuard, setupPageGuard, superadminGuard } from './core/guards';

export const routes: Routes = [
  // Páginas legales públicas, accesibles sin iniciar sesión.
  {
    path: 'privacidad',
    data: { lang: 'es' },
    loadComponent: () => import('./features/privacy/privacy.page').then(m => m.PrivacyPageComponent)
  },
  {
    path: 'privacy-policy',
    data: { lang: 'en' },
    loadComponent: () => import('./features/privacy/privacy.page').then(m => m.PrivacyPageComponent)
  },
  {
    path: 'condiciones',
    data: { lang: 'es' },
    loadComponent: () => import('./features/terms/terms.page').then(m => m.TermsPageComponent)
  },
  {
    path: 'terms',
    data: { lang: 'en' },
    loadComponent: () => import('./features/terms/terms.page').then(m => m.TermsPageComponent)
  },
  // Landing pública (lista de espera).
  {
    path: '',
    pathMatch: 'full',
    data: { lang: 'es' },
    loadComponent: () => import('./features/landing/landing.page').then(m => m.LandingPageComponent)
  },
  {
    path: 'en',
    data: { lang: 'en' },
    loadComponent: () => import('./features/landing/landing.page').then(m => m.LandingPageComponent)
  },
  // Enlaces de los correos de la lista de espera: confirmar el correo y darse de baja.
  {
    path: 'waitlist/confirm',
    data: { action: 'confirm' },
    loadComponent: () => import('./features/waitlist/waitlist-action.page').then(m => m.WaitlistActionPageComponent)
  },
  {
    path: 'waitlist/unsubscribe',
    data: { action: 'unsubscribe' },
    loadComponent: () => import('./features/waitlist/waitlist-action.page').then(m => m.WaitlistActionPageComponent)
  },
  {
    path: 'setup',
    canActivate: [setupPageGuard],
    loadComponent: () => import('./features/setup/setup.page').then(m => m.SetupPageComponent)
  },
  {
    path: 'account/reset-password',
    loadComponent: () => import('./features/account/reset-password.page').then(m => m.ResetPasswordPageComponent)
  },
  {
    path: 'account/verify-email',
    loadComponent: () => import('./features/account/verify-email.page').then(m => m.VerifyEmailPageComponent)
  },
  {
    path: 'login',
    canActivate: [setupGuard, loginGuard],
    loadComponent: () => import('./features/login/login.page').then(m => m.LoginPageComponent)
  },
  {
    path: '',
    canActivate: [setupGuard, adminGuard],
    loadComponent: () => import('./features/shell/shell.component').then(m => m.ShellComponent),
    children: [
      // Open to every administrator.
      { path: 'reviews', loadComponent: () => import('./features/reviews/reviews.page').then(m => m.ReviewsPageComponent) },
      // Superadmin only (the API refuses the rest to administrators as well).
      {
        path: '',
        canActivate: [superadminGuard],
        children: [
          { path: 'overview', loadComponent: () => import('./features/overview/overview.component').then(m => m.OverviewComponent) },
          { path: 'users', loadComponent: () => import('./features/users/users.page').then(m => m.UsersPageComponent) },
          { path: 'referrals', loadComponent: () => import('./features/referrals/referrals.page').then(m => m.ReferralsPageComponent) },
          { path: 'fiscal', loadComponent: () => import('./features/fiscal/fiscal.page').then(m => m.FiscalPageComponent) },
          { path: 'subscriptions', loadComponent: () => import('./features/subscriptions/subscriptions.page').then(m => m.SubscriptionsPageComponent) },
          { path: 'exercises', loadComponent: () => import('./features/exercises/exercises.page').then(m => m.ExercisesPageComponent) },
          { path: 'planning', loadComponent: () => import('./features/planning/planning.page').then(m => m.PlanningPageComponent) },
          { path: 'logs', loadComponent: () => import('./features/logs/logs.page').then(m => m.LogsPageComponent) },
          { path: 'error-tickets', loadComponent: () => import('./features/error-tickets/error-tickets.page').then(m => m.ErrorTicketsPageComponent) },
          { path: 'waitlist', loadComponent: () => import('./features/waitlist/waitlist.page').then(m => m.WaitlistPageComponent) },
          { path: 'settings', loadComponent: () => import('./features/settings/settings.page').then(m => m.SettingsPageComponent) }
        ]
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
