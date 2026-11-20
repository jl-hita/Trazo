import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { AdminPremiumGrant, AdminUserDetail, AdminUserSummary, UserAction } from '../../core/admin.models';
import { UsersApi } from './users.api';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/setup.api';

const ROLES = ['user', 'admin', 'accountant', 'commercial', 'superadmin'];

/** Users of the platform: search, filter and manage accounts. */
@Component({
  selector: 'trazo-users-page',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './users.page.scss',
  templateUrl: './users.page.html'
})
export class UsersPageComponent {
  private readonly api = inject(UsersApi);
  private readonly auth = inject(AuthService);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private requestId = 0;
  private premiumRequestId = 0;

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
  protected readonly creating = signal(false);
  protected readonly newName = signal('');
  protected readonly newEmail = signal('');
  protected readonly newRole = signal('user');
  protected readonly editName = signal('');
  protected readonly editRole = signal('user');
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly panelError = signal('');
  protected readonly confirming = signal<UserAction | null>(null);
  protected readonly deleteConfirm = signal('');
  protected readonly premiumGrants = signal<AdminPremiumGrant[]>([]);
  protected readonly premiumCurrent = computed(() => this.premiumGrants().some(g => g.isCurrent));
  protected readonly premiumLoading = signal(false);
  protected readonly premiumDuration = signal('30');
  protected readonly premiumReason = signal('');
  protected readonly premiumRevokeId = signal<string | null>(null);
  protected readonly premiumRevokeReason = signal('');

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

  protected startCreate(): void {
    this.selected.set(null);
    this.creating.set(true);
    this.newName.set('');
    this.newEmail.set('');
    this.newRole.set('user');
    this.message.set('');
    this.panelError.set('');
  }

  protected cancelCreate(): void {
    this.creating.set(false);
    this.panelError.set('');
  }

  protected async createUser(): Promise<void> {
    const displayName = this.newName().trim();
    const email = this.newEmail().trim();
    if (displayName.length < 2 || displayName.length > 100 || !email) return;
    this.busy.set(true);
    this.panelError.set('');
    try {
      const created = await this.api.createUser({ displayName, email, role: this.newRole() });
      this.creating.set(false);
      this.show(created);
      this.message.set('Invitation sent. The account will be activated when the user sets a password.');
      this.page.set(1);
      await this.load();
    } catch (e) {
      this.panelError.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async select(summary: AdminUserSummary): Promise<void> {
    this.creating.set(false);
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

  protected async grantPremium(): Promise<void> {
    const u = this.selected();
    const reason = this.premiumReason().trim();
    if (!u || reason.length < 5) return;
    const permanent = this.premiumDuration() === 'permanent';
    this.busy.set(true);
    this.panelError.set('');
    try {
      await this.api.grantPremium(u.id, permanent
        ? { permanent: true, reason }
        : { permanent: false, days: Number(this.premiumDuration()), reason });
      this.premiumReason.set('');
      this.message.set(permanent ? 'Permanent Premium access granted.' : 'Premium time granted.');
      await this.loadPremiumGrants(u.id);
    } catch (e) {
      this.panelError.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async revokePremium(grantId: string): Promise<void> {
    const u = this.selected();
    const reason = this.premiumRevokeReason().trim();
    if (!u || reason.length < 5) return;
    this.busy.set(true);
    this.panelError.set('');
    try {
      await this.api.revokePremium(u.id, grantId, reason);
      this.premiumRevokeId.set(null);
      this.premiumRevokeReason.set('');
      this.message.set('Manual Premium grant revoked. Store purchases and referral rewards are unchanged.');
      await this.loadPremiumGrants(u.id);
    } catch (e) {
      this.panelError.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async loadPremiumGrants(userId: string): Promise<void> {
    const requestId = ++this.premiumRequestId;
    this.premiumLoading.set(true);
    try {
      const grants = await this.api.listPremiumGrants(userId);
      if (requestId === this.premiumRequestId && this.selected()?.id === userId) this.premiumGrants.set(grants);
    } catch (e) {
      if (requestId === this.premiumRequestId) this.panelError.set(errorMessage(e));
    } finally {
      if (requestId === this.premiumRequestId) this.premiumLoading.set(false);
    }
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

  protected async sendPasswordInvitation(): Promise<void> {
    const u = this.selected();
    if (!u || u.hasPassword || u.disabled) return;
    this.busy.set(true);
    this.panelError.set('');
    this.message.set('');
    try {
      await this.api.sendPasswordInvitation(u.id);
      this.message.set('Password setup link sent to ' + u.email + '.');
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
    this.premiumGrants.set([]);
    this.premiumRevokeId.set(null);
    void this.loadPremiumGrants(detail.id);
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
