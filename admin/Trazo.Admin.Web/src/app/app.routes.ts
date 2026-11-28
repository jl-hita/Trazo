import { ResolveFn, Routes } from '@angular/router';
import { LANDING_LANGUAGES, WaitlistPagesText, landingLanguage } from './features/landing/landing.content';
import { adminGuard, fiscalGuard, loginGuard, marketingGuard, referralsGuard, setupGuard, setupPageGuard, staffGuard, superadminGuard } from './core/guards';

// Los textos de las páginas de los correos van en el idioma del enlace (`?lang=`).
const waitlistText: ResolveFn<WaitlistPagesText> = route =>
  landingLanguage(route.queryParamMap.get('lang')).load().then(content => content.waitlistPages);

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
  // Landing pública (lista de espera): una ruta por idioma (`/`, `/en`, `/de`...).
  ...LANDING_LANGUAGES.map(language => ({
    path: language.path,
    pathMatch: 'full' as const,
    data: { lang: language.code },
    resolve: { content: () => language.load() },
    loadComponent: () => import('./features/landing/landing.page').then(m => m.LandingPageComponent)
  })),
  // Enlaces de los correos de la lista de espera: confirmar el correo y darse de baja.
  {
    path: 'waitlist/confirm',
    data: { action: 'confirm' },
    resolve: { content: waitlistText },
    loadComponent: () => import('./features/waitlist/waitlist-action.page').then(m => m.WaitlistActionPageComponent)
  },
  {
    path: 'waitlist/unsubscribe',
    data: { action: 'unsubscribe' },
    resolve: { content: waitlistText },
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
      { path: 'reviews', canActivate: [staffGuard], loadComponent: () => import('./features/reviews/reviews.page').then(m => m.ReviewsPageComponent) },
      // Tax management: the superadmin and the accountant (gestor).
      { path: 'fiscal', canActivate: [fiscalGuard], loadComponent: () => import('./features/fiscal/fiscal.page').then(m => m.FiscalPageComponent) },
      // Marketing panel: the superadmin and the commercial role (aggregates only).
      { path: 'marketing', canActivate: [marketingGuard], loadComponent: () => import('./features/marketing/marketing.page').then(m => m.MarketingPageComponent) },
      // Referral programme management: superadmin and commercial; the API enforces the same policy.
      { path: 'referrals', canActivate: [referralsGuard], loadComponent: () => import('./features/referrals/referrals.page').then(m => m.ReferralsPageComponent) },
      // Superadmin only (the API refuses the rest to administrators as well).
      {
        path: '',
        canActivate: [superadminGuard],
        children: [
          { path: 'overview', loadComponent: () => import('./features/overview/overview.component').then(m => m.OverviewComponent) },
          { path: 'intelligence', loadComponent: () => import('./features/intelligence/intelligence.page').then(m => m.IntelligencePageComponent) },
          { path: 'users', loadComponent: () => import('./features/users/users.page').then(m => m.UsersPageComponent) },
          { path: 'subscriptions', loadComponent: () => import('./features/subscriptions/subscriptions.page').then(m => m.SubscriptionsPageComponent) },
          { path: 'exercises', loadComponent: () => import('./features/exercises/exercises.page').then(m => m.ExercisesPageComponent) },
          { path: 'planning', loadComponent: () => import('./features/planning/planning.page').then(m => m.PlanningPageComponent) },
          { path: 'logs', loadComponent: () => import('./features/logs/logs.page').then(m => m.LogsPageComponent) },
          { path: 'error-tickets', loadComponent: () => import('./features/error-tickets/error-tickets.page').then(m => m.ErrorTicketsPageComponent) },
          { path: 'feedback', loadComponent: () => import('./features/feedback/feedback.page').then(m => m.FeedbackPageComponent) },
          { path: 'backups', loadComponent: () => import('./features/backups/backups.page').then(m => m.BackupsPageComponent) },
          { path: 'waitlist', loadComponent: () => import('./features/waitlist/waitlist.page').then(m => m.WaitlistPageComponent) },
          { path: 'settings', loadComponent: () => import('./features/settings/settings.page').then(m => m.SettingsPageComponent) }
        ]
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
