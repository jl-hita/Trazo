import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi, PlayBillingView } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

/** Google Play Billing configuration: package name, service-account key and notification (RTDN) authentication. */
@Component({
  selector: 'trazo-play-billing-form',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .pb { display: grid; gap: 22px; max-width: 760px; }
    .pb fieldset { margin: 0; padding: 22px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 14px; }
    .pb legend { padding: 0 8px; color: #a9c2df; font-size: 12px; letter-spacing: .12em; text-transform: uppercase; }
    .pb label { display: grid; gap: 6px; color: #aab6c7; font-size: 12px; }
    .pb input, .pb textarea { padding: 10px 12px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font: inherit; }
    .pb textarea { min-height: 120px; font-family: ui-monospace, Consolas, monospace; font-size: 11px; resize: vertical; }
    .pb input:focus, .pb textarea:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .pb .hint { color: #7f8da0; font-size: 11px; margin: 0; }
    .pb .actions { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .pb button { padding: 10px 16px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; cursor: pointer; }
    .pb button.primary { background: #3b6ea8; border-color: #6d9bd2; }
    .pb button:disabled { opacity: .45; cursor: not-allowed; }
    .pb .ok { color: #7fd3a0; font-size: 12px; }
    .pb .err { color: #f0a3a3; font-size: 12px; }
    .pb .stored { color: #7fd3a0; }
  `],
  template: `
    <form class="pb" (ngSubmit)="save()" autocomplete="off">
      <fieldset>
        <legend>Google Play Billing</legend>
        <p class="hint">Subscriptions are verified on the server with the Google Play Developer API. Nothing here is ever sent to the Android app.</p>
        <label>Package name
          <input type="text" name="packageName" required placeholder="com.trazo.android" [ngModel]="packageName()" (ngModelChange)="packageName.set($event)">
        </label>
        <label>Service-account key (JSON)
          <textarea name="serviceAccountJson" spellcheck="false" autocomplete="off"
                    [placeholder]="view()?.serviceAccountSet ? 'Stored (leave blank to keep it)' : 'Paste the whole JSON key file'"
                    [ngModel]="serviceAccountJson()" (ngModelChange)="serviceAccountJson.set($event)"></textarea>
        </label>
        @if (view()?.serviceAccountEmail; as email) { <p class="hint">Stored key for <span class="stored">{{ email }}</span>. The key itself is encrypted and cannot be shown again.</p> }
        <p class="hint">Link this service account in Play Console (Users and permissions) with access to financial data and orders.</p>
      </fieldset>

      <fieldset>
        <legend>Real-time notifications (Pub/Sub push)</legend>
        <p class="hint">Push endpoint: <code>/api/v1/subscription/notifications</code>. Use an OIDC token (recommended) or a secret appended as <code>?secret=…</code>.</p>
        <label>OIDC audience
          <input type="text" name="rtdnAudience" placeholder="https://your-api.example/api/v1/subscription/notifications" [ngModel]="rtdnAudience()" (ngModelChange)="rtdnAudience.set($event)">
        </label>
        <label>Pub/Sub service-account email (optional, pins the sender)
          <input type="text" name="rtdnEmail" placeholder="pubsub-push@project.iam.gserviceaccount.com" [ngModel]="rtdnEmail()" (ngModelChange)="rtdnEmail.set($event)">
        </label>
        <label>Shared secret (alternative, at least 16 characters)
          <input type="password" name="rtdnSecret" autocomplete="new-password"
                 [placeholder]="view()?.rtdnSecretSet ? 'Stored (leave blank to keep it)' : ''" [ngModel]="rtdnSecret()" (ngModelChange)="rtdnSecret.set($event)">
        </label>
        @if (view(); as v) {
          <p class="hint">{{ v.rtdnConfigured ? 'Notifications are accepted.' : 'Notifications are rejected until an audience or a secret is saved.' }}</p>
        }
      </fieldset>

      <div class="actions">
        <button class="primary" type="submit" [disabled]="busy()">Save Play Billing</button>
        <button type="button" (click)="test()" [disabled]="busy() || !view()?.configured">Test connection</button>
        @if (message()) { <span class="ok" role="status">{{ message() }}</span> }
        @if (error()) { <span class="err" role="alert">{{ error() }}</span> }
      </div>
    </form>
  `
})
export class PlayBillingFormComponent {
  private readonly api = inject(AdminApi);

  protected readonly view = signal<PlayBillingView | null>(null);
  protected readonly packageName = signal('');
  protected readonly serviceAccountJson = signal('');
  protected readonly rtdnAudience = signal('');
  protected readonly rtdnEmail = signal('');
  protected readonly rtdnSecret = signal('');
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');

  constructor() { void this.load(); }

  protected async save(): Promise<void> {
    this.busy.set(true); this.message.set(''); this.error.set('');
    try {
      await this.api.savePlayBilling({
        packageName: this.packageName().trim(),
        // Blank keeps the stored secret; only a typed value replaces it.
        serviceAccountJson: this.serviceAccountJson().trim() || null,
        rtdnSecret: this.rtdnSecret() || null,
        rtdnAudience: this.rtdnAudience().trim(),
        rtdnServiceAccountEmail: this.rtdnEmail().trim()
      });
      this.serviceAccountJson.set(''); this.rtdnSecret.set('');
      await this.load();
      this.message.set('Play Billing settings saved.');
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async test(): Promise<void> {
    this.busy.set(true); this.message.set(''); this.error.set('');
    try {
      const result = await this.api.testPlayBilling();
      (result.ok ? this.message : this.error).set(result.message);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const v = await this.api.getPlayBilling();
      this.view.set(v);
      this.packageName.set(v.packageName);
      this.rtdnAudience.set(v.rtdnAudience ?? '');
      this.rtdnEmail.set(v.rtdnServiceAccountEmail ?? '');
    } catch (e) {
      this.error.set(errorMessage(e));
    }
  }
}
