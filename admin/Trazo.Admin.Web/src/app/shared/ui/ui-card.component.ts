import { Component, Input } from '@angular/core';

@Component({
  selector: 'trazo-card',
  standalone: true,
  template: '<article [class]="\'trazo-card \' + contentClass" [attr.aria-labelledby]="ariaLabelledBy || null"><ng-content /></article>'
})
export class UiCardComponent {
  @Input() contentClass = '';
  @Input() ariaLabelledBy = '';
}
