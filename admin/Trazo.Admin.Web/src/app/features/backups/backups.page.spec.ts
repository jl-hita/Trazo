import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { BackupStatus } from '../../core/admin.models';
import { formatUiBytes } from '../../core/ui-format';
import { BackupsApi } from './backups.api';
import { BackupsPageComponent, ageLabel, isWeekly } from './backups.page';

const status = (over: Partial<BackupStatus> = {}): BackupStatus => ({
  configured: true, health: 'healthy', message: 'Hay una copia reciente en Drive.', checkedAt: '2026-11-22T09:00:00Z',
  maxAgeHours: 36, remote: 'trazo-drive:trazo-backups',
  lastRun: { ok: true, startedAt: '2026-11-22T03:38:00Z', finishedAt: '2026-11-22T03:39:00Z', copy: 'trazo-20261122T033800Z.tar.age', error: null },
  copies: [
    { name: 'trazo-20261122T033800Z.tar.age', sizeBytes: 4_700_000, createdAt: '2026-11-22T03:38:00Z' },
    { name: 'trazo-20261121T033800Z.tar.age', sizeBytes: 4_600_000, createdAt: '2026-11-21T03:38:00Z' }
  ],
  ...over
});

async function render(api: { status: ReturnType<typeof vi.fn> }) {
  await TestBed.configureTestingModule({
    imports: [BackupsPageComponent],
    providers: [{ provide: BackupsApi, useValue: api }]
  }).compileComponents();
  const fixture = TestBed.createComponent(BackupsPageComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

describe('BackupsApi', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('reads the status from the superadmin endpoint', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const result = TestBed.inject(BackupsApi).status();
    const request = http.expectOne('/api/v1/admin/backups');
    expect(request.request.method).toBe('GET');
    request.flush(status());
    await expect(result).resolves.toMatchObject({ health: 'healthy' });
    http.verify();
  });
});

describe('BackupsPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows the health, the latest copy and a row per copy, marking the Sunday ones as weekly', async () => {
    const fixture = await render({ status: vi.fn().mockResolvedValue(status()) });
    const text: string = fixture.nativeElement.textContent;

    expect(text).toContain('Correcto');
    expect(text).toContain('Hay una copia reciente en Drive.');
    expect(text).toContain('4,7 MB');
    expect(text).toContain('trazo-20261122T033800Z.tar.age');
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(2);
    expect(text).toContain('Semanal (domingo)'); // 2026-11-22 is a Sunday
    expect(text).toContain('Diaria');
  });

  it('shows a failed last run with its error', async () => {
    const fixture = await render({ status: vi.fn().mockResolvedValue(status({
      health: 'error', message: 'La última ejecución del backup ha fallado.',
      lastRun: { ok: false, startedAt: null, finishedAt: '2026-11-22T03:39:00Z', copy: null, error: 'Fallo en el paso subida a Drive' }
    })) });
    const text: string = fixture.nativeElement.textContent;

    expect(text).toContain('Fallida');
    expect(text).toContain('Fallo en el paso subida a Drive');
    expect(fixture.nativeElement.querySelector('.bk-status').getAttribute('data-health')).toBe('error');
  });

  it('explains that nothing is recorded yet instead of showing empty figures', async () => {
    const fixture = await render({ status: vi.fn().mockResolvedValue(status({
      configured: false, health: 'unknown', message: null, checkedAt: null, lastRun: null, copies: []
    })) });
    const text: string = fixture.nativeElement.textContent;

    expect(text).toContain('Sin datos');
    expect(text).toContain('docs/backups.md');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
  });

  it('shows the error when the status cannot be read', async () => {
    const fixture = await render({ status: vi.fn().mockRejectedValue(new Error('boom')) });
    expect(fixture.nativeElement.querySelector('[role=alert]')).not.toBeNull();
  });
});

describe('backup helpers', () => {
  it('formats the age of a copy in hours and then days', () => {
    const now = new Date('2026-11-22T12:00:00Z').getTime();
    expect(ageLabel('2026-11-22T09:00:00Z', now)).toBe('3 h');
    expect(ageLabel('2026-11-19T12:00:00Z', now)).toBe('3 d');
    expect(ageLabel(null, now)).toBeNull();
    expect(ageLabel('nope', now)).toBeNull();
  });

  it('treats Sunday copies as weekly', () => {
    expect(isWeekly({ name: 'a', sizeBytes: 1, createdAt: '2026-11-22T03:38:00Z' })).toBe(true);
    expect(isWeekly({ name: 'b', sizeBytes: 1, createdAt: '2026-11-21T03:38:00Z' })).toBe(false);
  });

  it('formats sizes with decimal units', () => {
    expect(formatUiBytes(512)).toBe('512 B');
    expect(formatUiBytes(4_700_000)).toBe('4,7 MB');
    expect(formatUiBytes(514_000_000)).toBe('514 MB');
    expect(formatUiBytes(-1)).toBe('—');
  });
});
