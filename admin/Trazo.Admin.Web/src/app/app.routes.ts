import { Routes } from '@angular/router';
import { adminGuard, loginGuard, setupGuard, setupPageGuard } from './core/guards';

export const routes: Routes = [
  {
    path: 'setup',
    canActivate: [setupPageGuard],
    loadComponent: () => import('./features/setup/setup.page').then(m => m.SetupPageComponent)
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
      { path: '', pathMatch: 'full', loadComponent: () => import('./features/overview/overview.component').then(m => m.OverviewComponent) },
      { path: 'users', loadComponent: () => import('./features/users/users.page').then(m => m.UsersPageComponent) },
      { path: 'subscriptions', loadComponent: () => import('./features/subscriptions/subscriptions.page').then(m => m.SubscriptionsPageComponent) },
      { path: 'exercises', loadComponent: () => import('./features/exercises/exercises.page').then(m => m.ExercisesPageComponent) },
      { path: 'planning', loadComponent: () => import('./features/planning/planning.page').then(m => m.PlanningPageComponent) },
      { path: 'logs', loadComponent: () => import('./features/logs/logs.page').then(m => m.LogsPageComponent) },
      { path: 'settings', loadComponent: () => import('./features/settings/settings.page').then(m => m.SettingsPageComponent) }
    ]
  },
  { path: '**', redirectTo: '' }
];
