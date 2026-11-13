import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AdminApi, CatalogReadiness, PlanPreviewRequest, PlanPreviewResponse, PlanSession
} from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

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
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .pl-grid { display: grid; grid-template-columns: 340px minmax(0, 1fr); gap: 18px; align-items: start; }
    .pl-card { padding: 18px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 12px; }
    .pl-card h2 { margin: 0; font-size: 15px; font-weight: 600; color: #f2f5fa; }
    .pl-card label { display: grid; gap: 6px; color: #aab6c7; font-size: 12px; }
    .pl-card input[type=text], .pl-card input[type=number], .pl-card input[type=date], .pl-card select {
      padding: 9px 11px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; font-family: inherit; }
    .pl-card input:focus, .pl-card select:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .pl-days { display: flex; flex-wrap: wrap; gap: 6px; }
    .pl-days label { display: flex; align-items: center; gap: 5px; padding: 5px 9px; border: 1px solid #34465b; border-radius: 6px; font-size: 12px; cursor: pointer; }
    .pl-days label.on { border-color: #6d9bd2; background: #1b2a3e; color: #e4edf8; }
    .pl-days input { display: none; }
    .pl-row { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .pl-card .hint { color: #7f8da0; font-size: 11px; margin: 0; }
    .pl-card .err { color: #f0a3a3; font-size: 12px; margin: 0; }
    .pl-card button, .pl-weeks button { padding: 9px 14px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .pl-card button.primary { background: #3b6ea8; border-color: #6d9bd2; }
    .pl-card button:disabled { opacity: .45; cursor: not-allowed; }
    .pl-stack { display: grid; gap: 14px; min-width: 0; }
    .pl-stats { display: flex; flex-wrap: wrap; gap: 8px; }
    .pl-stats .badge { margin: 0; }
    .badge { display: inline-block; padding: 2px 8px; border: 1px solid #34465b; border-radius: 999px; color: #aab6c7; font-size: 11px; }
    .badge.warn { border-color: #6b5a2e; color: #e6d39b; }
    .badge.good { border-color: #2f6a4b; color: #7fd3a0; }
    .pl-weeks { display: flex; flex-wrap: wrap; gap: 6px; }
    .pl-weeks button.on { background: #3b6ea8; border-color: #6d9bd2; }
    .pl-session { border: 1px solid #293444; border-radius: 8px; overflow-x: auto; }
    .pl-stack > section { min-width: 0; overflow-x: auto; }
    .pl-session header { display: flex; justify-content: space-between; padding: 10px 14px; background: #182231; font-size: 13px; color: #e8edf5; }
    .pl-session header span { color: #8291a6; font-size: 12px; }
    .pl-table { width: 100%; border-collapse: collapse; font-size: 12px; }
    .pl-table th { padding: 8px 12px; text-align: left; color: #8291a6; font-size: 10px; letter-spacing: .12em; text-transform: uppercase; border-bottom: 1px solid #293444; }
    .pl-table td { padding: 8px 12px; border-bottom: 1px solid #1d2735; color: #c6d0dd; vertical-align: top; }
    .pl-table tr:last-child td { border-bottom: 0; }
    .pl-table .muted { color: #8291a6; font-size: 11px; }
    .pl-warnings { margin: 0; padding-left: 18px; color: #e6d39b; font-size: 12px; line-height: 1.6; display: grid; gap: 4px; }
    .pl-phases { margin: 0; padding: 0; list-style: none; display: grid; gap: 6px; font-size: 12px; color: #aab6c7; }
    .pl-phases b { color: #e4edf8; font-weight: 500; }
    details summary { cursor: pointer; color: #8fb0d6; font-size: 12px; }
    @media (max-width: 1360px) { .pl-grid { grid-template-columns: 1fr; } }
  `],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">PLANNING ENGINE</p>
        <h1>Plan preview</h1>
        <p class="subtitle">Generate a program from a profile with the real catalog and review it. Nothing is saved.</p>
      </div>
    </div>

    <div class="pl-grid">
      <form class="pl-card" (ngSubmit)="generate()" aria-label="Profile">
        <h2>Profile</h2>
        <div class="pl-row">
          <label>Goal
            <select name="goal" [ngModel]="goal()" (ngModelChange)="goal.set($event)">
              <option value="GeneralFitness">General fitness</option><option value="Hypertrophy">Hypertrophy</option><option value="Strength">Strength</option>
            </select>
          </label>
          <label>Experience
            <select name="experience" [ngModel]="experience()" (ngModelChange)="experience.set($event)">
              <option>Beginner</option><option>Intermediate</option><option>Advanced</option>
            </select>
          </label>
        </div>
        <div>
          <p class="hint" style="margin-bottom:6px">Training days</p>
          <div class="pl-days">
            @for (d of days; track d) {
              <label [class.on]="selectedDays().includes(d)">
                <input type="checkbox" [checked]="selectedDays().includes(d)" (change)="toggleDay(d)">{{ d.slice(0, 3) }}
              </label>
            }
          </div>
        </div>
        <div class="pl-row">
          <label>Split
            <select name="split" [ngModel]="split()" (ngModelChange)="split.set($event)">
              <option value="Automatic">Automatic</option><option value="FullBody">Full body</option>
              <option value="UpperLower">Upper / lower</option><option value="PushPullLegs">Push / pull / legs</option>
            </select>
          </label>
          <label>Session minutes
            <input type="number" name="minutes" min="35" max="180" [ngModel]="minutes()" (ngModelChange)="minutes.set($event)">
          </label>
        </div>
        <div class="pl-row">
          <label>Weeks to generate
            <input type="number" name="weeks" min="1" max="52" [ngModel]="weeks()" (ngModelChange)="weeks.set($event)">
          </label>
          <label>Start date
            <input type="date" name="start" [ngModel]="startDate()" (ngModelChange)="startDate.set($event)">
          </label>
        </div>
        <label>Available equipment
          <input type="text" name="equipment" placeholder="empty = everything" [ngModel]="equipment()" (ngModelChange)="equipment.set($event)">
          <span class="hint">Comma separated, e.g. barbell, dumbbell, bench, machine, cable, pull-up-bar.</span>
        </label>
        <label>Excluded exercise ids
          <input type="text" name="excluded" [ngModel]="excluded()" (ngModelChange)="excluded.set($event)">
        </label>
        <div class="pl-row">
          <label>Macrocycle weeks
            <input type="number" name="macro" min="2" max="52" placeholder="optional" [ngModel]="macroWeeks()" (ngModelChange)="macroWeeks.set($event)">
          </label>
          <label>Deload every (weeks)
            <input type="number" name="deload" min="4" max="8" placeholder="none" [ngModel]="deloadEvery()" (ngModelChange)="deloadEvery.set($event)">
          </label>
        </div>
        <button class="primary" type="submit" [disabled]="busy()">{{ busy() ? 'Generating…' : 'Generate preview' }}</button>
        @if (error()) { <p class="err" role="alert">{{ error() }}</p> }
      </form>

      <div class="pl-stack">
        @if (readiness(); as r) {
          <section class="pl-card" aria-label="Catalog readiness">
            <h2>Catalog readiness</h2>
            <div class="pl-stats">
              <span class="badge good">{{ r.usableExercises }} usable exercises</span>
              @for (g of r.byPattern; track g.name) { <span class="badge">{{ g.name }} {{ g.count }}</span> }
            </div>
            <div class="pl-stats">
              @for (g of r.byExperience; track g.name) { <span class="badge">{{ g.name }}: {{ g.count }}</span> }
              @for (g of r.byGoal; track g.name) { <span class="badge">{{ g.name }}: {{ g.count }}</span> }
            </div>
            @if (r.skipped.length) {
              <details><summary>{{ r.skipped.length }} exercise{{ r.skipped.length === 1 ? '' : 's' }} the engine cannot use</summary>
                <ul class="pl-warnings">@for (s of r.skipped; track s.exerciseId) { <li><b>{{ s.exerciseId }}</b>: {{ s.reason }}</li> }</ul>
              </details>
            }
          </section>
        }

        @if (result(); as res) {
          @if (res.macrocycle; as m) {
            <section class="pl-card" aria-label="Macrocycle">
              <h2>{{ m.name }}</h2>
              <p class="hint">{{ m.periodizationModel }} · {{ m.startDate }} to {{ m.endDate }}</p>
              <ul class="pl-phases">@for (p of m.phases; track p.sequenceNumber) { <li><b>{{ p.name }}</b> ({{ p.startDate }} to {{ p.endDate }}): {{ p.objective }}</li> }</ul>
            </section>
          }

          <section class="pl-card" aria-label="Program">
            <h2>Program · {{ res.program.sessions.length }} sessions over {{ res.program.durationWeeks }} weeks</h2>
            <div class="pl-weeks">
              @for (w of weekNumbers(); track w) { <button type="button" [class.on]="week() === w" (click)="week.set(w)">Week {{ w }}</button> }
            </div>
            @for (s of weekSessions(); track s.day) {
              <article class="pl-session">
                <header><strong>{{ s.title }}</strong><span>{{ s.day }} · ~{{ s.estimatedDurationMinutes }} min</span></header>
                <table class="pl-table">
                  <thead><tr><th>#</th><th>Exercise</th><th>Sets × reps</th><th>RIR</th><th>Rest</th><th>Progression</th></tr></thead>
                  <tbody>
                    @for (e of s.exercises; track e.order) {
                      <tr [title]="e.selectionRationale">
                        <td>{{ e.order }}</td>
                        <td>{{ e.exerciseNameSnapshot }}<div class="muted">{{ e.primaryMuscleGroups.join(', ') }}@if (e.secondaryMuscleGroups.length) { · {{ e.secondaryMuscleGroups.join(', ') }} }</div></td>
                        <td>{{ e.sets }} × {{ e.minRepetitions }}–{{ e.maxRepetitions }}@if (e.warmupSets) { <div class="muted">+{{ e.warmupSets }} warm-up</div> }</td>
                        <td>{{ e.targetRepetitionsInReserve ?? '–' }}</td>
                        <td>{{ e.restSeconds }}s</td>
                        <td class="muted">{{ e.progressionRule }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </article>
            }
          </section>

          @if (weekVolume().length) {
            <section class="pl-card" aria-label="Weekly volume">
              <h2>Week {{ week() }} volume by muscle</h2>
              <table class="pl-table">
                <thead><tr><th>Muscle</th><th>Primary sets</th><th>Secondary sets</th><th>Effective sets</th><th>Sessions</th></tr></thead>
                <tbody>
                  @for (m of weekVolume(); track m.muscleGroup) {
                    <tr><td>{{ m.muscleGroup }}</td><td>{{ m.primarySets }}</td><td>{{ m.secondarySets }}</td><td>{{ m.estimatedEffectiveSets }}</td><td>{{ m.primaryFrequencySessions }}</td></tr>
                  }
                </tbody>
              </table>
            </section>
          }

          @if (res.program.qualityReport?.warnings?.length) {
            <section class="pl-card" aria-label="Warnings">
              <h2>{{ res.program.qualityReport!.warnings.length }} review notes</h2>
              <details><summary>Show</summary>
                <ul class="pl-warnings">@for (w of res.program.qualityReport!.warnings; track $index) { <li>{{ w }}</li> }</ul>
              </details>
              <p class="hint">Engine {{ res.program.engineVersion }}. The output is a draft for review, not a clinical prescription.</p>
            </section>
          }

          @if (res.skipped.length) {
            <p class="hint">{{ res.skipped.length }} catalog exercise(s) were not available to the engine (see Catalog readiness).</p>
          }
        } @else if (!busy()) {
          <section class="pl-card"><p class="hint">Choose a profile and press Generate preview.</p></section>
        }
      </div>
    </div>
  `
})
export class PlanningPageComponent {
  private readonly api = inject(AdminApi);

  protected readonly days = DAYS;
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
      this.result.set(await this.api.previewPlan(request));
      this.week.set(1);
    } catch (e) {
      this.result.set(null);
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
