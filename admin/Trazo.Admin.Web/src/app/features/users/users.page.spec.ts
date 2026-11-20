import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/auth.service';
import { UsersApi } from './users.api';
import { UsersPageComponent } from './users.page';

const emptyPage = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 1 };

async function createPage(listUsers: ReturnType<typeof vi.fn>) {
  const api = {
    listUsers,
    listPremiumGrants: vi.fn().mockResolvedValue([])
  };
  await TestBed.configureTestingModule({
    imports: [UsersPageComponent],
    providers: [
      { provide: UsersApi, useValue: api },
      { provide: AuthService, useValue: { user: () => ({ id: 'admin' }) } }
    ]
  }).compileComponents();
  const fixture = TestBed.createComponent(UsersPageComponent);
  fixture.detectChanges();
  return { fixture, api };
}

describe('UsersPageComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('shows loading and then the empty state', async () => {
    let resolveList!: (value: typeof emptyPage) => void;
    const listUsers = vi.fn(() => new Promise<typeof emptyPage>(resolve => { resolveList = resolve; }));
    const { fixture } = await createPage(listUsers);

    expect(fixture.nativeElement.textContent).toContain('Cargando…');
    resolveList(emptyPage);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No hay usuarios que coincidan con estos filtros.');
  });

  it('renders API errors in an accessible alert', async () => {
    const listUsers = vi.fn().mockRejectedValue(new Error('API no disponible'));
    const { fixture } = await createPage(listUsers);
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = fixture.nativeElement.querySelector('.users-error') as HTMLElement;
    expect(alert).not.toBeNull();
    expect(alert.getAttribute('role')).toBe('alert');
    expect(alert.textContent).toContain('API no disponible');
  });

  it('opens the create-user form from the list action', async () => {
    const listUsers = vi.fn().mockResolvedValue(emptyPage);
    const { fixture } = await createPage(listUsers);
    await fixture.whenStable();
    fixture.detectChanges();

    const createButton = [...fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>]
      .find(button => button.textContent?.includes('Crear usuario'));
    expect(createButton).toBeDefined();
    createButton!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Enviaremos un enlace seguro');
    expect(fixture.nativeElement.querySelector('#new-user-email')).not.toBeNull();
  });
});
