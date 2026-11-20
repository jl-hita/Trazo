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
  styleUrl: './reviews.page.scss',
  templateUrl: './reviews.page.html'
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
