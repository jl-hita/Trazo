import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import type { SystemInfo } from '../../core/admin.models';
import { OverviewApi } from '../overview/overview.api';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';

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
    { label: 'Overview', icon: '◫', link: '/overview' },
    { label: 'Trazo Intelligence', icon: '⌁', link: '/intelligence' },
    { label: 'Users', icon: '◎', link: '/users' },
    { label: 'Subscriptions', icon: '◇', link: '/subscriptions' },
    { label: 'Marketing', icon: '◉', link: '/marketing' },
    { label: 'Referrals & funnel', icon: '⇄', link: '/referrals' },
    { label: 'Tax management', icon: '€', link: '/fiscal' },
    { label: 'Training library', icon: '▤', link: '/exercises' },
    { label: 'Plan reviews', icon: '✎', link: '/reviews' },
    { label: 'Planning engine', icon: '⌘', link: '/planning' },
    { label: 'Logs', icon: '≋', link: '/logs' },
    { label: 'Error tickets', icon: '⚠', link: '/error-tickets' },
    { label: 'Waitlist', icon: '✉', link: '/waitlist' },
    { label: 'Backups', icon: '▣' },
    { label: 'Settings', icon: '⚙', link: '/settings' }
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
