import { Component, ViewEncapsulation, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import {
  AdminApi, AdminExerciseDetail, TransferResult, AdminExerciseInput, AdminExerciseOptions, AdminExerciseSummary, ExerciseAction
} from '../../core/admin.api';
import { ExercisesApi } from './exercises.api';
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
  styleUrl: './exercises.page.scss',
  templateUrl: './exercises.page.html'
})
export class ExercisesPageComponent {
  private readonly api = inject(ExercisesApi);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private requestId = 0;

  protected readonly fields = FIELDS;
  protected readonly imageSlots = [0, 1, 2, 3];
  protected readonly transferBusy = signal(false);
  protected readonly transferConfirm = signal(false);
  protected readonly transferMessage = signal('');
  protected readonly transferSkipped = signal<{ exerciseId: string; reason: string }[]>([]);
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
    void this.api.options().then(o => this.options.set(o), () => undefined);
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
      this.show(await this.api.get(summary.id));
    } catch (e) {
      this.editing.set(true);
      this.panelError.set(errorMessage(e));
    }
  }

  protected async save(): Promise<void> {
    const input = this.toInput();
    const current = this.detail();
    await this.perform(
      () => current ? this.api.update(current.id, input) : this.api.create(input),
      current ? 'Changes saved.' : 'Exercise created.');
  }

  /** Sends the exercises created in TRAZO (with their images) to DietoExpress, after a second click. */
  protected async sendToDietoExpress(): Promise<void> {
    if (!this.transferConfirm()) { this.transferConfirm.set(true); this.transferMessage.set(''); return; }
    this.transferConfirm.set(false);
    this.transferBusy.set(true);
    this.transferSkipped.set([]);
    try {
      const r: TransferResult = await this.api.transferToDietoExpress();
      const remote = r.remote;
      this.transferMessage.set(`Sent ${r.exercises} exercise(s) and ${r.images} image(s) to DietoExpress.` +
        (remote ? ` Added: ${remote.exercisesInserted ?? 0}, updated: ${remote.exercisesUpdated ?? 0}, already there: ${remote.exercisesSkipped ?? 0}.` : ''));
      this.transferSkipped.set(r.skipped);
    } catch (e) {
      this.transferMessage.set(errorMessage(e));
    } finally {
      this.transferBusy.set(false);
    }
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
    await this.imageOperation(() => this.api.uploadImage(current.id, slot, file), 'Image uploaded.');
  }

  protected async removeImage(slot: number): Promise<void> {
    const current = this.detail();
    if (current) await this.imageOperation(() => this.api.deleteImage(current.id, slot), 'Image removed.');
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
    await this.perform(() => this.api.action(current.id, action), messages[action]);
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
      const result = await this.api.list({
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
