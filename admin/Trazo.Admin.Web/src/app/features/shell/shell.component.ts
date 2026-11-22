import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import type { SystemInfo } from '../../core/admin.models';
import { OverviewApi } from '../overview/overview.api';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UI_COPY } from '../../core/ui-copy';

@Component({
  selector: 'trazo-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, UiButtonComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
  // Shared page styles (headings, cards, forms) apply to the routed child pages.
  encapsulation: ViewEncapsulation.None
})
export class ShellComponent {
  protected readonly copy = UI_COPY.shell;
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly api = inject(OverviewApi);
  /** Version and environment of the deployed API; stays null when it cannot be read (the footer then shows a dash). */
  protected readonly system = signal<SystemInfo | null>(null);
  protected readonly environmentLabel = computed(() => {
    const name = this.system()?.environment;
    return !name ? '—' : name === 'Production' ? 'PRODUCCIÓN' : name === 'Development' ? 'DESARROLLO' : name.toUpperCase();
  });
  protected readonly versionTitle = computed(() => {
    const s = this.system();
    return s ? `Entorno ${s.environment}, versión ${s.version}${s.commit ? `, commit ${s.commit}` : ''}` : 'Versión no disponible';
  });
  protected readonly initial = computed(() => (this.auth.user()?.displayName ?? 'A').charAt(0).toUpperCase());
  /** Administrators get plan reviews, the accountant gets tax management, and the commercial role gets marketing and referrals; every other section is for the superadmin. */
  protected get navigation(): { label: string; icon: string; link?: string }[] {
    if (this.auth.isSuperadmin) return this.allNavigation;
    if (this.auth.isCommercial) return this.allNavigation.filter(item => item.link === '/marketing' || item.link === '/referrals');
    return this.allNavigation.filter(item => item.link === (this.auth.isAccountant ? '/fiscal' : '/reviews'));
  }
  private readonly allNavigation: { label: string; icon: string; link?: string }[] = [
    { label: UI_COPY.navigation.overview, icon: '◫', link: '/overview' },
    { label: UI_COPY.navigation.intelligence, icon: '⌁', link: '/intelligence' },
    { label: UI_COPY.navigation.users, icon: '◎', link: '/users' },
    { label: UI_COPY.navigation.subscriptions, icon: '◇', link: '/subscriptions' },
    { label: UI_COPY.navigation.marketing, icon: '◉', link: '/marketing' },
    { label: UI_COPY.navigation.referrals, icon: '⇄', link: '/referrals' },
    { label: UI_COPY.navigation.fiscal, icon: '€', link: '/fiscal' },
    { label: UI_COPY.navigation.exercises, icon: '▤', link: '/exercises' },
    { label: UI_COPY.navigation.reviews, icon: '✎', link: '/reviews' },
    { label: UI_COPY.navigation.planning, icon: '⌘', link: '/planning' },
    { label: UI_COPY.navigation.logs, icon: '≋', link: '/logs' },
    { label: UI_COPY.navigation.errorTickets, icon: '⚠', link: '/error-tickets' },
    { label: UI_COPY.navigation.waitlist, icon: '✉', link: '/waitlist' },
    { label: UI_COPY.navigation.backups, icon: '▣', link: '/backups' },
    { label: UI_COPY.navigation.settings, icon: '⚙', link: '/settings' }
  ];

  constructor() {
    if (!this.auth.isSuperadmin) return; // the system info is a superadmin route
    this.api.systemInfo().then(info => this.system.set(info), () => this.system.set(null));
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
