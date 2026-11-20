import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import type { OperationsView } from '../../core/admin.models';
import { SettingsApi } from './settings.api';
import { errorMessage } from '../../core/setup.api';

const LOG_LEVELS = ['Debug', 'Information', 'Warning', 'Error', 'Critical'];

/**
 * Operational settings: extra CORS origins, rate limits, log level and retention, DietoExpress transfer.
 * None of these is needed to start the API or to sign in, so a mistake here can always be fixed from this page.
 */
@Component({
  selector: 'trazo-operations-form',
  standalone: true,
  imports: [FormsModule, UiFormFieldComponent, UiButtonComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './operations-form.component.scss',
  templateUrl: './operations-form.component.html'
})
export class OperationsFormComponent {
  private readonly api = inject(SettingsApi);

  protected readonly levels = LOG_LEVELS;
  protected readonly view = signal<OperationsView | null>(null);
  protected readonly origins = signal('');
  protected readonly setupPerMinute = signal(30);
  protected readonly setupTestPerMinute = signal(5);
  protected readonly subscriptionPerMinute = signal(30);
  protected readonly logLevel = signal('Information');
  protected readonly logRetentionDays = signal(30);
  protected readonly dietoEndpoint = signal('');
  protected readonly dietoKey = signal('');
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');
  protected readonly fieldErrors = signal<Record<string, string>>({});

  constructor() { void this.load(); }

  protected keyPlaceholder(): string {
    return this.view()?.dietoExpressApiKeySet ? 'Guardada (déjalo vacío para conservarla)' : '';
  }

  protected fieldError(field: string): string { return this.fieldErrors()[field] ?? ''; }

  protected async save(): Promise<void> {
    this.busy.set(true); this.message.set(''); this.error.set(''); this.fieldErrors.set({});
    try {
      const saved = await this.api.saveOperations({
        additionalCorsOrigins: this.origins().split(/[\n,;]+/).map(x => x.trim()).filter(x => x.length > 0),
        setupPerMinute: Number(this.setupPerMinute()),
        setupTestPerMinute: Number(this.setupTestPerMinute()),
        subscriptionPerMinute: Number(this.subscriptionPerMinute()),
        logLevel: this.logLevel(),
        logRetentionDays: Number(this.logRetentionDays()),
        dietoExpressEndpoint: this.dietoEndpoint().trim(),
        // Empty keeps the stored key; only a typed value replaces it.
        dietoExpressApiKey: this.dietoKey() || null
      });
      this.apply(saved);
      this.message.set('Ajustes guardados y aplicados.');
    } catch (e) {
      this.fieldErrors.set(validationErrors(e));
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async testDieto(): Promise<void> {
    this.busy.set(true); this.message.set(''); this.error.set('');
    try {
      const result = await this.api.testDietoExpress(this.dietoEndpoint().trim());
      (result.ok ? this.message : this.error).set(result.message);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try { this.apply(await this.api.getOperations()); } catch (e) { this.error.set(errorMessage(e)); }
  }

  private apply(v: OperationsView): void {
    this.view.set(v);
    this.origins.set(v.additionalCorsOrigins.join('\n'));
    this.setupPerMinute.set(v.setupPerMinute);
    this.setupTestPerMinute.set(v.setupTestPerMinute);
    this.subscriptionPerMinute.set(v.subscriptionPerMinute);
    this.logLevel.set(v.logLevel);
    this.logRetentionDays.set(v.logRetentionDays);
    this.dietoEndpoint.set(v.dietoExpressEndpoint);
    this.dietoKey.set('');
  }
}

/** ASP.NET validation problems: { errors: { field: [messages] } } -> first message per field. */
function validationErrors(e: unknown): Record<string, string> {
  const errors = (e as { error?: { errors?: Record<string, string[]> } })?.error?.errors;
  if (!errors) return {};
  return Object.fromEntries(Object.entries(errors).map(([k, v]) => [k, v[0]]));
}
