import { Component, ViewEncapsulation, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth.service';

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
  protected readonly initial = computed(() => (this.auth.user()?.displayName ?? 'A').charAt(0).toUpperCase());
  protected readonly navigation: { label: string; icon: string; link?: string }[] = [
    { label: 'Overview', icon: '◫', link: '/' },
    { label: 'Users', icon: '◎', link: '/users' },
    { label: 'Subscriptions', icon: '◇' },
    { label: 'Training library', icon: '▤', link: '/exercises' },
    { label: 'Planning engine', icon: '⌘', link: '/planning' },
    { label: 'Logs', icon: '≋' },
    { label: 'Backups', icon: '▣' },
    { label: 'Settings', icon: '⚙', link: '/settings' }
  ];

  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }
}
