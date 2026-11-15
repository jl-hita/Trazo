import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi, PushView } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

/** Push notifications (Firebase Cloud Messaging): client options for the app, the server key, a test and announcements. */
@Component({
  selector: 'trazo-push-form',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .pf { display: grid; gap: 22px; max-width: 760px; }
    .pf fieldset { margin: 0; padding: 22px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 14px; }
    .pf legend { padding: 0 8px; color: #a9c2df; font-size: 12px; letter-spacing: .12em; text-transform: uppercase; }
    .pf label { display: grid; gap: 6px; color: #aab6c7; font-size: 12px; }
    .pf input, .pf textarea { padding: 10px 12px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font: inherit; }
    .pf textarea { min-height: 120px; font-family: ui-monospace, Consolas, monospace; font-size: 11px; resize: vertical; }
    .pf textarea.short { min-height: 70px; font-family: inherit; font-size: 13px; }
    .pf input:focus, .pf textarea:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .pf .hint { color: #7f8da0; font-size: 11px; margin: 0; }
    .pf .actions { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .pf button { padding: 10px 16px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; cursor: pointer; }
    .pf button.primary { background: #3b6ea8; border-color: #6d9bd2; }
    .pf button:disabled { opacity: .45; cursor: not-allowed; }
    .pf .ok { color: #7fd3a0; font-size: 12px; }
    .pf .err { color: #f0a3a3; font-size: 12px; }
    .pf .stored { color: #7fd3a0; }
  `],
  template: `
    <form class="pf" (ngSubmit)="save()" autocomplete="off">
      <fieldset>
        <legend>Push notifications (Firebase)</legend>
        <p class="hint">Create a Firebase project, add an Android app with the package <code>com.trazo.android</code> and copy these values from
          <code>google-services.json</code>. The app asks the API for them after sign-in, so no file is built into it. Leave everything empty to turn push off.</p>
        <label>Project ID (<code>project_info.project_id</code>)
          <input type="text" name="projectId" placeholder="trazo-app" [ngModel]="projectId()" (ngModelChange)="projectId.set($event)">
        </label>
        <label>Android app ID (<code>client_info.mobilesdk_app_id</code>)
          <input type="text" name="applicationId" placeholder="1:123456789012:android:0123456789abcdef" [ngModel]="applicationId()" (ngModelChange)="applicationId.set($event)">
        </label>
        <label>Android API key (<code>api_key.current_key</code>)
          <input type="text" name="apiKey" placeholder="AIza…" [ngModel]="apiKey()" (ngModelChange)="apiKey.set($event)">
        </label>
        <label>Sender ID (<code>project_info.project_number</code>, optional)
          <input type="text" name="senderId" placeholder="123456789012" [ngModel]="senderId()" (ngModelChange)="senderId.set($event)">
        </label>
        <label>Service-account key (JSON)
          <textarea name="serviceAccountJson" spellcheck="false" autocomplete="off"
                    [placeholder]="view()?.serviceAccountSet ? 'Stored (leave blank to keep it)' : 'Paste the whole JSON key file'"
                    [ngModel]="serviceAccountJson()" (ngModelChange)="serviceAccountJson.set($event)"></textarea>
        </label>
        @if (view()?.serviceAccountEmail; as email) { <p class="hint">Stored key for <span class="stored">{{ email }}</span>. The key itself is encrypted and cannot be shown again.</p> }
        <p class="hint">Firebase console → Project settings → Service accounts → Generate new private key. The Firebase Cloud Messaging API (V1) must be enabled.
          Nothing secret is ever sent to the app.</p>
        @if (view(); as v) {
          <p class="hint">{{ v.configured ? 'Push is active.' : 'Push is not active: the client values and the service-account key are all needed.' }}
            Registered devices: {{ v.deviceCount }}.</p>
        }
        <div class="actions">
          <button class="primary" type="submit" [disabled]="busy()">Save push settings</button>
          <button type="button" (click)="check()" [disabled]="busy() || !view()?.configured">Check connection</button>
          <button type="button" (click)="test()" [disabled]="busy() || !view()?.configured">Send test to my devices</button>
          @if (message()) { <span class="ok" role="status">{{ message() }}</span> }
          @if (error()) { <span class="err" role="alert">{{ error() }}</span> }
        </div>
      </fieldset>
    </form>

    <form class="pf" style="margin-top:22px" (ngSubmit)="broadcast()" autocomplete="off">
      <fieldset>
        <legend>Announcement to all devices</legend>
        <p class="hint">Sent to every registered phone. Do not include personal data: it travels through Google's servers.</p>
        <label>Title (max 65)
          <input type="text" name="title" maxlength="65" [ngModel]="title()" (ngModelChange)="title.set($event)">
        </label>
        <label>Text (max 240)
          <textarea class="short" name="body" maxlength="240" [ngModel]="body()" (ngModelChange)="body.set($event)"></textarea>
        </label>
        <div class="actions">
          <button class="primary" type="submit" [disabled]="busy() || !view()?.configured || !title().trim() || !body().trim()">Send announcement</button>
          @if (broadcastMessage()) { <span class="ok" role="status">{{ broadcastMessage() }}</span> }
          @if (broadcastError()) { <span class="err" role="alert">{{ broadcastError() }}</span> }
        </div>
      </fieldset>
    </form>
  `
})
export class PushFormComponent {
  private readonly api = inject(AdminApi);

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
