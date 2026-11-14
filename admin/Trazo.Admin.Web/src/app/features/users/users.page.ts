import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi, AdminUserDetail, AdminUserSummary, UserAction } from '../../core/admin.api';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/setup.api';

const ROLES = ['user', 'admin', 'superadmin'];

/** Users of the platform: search, filter and manage accounts. */
@Component({
  selector: 'trazo-users-page',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .users-layout > section { min-width: 0; overflow-x: auto; }
    .users-layout { display: grid; grid-template-columns: minmax(0, 1fr) 360px; gap: 18px; align-items: start; }
    .users-toolbar { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 14px; }
    .users-toolbar input, .users-toolbar select, .user-panel input, .user-panel select {
      padding: 9px 11px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; }
    .users-toolbar input { flex: 1 1 220px; }
    .users-toolbar input:focus, .user-panel input:focus, .user-panel select:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .users-table { width: 100%; border-collapse: collapse; border: 1px solid #293444; border-radius: 8px; background: #131b27; font-size: 13px; }
    .users-table th { padding: 11px 14px; text-align: left; color: #8291a6; font-size: 10px; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; border-bottom: 1px solid #293444; }
    .users-table td { padding: 11px 14px; border-bottom: 1px solid #1d2735; vertical-align: middle; }
    .users-table tr.row { cursor: pointer; }
    .users-table tr.row:hover td, .users-table tr.row.selected td { background: #182231; }
    .users-table tr:last-child td { border-bottom: 0; }
    .users-table .name { color: #e8edf5; font-weight: 500; }
    .users-table .sub { color: #8291a6; font-size: 11px; }
    .badge { display: inline-block; margin: 0 4px 3px 0; padding: 2px 7px; border: 1px solid #34465b; border-radius: 999px; color: #aab6c7; font-size: 10px; letter-spacing: .04em; }
    .badge.role-superadmin { border-color: #6d9bd2; color: #b9d3f2; }
    .badge.role-admin { border-color: #4a6585; color: #a9c2df; }
    .badge.bad { border-color: #7a3b3b; color: #f0a3a3; }
    .badge.warn { border-color: #6b5a2e; color: #e6d39b; }
    .badge.good { border-color: #2f6a4b; color: #7fd3a0; }
    .users-pager { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-top: 12px; color: #8291a6; font-size: 12px; }
    .users-pager button, .user-panel button { padding: 8px 14px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .users-pager button:disabled, .user-panel button:disabled { opacity: .45; cursor: not-allowed; }
    .user-panel { position: sticky; top: 16px; padding: 20px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 14px; }
    .user-panel h2 { margin: 0; font-size: 17px; font-weight: 600; color: #f2f5fa; }
    .user-panel .email { margin: 2px 0 0; color: #98a6b8; font-size: 12px; word-break: break-all; }
    .user-panel label { display: grid; gap: 6px; color: #aab6c7; font-size: 12px; }
    .user-panel .facts { display: grid; gap: 6px; margin: 0; padding: 0; list-style: none; color: #aab6c7; font-size: 12px; }
    .user-panel .facts b { color: #e4edf8; font-weight: 500; }
    .user-panel .actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .user-panel button.primary { background: #3b6ea8; border-color: #6d9bd2; }
    .user-panel button.danger { border-color: #7a3b3b; color: #f0a3a3; background: #2a1616; }
    .user-panel .sessions { margin: 0; padding: 0; list-style: none; color: #8291a6; font-size: 11px; display: grid; gap: 4px; }
    .user-panel .ok { color: #7fd3a0; font-size: 12px; margin: 0; }
    .user-panel .err, .users-error { color: #f0a3a3; font-size: 12px; margin: 0; }
    .user-panel .hint { color: #7f8da0; font-size: 11px; margin: 0; }
    .user-panel .danger-zone { display: grid; gap: 8px; padding-top: 12px; border-top: 1px solid #5a2a2a; }
    .user-panel .empty { color: #8291a6; font-size: 13px; text-align: center; padding: 18px 0; }
    @media (max-width: 1280px) { .users-layout { grid-template-columns: 1fr; } .user-panel { position: static; } }
  `],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">ADMINISTRATION</p>
        <h1>Users</h1>
        <p class="subtitle">Search accounts, change roles and manage access.</p>
      </div>
    </div>

    <div class="users-layout">
      <section aria-label="User list">
        <div class="users-toolbar">
          <input type="search" placeholder="Search by name or email" aria-label="Search users"
                 [ngModel]="q()" (ngModelChange)="onSearch($event)">
          <select aria-label="Filter by role" [ngModel]="role()" (ngModelChange)="setFilter('role', $event)">
            <option value="">All roles</option>
            @for (r of roles; track r) { <option [value]="r">{{ r }}</option> }
          </select>
          <select aria-label="Filter by status" [ngModel]="status()" (ngModelChange)="setFilter('status', $event)">
            <option value="">Any status</option>
            <option value="active">Active</option>
            <option value="disabled">Disabled</option>
            <option value="unverified">Email not verified</option>
          </select>
          <select aria-label="Sort" [ngModel]="sort()" (ngModelChange)="setFilter('sort', $event)">
            <option value="">Sort: email</option>
            <option value="created">Sort: newest</option>
          </select>
        </div>

        @if (error()) { <p class="users-error" role="alert">{{ error() }}</p> }

        <table class="users-table">
          <thead><tr><th>User</th><th>Role</th><th>Status</th><th>Last sign-in</th></tr></thead>
          <tbody>
            @for (u of users(); track u.id) {
              <tr class="row" [class.selected]="selected()?.id === u.id" (click)="select(u)" tabindex="0" (keydown.enter)="select(u)">
                <td><div class="name">{{ u.displayName }}</div><div class="sub">{{ u.email }}</div></td>
                <td><span class="badge" [class]="'badge role-' + u.role">{{ u.role }}</span></td>
                <td>
                  @if (u.disabled) { <span class="badge bad">disabled</span> }
                  @if (u.lockedOut) { <span class="badge warn">locked</span> }
                  @if (!u.emailVerified) { <span class="badge warn">unverified</span> }
                  @if (u.twoFactorEnabled) { <span class="badge good">2FA</span> }
                  @if (u.hasGoogle) { <span class="badge">Google</span> }
                  @if (!u.disabled && !u.lockedOut && u.emailVerified && !u.twoFactorEnabled && !u.hasGoogle) { <span class="badge">active</span> }
                </td>
                <td>{{ u.lastLoginAt ? formatDate(u.lastLoginAt) : 'Never' }}</td>
              </tr>
            } @empty {
              <tr><td colspan="4" class="sub" style="text-align:center;padding:26px">{{ loading() ? 'Loading…' : 'No users match these filters.' }}</td></tr>
            }
          </tbody>
        </table>

        <div class="users-pager">
          <span>{{ totalCount() }} user{{ totalCount() === 1 ? '' : 's' }} · page {{ page() }} of {{ totalPages() || 1 }}</span>
          <span>
            <button type="button" (click)="goTo(page() - 1)" [disabled]="page() <= 1 || loading()">Previous</button>
            <button type="button" (click)="goTo(page() + 1)" [disabled]="page() >= totalPages() || loading()">Next</button>
          </span>
        </div>
      </section>

      <aside class="user-panel" aria-label="User details">
        @if (selected(); as u) {
          <div>
            <h2>{{ u.displayName }}</h2>
            <p class="email">{{ u.email }}</p>
          </div>

          @if (isSelf()) { <p class="hint">This is your account. You cannot change your own role or disable it.</p> }

          <label>Display name
            <input type="text" maxlength="100" [ngModel]="editName()" (ngModelChange)="editName.set($event)">
          </label>
          <label>Role
            <select [ngModel]="editRole()" (ngModelChange)="editRole.set($event)" [disabled]="isSelf()">
              @for (r of roles; track r) { <option [value]="r">{{ r }}</option> }
            </select>
          </label>
          <div class="actions">
            <button class="primary" type="button" (click)="save()" [disabled]="busy() || !dirty()">Save changes</button>
          </div>

          <ul class="facts">
            <li>Email: <b>{{ u.emailVerified ? 'verified' : 'not verified' }}</b></li>
            <li>Sign-in: <b>{{ methods(u) }}</b></li>
            <li>Two-factor: <b>{{ u.twoFactorEnabled ? 'enabled' : 'off' }}</b></li>
            <li>Created: <b>{{ formatDate(u.createdAt) }}</b></li>
            <li>Last sign-in: <b>{{ u.lastLoginAt ? formatDate(u.lastLoginAt) : 'never' }}</b></li>
            @if (u.disabledAt) { <li>Disabled on: <b>{{ formatDate(u.disabledAt) }}</b></li> }
            @if (u.lockedOut) { <li>Locked after failed sign-ins: <b>{{ u.failedLoginAttempts }} attempts</b></li> }
            <li>Active sessions: <b>{{ u.activeSessionCount }}</b></li>
          </ul>
          @if (u.sessions.length) {
            <ul class="sessions">
              @for (s of u.sessions; track s.createdAt) { <li>{{ s.deviceName || 'Unknown device' }} · since {{ formatDate(s.createdAt) }}</li> }
            </ul>
          }

          <div class="actions">
            @if (!u.emailVerified) { <button type="button" (click)="run('verify-email')" [disabled]="busy()">Mark email verified</button> }
            @if (u.lockedOut) { <button type="button" (click)="run('unlock')" [disabled]="busy()">Unlock</button> }
            <button type="button" (click)="run('revoke-sessions')" [disabled]="busy() || u.activeSessionCount === 0" [class.danger]="confirming() === 'revoke-sessions'">
              {{ confirming() === 'revoke-sessions' ? 'Confirm: sign out everywhere' : 'Revoke sessions' }}
            </button>
            @if (!isSelf()) {
              @if (u.disabled) {
                <button type="button" (click)="run('enable')" [disabled]="busy()">Enable account</button>
              } @else {
                <button type="button" (click)="run('disable')" [disabled]="busy()" [class.danger]="confirming() === 'disable'">
                  {{ confirming() === 'disable' ? 'Confirm: disable account' : 'Disable account' }}
                </button>
              }
            }
          </div>

          @if (!isSelf()) {
            <div class="danger-zone">
              <p class="hint">Delete account: permanently erases the account and all its data (plans, logs, subscriptions). It cannot be undone. Type the email to confirm.</p>
              <input type="email" aria-label="Type the account email to confirm deletion" [ngModel]="deleteConfirm()" (ngModelChange)="deleteConfirm.set($event)" placeholder="{{ u.email }}">
              <button type="button" class="danger" (click)="deleteAccount()" [disabled]="busy() || deleteConfirm().trim().toLowerCase() !== u.email.toLowerCase()">Delete account permanently</button>
            </div>
          }

          @if (message()) { <p class="ok" role="status">{{ message() }}</p> }
          @if (panelError()) { <p class="err" role="alert">{{ panelError() }}</p> }
        } @else {
          <p class="empty">Select a user to see details and manage the account.</p>
        }
      </aside>
    </div>
  `
})
export class UsersPageComponent {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private requestId = 0;

  protected readonly roles = ROLES;
  protected readonly users = signal<AdminUserSummary[]>([]);
  protected readonly q = signal('');
  protected readonly role = signal('');
  protected readonly status = signal('');
  protected readonly sort = signal('');
  protected readonly page = signal(1);
  protected readonly totalPages = signal(1);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  protected readonly selected = signal<AdminUserDetail | null>(null);
  protected readonly editName = signal('');
  protected readonly editRole = signal('user');
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly panelError = signal('');
  protected readonly confirming = signal<UserAction | null>(null);
  protected readonly deleteConfirm = signal('');

  protected readonly isSelf = computed(() => this.selected()?.id === this.auth.user()?.id);
  protected readonly dirty = computed(() => {
    const u = this.selected();
    return !!u && (this.editName().trim() !== u.displayName || this.editRole() !== u.role);
  });

  constructor() { void this.load(); }

  protected onSearch(value: string): void {
    this.q.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => { this.page.set(1); void this.load(); }, 300);
  }

  protected setFilter(kind: 'role' | 'status' | 'sort', value: string): void {
    this[kind].set(value);
    this.page.set(1);
    void this.load();
  }

  protected goTo(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.page.set(page);
    void this.load();
  }

  protected async select(summary: AdminUserSummary): Promise<void> {
    this.message.set('');
    this.panelError.set('');
    this.confirming.set(null);
    try { this.show(await this.api.getUser(summary.id)); }
    catch (e) { this.panelError.set(errorMessage(e)); }
  }

  protected async save(): Promise<void> {
    const u = this.selected();
    if (!u) return;
    const changes: { displayName?: string; role?: string } = {};
    if (this.editName().trim() !== u.displayName) changes.displayName = this.editName().trim();
    if (this.editRole() !== u.role) changes.role = this.editRole();
    await this.perform(() => this.api.updateUser(u.id, changes), 'Changes saved.');
  }

  protected async run(action: UserAction): Promise<void> {
    const u = this.selected();
    if (!u) return;
    if ((action === 'disable' || action === 'revoke-sessions') && this.confirming() !== action) {
      this.confirming.set(action);
      this.message.set('');
      return;
    }
    const messages: Record<UserAction, string> = {
      disable: 'Account disabled and sessions revoked.', enable: 'Account enabled.',
      'revoke-sessions': 'All sessions revoked.', unlock: 'Account unlocked.', 'verify-email': 'Email marked as verified.'
    };
    await this.perform(() => this.api.userAction(u.id, action), messages[action]);
  }

  protected async deleteAccount(): Promise<void> {
    const u = this.selected();
    if (!u) return;
    this.busy.set(true);
    this.panelError.set('');
    try {
      const result = await this.api.deleteUser(u.id, this.deleteConfirm().trim());
      this.selected.set(null);
      this.deleteConfirm.set('');
      this.message.set(result.playSubscriptionNotCancelled ? 'Account deleted. Its Google Play subscription is NOT cancelled.' : 'Account deleted.');
      await this.load();
    } catch (e) {
      this.panelError.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected methods(u: AdminUserDetail): string {
    const m = [u.hasPassword ? 'password' : '', u.hasGoogle ? 'Google' : ''].filter(Boolean);
    return m.length ? m.join(' + ') : 'none';
  }

  protected formatDate(value: string): string {
    return new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
  }

  private async perform(call: () => Promise<AdminUserDetail>, success: string): Promise<void> {
    this.busy.set(true);
    this.panelError.set('');
    this.confirming.set(null);
    try {
      this.show(await call());
      this.message.set(success);
      await this.load();
    } catch (e) {
      this.panelError.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private show(detail: AdminUserDetail): void {
    this.selected.set(detail);
    this.deleteConfirm.set('');
    this.editName.set(detail.displayName);
    this.editRole.set(detail.role);
  }

  private async load(): Promise<void> {
    const id = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    try {
      const result = await this.api.listUsers({
        q: this.q(), role: this.role(), status: this.status(), sort: this.sort(), page: this.page(), pageSize: 25
      });
      if (id !== this.requestId) return; // a newer request replaced this one
      this.users.set(result.items);
      this.totalCount.set(result.totalCount);
      this.totalPages.set(result.totalPages);
    } catch (e) {
      if (id === this.requestId) this.error.set(errorMessage(e));
    } finally {
      if (id === this.requestId) this.loading.set(false);
    }
  }
}
