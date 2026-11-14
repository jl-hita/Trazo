import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi, OperationsView } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

const LOG_LEVELS = ['Debug', 'Information', 'Warning', 'Error', 'Critical'];

/**
 * Operational settings: extra CORS origins, rate limits, log level and retention, DietoExpress transfer.
 * None of these is needed to start the API or to sign in, so a mistake here can always be fixed from this page.
 */
@Component({
  selector: 'trazo-operations-form',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .op { display: grid; gap: 22px; max-width: 760px; }
    .op fieldset { margin: 0; padding: 22px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 14px; }
    .op legend { padding: 0 8px; color: #a9c2df; font-size: 12px; letter-spacing: .12em; text-transform: uppercase; }
    .op label { display: grid; gap: 6px; color: #aab6c7; font-size: 12px; }
    .op input, .op textarea, .op select { padding: 10px 12px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font: inherit; }
    .op textarea { min-height: 90px; font-family: ui-monospace, Consolas, monospace; font-size: 12px; resize: vertical; }
    .op input:focus, .op textarea:focus, .op select:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .op .row { display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: 14px; }
    .op .hint { color: #7f8da0; font-size: 11px; margin: 0; }
    .op .actions { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .op button { padding: 10px 16px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; cursor: pointer; }
    .op button.primary { background: #3b6ea8; border-color: #6d9bd2; }
    .op button:disabled { opacity: .45; cursor: not-allowed; }
    .op .ok { color: #7fd3a0; font-size: 12px; }
    .op .err { color: #f0a3a3; font-size: 12px; }
    .op .stored { color: #7fd3a0; }
    .op ul { margin: 0; padding-left: 18px; color: #aab6c7; font-size: 12px; }
    .op .fielderr { color: #f0a3a3; font-size: 11px; }
  `],
  template: `
    <form class="op" (ngSubmit)="save()" autocomplete="off">
      <fieldset>
        <legend>Acceso web (CORS)</legend>
        <p class="hint">Orígenes desde los que un navegador puede llamar a la API. Los del archivo de entorno siempre están permitidos; aquí solo se <strong>añaden</strong> otros, así que un error nunca te deja sin acceso a esta pantalla.</p>
        @if (view()?.environmentCorsOrigins?.length) {
          <p class="hint">Fijos (entorno, solo lectura):</p>
          <ul>@for (o of view()!.environmentCorsOrigins; track o) { <li><code>{{ o }}</code></li> }</ul>
        }
        <label>Orígenes adicionales (uno por línea)
          <textarea name="origins" spellcheck="false" placeholder="https://admin.ejemplo.com" [ngModel]="origins()" (ngModelChange)="origins.set($event)"></textarea>
        </label>
        <p class="hint">Formato <code>https://dominio[:puerto]</code>, sin ruta. Se admite <code>http</code> solo para localhost. Máximo 20.</p>
        @if (fieldError('additionalCorsOrigins')) { <span class="fielderr" role="alert">{{ fieldError('additionalCorsOrigins') }}</span> }
      </fieldset>

      <fieldset>
        <legend>Límites de peticiones</legend>
        <p class="hint">Peticiones por minuto y por IP (o usuario). El límite del inicio de sesión es fijo y no se puede cambiar.</p>
        <div class="row">
          <label>Ajustes y asistente (10–600)
            <input type="number" name="setup" min="10" max="600" [ngModel]="setupPerMinute()" (ngModelChange)="setupPerMinute.set($event)">
            @if (fieldError('setupPerMinute')) { <span class="fielderr" role="alert">{{ fieldError('setupPerMinute') }}</span> }
          </label>
          <label>Pruebas de conexión (3–60)
            <input type="number" name="setupTest" min="3" max="60" [ngModel]="setupTestPerMinute()" (ngModelChange)="setupTestPerMinute.set($event)">
            @if (fieldError('setupTestPerMinute')) { <span class="fielderr" role="alert">{{ fieldError('setupTestPerMinute') }}</span> }
          </label>
          <label>Suscripciones (5–600)
            <input type="number" name="subscription" min="5" max="600" [ngModel]="subscriptionPerMinute()" (ngModelChange)="subscriptionPerMinute.set($event)">
            @if (fieldError('subscriptionPerMinute')) { <span class="fielderr" role="alert">{{ fieldError('subscriptionPerMinute') }}</span> }
          </label>
        </div>
      </fieldset>

      <fieldset>
        <legend>Registros</legend>
        <div class="row">
          <label>Nivel mínimo
            <select name="logLevel" [ngModel]="logLevel()" (ngModelChange)="logLevel.set($event)">
              @for (l of levels; track l) { <option [value]="l">{{ l }}</option> }
            </select>
            @if (fieldError('logLevel')) { <span class="fielderr" role="alert">{{ fieldError('logLevel') }}</span> }
          </label>
          <label>Conservar (días, 0 = siempre)
            <input type="number" name="retention" min="0" max="3650" [ngModel]="logRetentionDays()" (ngModelChange)="logRetentionDays.set($event)">
            @if (fieldError('logRetentionDays')) { <span class="fielderr" role="alert">{{ fieldError('logRetentionDays') }}</span> }
          </label>
        </div>
        <p class="hint">Con «Warning» o superior se dejan de guardar los mensajes informativos, pero los cambios de administración se siguen registrando.</p>
      </fieldset>

      <fieldset>
        <legend>DietoExpress</legend>
        <p class="hint">Destino al que se envían los ejercicios creados en TRAZO. La misma clave autentica la importación que recibe TRAZO.</p>
        <label>Dirección de DietoExpress
          <input type="text" name="dietoEndpoint" placeholder="https://dietoexpress.ejemplo.com" [ngModel]="dietoEndpoint()" (ngModelChange)="dietoEndpoint.set($event)">
          @if (fieldError('dietoExpressEndpoint')) { <span class="fielderr" role="alert">{{ fieldError('dietoExpressEndpoint') }}</span> }
        </label>
        <label>Clave compartida
          <input type="password" name="dietoKey" autocomplete="new-password" [placeholder]="keyPlaceholder()" [ngModel]="dietoKey()" (ngModelChange)="dietoKey.set($event)">
          @if (fieldError('dietoExpressApiKey')) { <span class="fielderr" role="alert">{{ fieldError('dietoExpressApiKey') }}</span> }
        </label>
        @if (view(); as v) {
          <p class="hint">
            @switch (v.dietoExpressApiKeySource) {
              @case ('database') { <span class="stored">Clave guardada (cifrada) en la base de datos.</span> No se puede volver a mostrar. }
              @case ('environment') { Se está usando la clave del archivo de entorno. Si guardas una aquí, tendrá prioridad. }
              @default { Sin clave configurada: la transferencia y la importación están desactivadas. }
            }
          </p>
        }
        <div class="actions">
          <button type="button" (click)="testDieto()" [disabled]="busy() || !dietoEndpoint().trim()">Probar conexión</button>
        </div>
      </fieldset>

      <div class="actions">
        <button class="primary" type="submit" [disabled]="busy()">Guardar ajustes</button>
        @if (message()) { <span class="ok" role="status">{{ message() }}</span> }
        @if (error()) { <span class="err" role="alert">{{ error() }}</span> }
      </div>

      <fieldset>
        <legend>Solo en el archivo de entorno</legend>
        <p class="hint">Estos valores no se editan aquí porque un error impediría arrancar la API o iniciar sesión. Cambiarlos exige editar <code>/etc/trazo/trazo.env</code> y <strong>reiniciar</strong> el servicio:</p>
        <ul>
          <li>Cadena de conexión a PostgreSQL y claves de Data Protection.</li>
          <li>Emisor, audiencia y clave de firma del JWT.</li>
          <li><code>AllowedHosts</code>, URL de escucha y orígenes CORS base.</li>
          <li>Activar o desactivar el almacenamiento de logs en base de datos.</li>
        </ul>
        <p class="hint">Más detalle en <code>docs/configuration.md</code>.</p>
      </fieldset>
    </form>
  `
})
export class OperationsFormComponent {
  private readonly api = inject(AdminApi);

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
