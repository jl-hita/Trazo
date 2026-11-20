import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import type { PushView } from '../../core/admin.models';
import { SettingsApi } from './settings.api';
import { errorMessage } from '../../core/setup.api';

/** Push notifications (Firebase Cloud Messaging): client options for the app, the server key, a test and announcements. */
@Component({
  selector: 'trazo-push-form',
  standalone: true,
  imports: [FormsModule, UiFormFieldComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './push-form.component.scss',
  templateUrl: './push-form.component.html'
})
export class PushFormComponent {
  private readonly api = inject(SettingsApi);

  protected readonly view = signal<PushView | null>(null);
  protected readonly projectId = signal('');
  protected readonly applicationId = signal('');
  protected readonly apiKey = signal('');
  protected readonly senderId = signal('');
  protected readonly serviceAccountJson = signal('');
  protected readonly title = signal('');
  protected readonly body = signal('');
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');
  protected readonly broadcastMessage = signal('');
  protected readonly broadcastError = signal('');

  constructor() { void this.load(); }

  protected async save(): Promise<void> {
    this.busy.set(true); this.message.set(''); this.error.set('');
    try {
      await this.api.savePush({
        projectId: this.projectId().trim(),
        applicationId: this.applicationId().trim(),
        apiKey: this.apiKey().trim(),
        senderId: this.senderId().trim(),
        // Blank keeps the stored key; only a pasted value replaces it.
        serviceAccountJson: this.serviceAccountJson().trim() || null
      });
      this.serviceAccountJson.set('');
      await this.load();
      this.message.set('Push settings saved.');
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async check(): Promise<void> {
    await this.run(() => this.api.checkPush());
  }

  protected async test(): Promise<void> {
    await this.run(() => this.api.testPush());
  }

  protected async broadcast(): Promise<void> {
    this.busy.set(true); this.broadcastMessage.set(''); this.broadcastError.set('');
    try {
      const result = await this.api.broadcastPush(this.title().trim(), this.body().trim());
      (result.ok ? this.broadcastMessage : this.broadcastError).set(result.message);
      if (result.ok) { this.title.set(''); this.body.set(''); }
    } catch (e) {
      this.broadcastError.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async run(action: () => Promise<{ ok: boolean; message: string }>): Promise<void> {
    this.busy.set(true); this.message.set(''); this.error.set('');
    try {
      const result = await action();
      (result.ok ? this.message : this.error).set(result.message);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const v = await this.api.getPush();
      this.view.set(v);
      this.projectId.set(v.projectId);
      this.applicationId.set(v.applicationId);
      this.apiKey.set(v.apiKey);
      this.senderId.set(v.senderId ?? '');
    } catch (e) {
      this.error.set(errorMessage(e));
    }
  }
}
