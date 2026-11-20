import { Component, Input } from '@angular/core';

@Component({
  selector: 'trazo-form-field',
  standalone: true,
  template: `
    <div [class]="'trazo-field ' + fieldClass">
      <label [for]="controlId">{{ label }}</label>
      <ng-content />
      @if (error) { <small class="trazo-field__error" role="alert">{{ error }}</small> }
      @else if (hint) { <small class="trazo-field__hint">{{ hint }}</small> }
    </div>
  `
})
export class UiFormFieldComponent {
  @Input({ required: true }) controlId = '';
  @Input({ required: true }) label = '';
  @Input() hint = '';
  @Input() error = '';
  @Input() fieldClass = '';
}
