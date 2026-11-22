import { Injectable, inject } from '@angular/core';
import { ApiHttpClient } from '../../core/api-http.client';
import type { BackupStatus } from '../../core/admin.models';

@Injectable({ providedIn: 'root' })
export class BackupsApi {
  private readonly http = inject(ApiHttpClient);

  /** Read-only: what the server's backup scripts recorded (copies in Drive and how the last run ended). */
  status(): Promise<BackupStatus> {
    return this.http.get<BackupStatus>('/api/v1/admin/backups');
  }
}
