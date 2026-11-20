import { Component } from '@angular/core';

@Component({
  selector: 'trazo-table',
  standalone: true,
  template: '<div class="trazo-table-wrap"><table class="trazo-table"><ng-content /></table></div>'
})
export class UiTableComponent {}
