import { Component, inject, signal } from '@angular/core';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { WaitlistPagesText } from '../landing/landing.content';

type Action = 'confirm' | 'unsubscribe';
type Phase = 'ready' | 'working' | 'done' | 'failed' | 'missing';

/**
 * The pages behind the links of the waitlist emails (`/waitlist/confirm` and `/waitlist/unsubscribe`). Nothing happens on load:
 * the person presses a button, so a mail scanner that merely opens the link cannot confirm or remove anybody.
 */
@Component({
  selector: 'trazo-waitlist-action-page',
  standalone: true,
  imports: [UiButtonComponent],
  styleUrl: './waitlist-action.page.scss',
  templateUrl: './waitlist-action.page.html'
})
export class WaitlistActionPageComponent {
  private readonly http = inject(HttpClient);
  private token: string | null;

  protected readonly action: Action;
  protected readonly t: WaitlistPagesText;
  protected readonly phase = signal<Phase>('ready');

  constructor() {
    const route = inject(ActivatedRoute);
    this.action = route.snapshot.data['action'] === 'unsubscribe' ? 'unsubscribe' : 'confirm';
    this.t = route.snapshot.data['content'];
    this.token = route.snapshot.queryParamMap.get('token');
    // The token stays in memory only: out of the address bar and the browser history.
    inject(Location).replaceState(`/waitlist/${this.action}`);
    if (!this.token) this.phase.set('missing');
  }

  protected title(): string {
    switch (this.phase()) {
      case 'done': return this.t[this.action].done;
      case 'failed': return this.t.failed;
      case 'missing': return this.t.missing;
      default: return this.t[this.action].title;
    }
  }

  protected message(): string {
    switch (this.phase()) {
      case 'done': return this.t[this.action].doneText;
      case 'failed': return this.t.failedText;
      case 'missing': return this.t.missingText;
      default: return this.t[this.action].text;
    }
  }

  protected async run(): Promise<void> {
    if (!this.token) return;
    this.phase.set('working');
    try {
      await firstValueFrom(this.http.post(`/api/v1/waitlist/${this.action}`, { token: this.token }));
      this.phase.set('done');
    } catch {
      this.phase.set('failed');
    }
  }
}
