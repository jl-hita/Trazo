import { UI_COPY } from '../../core/ui-copy';
import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { SettingsApi } from './settings.api';
import { errorMessage } from '../../core/api-error';

/** The oldest Android version that may be used and the one users are invited to update to. */
@Component({
  selector: 'trazo-app-version-form',
  standalone: true,
  imports: [FormsModule, UiFormFieldComponent, UiButtonComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './app-version-form.component.scss',
  templateUrl: './app-version-form.component.html'
})
export class AppVersionFormComponent {
  protected readonly labels = UI_COPY.settings.appVersion;

  private readonly api = inject(SettingsApi);
  protected readonly minimum = signal(0);
  protected readonly recommended = signal(0);
  private savedMinimum = 0;
  /** Nothing can be saved before the stored values are known: saving the empty form would turn the policy off. */
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');

  constructor() {
    this.api.getAppVersion().then(v => {
      this.minimum.set(v.minimumAndroidVersionCode); this.recommended.set(v.recommendedAndroidVersionCode);
      this.savedMinimum = v.minimumAndroidVersionCode; this.loaded.set(true);
    }, e => this.error.set(errorMessage(e)));
  }

  protected async save(): Promise<void> {
    // Raising the minimum locks people out until they update: it is confirmed.
    if (this.minimum() > this.savedMinimum && !window.confirm(this.labels.confirm)) return;
    this.busy.set(true); this.message.set(''); this.error.set('');
    try {
      await this.api.saveAppVersion({
        minimumAndroidVersionCode: this.minimum(), recommendedAndroidVersionCode: this.recommended()
      });
      this.savedMinimum = this.minimum();
      this.message.set(this.labels.saved);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
