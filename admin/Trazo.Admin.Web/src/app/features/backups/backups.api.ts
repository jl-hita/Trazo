import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import type { BackupAlertTestResult, BackupAlertView, BackupStatus } from '../../core/admin.models';

@Injectable({ providedIn: 'root' })
export class BackupsApi {
  private readonly http = inject(ApiHttpClient);

  /** Read-only: what the server's backup scripts recorded (copies in Drive and how the last run ended). */
  status(): Promise<BackupStatus> {
    return this.http.get<BackupStatus>('/api/v1/admin/backups');
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
