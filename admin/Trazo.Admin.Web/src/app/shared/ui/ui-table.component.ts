import { Component, Input } from '@angular/core';

@Component({
  selector: 'trazo-table',
  standalone: true,
  template: '<div class="trazo-table-wrap"><table [class]="\'trazo-table \' + tableClass"><ng-content /></table></div>'
})
export class UiTableComponent {
  @Input() tableClass = '';
}
