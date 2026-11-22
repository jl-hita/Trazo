import { Component, DestroyRef, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiBadgeComponent, type UiBadgeTone } from '../../shared/ui/ui-badge.component';
import { UiTableComponent } from '../../shared/ui/ui-table.component';
import type { BackupAlertView, BackupCopy, BackupHealth, BackupStatus } from '../../core/admin.models';
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

/** A copy taken on a Sunday (UTC) is also what the weekly retention keeps. */
export function isWeekly(copy: BackupCopy): boolean {
  const date = new Date(copy.createdAt);
  return !Number.isNaN(date.getTime()) && date.getUTCDay() === 0;
}

/**
 * Backups of the installation (superadmin only): whether there is a recent encrypted copy in Drive, how the last run ended
 * and which copies exist. Read-only: the server's own timer makes the copies and the key that opens them is never here.
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

  protected readonly copy = UI_COPY.backups;
  protected readonly status = signal<BackupStatus | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly latest = computed(() => this.status()?.copies[0] ?? null);
  protected readonly totalBytes = computed(() => (this.status()?.copies ?? []).reduce((sum, c) => sum + c.sizeBytes, 0));
  protected readonly tone = computed(() => TONES[this.status()?.health ?? 'unknown']);
  protected readonly healthLabel = computed(() => this.copy.health[this.status()?.health ?? 'unknown']);

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
    this.timer = setInterval(() => { if (!this.loading()) void this.load(true); }, 5 * 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(this.timer));
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

  private async load(quiet = false): Promise<void> {
    if (!quiet) this.loading.set(true);
    try {
      this.status.set(await this.api.status());
      this.error.set('');
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
}
