import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ConfigFormComponent } from './config-form.component';
import { SETUP_PAGE_COPY } from './setup.page.copy';

@Component({
  selector: 'trazo-setup-page',
  standalone: true,
  imports: [ConfigFormComponent],
  styleUrl: './setup.page.scss',
  templateUrl: './setup.page.html'
})
export class SetupPageComponent {
  private readonly router = inject(Router);
  protected readonly copy = SETUP_PAGE_COPY;
  protected finished(): void { void this.router.navigateByUrl('/login'); }
}
