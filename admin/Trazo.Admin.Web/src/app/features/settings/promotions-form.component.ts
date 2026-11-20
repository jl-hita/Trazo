import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SettingsApi } from './settings.api';
import { errorMessage } from '../../core/setup.api';

/** Promotional Premium time: the welcome period of new accounts and the reward of the referral programme. */
@Component({
  selector: 'trazo-promotions-form',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './promotions-form.component.scss',
  templateUrl: './promotions-form.component.html'
})
export class PromotionsFormComponent {
  private readonly api = inject(SettingsApi);
  protected readonly trialEnabled = signal(true);
  protected readonly trialDays = signal(30);
  protected readonly referralsEnabled = signal(true);
  protected readonly rewardDays = signal(30);
  protected readonly maxDays = signal(365);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');

  constructor() {
    this.api.getPromotions().then(p => {
      this.trialEnabled.set(p.signupTrialEnabled); this.trialDays.set(p.signupTrialDays);
      this.referralsEnabled.set(p.referralsEnabled); this.rewardDays.set(p.referralRewardDays); this.maxDays.set(p.maxDays);
    }, e => this.error.set(errorMessage(e)));
  }

  protected async save(): Promise<void> {
    this.busy.set(true); this.message.set(''); this.error.set('');
    try {
      await this.api.savePromotions({
        signupTrialEnabled: this.trialEnabled(), signupTrialDays: this.trialDays(),
        referralsEnabled: this.referralsEnabled(), referralRewardDays: this.rewardDays()
      });
      this.message.set('Promotions saved.');
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
