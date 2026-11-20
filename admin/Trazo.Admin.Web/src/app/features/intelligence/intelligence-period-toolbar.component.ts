import { Component, input, output } from '@angular/core';
import { INTELLIGENCE_COPY } from './intelligence.copy';

export interface IntelligencePeriodPreset {
  days: number;
  label: string;
}

/** Isolated period selection and page actions for the Intelligence dashboard. */
@Component({
  selector: 'trazo-intelligence-period-toolbar',
  standalone: true,
  templateUrl: './intelligence-period-toolbar.component.html'
})
export class IntelligencePeriodToolbarComponent {
  readonly copy = INTELLIGENCE_COPY.toolbar;
  readonly presets = input.required<IntelligencePeriodPreset[]>();
  readonly preset = input.required<number | null>();
  readonly from = input.required<string>();
  readonly to = input.required<string>();
  readonly loading = input(false);
  readonly exporting = input(false);

  readonly presetSelected = output<number>();
  readonly fromChanged = output<string>();
  readonly toChanged = output<string>();
  readonly refreshRequested = output<void>();
  readonly exportRequested = output<void>();
}
