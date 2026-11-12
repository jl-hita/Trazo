import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ConfigFormComponent } from './config-form.component';

@Component({
  selector: 'trazo-setup-page',
  standalone: true,
  imports: [ConfigFormComponent],
  styles: [`
    :host { display: block; min-height: 100vh; padding: 48px 20px; background: #0d1119; color: #e8edf5; }
    .wrap { max-width: 760px; margin: 0 auto; }
    h1 { margin: 0 0 6px; font-size: 30px; font-weight: 600; letter-spacing: -.03em; }
    .eyebrow { margin: 0 0 9px; color: #8291a6; font-size: 10px; font-weight: 700; letter-spacing: .16em; }
    .subtitle { margin: 0 0 26px; color: #98a6b8; font-size: 14px; }
  `],
  template: `
    <div class="wrap">
      <p class="eyebrow">FIRST-RUN SETUP</p>
      <h1>Set up TRAZO</h1>
      <p class="subtitle">Create the superadmin account and connect Google sign-in and email. You can change these later from Settings.</p>
      <trazo-config-form mode="setup" (done)="finished()" />
    </div>
  `
})
export class SetupPageComponent {
  private readonly router = inject(Router);
  protected finished(): void { void this.router.navigateByUrl('/login'); }
}
