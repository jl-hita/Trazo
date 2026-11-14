import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { AdminApi, SystemInfo } from '../../core/admin.api';

@Component({
  selector: 'trazo-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
  // Shared page styles (headings, cards, forms) apply to the routed child pages.
  encapsulation: ViewEncapsulation.None
})
export class ShellComponent {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly api = inject(AdminApi);
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
  protected readonly navigation: { label: string; icon: string; link?: string }[] = [
    { label: 'Overview', icon: '◫', link: '/' },
    { label: 'Users', icon: '◎', link: '/users' },
    { label: 'Subscriptions', icon: '◇', link: '/subscriptions' },
    { label: 'Training library', icon: '▤', link: '/exercises' },
    { label: 'Planning engine', icon: '⌘', link: '/planning' },
    { label: 'Logs', icon: '≋', link: '/logs' },
    { label: 'Backups', icon: '▣' },
    { label: 'Settings', icon: '⚙', link: '/settings' }
  ];

  constructor() {
    this.api.systemInfo().then(info => this.system.set(info), () => this.system.set(null));
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
