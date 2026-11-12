import { Component, ViewEncapsulation, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EmailSettings, SetupApi, SettingsView, TestResult, errorMessage } from '../../core/setup.api';

type Mode = 'setup' | 'settings';

/**
 * Shared configuration form (Google client ID + SMTP). In `setup` mode it also collects the
 * superadmin account. Each required part must pass its test before saving.
 */
@Component({
  selector: 'trazo-config-form',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .cfg { display: grid; gap: 22px; max-width: 760px; }
    .cfg fieldset { margin: 0; padding: 22px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 14px; }
    .cfg legend { padding: 0 8px; color: #a9c2df; font-size: 12px; letter-spacing: .12em; text-transform: uppercase; }
    .cfg label { display: grid; gap: 6px; color: #aab6c7; font-size: 12px; }
    .cfg input[type=text], .cfg input[type=email], .cfg input[type=password], .cfg input[type=number], .cfg input[type=url] {
      padding: 10px 12px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; }
    .cfg input:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .cfg .row { display: grid; grid-template-columns: 1fr 120px; gap: 14px; }
    .cfg .check { display: flex; align-items: center; gap: 8px; }
    .cfg .hint { color: #7f8da0; font-size: 11px; margin: 0; }
    .cfg .actions { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .cfg button { padding: 10px 16px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; cursor: pointer; }
    .cfg button.primary { background: #3b6ea8; border-color: #6d9bd2; }
    .cfg button:disabled { opacity: .45; cursor: not-allowed; }
    .cfg .ok { color: #7fd3a0; font-size: 12px; }
    .cfg .err { color: #f0a3a3; font-size: 12px; }
    .cfg .banner { padding: 14px 16px; border: 1px solid #6b5a2e; border-radius: 8px; background: #2a2412; color: #e6d39b; font-size: 12px; line-height: 1.6; }
  `],
  template: `
    <form class="cfg" (ngSubmit)="submit()" autocomplete="off">
      @if (mode() === 'setup') {
        <div class="banner" role="note">
          Whoever completes this form first becomes the <strong>superadmin</strong> of this installation.
          Complete it right after the first deployment, and only from a network you trust.
        </div>
        <fieldset>
          <legend>Superadmin account</legend>
          <label>Email <input type="email" name="adminEmail" required autocomplete="username" [ngModel]="adminEmail()" (ngModelChange)="adminEmail.set($event)"></label>
          <label>Display name <input type="text" name="adminName" required minlength="2" maxlength="100" [ngModel]="adminName()" (ngModelChange)="adminName.set($event)"></label>
          <label>Password <input type="password" name="adminPassword" required minlength="12" autocomplete="new-password" [ngModel]="adminPassword()" (ngModelChange)="adminPassword.set($event)"></label>
          <label>Repeat password <input type="password" name="adminPassword2" required autocomplete="new-password" [ngModel]="adminPassword2()" (ngModelChange)="adminPassword2.set($event)"></label>
          <p class="hint">At least 12 characters with uppercase, lowercase and a number.</p>
        </fieldset>
      }

      <fieldset>
        <legend>Google sign-in</legend>
        <label>Web client ID
          <input type="text" name="clientId" required placeholder="1234567890-abc.apps.googleusercontent.com" [ngModel]="clientId()" (ngModelChange)="clientId.set($event)">
        </label>
        <div class="actions">
          <button type="button" (click)="testGoogle()" [disabled]="busy() || !clientId().trim()">Test</button>
          @if (googleResult(); as r) { <span [class]="r.ok ? 'ok' : 'err'" role="status">{{ r.message }}</span> }
        </div>
      </fieldset>

      <fieldset>
        <legend>Transactional email (SMTP)</legend>
        <div class="row">
          <label>Host <input type="text" name="host" required [ngModel]="host()" (ngModelChange)="host.set($event)"></label>
          <label>Port <input type="number" name="port" required min="1" max="65535" [ngModel]="port()" (ngModelChange)="port.set($event)"></label>
        </div>
        <label class="check"><input type="checkbox" name="ssl" [ngModel]="ssl()" (ngModelChange)="ssl.set($event)"> Use SSL/TLS</label>
        <label>Username (optional) <input type="text" name="smtpUser" autocomplete="off" [ngModel]="username()" (ngModelChange)="username.set($event)"></label>
        <label>Password (optional)
          <input type="password" name="smtpPass" autocomplete="new-password" [placeholder]="passwordSet() && password() === null ? 'Stored (leave blank to keep)' : ''"
                 [ngModel]="password() ?? ''" (ngModelChange)="password.set($event === '' && passwordSet() ? null : $event)">
        </label>
        <label>Sender (From) <input type="text" name="from" required placeholder="TRAZO &lt;no-reply@example.com&gt;" [ngModel]="from()" (ngModelChange)="from.set($event)"></label>
        <label>Action base URL <input type="text" name="actionBaseUrl" required placeholder="trazo://account" [ngModel]="actionBaseUrl()" (ngModelChange)="actionBaseUrl.set($event)"></label>
        <p class="hint">Links in verification and password-reset emails are built from this URL (for example trazo://account for the Android app).</p>
        <label>Send test email to <input type="email" name="recipient" [ngModel]="recipient()" (ngModelChange)="recipient.set($event)"></label>
        <div class="actions">
          <button type="button" (click)="testSmtp()" [disabled]="busy() || !recipient().trim()">Send test email</button>
          @if (smtpResult(); as r) { <span [class]="r.ok ? 'ok' : 'err'" role="status">{{ r.message }}</span> }
        </div>
      </fieldset>

      <div class="actions">
        <button class="primary" type="submit" [disabled]="busy() || !canSave()">{{ mode() === 'setup' ? 'Finish setup' : 'Save settings' }}</button>
        @if (!testsPassed()) { <span class="hint">Run both tests successfully before saving.</span> }
        @if (error()) { <span class="err" role="alert">{{ error() }}</span> }
        @if (saved()) { <span class="ok" role="status">Settings saved.</span> }
      </div>
    </form>
  `
})
export class ConfigFormComponent {
  private readonly api = inject(SetupApi);

  readonly mode = input<Mode>('setup');
  readonly initial = input<SettingsView | null>(null);
  readonly done = output<void>();

  protected readonly adminEmail = signal('');
  protected readonly adminName = signal('');
  protected readonly adminPassword = signal('');
  protected readonly adminPassword2 = signal('');
  protected readonly clientId = signal('');
  protected readonly host = signal('');
  protected readonly port = signal(587);
  protected readonly ssl = signal(true);
  protected readonly username = signal('');
  /** null = keep the stored password (only meaningful when one is stored). */
  protected readonly password = signal<string | null>('');
  protected readonly passwordSet = signal(false);
  protected readonly from = signal('');
  protected readonly actionBaseUrl = signal('');
  protected readonly recipient = signal('');

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly saved = signal(false);
  protected readonly googleResult = signal<TestResult | null>(null);
  protected readonly smtpResult = signal<TestResult | null>(null);
  private readonly googleTestedKey = signal<string | null>(null);
  private readonly smtpTestedKey = signal<string | null>(null);
  private initialGoogleKey: string | null = null;
  private initialSmtpKey: string | null = null;

  private readonly googleKey = computed(() => this.clientId().trim());
  private readonly smtpKey = computed(() => JSON.stringify([
    this.host().trim(), this.port(), this.ssl(), this.username().trim(), this.password(), this.from().trim(), this.actionBaseUrl().trim()]));

  protected readonly googlePassed = computed(() =>
    this.googleTestedKey() === this.googleKey() || (this.googleKey() !== '' && this.googleKey() === this.initialGoogleKey));
  protected readonly smtpPassed = computed(() =>
    this.smtpTestedKey() === this.smtpKey() || this.smtpKey() === this.initialSmtpKey);
  protected readonly testsPassed = computed(() => this.googlePassed() && this.smtpPassed());
  protected readonly canSave = computed(() => this.testsPassed() && (this.mode() === 'settings' || (
    this.adminEmail().trim() !== '' && this.adminName().trim().length >= 2 &&
    this.adminPassword().length >= 12 && this.adminPassword() === this.adminPassword2())));

  constructor() {
    effect(() => { const s = this.initial(); untracked(() => this.applyInitial(s)); });
  }

  private applied: SettingsView | null = null;
  private applyInitial(s: SettingsView | null): void {
    if (!s || s === this.applied) return;
    this.applied = s;
    this.clientId.set(s.googleClientId);
    this.host.set(s.smtpHost);
    this.port.set(s.smtpPort);
    this.ssl.set(s.smtpEnableSsl);
    this.username.set(s.smtpUsername ?? '');
    this.passwordSet.set(s.smtpPasswordSet);
    this.password.set(s.smtpPasswordSet ? null : '');
    this.from.set(s.emailFrom);
    this.actionBaseUrl.set(s.emailActionBaseUrl);
    this.initialGoogleKey = this.googleKey();
    this.initialSmtpKey = this.smtpKey();
  }

  private email(): EmailSettings {
    return {
      host: this.host().trim(), port: Number(this.port()), enableSsl: this.ssl(), username: this.username().trim(),
      password: this.password(), from: this.from().trim(), actionBaseUrl: this.actionBaseUrl().trim()
    };
  }

  protected async testGoogle(): Promise<void> {
    await this.run(async () => {
      const key = this.googleKey();
      const r = await this.api.testGoogle(key, this.mode() === 'settings');
      this.googleResult.set(r);
      this.googleTestedKey.set(r.ok ? key : null);
    });
  }

  protected async testSmtp(): Promise<void> {
    await this.run(async () => {
      const key = this.smtpKey();
      const r = await this.api.testSmtp(this.email(), this.recipient().trim(), this.mode() === 'settings');
      this.smtpResult.set(r);
      this.smtpTestedKey.set(r.ok ? key : null);
    });
  }

  protected async submit(): Promise<void> {
    if (!this.canSave()) return;
    await this.run(async () => {
      const google = { clientId: this.googleKey() };
      if (this.mode() === 'setup') {
        await this.api.complete(google, this.email(), {
          email: this.adminEmail().trim(), displayName: this.adminName().trim(), password: this.adminPassword()
        });
        this.done.emit();
      } else {
        await this.api.saveSettings(google, this.email());
        this.saved.set(true);
        this.initialGoogleKey = this.googleKey();
        this.initialSmtpKey = this.smtpKey();
        this.done.emit();
      }
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.saved.set(false);
    try { await action(); } catch (e) { this.error.set(errorMessage(e)); } finally { this.busy.set(false); }
  }
}
