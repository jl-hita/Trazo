import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { BackupAlertView, BackupStatus } from '../../core/admin.models';
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

const alertView = (over: Partial<BackupAlertView> = {}): BackupAlertView => ({
  recipientUserId: null, recipientName: null, recipientDevices: 0,
  candidates: [
    { id: 'u-ana', displayName: 'Ana', email: 'ana@trazo.test', role: 'superadmin', devices: 1 },
    { id: 'u-beto', displayName: 'Beto', email: 'beto@trazo.test', role: 'admin', devices: 0 }
  ],
  ...over
});

type FakeApi = {
  status: ReturnType<typeof vi.fn>; alerts?: ReturnType<typeof vi.fn>;
  saveAlerts?: ReturnType<typeof vi.fn>; testAlert?: ReturnType<typeof vi.fn>;
};

async function render(api: FakeApi) {
  const full = { alerts: vi.fn().mockResolvedValue(alertView()), saveAlerts: vi.fn(), testAlert: vi.fn(), ...api };
  await TestBed.configureTestingModule({
    imports: [BackupsPageComponent],
    providers: [{ provide: BackupsApi, useValue: full }]
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

  it('reads, saves and tests the notification recipient', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const api = TestBed.inject(BackupsApi);

    const read = api.alerts();
    const get = http.expectOne('/api/v1/admin/backups/alerts');
    expect(get.request.method).toBe('GET');
    get.flush(alertView());
    await read;

    const save = api.saveAlerts('u-ana');
    const put = http.expectOne('/api/v1/admin/backups/alerts');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ userId: 'u-ana' });
    put.flush(alertView({ recipientUserId: 'u-ana' }));
    await save;

    const test = api.testAlert();
    const post = http.expectOne('/api/v1/admin/backups/alerts/test');
    expect(post.request.method).toBe('POST');
    post.flush({ ok: true, message: 'Enviada' });
    await expect(test).resolves.toMatchObject({ ok: true });
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

describe('BackupsPageComponent notifications', () => {
  afterEach(() => TestBed.resetTestingModule());

  const pick = async (fixture: Awaited<ReturnType<typeof render>>, id: string) => {
    const select: HTMLSelectElement = fixture.nativeElement.querySelector('#bk-recipient');
    select.value = id;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const button = (fixture: Awaited<ReturnType<typeof render>>, label: string): HTMLButtonElement =>
    Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('.bk-alerts button')).find(b => b.textContent?.includes(label))!;

  it('offers the staff accounts in a dropdown, with nobody selected at first', async () => {
    const fixture = await render({ status: vi.fn().mockResolvedValue(status()) });
    const options = Array.from<HTMLOptionElement>(fixture.nativeElement.querySelectorAll('#bk-recipient option')).map(o => o.textContent ?? '');

    expect(options.length).toBe(3);
    expect(options[0]).toContain('Nadie');
    expect(options[1]).toContain('Ana');
    expect(options[1]).toContain('superadministración');
    expect(button(fixture, 'Guardar').disabled).toBe(true);
    expect(button(fixture, 'prueba').disabled).toBe(true);
  });

  it('saves the chosen account and then allows the test', async () => {
    const saveAlerts = vi.fn().mockResolvedValue(alertView({ recipientUserId: 'u-ana', recipientName: 'Ana', recipientDevices: 1 }));
    const testAlert = vi.fn().mockResolvedValue({ ok: true, message: 'Enviada a 1 de 1 dispositivos.' });
    const fixture = await render({ status: vi.fn().mockResolvedValue(status()), saveAlerts, testAlert });

    await pick(fixture, 'u-ana');
    expect(button(fixture, 'Guardar').disabled).toBe(false);
    expect(button(fixture, 'prueba').disabled).toBe(true);

    button(fixture, 'Guardar').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(saveAlerts).toHaveBeenCalledWith('u-ana');
    expect(fixture.nativeElement.textContent).toContain('Destinatario guardado.');
    expect(button(fixture, 'prueba').disabled).toBe(false);

    button(fixture, 'prueba').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(testAlert).toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Enviada a 1 de 1 dispositivos.');
  });

  it('turns the notices off by choosing nobody', async () => {
    const saveAlerts = vi.fn().mockResolvedValue(alertView());
    const fixture = await render({
      status: vi.fn().mockResolvedValue(status()),
      alerts: vi.fn().mockResolvedValue(alertView({ recipientUserId: 'u-ana', recipientName: 'Ana', recipientDevices: 1 })),
      saveAlerts
    });

    await pick(fixture, '');
    button(fixture, 'Guardar').click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(saveAlerts).toHaveBeenCalledWith(null);
    expect(fixture.nativeElement.textContent).toContain('Avisos desactivados.');
  });

  it('warns when the chosen account has no phone registered', async () => {
    const fixture = await render({ status: vi.fn().mockResolvedValue(status()) });
    await pick(fixture, 'u-beto');
    expect(fixture.nativeElement.textContent).toContain('no tiene ningún móvil registrado');
  });

  it('shows the error when the choice cannot be saved', async () => {
    const fixture = await render({ status: vi.fn().mockResolvedValue(status()), saveAlerts: vi.fn().mockRejectedValue(new Error('boom')) });
    await pick(fixture, 'u-ana');
    button(fixture, 'Guardar').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.bk-alerts [role=alert]')).not.toBeNull();
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
