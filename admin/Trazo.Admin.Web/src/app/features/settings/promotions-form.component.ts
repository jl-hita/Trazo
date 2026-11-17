import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApi } from '../../core/admin.api';
import { errorMessage } from '../../core/setup.api';

/** Promotional Premium time: the welcome period of new accounts and the reward of the referral programme. */
@Component({
  selector: 'trazo-promotions-form',
  standalone: true,
  imports: [FormsModule],
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .pr { display: grid; gap: 22px; max-width: 760px; }
    .pr fieldset { margin: 0; padding: 22px; border: 1px solid #293444; border-radius: 8px; background: #131b27; display: grid; gap: 14px; }
    .pr legend { padding: 0 8px; color: #a9c2df; font-size: 12px; letter-spacing: .12em; text-transform: uppercase; }
    .pr label { display: grid; gap: 6px; color: #aab6c7; font-size: 12px; }
    .pr label.check { display: flex; align-items: center; gap: 10px; font-size: 13px; color: #d4deea; }
    .pr input[type=number] { width: 120px; padding: 10px 12px; border: 1px solid #34465b; border-radius: 6px; background: #0d1119; color: #e8edf5; font: inherit; }
    .pr input:focus { outline: 2px solid #6d8eb5; outline-offset: 1px; }
    .pr .hint { color: #7f8da0; font-size: 11px; margin: 0; }
    .pr .actions { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .pr button { padding: 10px 16px; border: 1px solid #6d9bd2; border-radius: 6px; background: #3b6ea8; color: #e4edf8; cursor: pointer; }
    .pr button:disabled { opacity: .45; cursor: not-allowed; }
    .pr .ok { color: #7fd3a0; font-size: 12px; }
    .pr .err { color: #f0a3a3; font-size: 12px; }
  `],
  template: `
    <form class="pr" (ngSubmit)="save()" autocomplete="off">
      <fieldset>
        <legend>Promotions: free Premium time</legend>
        <label class="check">
          <input type="checkbox" name="trialOn" [ngModel]="trialEnabled()" (ngModelChange)="trialEnabled.set($event)" [disabled]="busy()">
          New accounts get a free Premium period
        </label>
        <label>Welcome period (days)
          <input type="number" name="trialDays" min="1" [max]="maxDays()" [ngModel]="trialDays()" (ngModelChange)="trialDays.set(+$event)" [disabled]="busy()">
        </label>
        <p class="hint">Given when the account is activated (e-mail verified, or first Google sign-in). Accounts that already exist are not affected, and a change never
          alters periods already granted: each grant records the days it was given with.</p>
        <label class="check">
          <input type="checkbox" name="refOn" [ngModel]="referralsEnabled()" (ngModelChange)="referralsEnabled.set($event)" [disabled]="busy()">
          Referral programme active (invite codes and rewards)
        </label>
        <label>Referral reward (days of Premium per referred user who pays)
          <input type="number" name="refDays" min="1" [max]="maxDays()" [ngModel]="rewardDays()" (ngModelChange)="rewardDays.set(+$event)" [disabled]="busy()">
        </label>
        <p class="hint">Switching the programme off stops new codes, attributions and rewards; what was already earned is kept.</p>
        <div class="actions">
          <button type="submit" [disabled]="busy()">Save promotions</button>
          @if (message()) { <span class="ok" role="status">{{ message() }}</span> }
          @if (error()) { <span class="err" role="alert">{{ error() }}</span> }
        </div>
      </fieldset>
    </form>
  `
})
export class PromotionsFormComponent {
  private readonly api = inject(AdminApi);
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
