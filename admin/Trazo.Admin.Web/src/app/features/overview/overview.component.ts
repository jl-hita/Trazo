import { OVERVIEW_COPY } from './overview.copy';
import { formatUiDateTime, formatUiNumber, formatUiShortDate } from '../../core/ui-format';
import { Component, DestroyRef, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import type { AlertSeverity, Overview } from '../../core/admin.models';
import { OverviewApi } from './overview.api';
import { errorMessage } from '../../core/api-error';
import { UiBadgeComponent, type UiBadgeTone } from '../../shared/ui/ui-badge.component';
import { UiCardComponent } from '../../shared/ui/ui-card.component';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiStateComponent } from '../../shared/ui/ui-state.component';
import { UiTableComponent } from '../../shared/ui/ui-table.component';

type Tone = 'ok' | 'warn' | 'crit' | 'info';

interface Status { tone: Tone; label: string; }

const SEVERITY_LABEL: Record<AlertSeverity, string> = { critical: OVERVIEW_COPY.severity.critical, warning: OVERVIEW_COPY.severity.warning, info: OVERVIEW_COPY.severity.info };
const SEVERITY_TONE: Record<AlertSeverity, Tone> = { critical: 'crit', warning: 'warn', info: 'info' };
/** Glyphs are decorative (aria-hidden); the meaning is always carried by the text next to them. */
const TONE_GLYPH: Record<Tone, string> = { ok: '✓', warn: '!', crit: '✕', info: 'i' };

const SUBSCRIPTION_LABEL: Record<string, string> = OVERVIEW_COPY.subscriptionStatuses;

const PAYWALL_SOURCE_LABEL: Record<string, string> = OVERVIEW_COPY.paywallSources;

const ADAPTATION_LABEL: Record<string, string> = OVERVIEW_COPY.adaptations;

const REASON_LABEL: Record<string, string> = OVERVIEW_COPY.reasons;

/**
 * Operational overview: system health, alerts that say what to do next, and quick links to the areas that need attention.
 * The API caches the result for a short time and aggregates only (no personal or health data).
 */
@Component({
  selector: 'trazo-overview',
  standalone: true,
  imports: [RouterLink, UiBadgeComponent, UiButtonComponent, UiCardComponent, UiStateComponent, UiTableComponent],
  templateUrl: './overview.component.html',
  styleUrl: './overview.component.scss',
  encapsulation: ViewEncapsulation.None
})
export class OverviewComponent {
  protected readonly labels = OVERVIEW_COPY;
  private readonly api = inject(OverviewApi);
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
    if (!h) return { tone: 'info', label: OVERVIEW_COPY.statuses.noData };
    if (!h.databaseOk) return { tone: 'crit', label: OVERVIEW_COPY.statuses.databaseDown };
    if (h.pendingMigrations.length > 0) return { tone: 'crit', label: OVERVIEW_COPY.statuses.pendingMigrations };
    return { tone: 'ok', label: OVERVIEW_COPY.statuses.allGood };
  });

  protected readonly logsStatus = computed<Status>(() => {
    const l = this.data()?.logs;
    if (!l) return { tone: 'info', label: OVERVIEW_COPY.statuses.noData };
    if (l.errors24h === 0) return { tone: 'ok', label: OVERVIEW_COPY.statuses.noErrors };
    const rising = l.errors24h >= 5 && l.errors24h >= 2 * Math.max(l.errorsPrevious24h, 1);
    return { tone: rising ? 'crit' : 'warn', label: rising ? OVERVIEW_COPY.statuses.errorsRising : OVERVIEW_COPY.statuses.hasErrors };
  });

  protected readonly smtp = computed<Status>(() => {
    const c = this.data()?.configuration;
    if (!c) return { tone: 'info', label: OVERVIEW_COPY.statuses.noData };
    if (!c.smtpConfigured) return { tone: 'warn', label: OVERVIEW_COPY.statuses.notConfigured };
    if (c.lastSmtpSend && !c.lastSmtpSend.succeeded) return { tone: 'crit', label: OVERVIEW_COPY.statuses.lastSendFailed };
    return { tone: 'ok', label: c.lastSmtpSend ? OVERVIEW_COPY.statuses.works : OVERVIEW_COPY.statuses.configured };
  });

  protected readonly google = computed<Status>(() =>
    this.data()?.configuration.googleConfigured ? { tone: 'ok', label: OVERVIEW_COPY.statuses.configured } : { tone: 'warn', label: OVERVIEW_COPY.statuses.notConfigured });

  protected readonly play = computed<Status>(() => {
    const c = this.data()?.configuration;
    if (!c) return { tone: 'info', label: OVERVIEW_COPY.statuses.noData };
    if (!c.playBillingConfigured) return c.subscriptionsEnforced ? { tone: 'crit', label: OVERVIEW_COPY.statuses.notConfigured } : { tone: 'info', label: OVERVIEW_COPY.statuses.configuredOptional };
    return c.playRtdnConfigured ? { tone: 'ok', label: OVERVIEW_COPY.statuses.configuredWithRtdn } : { tone: 'warn', label: OVERVIEW_COPY.statuses.withoutRtdn };
  });

  protected readonly catalog = computed<Status>(() => {
    const s = this.data()?.summary;
    if (!s) return { tone: 'info', label: OVERVIEW_COPY.statuses.noData };
    if (s.exercisesUsable === 0) return { tone: 'crit', label: OVERVIEW_COPY.statuses.noUsableExercises };
    if (s.exercisesUnusable > 0 || s.exercisesWithoutImages > 0) return { tone: 'warn', label: OVERVIEW_COPY.statuses.needsReview };
    return { tone: 'ok', label: OVERVIEW_COPY.statuses.correct };
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
        label: formatUiShortDate(d.date),
        signupsH: (d.signups / max) * 100, plansH: (d.plans / max) * 100, workoutsH: (d.workouts / max) * 100
      }))
    };
  });

  protected readonly funnel = computed(() => {
    const f = this.data()?.insights.funnel;
    if (!f) return [];
    const base = Math.max(1, f.registered);
    return [
      { label: OVERVIEW_COPY.funnel.registered, count: f.registered, hint: OVERVIEW_COPY.funnel.registeredHint },
      { label: OVERVIEW_COPY.funnel.verified, count: f.emailVerified, hint: OVERVIEW_COPY.funnel.verifiedHint },
      { label: OVERVIEW_COPY.funnel.withPlan, count: f.withPlan, hint: OVERVIEW_COPY.funnel.withPlanHint },
      { label: OVERVIEW_COPY.funnel.withWorkout, count: f.withWorkout, hint: OVERVIEW_COPY.funnel.withWorkoutHint },
      { label: OVERVIEW_COPY.funnel.recurring, count: f.recurring, hint: OVERVIEW_COPY.funnel.recurringHint }
    ].map(step => ({ ...step, percent: Math.round((100 * step.count) / base) }));
  });

  /** Sign-up cohorts by week: share that started a subscription (trial or paid) and share that has paid. */
  protected readonly cohorts = computed(() =>
    (this.data()?.insights.conversion.cohorts ?? []).map(c => ({
      ...c,
      label: formatUiShortDate(c.weekStart),
      subscribedPercent: c.signups === 0 ? null : Math.round((100 * c.subscribed) / c.signups),
      paidPercent: c.signups === 0 ? null : Math.round((100 * c.paid) / c.signups)
    })).reverse());

  protected readonly paywall = computed(() => {
    const p = this.data()?.insights.conversion.paywall;
    return {
      days: p?.days ?? 30,
      views: p?.views ?? 0,
      sources: (p?.bySource ?? []).map(x => ({ label: PAYWALL_SOURCE_LABEL[x.name] ?? x.name, count: x.count }))
    };
  });

  protected readonly adaptations = computed(() =>
    (this.data()?.insights.engine.adaptationsByKind ?? []).map(x => ({ label: ADAPTATION_LABEL[x.name] ?? x.name, count: x.count })));

  protected readonly reasons = computed(() =>
    (this.data()?.insights.engine.notCompletedReasons ?? []).map(x => ({ label: REASON_LABEL[x.name] ?? x.name, count: x.count })));

  protected readonly alertSummary = computed(() => {
    const n = this.alerts().length;
    return n === 0 ? OVERVIEW_COPY.noAlerts : n === 1 ? OVERVIEW_COPY.oneAlert : `${n} ${OVERVIEW_COPY.alertsSuffix}`;
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

  protected badgeTone(tone: Tone): UiBadgeTone {
    return tone === 'ok' ? 'success' : tone === 'warn' ? 'warning' : tone === 'crit' ? 'danger' : 'info';
  }

  /** Alert and card links are admin routes with an optional query string; the router parses them into a UrlTree. */
  protected tree(link: string) { return this.router.parseUrl(link); }

  protected when(iso: string): string { return formatUiDateTime(iso); }

  protected format(n: number): string { return formatUiNumber(n, 0); }

  private async load(refresh: boolean, announce: boolean): Promise<void> {
    const request = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    try {
      const result = await this.api.overview(refresh);
      if (request !== this.requestId) return; // superseded or the page was left
      this.data.set(result);
      if (announce) this.announcement.set(`${OVERVIEW_COPY.announcements.refreshed} ${result.alerts.length === 0 ? OVERVIEW_COPY.announcements.noAlerts : `${result.alerts.length} ${OVERVIEW_COPY.alertsSuffix}.`}`);
    } catch (e) {
      if (request !== this.requestId) return;
      const status = (e as { status?: number })?.status;
      this.error.set(errorMessage(e, OVERVIEW_COPY.refreshError));
      if (announce) this.announcement.set(OVERVIEW_COPY.announcements.failed);
    } finally {
      if (request === this.requestId) this.loading.set(false);
    }
  }
}
