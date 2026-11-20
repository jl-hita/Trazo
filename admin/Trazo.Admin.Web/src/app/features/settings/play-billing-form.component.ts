import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import type { PlayBillingView } from '../../core/admin.models';
import { SettingsApi } from './settings.api';
import { errorMessage } from '../../core/setup.api';

/** Google Play Billing configuration: package name, service-account key and notification (RTDN) authentication. */
@Component({
  selector: 'trazo-play-billing-form',
  standalone: true,
  imports: [FormsModule, UiFormFieldComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './play-billing-form.component.scss',
  templateUrl: './play-billing-form.component.html'
})
export class PlayBillingFormComponent {
  private readonly api = inject(SettingsApi);

  protected readonly view = signal<PlayBillingView | null>(null);
  protected readonly packageName = signal('');
  protected readonly serviceAccountJson = signal('');
  protected readonly rtdnAudience = signal('');
  protected readonly rtdnEmail = signal('');
  protected readonly rtdnSecret = signal('');
  protected readonly enforced = signal(false);
  protected readonly accessMessage = signal('');
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');

  constructor() { void this.load(); }

  protected async setEnforced(value: boolean): Promise<void> {
    this.busy.set(true); this.accessMessage.set(''); this.error.set('');
    try {
      const saved = await this.api.saveAccessSettings(value);
      this.enforced.set(saved.subscriptionsEnforced);
      this.accessMessage.set(saved.subscriptionsEnforced ? 'Subscriptions are now required.' : 'Subscriptions are off: everything is free.');
    } catch (e) {
      this.enforced.set(!value);
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

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
      this.enforced.set((await this.api.getAccessSettings()).subscriptionsEnforced);
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
