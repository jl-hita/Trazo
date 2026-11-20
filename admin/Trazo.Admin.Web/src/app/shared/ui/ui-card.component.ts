import { Component, Input } from '@angular/core';

@Component({
  selector: 'trazo-card',
  standalone: true,
  template: '<section [class]="\'trazo-card \' + contentClass"><ng-content /></section>'
})
export class UiCardComponent {
  @Input() contentClass = '';
}
