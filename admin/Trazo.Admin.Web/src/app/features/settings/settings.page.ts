import { Component, inject, signal } from '@angular/core';
import { SetupApi, SettingsView, errorMessage } from '../../core/setup.api';
import { ConfigFormComponent } from '../setup/config-form.component';

@Component({
  selector: 'trazo-settings-page',
  standalone: true,
  imports: [ConfigFormComponent],
  template: `
    <div class="page-heading">
      <div><p class="eyebrow">SYSTEM</p><h1>Settings</h1><p class="subtitle">Google sign-in and email. Changes apply immediately, without restarting the API.</p></div>
    </div>
    @if (error()) { <p class="err" role="alert" style="color:#f0a3a3">{{ error() }}</p> }
    @if (settings(); as s) { <trazo-config-form mode="settings" [initial]="s" /> }
  `
})
export class SettingsPageComponent {
  protected readonly settings = signal<SettingsView | null>(null);
  protected readonly error = signal('');

  constructor() {
    inject(SetupApi).getSettings().then(s => this.settings.set(s), e => this.error.set(errorMessage(e)));
  }
}
