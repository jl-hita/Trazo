import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import type { BackupActionResult, BackupAlertTestResult, BackupAlertView, BackupRestoreMode, BackupStatus } from '../../core/admin.models';

@Injectable({ providedIn: 'root' })
export class BackupsApi {
  private readonly http = inject(ApiHttpClient);

  /** Read-only: what the server's backup scripts recorded (copies in Drive and how the last run ended). */
  status(): Promise<BackupStatus> {
    return this.http.get<BackupStatus>('/api/v1/admin/backups');
  }

  /** Asks the server to take a backup now and upload it to Drive. */
  runBackup(): Promise<BackupActionResult> {
    return this.http.post<BackupActionResult>('/api/v1/admin/backups/run', {});
  }

  /**
   * Verifies a copy in the scratch database or restores it in production. The age private key is sent once, over HTTPS, and the
   * server only keeps it in memory-backed storage until its script has used it.
   */
  restore(copy: string, mode: BackupRestoreMode, key: string, confirmation: string): Promise<BackupActionResult> {
    return this.http.post<BackupActionResult>('/api/v1/admin/backups/restore', { copy, mode, key, confirmation });
  }

  /** Who receives the push notification when the backups go wrong, and the accounts that can be chosen. */
  alerts(): Promise<BackupAlertView> {
    return this.http.get<BackupAlertView>('/api/v1/admin/backups/alerts');
  }

  /** Chooses the recipient; null turns the notices off. */
  saveAlerts(userId: string | null): Promise<BackupAlertView> {
    return this.http.put<BackupAlertView>('/api/v1/admin/backups/alerts', { userId });
  }

  /** Sends a test notice to the saved recipient. */
  testAlert(): Promise<BackupAlertTestResult> {
    return this.http.post<BackupAlertTestResult>('/api/v1/admin/backups/alerts/test', {});
  }
}
