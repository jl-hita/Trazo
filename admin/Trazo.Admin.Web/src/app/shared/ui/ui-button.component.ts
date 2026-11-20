import { Component, EventEmitter, Input, Output } from '@angular/core';

export type UiButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';

@Component({
  selector: 'trazo-button',
  standalone: true,
  template: `
    <button class="trazo-button" [class]="'trazo-button trazo-button--' + variant"
            [type]="type" [disabled]="disabled || loading"
            [attr.aria-busy]="loading ? 'true' : null" (click)="pressed.emit()">
      @if (loading) { <span class="trazo-button__spinner" aria-hidden="true"></span> }
      <ng-content />
    </button>
  `
})
export class UiButtonComponent {
  @Input() variant: UiButtonVariant = 'secondary';
  @Input() type: 'button' | 'submit' | 'reset' = 'button';
  @Input() disabled = false;
  @Input() loading = false;
  @Output() pressed = new EventEmitter<void>();
}
