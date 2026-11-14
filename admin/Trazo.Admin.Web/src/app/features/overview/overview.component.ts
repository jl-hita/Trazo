import { Component, DestroyRef, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AdminApi, AlertSeverity, Overview } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

type Tone = 'ok' | 'warn' | 'crit' | 'info';

interface Status { tone: Tone; label: string; }

const SEVERITY_LABEL: Record<AlertSeverity, string> = { critical: 'Crítica', warning: 'Aviso', info: 'Información' };
const SEVERITY_TONE: Record<AlertSeverity, Tone> = { critical: 'crit', warning: 'warn', info: 'info' };
/** Glyphs are decorative (aria-hidden); the meaning is always carried by the text next to them. */
const TONE_GLYPH: Record<Tone, string> = { ok: '✓', warn: '!', crit: '✕', info: 'i' };

const SUBSCRIPTION_LABEL: Record<string, string> = {
  pending: 'Pendientes', trial: 'En prueba', active: 'Activas', grace_period: 'En periodo de gracia',
  on_hold: 'En espera', canceled: 'Canceladas', expired: 'Caducadas', revoked: 'Revocadas'
};

const ADAPTATION_LABEL: Record<string, string> = {
  reduction_sets: 'Series reducidas', raise_reserve: 'Más margen (RIR)', restore_sets: 'Series restauradas',
  deload: 'Descargas', hold_progression: 'Progresión retenida', load_update: 'Cargas actualizadas'
};

const REASON_LABEL: Record<string, string> = {
  Time: 'Tiempo', Fatigue: 'Fatiga', Equipment: 'Material', Motivation: 'Motivación', Illness: 'Enfermedad', Other: 'Otros'
};

/**
 * Operational overview: system health, alerts that say what to do next, and quick links to the areas that need attention.
 * The API caches the result for a short time and aggregates only (no personal or health data).
 */
@Component({
  selector: 'trazo-overview',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './overview.component.html',
  styleUrl: './overview.component.scss',
  encapsulation: ViewEncapsulation.None
})
export class OverviewComponent {
  private readonly api = inject(AdminApi);
  private readonly router = inject(Router);
  private requestId = 0;

  protected readonly data = signal<Overview | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal('');
  /** Text for the polite live region: announces the outcome of a refresh to screen readers. */
  protected readonly announcement = signal('');
  protected readonly skeletons = [1, 2, 3, 4, 5, 6];

  protected readonly alerts = computed(() => this.data()?.alerts ?? []);

  protected readonly health = computed<Status>(() => {
    const h = this.data()?.health;
    if (!h) return { tone: 'info', label: 'Sin datos' };
    if (!h.databaseOk) return { tone: 'crit', label: 'Base de datos caída' };
    if (h.pendingMigrations.length > 0) return { tone: 'crit', label: 'Migraciones pendientes' };
    return { tone: 'ok', label: 'Todo correcto' };
  });

  protected readonly logsStatus = computed<Status>(() => {
    const l = this.data()?.logs;
    if (!l) return { tone: 'info', label: 'Sin datos' };
    if (l.errors24h === 0) return { tone: 'ok', label: 'Sin errores en 24 h' };
    const rising = l.errors24h >= 5 && l.errors24h >= 2 * Math.max(l.errorsPrevious24h, 1);
    return { tone: rising ? 'crit' : 'warn', label: rising ? 'Errores en aumento' : 'Hay errores' };
  });

  protected readonly smtp = computed<Status>(() => {
    const c = this.data()?.configuration;
    if (!c) return { tone: 'info', label: 'Sin datos' };
    if (!c.smtpConfigured) return { tone: 'warn', label: 'Sin configurar' };
    if (c.lastSmtpSend && !c.lastSmtpSend.succeeded) return { tone: 'crit', label: 'Último envío fallido' };
    return { tone: 'ok', label: c.lastSmtpSend ? 'Funciona' : 'Configurado' };
  });

  protected readonly google = computed<Status>(() =>
    this.data()?.configuration.googleConfigured ? { tone: 'ok', label: 'Configurado' } : { tone: 'warn', label: 'Sin configurar' });

  protected readonly play = computed<Status>(() => {
    const c = this.data()?.configuration;
    if (!c) return { tone: 'info', label: 'Sin datos' };
    if (!c.playBillingConfigured) return c.subscriptionsEnforced ? { tone: 'crit', label: 'Sin configurar' } : { tone: 'info', label: 'Sin configurar (opcional)' };
    return c.playRtdnConfigured ? { tone: 'ok', label: 'Configurado con RTDN' } : { tone: 'warn', label: 'Sin RTDN' };
  });

  protected readonly catalog = computed<Status>(() => {
    const s = this.data()?.summary;
    if (!s) return { tone: 'info', label: 'Sin datos' };
    if (s.exercisesUsable === 0) return { tone: 'crit', label: 'Sin ejercicios utilizables' };
    if (s.exercisesUnusable > 0 || s.exercisesWithoutImages > 0) return { tone: 'warn', label: 'Requiere revisión' };
    return { tone: 'ok', label: 'Correcto' };
  });

  protected readonly subscriptions = computed(() =>
    (this.data()?.summary.subscriptionsByStatus ?? []).filter(x => x.count > 0)
      .map(x => ({ label: SUBSCRIPTION_LABEL[x.name] ?? x.name, count: x.count })));

  /** Daily activity of the last 14 days; heights are relative to the busiest day so small numbers stay readable. */
  protected readonly chart = computed(() => {
    const days = this.data()?.insights.activity.days ?? [];
    const max = Math.max(1, ...days.flatMap(d => [d.signups, d.plans, d.workouts]));
    const total = (pick: (d: (typeof days)[number]) => number) => days.reduce((sum, d) => sum + pick(d), 0);
    return {
      max,
      empty: days.every(d => d.signups + d.plans + d.workouts === 0),
      totals: { signups: total(d => d.signups), plans: total(d => d.plans), workouts: total(d => d.workouts) },
      days: days.map(d => ({
        ...d,
        label: new Date(d.date + 'T00:00:00').toLocaleDateString('es-ES', { day: 'numeric', month: 'short' }),
        signupsH: (d.signups / max) * 100, plansH: (d.plans / max) * 100, workoutsH: (d.workouts / max) * 100
      }))
    };
  });

  protected readonly funnel = computed(() => {
    const f = this.data()?.insights.funnel;
    if (!f) return [];
    const base = Math.max(1, f.registered);
    return [
      { label: 'Registrados', count: f.registered, hint: 'Cuentas creadas' },
      { label: 'Correo verificado', count: f.emailVerified, hint: 'Correo verificado o acceso con Google' },
      { label: 'Con un plan', count: f.withPlan, hint: 'Han generado al menos un plan' },
      { label: 'Con una sesión registrada', count: f.withWorkout, hint: 'Han registrado al menos un entrenamiento' },
      { label: 'Constantes', count: f.recurring, hint: 'Con 3 o más sesiones registradas' }
    ].map(step => ({ ...step, percent: Math.round((100 * step.count) / base) }));
  });

  protected readonly adaptations = computed(() =>
    (this.data()?.insights.engine.adaptationsByKind ?? []).map(x => ({ label: ADAPTATION_LABEL[x.name] ?? x.name, count: x.count })));

  protected readonly reasons = computed(() =>
    (this.data()?.insights.engine.notCompletedReasons ?? []).map(x => ({ label: REASON_LABEL[x.name] ?? x.name, count: x.count })));

  protected readonly alertSummary = computed(() => {
    const n = this.alerts().length;
    return n === 0 ? 'Sin alertas' : n === 1 ? '1 alerta' : `${n} alertas`;
  });

  constructor() {
    void this.load(false, false);
    inject(DestroyRef).onDestroy(() => { this.requestId++; });
  }

  protected retry(): void { void this.load(false, false); }

  protected refresh(): void { void this.load(true, true); }

  protected severityLabel(severity: AlertSeverity): string { return SEVERITY_LABEL[severity]; }
  protected severityTone(severity: AlertSeverity): Tone { return SEVERITY_TONE[severity]; }
  protected glyph(tone: Tone): string { return TONE_GLYPH[tone]; }

  /** Alert and card links are admin routes with an optional query string; the router parses them into a UrlTree. */
  protected tree(link: string) { return this.router.parseUrl(link); }

  protected when(iso: string): string {
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString('es-ES', { dateStyle: 'short', timeStyle: 'short' });
  }

  protected format(n: number): string { return n.toLocaleString('es-ES'); }

  private async load(refresh: boolean, announce: boolean): Promise<void> {
    const request = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    try {
      const result = await this.api.overview(refresh);
      if (request !== this.requestId) return; // superseded or the page was left
      this.data.set(result);
      if (announce) this.announcement.set(`Datos actualizados. ${result.alerts.length === 0 ? 'Sin alertas.' : `${result.alerts.length} alertas.`}`);
    } catch (e) {
      if (request !== this.requestId) return;
      const status = (e as { status?: number })?.status;
      this.error.set(status === 503 ? 'La base de datos no responde.' : errorMessage(e));
      if (announce) this.announcement.set('No se han podido actualizar los datos.');
    } finally {
      if (request === this.requestId) this.loading.set(false);
    }
  }
}
