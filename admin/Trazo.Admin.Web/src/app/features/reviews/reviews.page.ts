import { Component, DestroyRef, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi, AdminPlanReview, AdminPlanReviewDetail, Page } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

const FILTERS: { value: string; label: string }[] = [
  { value: 'pending', label: 'Pending' }, { value: 'mine', label: 'Mine' }, { value: 'in_progress', label: 'In progress' },
  { value: 'reviewed', label: 'Reviewed' }, { value: '', label: 'All' }
];

/**
 * Manual plan reviews. Users ask for a human check of their plan and can talk with the team. Anyone may read a request and take
 * a pending one; only the administrator who has it can answer, give it back to the queue or close it.
 */
@Component({
  selector: 'trazo-reviews-page',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .rv-layout { display: grid; grid-template-columns: minmax(0, 380px) minmax(0, 1fr); gap: 18px; align-items: start; }
    .rv-toolbar { display: flex; gap: 8px; margin-bottom: 12px; align-items: center; flex-wrap: wrap; }
    .rv-toolbar select, .rv-toolbar button { padding: 8px 11px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; cursor: pointer; }
    .rv-count { color: #8291a6; font-size: 12px; }
    .rv-list { display: grid; gap: 8px; }
    .rv-item { text-align: left; padding: 12px 14px; border: 1px solid #293444; border-radius: 8px; background: #131b27; color: #e8edf5; cursor: pointer; display: grid; gap: 4px; }
    .rv-item:hover, .rv-item.selected { background: #182231; border-color: #4a6585; }
    .rv-item b { font-size: 14px; font-weight: 600; }
    .rv-item .meta { color: #8291a6; font-size: 11px; }
    .rv-item .preview { color: #b9c5d6; font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .rv-badge { display: inline-block; margin-left: 6px; padding: 1px 8px; border: 1px solid #6b5a2e; border-radius: 999px; color: #e6d39b; font-size: 10px; font-weight: 400; }
    .rv-badge.progress { border-color: #3d5f8c; color: #a9c2df; }
    .rv-badge.done { border-color: #2f6a4b; color: #7fd3a0; }
    .rv-badge.reply { border-color: #7a3b3b; color: #f0a3a3; }
    .rv-panel { padding: 18px 20px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 14px; position: sticky; top: 16px; }
    .rv-panel h2 { margin: 0; font-size: 17px; font-weight: 600; color: #f2f5fa; }
    .rv-panel .meta { margin: 2px 0 0; color: #8291a6; font-size: 12px; }
    .rv-thread { display: grid; gap: 8px; max-height: 46vh; overflow-y: auto; padding-right: 4px; }
    .rv-msg { max-width: 85%; padding: 9px 12px; border-radius: 8px; font-size: 13px; white-space: pre-wrap; word-break: break-word; }
    .rv-msg small { display: block; margin-bottom: 3px; color: #8291a6; font-size: 10px; }
    .rv-msg.user { justify-self: start; background: #0d1119; border: 1px solid #293444; color: #d4dce8; }
    .rv-msg.staff { justify-self: end; background: #1b2a3e; border: 1px solid #3d5f8c; color: #e4edf8; }
    .rv-msg.system { justify-self: center; max-width: 100%; padding: 2px 8px; color: #8291a6; font-size: 11px; font-style: italic; background: none; border: 0; }
    .rv-panel textarea { width: 100%; min-height: 76px; padding: 9px 11px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; box-sizing: border-box; resize: vertical; }
    .rv-actions { display: flex; gap: 8px; flex-wrap: wrap; }
    .rv-actions button { padding: 8px 14px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .rv-actions button.primary { background: #2c4a73; }
    .rv-actions button:disabled { opacity: .45; cursor: not-allowed; }
    .rv-note { margin: 0; color: #8291a6; font-size: 12px; }
    .rv-error { color: #f0a3a3; font-size: 12px; margin: 0 0 8px; }
    .rv-empty { color: #8291a6; text-align: center; padding: 28px 0; }
    @media (max-width: 1100px) { .rv-layout { grid-template-columns: 1fr; } .rv-panel { position: static; } }
  `],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">ADMINISTRATION</p>
        <h1>Plan reviews</h1>
        <p class="subtitle">Users who asked for a manual review of their routine. Take a request to talk with the user; give it back to the queue if somebody else should continue.</p>
      </div>
    </div>
    @if (error()) { <p class="rv-error" role="alert">{{ error() }}</p> }
    <div class="rv-layout">
      <section aria-label="Requests">
        <div class="rv-toolbar">
          <select aria-label="Filter by status" [ngModel]="status()" (ngModelChange)="setStatus($event)">
            @for (f of filters; track f.value) { <option [value]="f.value">{{ f.label }}</option> }
          </select>
          <button type="button" (click)="refresh()">Refresh</button>
          @if (counts(); as c) { <span class="rv-count">{{ c.pending }} pending · {{ c.mine }} mine@if (c.mineAwaitingReply > 0) { ({{ c.mineAwaitingReply }} awaiting your reply) }</span> }
        </div>
        <div class="rv-list">
          @for (r of data()?.items ?? []; track r.id) {
            <button type="button" class="rv-item" [class.selected]="selected()?.review?.id === r.id" (click)="open(r.id)">
              <b>{{ r.userName }}
                @switch (r.status) {
                  @case ('pending') { <span class="rv-badge">Pending</span> }
                  @case ('in_progress') { <span class="rv-badge progress">{{ r.assignedToMe ? 'Mine' : 'With ' + (r.assignedToName ?? 'someone') }}</span> }
                  @default { <span class="rv-badge done">Reviewed</span> }
                }
                @if (r.awaitingReply) { <span class="rv-badge reply">Awaiting reply</span> }
              </b>
              <span class="meta">{{ r.goal }} · {{ r.experience }} · {{ r.split }} · {{ r.totalWeeks }} weeks · {{ r.messageCount }} messages · {{ when(r.updatedAt) }}</span>
              @if (r.preview) { <span class="preview">{{ r.preview }}</span> }
            </button>
          } @empty {
            @if (!loading()) { <p class="rv-empty">No requests.</p> }
          }
        </div>
      </section>

      <section class="rv-panel" aria-label="Conversation">
        @if (selected(); as d) {
          <div>
            <h2>{{ d.review.userName }}</h2>
            <p class="meta">{{ d.review.userEmail }} · {{ d.review.goal }} · {{ d.review.experience }} · {{ d.review.split }} · {{ d.review.totalWeeks }} weeks from {{ d.startDate }}</p>
            @if (d.review.status === 'in_progress') { <p class="meta">Held by {{ d.review.assignedToMe ? 'you' : d.review.assignedToName }}</p> }
          </div>
          <div class="rv-thread" role="log" aria-label="Messages">
            @for (m of d.messages; track m.id) {
              <div class="rv-msg" [class.user]="m.author === 'user'" [class.staff]="m.author === 'staff'" [class.system]="m.author === 'system'">
                @if (m.author !== 'system') { <small>{{ m.author === 'user' ? d.review.userName : (m.authorName ?? 'Team') }} · {{ when(m.createdAt) }}</small> }
                {{ m.body }}
              </div>
            } @empty { <p class="rv-note">The user did not write a message.</p> }
          </div>
          @if (d.canReply) {
            <textarea placeholder="Reply to the user (they see it as coming from the team)" maxlength="2000" [(ngModel)]="draft" aria-label="Reply"></textarea>
          } @else if (d.review.status === 'in_progress') {
            <p class="rv-note">Only {{ d.review.assignedToName }} can reply while they hold this request.</p>
          } @else if (d.review.status === 'reviewed') {
            <p class="rv-note">Closed {{ when(d.review.reviewedAt) }}. The user can open a new request to continue.</p>
          }
          <div class="rv-actions">
            @if (d.canTake) { <button type="button" class="primary" [disabled]="busy()" (click)="claim(d)">Take it</button> }
            @if (d.canReply) {
              <button type="button" class="primary" [disabled]="busy() || !draft.trim()" (click)="send(d)">Send reply</button>
              <button type="button" [disabled]="busy()" (click)="close(d)">Mark as reviewed{{ draft.trim() ? ' with this message' : '' }}</button>
            }
            @if (d.canRelease) { <button type="button" [disabled]="busy()" (click)="release(d)">Give back to the queue</button> }
          </div>
        } @else {
          <p class="rv-empty">Select a request.</p>
        }
      </section>
    </div>
  `
})
export class ReviewsPageComponent {
  private readonly api = inject(AdminApi);
  protected readonly filters = FILTERS;
  protected readonly status = signal('pending');
  protected readonly data = signal<Page<AdminPlanReview> | null>(null);
  protected readonly selected = signal<AdminPlanReviewDetail | null>(null);
  protected readonly counts = signal<{ pending: number; mine: number; mineAwaitingReply: number } | null>(null);
  protected readonly error = signal('');
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected draft = '';

  constructor() {
    void this.refresh();
    // Users write at any time: keep the queue and the open conversation fresh without a manual reload.
    const timer = setInterval(() => { if (!this.busy() && !document.hidden) void this.refresh(true); }, 30000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected when(value: string | null): string { return value ? value.slice(0, 16).replace('T', ' ') : ''; }

  protected setStatus(value: string): void { this.status.set(value); void this.refresh(); }

  protected async refresh(quiet = false): Promise<void> {
    if (!quiet) this.loading.set(true);
    try {
      const [list, counts] = await Promise.all([this.api.listPlanReviews(this.status(), 1), this.api.planReviewsCounts()]);
      this.data.set(list);
      this.counts.set(counts);
      const open = this.selected()?.review.id;
      if (open) this.selected.set(await this.api.planReview(open));
      if (!quiet) this.error.set('');
    } catch (e) { if (!quiet) this.error.set(errorMessage(e)); }
    finally { this.loading.set(false); }
  }

  protected async open(id: string): Promise<void> {
    this.error.set('');
    this.draft = '';
    try { this.selected.set(await this.api.planReview(id)); }
    catch (e) { this.error.set(errorMessage(e)); }
  }

  private async act(d: AdminPlanReviewDetail, action: () => Promise<void>, clearDraft: boolean): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
      if (clearDraft) this.draft = '';
      this.selected.set(await this.api.planReview(d.review.id));
      await this.refresh(true);
    } catch (e) {
      this.error.set(errorMessage(e));
      await this.refresh(true); // somebody else may have taken or closed it meanwhile
    } finally { this.busy.set(false); }
  }

  protected claim(d: AdminPlanReviewDetail): Promise<void> { return this.act(d, () => this.api.claimPlanReview(d.review.id), false); }
  protected send(d: AdminPlanReviewDetail): Promise<void> { return this.act(d, () => this.api.messagePlanReview(d.review.id, this.draft.trim()), true); }
  protected close(d: AdminPlanReviewDetail): Promise<void> { return this.act(d, () => this.api.closePlanReview(d.review.id, this.draft), true); }

  protected release(d: AdminPlanReviewDetail): Promise<void> {
    const reason = window.prompt('Why are you giving it back? (optional, the next reviewer will see it)', '');
    return reason === null ? Promise.resolve() : this.act(d, () => this.api.releasePlanReview(d.review.id, reason), false);
  }
}
