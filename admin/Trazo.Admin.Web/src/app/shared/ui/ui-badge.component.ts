import { Component, Input } from '@angular/core';

export type UiBadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

@Component({
  selector: 'trazo-badge',
  standalone: true,
  template: '<span [class]="\'trazo-badge \' + badgeClass" [attr.data-tone]="tone"><ng-content /></span>'
})
export class UiBadgeComponent {
  @Input() tone: UiBadgeTone = 'neutral';
  @Input() badgeClass = '';
}
