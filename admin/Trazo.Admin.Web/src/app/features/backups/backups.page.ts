import { Component, DestroyRef, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiBadgeComponent, type UiBadgeTone } from '../../shared/ui/ui-badge.component';
import { UiTableComponent } from '../../shared/ui/ui-table.component';
import type { BackupCopy, BackupHealth, BackupStatus } from '../../core/admin.models';
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
  imports: [UiButtonComponent, UiBadgeComponent, UiTableComponent],
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

  constructor() {
    void this.load();
    // The files change once a day; a slow refresh keeps an open tab honest without hammering the API.
    this.timer = setInterval(() => { if (!this.loading()) void this.load(true); }, 5 * 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(this.timer));
  }

  protected refresh(): void { void this.load(); }

  protected readonly formatTime = (iso: string | null | undefined) => formatUiDateTime(iso);
  protected readonly formatSize = (bytes: number) => formatUiBytes(bytes);
  protected readonly age = (iso: string | null | undefined) => ageLabel(iso);
  protected readonly kind = (c: BackupCopy) => isWeekly(c) ? this.copy.weekly : this.copy.daily;

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
