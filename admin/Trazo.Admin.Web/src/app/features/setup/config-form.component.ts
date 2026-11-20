import { UI_COPY } from '../../core/ui-copy';
import { Component, ViewEncapsulation, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { EmailSettings, SetupApi, SettingsView, TestResult } from '../../core/setup.api';
import { errorMessage } from '../../core/api-error';

type Mode = 'setup' | 'settings';

/**
 * Shared configuration form (Google client ID + SMTP). In `setup` mode it also collects the
 * superadmin account. Each required part must pass its test before saving.
 */
@Component({
  selector: 'trazo-config-form',
  standalone: true,
  imports: [FormsModule, UiFormFieldComponent, UiButtonComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './config-form.component.scss',
  templateUrl: './config-form.component.html'
})
export class ConfigFormComponent {
  protected readonly labels = UI_COPY.setup;

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
  protected readonly emailEnabled = signal(true);
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
    this.emailEnabled.set(s.emailEnabled ?? true);
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
      password: this.password(), from: this.from().trim(), actionBaseUrl: this.actionBaseUrl().trim(),
      enabled: this.emailEnabled()
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
