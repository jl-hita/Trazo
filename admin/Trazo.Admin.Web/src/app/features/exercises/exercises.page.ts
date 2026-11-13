import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import {
  AdminApi, AdminExerciseDetail, AdminExerciseInput, AdminExerciseOptions, AdminExerciseSummary, ExerciseAction
} from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

interface Draft {
  id: string; nameEs: string; nameEn: string; descriptionEs: string; instructionsEs: string; tipsEs: string;
  movementPattern: string; difficulty: string; minimumExperience: string; forceType: string; mechanic: string;
  bodyPart: string; equipment: string; requiredEquipment: string; primaryMuscles: string; secondaryMuscles: string;
  suitableGoals: string; met: number; selectionPriority: number; isUnilateral: boolean; isBodyweight: boolean;
}

type Kind = 'text' | 'textarea' | 'lines' | 'tags' | 'number';
interface Field { key: keyof Draft; label: string; kind: Kind; options?: keyof AdminExerciseOptions; hint?: string; required?: boolean; newOnly?: boolean; }

const EMPTY: Draft = {
  id: '', nameEs: '', nameEn: '', descriptionEs: '', instructionsEs: '', tipsEs: '', movementPattern: '', difficulty: '',
  minimumExperience: '', forceType: '', mechanic: '', bodyPart: '', equipment: '', requiredEquipment: '', primaryMuscles: '',
  secondaryMuscles: '', suitableGoals: '', met: 3.5, selectionPriority: 50, isUnilateral: false, isBodyweight: false
};

const FIELDS: Field[] = [
  { key: 'id', label: 'Identifier', kind: 'text', hint: 'Optional. Leave empty to generate it from the English name.', newOnly: true },
  { key: 'nameEs', label: 'Name (Spanish)', kind: 'text', required: true },
  { key: 'nameEn', label: 'Name (English)', kind: 'text', required: true },
  { key: 'descriptionEs', label: 'Description (Spanish)', kind: 'textarea', required: true },
  { key: 'instructionsEs', label: 'Instructions', kind: 'lines', required: true, hint: 'One step per line.' },
  { key: 'tipsEs', label: 'Tips', kind: 'lines', hint: 'One tip per line.' },
  { key: 'movementPattern', label: 'Movement pattern', kind: 'text', options: 'movementPatterns', required: true },
  { key: 'difficulty', label: 'Difficulty', kind: 'text', options: 'difficulties', required: true },
  { key: 'minimumExperience', label: 'Minimum experience', kind: 'text', options: 'experiences', required: true },
  { key: 'primaryMuscles', label: 'Primary muscles', kind: 'tags', options: 'muscles', required: true, hint: 'Separated by commas.' },
  { key: 'secondaryMuscles', label: 'Secondary muscles', kind: 'tags', options: 'muscles', hint: 'Separated by commas.' },
  { key: 'equipment', label: 'Main equipment', kind: 'text', options: 'equipment' },
  { key: 'requiredEquipment', label: 'Required equipment', kind: 'tags', options: 'equipment', hint: 'Separated by commas.' },
  { key: 'suitableGoals', label: 'Suitable goals', kind: 'tags', options: 'goals', required: true, hint: 'Separated by commas.' },
  { key: 'forceType', label: 'Force type', kind: 'text', options: 'forceTypes' },
  { key: 'mechanic', label: 'Mechanic', kind: 'text', options: 'mechanics' },
  { key: 'bodyPart', label: 'Body part', kind: 'text', options: 'bodyParts' },
  { key: 'met', label: 'MET (energy cost)', kind: 'number' },
  { key: 'selectionPriority', label: 'Selection priority (0-100)', kind: 'number', hint: 'Higher values are preferred when plans are generated.' }
];

const OPTION_LISTS: (keyof AdminExerciseOptions)[] = [
  'movementPatterns', 'difficulties', 'experiences', 'equipment', 'forceTypes', 'mechanics', 'bodyParts', 'muscles', 'goals'
];

const lines = (value: string) => value.split('\n').map(v => v.trim()).filter(Boolean);
const tags = (value: string) => value.split(',').map(v => v.trim()).filter(Boolean);

/** Exercise library: browse, edit, create and hide exercises. */
@Component({
  selector: 'trazo-exercises-page',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .ex-layout { display: grid; grid-template-columns: minmax(0, 1fr) 460px; gap: 18px; align-items: start; }
    .ex-layout > section { min-width: 0; overflow-x: auto; }
    .ex-toolbar { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 14px; }
    .ex-toolbar input, .ex-toolbar select, .ex-panel input[type=text], .ex-panel input[type=number], .ex-panel textarea {
      padding: 9px 11px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font-size: 13px; font-family: inherit; }
    .ex-toolbar input { flex: 1 1 200px; }
    .ex-toolbar input:focus, .ex-toolbar select:focus, .ex-panel input:focus, .ex-panel textarea:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .ex-toolbar button.primary, .ex-panel button.primary { background: #3b6ea8; border-color: #6d9bd2; }
    .ex-toolbar button, .ex-pager button, .ex-panel button { padding: 8px 14px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 12px; cursor: pointer; }
    .ex-pager button:disabled, .ex-panel button:disabled { opacity: .45; cursor: not-allowed; }
    .ex-table { width: 100%; border-collapse: collapse; border: 1px solid #293444; background: #131b27; font-size: 13px; }
    .ex-table th { padding: 11px 14px; text-align: left; color: #8291a6; font-size: 10px; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; border-bottom: 1px solid #293444; white-space: nowrap; }
    .ex-table td { padding: 10px 14px; border-bottom: 1px solid #1d2735; vertical-align: middle; }
    .ex-table tr.row { cursor: pointer; }
    .ex-table tr.row:hover td, .ex-table tr.row.selected td { background: #182231; }
    .ex-table tr.inactive td { opacity: .55; }
    .ex-table .name { color: #e8edf5; font-weight: 500; }
    .ex-table .sub { color: #8291a6; font-size: 11px; }
    .badge { display: inline-block; margin: 0 4px 3px 0; padding: 2px 7px; border: 1px solid #34465b; border-radius: 999px; color: #aab6c7; font-size: 10px; letter-spacing: .04em; }
    .badge.bad { border-color: #7a3b3b; color: #f0a3a3; }
    .badge.warn { border-color: #6b5a2e; color: #e6d39b; }
    .badge.good { border-color: #2f6a4b; color: #7fd3a0; }
    .ex-pager { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-top: 12px; color: #8291a6; font-size: 12px; }
    .ex-panel { position: sticky; top: 16px; max-height: calc(100vh - 32px); overflow-y: auto; padding: 20px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 14px; }
    .ex-panel h2 { margin: 0; font-size: 17px; font-weight: 600; color: #f2f5fa; }
    .ex-panel label { display: grid; gap: 6px; color: #aab6c7; font-size: 12px; }
    .ex-panel .check { display: flex; align-items: center; gap: 8px; }
    .ex-panel .hint { color: #7f8da0; font-size: 11px; }
    .ex-panel .err { color: #f0a3a3; font-size: 12px; margin: 0; }
    .ex-panel .ok { color: #7fd3a0; font-size: 12px; margin: 0; }
    .ex-panel .actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .ex-panel button.danger { border-color: #7a3b3b; color: #f0a3a3; background: #2a1616; }
    .ex-panel .meta { display: flex; flex-wrap: wrap; gap: 4px; }
    .ex-panel .images { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 8px; }
    .ex-panel .img-slot { display: grid; gap: 6px; justify-items: stretch; min-width: 0; }
    .ex-panel .img-empty { box-sizing: border-box; }
    .ex-panel .images img, .ex-panel .img-empty { width: 100%; aspect-ratio: 1; object-fit: cover; border: 1px solid #293444; border-radius: 6px; background: #0d1119; }
    .ex-panel .img-empty { display: grid; place-items: center; color: #7f8da0; font-size: 11px; border-style: dashed; }
    .ex-panel .img-btn, .ex-panel .img-remove { padding: 5px 6px; border: 1px solid #4a6585; border-radius: 6px; background: #1b2a3e; color: #e4edf8; font-size: 11px; text-align: center; cursor: pointer; }
    .ex-panel .img-remove { border-color: #7a3b3b; color: #f0a3a3; background: #2a1616; }
    .ex-panel .empty { color: #8291a6; font-size: 13px; text-align: center; padding: 18px 0; }
    .ex-panel .notice { margin: 0; padding: 10px 12px; border: 1px solid #6b5a2e; border-radius: 6px; background: #2a2412; color: #e6d39b; font-size: 11px; line-height: 1.5; }
    @media (max-width: 1360px) { .ex-layout { grid-template-columns: 1fr; } .ex-panel { position: static; max-height: none; } }
  `],
  template: `
    <div class="page-heading">
      <div>
        <p class="eyebrow">TRAINING LIBRARY</p>
        <h1>Exercises</h1>
        <p class="subtitle">The catalog used by the apps and by plan generation.</p>
      </div>
    </div>

    <div class="ex-layout">
      <section aria-label="Exercise list">
        <div class="ex-toolbar">
          <input type="search" placeholder="Search by name or identifier" aria-label="Search exercises" [ngModel]="q()" (ngModelChange)="onSearch($event)">
          <select aria-label="Movement pattern" [ngModel]="pattern()" (ngModelChange)="setFilter('pattern', $event)">
            <option value="">All patterns</option>
            @for (p of options()?.movementPatterns ?? []; track p) { <option [value]="p">{{ p }}</option> }
          </select>
          <select aria-label="Status" [ngModel]="status()" (ngModelChange)="setFilter('status', $event)">
            <option value="">Any status</option>
            <option value="active">Active</option>
            <option value="inactive">Hidden</option>
          </select>
          <select aria-label="Source" [ngModel]="source()" (ngModelChange)="setFilter('source', $event)">
            <option value="">Any source</option>
            @for (s of options()?.sources ?? []; track s) { <option [value]="s">{{ s }}</option> }
          </select>
          <select aria-label="Sort" [ngModel]="sort()" (ngModelChange)="setFilter('sort', $event)">
            <option value="">Sort: name</option>
            <option value="priority">Sort: priority</option>
            <option value="updated">Sort: recently edited</option>
          </select>
          <button class="primary" type="button" (click)="startNew()">New exercise</button>
        </div>

        @if (error()) { <p class="err" style="color:#f0a3a3;font-size:12px" role="alert">{{ error() }}</p> }

        <table class="ex-table">
          <thead><tr><th>Exercise</th><th>Pattern</th><th>Level</th><th>Priority</th><th>Status</th></tr></thead>
          <tbody>
            @for (e of exercises(); track e.id) {
              <tr class="row" [class.selected]="selectedId() === e.id" [class.inactive]="!e.isActive" (click)="select(e)" tabindex="0" (keydown.enter)="select(e)">
                <td><div class="name">{{ e.nameEs }}</div><div class="sub">{{ e.nameEn }} · {{ e.equipment || 'no equipment' }}</div></td>
                <td>{{ e.movementPattern }}</td>
                <td>{{ e.minimumExperience }}</td>
                <td>{{ e.selectionPriority }}</td>
                <td>
                  @if (!e.isActive) { <span class="badge bad">hidden</span> }
                  @if (e.locallyCurated) { <span class="badge good">edited</span> }
                  <span class="badge">{{ e.source === 'trazo' ? 'TRAZO' : 'imported' }}</span>
                  @if (e.imageCount === 0) { <span class="badge warn">no images</span> }
                </td>
              </tr>
            } @empty {
              <tr><td colspan="5" class="sub" style="text-align:center;padding:26px">{{ loading() ? 'Loading…' : 'No exercises match these filters.' }}</td></tr>
            }
          </tbody>
        </table>

        <div class="ex-pager">
          <span>{{ totalCount() }} exercise{{ totalCount() === 1 ? '' : 's' }} · page {{ page() }} of {{ totalPages() || 1 }}</span>
          <span>
            <button type="button" (click)="goTo(page() - 1)" [disabled]="page() <= 1 || loading()">Previous</button>
            <button type="button" (click)="goTo(page() + 1)" [disabled]="page() >= totalPages() || loading()">Next</button>
          </span>
        </div>
      </section>

      <aside class="ex-panel" aria-label="Exercise editor">
        @if (editing()) {
          <h2>{{ isNew() ? 'New exercise' : draft().nameEs }}</h2>

          @if (detail(); as d) {
            <div class="meta">
              @if (!d.isActive) { <span class="badge bad">hidden from the catalog</span> }
              @if (d.locallyCurated) { <span class="badge good">edited locally</span> }
              <span class="badge">{{ d.importedFrom ? 'imported from ' + d.importedFrom : 'created in TRAZO' }}</span>
            </div>
            @if (d.locallyCurated) {
              <p class="notice">This exercise was edited here, so future imports from DietoExpress will not overwrite it.</p>
            }
            <div class="images" aria-label="Images">
              @for (slot of imageSlots; track slot) {
                <div class="img-slot">
                  @if (d.imageSlots.includes(slot)) {
                    <img [src]="imageUrl(d.id, slot)" [alt]="d.nameEs + ' ' + (slot + 1)">
                  } @else { <div class="img-empty">Image {{ slot + 1 }}</div> }
                  <label class="img-btn">{{ d.imageSlots.includes(slot) ? 'Replace' : 'Upload' }}
                    <input type="file" accept="image/webp,image/png,image/jpeg" hidden (change)="upload(slot, $event)" [disabled]="busy()">
                  </label>
                  @if (d.imageSlots.includes(slot)) { <button type="button" class="img-remove" (click)="removeImage(slot)" [disabled]="busy()">Remove</button> }
                </div>
              }
            </div>
            <p class="hint">WebP, PNG or JPEG up to 1 MB. Uploading replaces an imported image and keeps it through future imports.</p>
          } @else {
            <p class="hint">Create the exercise first; images can be added once it exists.</p>
          }

          @for (f of fields; track f.key) {
            @if (!f.newOnly || isNew()) {
              <label>
                <span>{{ f.label }}@if (f.required) { <span aria-hidden="true"> *</span> }</span>
                @switch (f.kind) {
                  @case ('textarea') { <textarea rows="3" [ngModel]="text(f.key)" (ngModelChange)="patch(f.key, $event)"></textarea> }
                  @case ('lines') { <textarea rows="4" [ngModel]="text(f.key)" (ngModelChange)="patch(f.key, $event)"></textarea> }
                  @case ('number') { <input type="number" step="any" [ngModel]="text(f.key)" (ngModelChange)="patch(f.key, $event)"> }
                  @default { <input type="text" [attr.list]="f.options ? 'list-' + f.options : null" [ngModel]="text(f.key)" (ngModelChange)="patch(f.key, $event)"> }
                }
                @if (f.hint) { <span class="hint">{{ f.hint }}</span> }
                @if (fieldErrors()[f.key]) { <span class="err">{{ fieldErrors()[f.key] }}</span> }
              </label>
            }
          }
          <label class="check"><input type="checkbox" [ngModel]="draft().isUnilateral" (ngModelChange)="patch('isUnilateral', $event)"> Unilateral (one side at a time)</label>
          <label class="check"><input type="checkbox" [ngModel]="draft().isBodyweight" (ngModelChange)="patch('isBodyweight', $event)"> Bodyweight</label>

          @for (list of optionLists; track list) {
            <datalist [id]="'list-' + list">
              @for (v of options()?.[list] ?? []; track v) { <option [value]="v"></option> }
            </datalist>
          }

          <div class="actions">
            <button class="primary" type="button" (click)="save()" [disabled]="busy()">{{ isNew() ? 'Create exercise' : 'Save changes' }}</button>
            @if (detail(); as d) {
              @if (d.isActive) {
                <button type="button" (click)="run('deactivate')" [disabled]="busy()" [class.danger]="confirming() === 'deactivate'">
                  {{ confirming() === 'deactivate' ? 'Confirm: hide from the catalog' : 'Hide from the catalog' }}
                </button>
              } @else {
                <button type="button" (click)="run('activate')" [disabled]="busy()">Show in the catalog</button>
              }
              @if (d.locallyCurated && d.importedFrom) {
                <button type="button" (click)="run('release-curation')" [disabled]="busy()" [class.danger]="confirming() === 'release-curation'">
                  {{ confirming() === 'release-curation' ? 'Confirm: allow imports to overwrite' : 'Allow imports to overwrite' }}
                </button>
              }
            }
          </div>

          @if (message()) { <p class="ok" role="status">{{ message() }}</p> }
          @if (panelError()) { <p class="err" role="alert">{{ panelError() }}</p> }
        } @else {
          <p class="empty">Select an exercise to edit it, or create a new one.</p>
        }
      </aside>
    </div>
  `
})
export class ExercisesPageComponent {
  private readonly api = inject(AdminApi);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private requestId = 0;

  protected readonly fields = FIELDS;
  protected readonly imageSlots = [0, 1, 2, 3];
  private readonly imageVersion = signal(0);
  protected readonly optionLists = OPTION_LISTS;
  protected readonly options = signal<AdminExerciseOptions | null>(null);
  protected readonly exercises = signal<AdminExerciseSummary[]>([]);
  protected readonly q = signal('');
  protected readonly pattern = signal('');
  protected readonly status = signal('');
  protected readonly source = signal('');
  protected readonly sort = signal('');
  protected readonly page = signal(1);
  protected readonly totalPages = signal(1);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  protected readonly editing = signal(false);
  protected readonly detail = signal<AdminExerciseDetail | null>(null);
  protected readonly draft = signal<Draft>({ ...EMPTY });
  protected readonly selectedId = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly panelError = signal('');
  protected readonly fieldErrors = signal<Record<string, string>>({});
  protected readonly confirming = signal<ExerciseAction | null>(null);
  protected readonly isNew = computed(() => this.editing() && this.detail() === null);

  constructor() {
    void this.load();
    void this.api.exerciseOptions().then(o => this.options.set(o), () => undefined);
  }

  protected onSearch(value: string): void {
    this.q.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => { this.page.set(1); void this.load(); }, 300);
  }

  protected setFilter(kind: 'pattern' | 'status' | 'source' | 'sort', value: string): void {
    this[kind].set(value);
    this.page.set(1);
    void this.load();
  }

  protected goTo(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.page.set(page);
    void this.load();
  }

  protected text(key: keyof Draft): string { return String(this.draft()[key] ?? ''); }

  protected patch(key: keyof Draft, value: unknown): void {
    this.draft.update(d => ({ ...d, [key]: value }));
  }

  protected startNew(): void {
    this.reset();
    this.selectedId.set(null);
    this.detail.set(null);
    this.draft.set({ ...EMPTY });
    this.editing.set(true);
  }

  protected async select(summary: AdminExerciseSummary): Promise<void> {
    this.reset();
    try {
      this.show(await this.api.getExercise(summary.id));
    } catch (e) {
      this.editing.set(true);
      this.panelError.set(errorMessage(e));
    }
  }

  protected async save(): Promise<void> {
    const input = this.toInput();
    const current = this.detail();
    await this.perform(
      () => current ? this.api.updateExercise(current.id, input) : this.api.createExercise(input),
      current ? 'Changes saved.' : 'Exercise created.');
  }

  protected imageUrl(id: string, slot: number): string {
    return `/api/v1/exercises/${encodeURIComponent(id)}/images/${slot}?v=${this.imageVersion()}`;
  }

  protected async upload(slot: number, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    const current = this.detail();
    if (!file || !current) return;
    if (!['image/webp', 'image/png', 'image/jpeg'].includes(file.type)) { this.panelError.set('Use a WebP, PNG or JPEG image.'); return; }
    if (file.size > 1024 * 1024) { this.panelError.set('The image is larger than 1 MB.'); return; }
    await this.imageOperation(() => this.api.uploadExerciseImage(current.id, slot, file), 'Image uploaded.');
  }

  protected async removeImage(slot: number): Promise<void> {
    const current = this.detail();
    if (current) await this.imageOperation(() => this.api.deleteExerciseImage(current.id, slot), 'Image removed.');
  }

  /** Updates only the image state, so unsaved edits in the form are kept. */
  private async imageOperation(call: () => Promise<AdminExerciseDetail>, success: string): Promise<void> {
    this.busy.set(true);
    this.message.set('');
    this.panelError.set('');
    try {
      this.detail.set(await call());
      this.imageVersion.update(v => v + 1);
      this.message.set(success);
      await this.load();
    } catch (e) {
      this.panelError.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async run(action: ExerciseAction): Promise<void> {
    const current = this.detail();
    if (!current) return;
    if ((action === 'deactivate' || action === 'release-curation') && this.confirming() !== action) {
      this.confirming.set(action);
      this.message.set('');
      return;
    }
    const messages: Record<ExerciseAction, string> = {
      activate: 'The exercise is visible again.', deactivate: 'The exercise is hidden from the catalog.',
      'release-curation': 'Future imports may overwrite this exercise.'
    };
    await this.perform(() => this.api.exerciseAction(current.id, action), messages[action]);
  }

  private async perform(call: () => Promise<AdminExerciseDetail>, success: string): Promise<void> {
    this.busy.set(true);
    this.panelError.set('');
    this.fieldErrors.set({});
    this.confirming.set(null);
    try {
      this.show(await call());
      this.message.set(success);
      await this.load();
      void this.api.exerciseOptions().then(o => this.options.set(o), () => undefined);
    } catch (e) {
      this.fieldErrors.set(this.collectFieldErrors(e));
      this.panelError.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private collectFieldErrors(error: unknown): Record<string, string> {
    const errors = error instanceof HttpErrorResponse ? (error.error?.errors as Record<string, string[]> | undefined) : undefined;
    return Object.fromEntries(Object.entries(errors ?? {}).map(([k, v]) => [k, v.join(' ')]));
  }

  private reset(): void {
    this.message.set('');
    this.panelError.set('');
    this.fieldErrors.set({});
    this.confirming.set(null);
  }

  private show(d: AdminExerciseDetail): void {
    this.detail.set(d);
    this.selectedId.set(d.id);
    this.editing.set(true);
    this.draft.set({
      id: d.id, nameEs: d.nameEs, nameEn: d.nameEn, descriptionEs: d.descriptionEs,
      instructionsEs: d.instructionsEs.join('\n'), tipsEs: d.tipsEs.join('\n'), movementPattern: d.movementPattern,
      difficulty: d.difficulty, minimumExperience: d.minimumExperience, forceType: d.forceType, mechanic: d.mechanic,
      bodyPart: d.bodyPart, equipment: d.equipment ?? '', requiredEquipment: d.requiredEquipment.join(', '),
      primaryMuscles: d.primaryMuscles.join(', '), secondaryMuscles: d.secondaryMuscles.join(', '),
      suitableGoals: d.suitableGoals.join(', '), met: d.met, selectionPriority: d.selectionPriority,
      isUnilateral: d.isUnilateral, isBodyweight: d.isBodyweight
    });
  }

  private toInput(): AdminExerciseInput {
    const d = this.draft();
    return {
      id: d.id.trim() || undefined, nameEs: d.nameEs, nameEn: d.nameEn, descriptionEs: d.descriptionEs,
      instructionsEs: lines(d.instructionsEs), tipsEs: lines(d.tipsEs), forceType: d.forceType, mechanic: d.mechanic,
      difficulty: d.difficulty, equipment: d.equipment, bodyPart: d.bodyPart, met: Number(d.met) || 0,
      isUnilateral: d.isUnilateral, isBodyweight: d.isBodyweight, movementPattern: d.movementPattern,
      primaryMuscles: tags(d.primaryMuscles), secondaryMuscles: tags(d.secondaryMuscles),
      minimumExperience: d.minimumExperience, suitableGoals: tags(d.suitableGoals), requiredEquipment: tags(d.requiredEquipment),
      selectionPriority: Number.isFinite(Number(d.selectionPriority)) ? Number(d.selectionPriority) : 50
    };
  }

  private async load(): Promise<void> {
    const id = ++this.requestId;
    this.loading.set(true);
    this.error.set('');
    try {
      const result = await this.api.listExercises({
        q: this.q(), movementPattern: this.pattern(), status: this.status(), source: this.source(),
        sort: this.sort(), page: this.page(), pageSize: 25
      });
      if (id !== this.requestId) return;
      this.exercises.set(result.items);
      this.totalCount.set(result.totalCount);
      this.totalPages.set(result.totalPages);
    } catch (e) {
      if (id === this.requestId) this.error.set(errorMessage(e));
    } finally {
      if (id === this.requestId) this.loading.set(false);
    }
  }
}
