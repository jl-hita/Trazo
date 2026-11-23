import { Component, DestroyRef, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiBadgeComponent, type UiBadgeTone } from '../../shared/ui/ui-badge.component';
import { UiTableComponent } from '../../shared/ui/ui-table.component';
import type { BackupAlertView, BackupCopy, BackupHealth, BackupRestoreMode, BackupStatus } from '../../core/admin.models';
import { BackupsApi } from './backups.api';
import { errorMessage } from '../../core/api-error';
import { UI_COPY } from '../../core/ui-copy';
import { formatUiBytes, formatUiDateTime } from '../../core/ui-format';

const TONES: Record<BackupHealth, UiBadgeTone> = { healthy: 'success', warning: 'warning', error: 'danger', unknown: 'neutral' };

/** Age of a moment as "3 h" or "2 d"; null when the date cannot be read. */
export function ageLabel(iso: string | null | undefined, now: number = Date.now()): string | null {
  const time = iso ? new Date(iso).getTime() : NaN;
  if (Number.isNaN(time)) return null;
  const hours = Math.max(0, Math.floor((now - time) / 3_600_000));
  return hours < 48 ? `${hours} h` : `${Math.floor(hours / 24)} d`;
}

/** The word the superadmin types to confirm a restore over production (the server checks it too). */
export const RESTORE_CONFIRMATION = 'RESTAURAR';
const FAST_POLL_MS = 4_000;
const SLOW_POLL_TICKS = 75; // 5 minutes
/** How long to keep watching for a request that never shows signs of starting. */
const WATCH_GIVE_UP_MS = 3 * 60_000;

/** A copy taken on a Sunday (UTC) is also what the weekly retention keeps. */
export function isWeekly(copy: BackupCopy): boolean {
  const date = new Date(copy.createdAt);
  return !Number.isNaN(date.getTime()) && date.getUTCDay() === 0;
}

/**
 * Backups of the installation (superadmin only): whether there is a recent encrypted copy in Drive, how the last run ended
 * and which copies exist. The server's own timer makes the daily copies; from here the superadmin can also take one now and
 * verify or restore any copy by pasting the age private key (it is sent once and never stored). Restoring over production asks
 * for a typed confirmation.
 */
@Component({
  selector: 'trazo-backups-page',
  standalone: true,
  imports: [FormsModule, UiButtonComponent, UiBadgeComponent, UiTableComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './backups.page.scss',
  templateUrl: './backups.page.html'
})
export class BackupsPageComponent {
  private readonly api = inject(BackupsApi);
  private readonly timer: ReturnType<typeof setInterval>;
  private ticks = 0;
  private watchSince = 0;
  private sawActivity = false;

  protected readonly copy = UI_COPY.backups;
  protected readonly status = signal<BackupStatus | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly latest = computed(() => this.status()?.copies[0] ?? null);
  protected readonly totalBytes = computed(() => (this.status()?.copies ?? []).reduce((sum, c) => sum + c.sizeBytes, 0));
  protected readonly tone = computed(() => TONES[this.status()?.health ?? 'unknown']);
  protected readonly healthLabel = computed(() => this.copy.health[this.status()?.health ?? 'unknown']);

  /** True from the moment a copy or restore is requested until it is seen to finish (the API may be down meanwhile). */
  protected readonly watching = signal(false);
  protected readonly serverDown = signal(false);
  protected readonly actionMessage = signal('');
  protected readonly actionError = signal('');
  protected readonly startingBackup = signal(false);
  protected readonly busy = computed(() => this.watching() || !!this.status()?.running || this.status()?.restore?.state === 'running');
  protected readonly restoreRecent = computed(() => {
    const restore = this.status()?.restore;
    if (!restore) return null;
    if (restore.state === 'running') return restore;
    const finished = restore.finishedAt ? new Date(restore.finishedAt).getTime() : NaN;
    return Number.isNaN(finished) || Date.now() - finished < 24 * 3_600_000 ? restore : null;
  });

  protected readonly restoreTarget = signal<BackupCopy | null>(null);
  protected readonly restoreMode = signal<BackupRestoreMode>('test');
  protected readonly restoreKey = signal('');
  protected readonly restoreConfirm = signal('');
  protected readonly restoring = signal(false);
  protected readonly restoreError = signal('');
  protected readonly confirmWord = RESTORE_CONFIRMATION;
  protected readonly canRestore = computed(() =>
    !this.restoring() && /AGE-SECRET-KEY-1[A-Za-z0-9]{10,}/.test(this.restoreKey()) &&
    (this.restoreMode() === 'test' || this.restoreConfirm().trim() === RESTORE_CONFIRMATION));

  protected readonly alerts = signal<BackupAlertView | null>(null);
  /** The id picked in the dropdown ('' = nobody); it only becomes the recipient when saved. */
  protected readonly choice = signal('');
  protected readonly saving = signal(false);
  protected readonly testing = signal(false);
  protected readonly alertMessage = signal('');
  protected readonly alertError = signal('');
  protected readonly dirty = computed(() => this.choice() !== (this.alerts()?.recipientUserId ?? ''));
  protected readonly chosenHasNoDevices = computed(() => {
    const picked = this.alerts()?.candidates.find(c => c.id === this.choice());
    return !!picked && picked.devices === 0;
  });

  constructor() {
    void this.load();
    void this.loadAlerts();
    // The files change once a day; a slow refresh keeps an open tab honest without hammering the API.
    // Quickly while a copy or restore is in progress (even if the API goes down and comes back); slowly otherwise.
    this.timer = setInterval(() => {
      this.ticks++;
      if (this.loading()) return;
      if (this.busy() || this.ticks % SLOW_POLL_TICKS === 0) void this.load(true);
    }, FAST_POLL_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(this.timer));
  }

  protected async backupNow(): Promise<void> {
    this.startingBackup.set(true);
    this.actionMessage.set('');
    this.actionError.set('');
    try {
      const result = await this.api.runBackup();
      this.actionMessage.set(result.message);
      this.startWatching();
    } catch (e) {
      this.actionError.set(errorMessage(e));
    } finally {
      this.startingBackup.set(false);
    }
  }

  protected openRestore(copy: BackupCopy): void {
    this.restoreTarget.set(copy);
    this.restoreMode.set('test');
    this.restoreError.set('');
    this.restoreKey.set('');
    this.restoreConfirm.set('');
  }

  protected closeRestore(): void {
    if (this.restoring()) return;
    this.restoreTarget.set(null);
    this.restoreKey.set('');
    this.restoreConfirm.set('');
  }

  protected async submitRestore(): Promise<void> {
    const target = this.restoreTarget();
    if (!target || !this.canRestore()) return;
    this.restoring.set(true);
    this.restoreError.set('');
    try {
      const result = await this.api.restore(target.name, this.restoreMode(), this.restoreKey(), this.restoreConfirm().trim());
      this.restoreTarget.set(null);
      this.restoreKey.set('');
      this.restoreConfirm.set('');
      this.actionError.set('');
      this.actionMessage.set(result.message);
      this.startWatching();
    } catch (e) {
      this.restoreError.set(errorMessage(e));
    } finally {
      this.restoring.set(false);
    }
  }

  protected refresh(): void { void this.load(); void this.loadAlerts(); }

  protected async saveRecipient(): Promise<void> {
    this.saving.set(true);
    this.alertMessage.set('');
    this.alertError.set('');
    try {
      const view = await this.api.saveAlerts(this.choice() || null);
      this.applyAlerts(view);
      this.alertMessage.set(view.recipientUserId ? this.copy.alerts.saved : this.copy.alerts.savedOff);
    } catch (e) {
      this.alertError.set(errorMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  protected async sendTest(): Promise<void> {
    this.testing.set(true);
    this.alertMessage.set('');
    this.alertError.set('');
    try {
      const result = await this.api.testAlert();
      (result.ok ? this.alertMessage : this.alertError).set(result.message);
    } catch (e) {
      this.alertError.set(errorMessage(e));
    } finally {
      this.testing.set(false);
    }
  }

  protected roleLabel(role: string): string { return this.copy.alerts.roles[role] ?? role; }

  protected readonly modeLabel = (mode: string | null) => mode === 'production' ? this.copy.restore.modeProduction : this.copy.restore.modeTest;
  protected readonly formatTime = (iso: string | null | undefined) => formatUiDateTime(iso);
  protected readonly formatSize = (bytes: number) => formatUiBytes(bytes);
  protected readonly age = (iso: string | null | undefined) => ageLabel(iso);
  protected readonly kind = (c: BackupCopy) => isWeekly(c) ? this.copy.weekly : this.copy.daily;

  private applyAlerts(view: BackupAlertView): void {
    this.alerts.set(view);
    this.choice.set(view.recipientUserId ?? '');
  }

  private async loadAlerts(): Promise<void> {
    try {
      this.applyAlerts(await this.api.alerts());
    } catch (e) {
      this.alertError.set(errorMessage(e) || this.copy.alerts.loadFailed);
    }
  }

  private startWatching(): void {
    this.watching.set(true);
    this.sawActivity = false;
    this.watchSince = Date.now();
    void this.load(true);
  }

  private async load(quiet = false): Promise<void> {
    if (!quiet) this.loading.set(true);
    try {
      const status = await this.api.status();
      this.status.set(status);
      this.error.set('');
      this.serverDown.set(false);
      if (status.running || status.restore?.state === 'running') this.sawActivity = true;
      else if (this.watching() && (this.sawActivity || Date.now() - this.watchSince > WATCH_GIVE_UP_MS)) this.watching.set(false);
    } catch (e) {
      // A production restore stops the API for a few minutes: while we are watching that is expected, not an error.
      if (this.watching()) { this.serverDown.set(true); this.sawActivity = true; } else this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
