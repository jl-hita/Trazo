import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { FeedbackItem, FeedbackPage, FeedbackStatus } from '../../core/admin.models';
import { FeedbackApi } from './feedback.api';
import { errorMessage } from '../../core/api-error';
import { UI_COPY } from '../../core/ui-copy';

/** Comments sent from the app. Superadmin only: they carry who wrote them. */
@Component({
  selector: 'trazo-feedback-page',
  standalone: true,
  imports: [FormsModule, DatePipe, UiButtonComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './feedback.page.scss',
  templateUrl: './feedback.page.html'
})
export class FeedbackPageComponent {
  private readonly api = inject(FeedbackApi);
  private static readonly PageSize = 25;

  protected readonly labels = UI_COPY.feedback;
  protected readonly data = signal<FeedbackPage | null>(null);
  protected readonly status = signal('');
  protected readonly category = signal('');
  protected readonly page = signal(1);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly confirmId = signal<string | null>(null);
  protected readonly items = computed<FeedbackItem[]>(() => this.data()?.items ?? []);
  protected readonly totalPages = computed(() => Math.max(1, this.data()?.totalPages ?? 1));

  constructor() { void this.load(); }

  protected async load(): Promise<void> {
    this.loading.set(true);
    try {
      this.data.set(await this.api.list(this.status(), this.category(), this.page(), FeedbackPageComponent.PageSize));
      this.error.set('');
    } catch (e) { this.error.set(errorMessage(e)); }
    finally { this.loading.set(false); }
  }

  protected setFilter(which: 'status' | 'category', value: string): void {
    (which === 'status' ? this.status : this.category).set(value);
    this.page.set(1);
    void this.load();
  }

  protected go(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.totalPages()));
    void this.load();
  }

  protected async setStatus(item: FeedbackItem, status: FeedbackStatus): Promise<void> {
    try {
      await this.api.setStatus(item.id, status);
      await this.load();
    } catch (e) { this.error.set(errorMessage(e)); }
  }

  protected async remove(item: FeedbackItem): Promise<void> {
    try {
      await this.api.delete(item.id);
      this.confirmId.set(null);
      if (this.items().length === 1 && this.page() > 1) this.page.update(p => p - 1);
      await this.load();
    } catch (e) { this.error.set(errorMessage(e)); }
  }
}
