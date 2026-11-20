import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiTableComponent } from '../../shared/ui/ui-table.component';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import type {
  CatalogReadiness, PlanPreviewRequest, PlanPreviewResponse, PlanSession
} from '../../core/admin.models';
import { PlanningApi } from './planning.api';
import { errorMessage } from '../../core/setup.api';
import { UI_COPY } from '../../core/ui-copy';

const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const list = (value: string) => value.split(',').map(v => v.trim()).filter(Boolean);

function nextMonday(): string {
  const date = new Date();
  date.setDate(date.getDate() + ((8 - date.getDay()) % 7 || 7));
  return date.toISOString().slice(0, 10);
}

/** Runs the planning engine against the real catalog so the output can be reviewed. Nothing is saved. */
@Component({
  selector: 'trazo-planning-page',
  standalone: true,
  imports: [FormsModule, UiButtonComponent, UiTableComponent, UiFormFieldComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './planning.page.scss',
  templateUrl: './planning.page.html'
})
export class PlanningPageComponent {
  private readonly api = inject(PlanningApi);

  protected readonly copy = UI_COPY.planning;
  protected readonly days = DAYS;

  protected dayLabel(day: string): string {
    return this.copy.days[day as keyof typeof this.copy.days] ?? day;
  }
  protected readonly goal = signal('Hypertrophy');
  protected readonly experience = signal('Intermediate');
  protected readonly split = signal('Automatic');
  protected readonly selectedDays = signal<string[]>(['Monday', 'Tuesday', 'Thursday', 'Friday']);
  protected readonly minutes = signal(75);
  protected readonly weeks = signal(4);
  protected readonly startDate = signal(nextMonday());
  protected readonly equipment = signal('');
  protected readonly excluded = signal('');
  protected readonly macroWeeks = signal<number | null>(null);
  protected readonly deloadEvery = signal<number | null>(null);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly result = signal<PlanPreviewResponse | null>(null);
  protected readonly readiness = signal<CatalogReadiness | null>(null);
  protected readonly week = signal(1);

  protected readonly weekNumbers = computed(() =>
    Array.from({ length: this.result()?.program.durationWeeks ?? 0 }, (_, i) => i + 1));
  protected readonly weekSessions = computed<PlanSession[]>(() =>
    (this.result()?.program.sessions ?? []).filter(s => s.weekNumber === this.week()));
  protected readonly weekVolume = computed(() =>
    this.result()?.program.qualityReport?.weeklyMuscleVolumes.find(w => w.weekNumber === this.week())?.muscles ?? []);

  constructor() {
    void this.api.catalogReadiness().then(r => this.readiness.set(r), () => undefined);
  }

  protected toggleDay(day: string): void {
    this.selectedDays.update(days => days.includes(day) ? days.filter(d => d !== day) : [...days, day]);
  }

  protected async generate(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    const request: PlanPreviewRequest = {
      goal: this.goal(), experience: this.experience(), startDate: this.startDate(), durationWeeks: Number(this.weeks()),
      trainingDays: DAYS.filter(d => this.selectedDays().includes(d)), maxSessionMinutes: Number(this.minutes()),
      availableEquipment: list(this.equipment()), excludedExerciseIds: list(this.excluded()), split: this.split()
    };
    if (this.macroWeeks()) {
      request.macrocycleWeeks = Number(this.macroWeeks());
      if (this.deloadEvery()) { request.includeDeloadPhases = true; request.deloadEveryTrainingWeeks = Number(this.deloadEvery()); }
    }
    try {
      this.result.set(await this.api.preview(request));
      this.week.set(1);
    } catch (e) {
      this.result.set(null);
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
