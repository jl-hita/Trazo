import { Component, Input } from '@angular/core';

export type UiStateKind = 'loading' | 'empty' | 'error' | 'success';

@Component({
  selector: 'trazo-state',
  standalone: true,
  template: `
    <section class="trazo-state" [attr.data-kind]="kind"
             [attr.role]="kind === 'error' ? 'alert' : 'status'" aria-live="polite">
      <div class="trazo-state__content">
        @if (title) { <h2>{{ title }}</h2> }
        @if (description) { <p>{{ description }}</p> }
        <ng-content />
      </div>
    </section>
  `
})
export class UiStateComponent {
  @Input() kind: UiStateKind = 'empty';
  @Input() title = '';
  @Input() description = '';
}
