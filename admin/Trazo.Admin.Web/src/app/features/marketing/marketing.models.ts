/** A date range, both ends as yyyy-MM-dd (the end is exclusive on the server: the day after). */
export interface Range { from: string; to: string; }

export interface SegmentRow {
  kind: string; key: string; name: string; channel: string | null; status: string | null;
  clicks: number; installs: number; signups: number; verified: number; onboarded: number; activated: number; trials: number;
  paid: number; renewed: number; canceled: number; refunded: number;
  d7Eligible: number; d7Active: number; d30Eligible: number; d30Active: number;
  spend: number; estimatedRevenue: number;
  clickToInstall: number | null; installToSignup: number | null; signupToActivated: number | null; signupToTrial: number | null;
  signupToPaid: number | null; signupToPaidLow: number | null; signupToPaidHigh: number | null; trialToPaid: number | null;
  paidRenewed: number | null; d7: number | null; d30: number | null;
  costPerInstall: number | null; costPerSignup: number | null; costPerTrial: number | null; costPerPaid: number | null;
  returnOnSpend: number | null; reliable: boolean;
}

export interface Insight { kind: 'good' | 'warn' | 'info'; title: string; detail: string; }

export interface StrategiesReport {
  from: string; to: string; minReliable: number; overall: SegmentRow; channels: SegmentRow[]; campaigns: SegmentRow[]; insights: Insight[];
}

export interface FunnelStage { name: string; users: number; ofPrevious: number | null; ofCohort: number | null; }
export interface MarketingFunnel {
  segment: string; segmentName: string; cohort: number; stages: FunnelStage[];
  signupToPaid: number | null; signupToPaidLow: number | null; signupToPaidHigh: number | null; trialToPaid: number | null; paidRenewed: number | null;
}

export interface RetentionCohort { week: string; size: number; paid: number; renewed: number; active: (number | null)[]; }
export interface RetentionReport { segment: string; segmentName: string; weeks: number; cohort: number; average: (number | null)[]; cohorts: RetentionCohort[]; }

export interface Kpi { key: string; label: string; current: number; previous: number; }
export interface WeekPoint { week: string; signups: number; trials: number; firstPurchases: number; renewals: number; cancellations: number; refunds: number; paywallViews: number; }
export interface MixRow { key: string; name: string; signups: number; paid: number; }
export interface SummaryReport {
  kpis: Kpi[]; signupToTrial: number | null; signupToPaid: number | null; trialToPaid: number | null; cohort: number; paywallViews: number;
  subscribers: { paying: number; trialing: number; cancelingAtPeriodEnd: number; billing: number };
  mix: MixRow[]; weeks: WeekPoint[]; waitlistConfirmed: number; activeCampaigns: number; downloads: number | null; downloadToSignup: number | null;
}

export interface PaywallReport {
  total: number; firstPurchases: number; viewsPerPurchase: number | null;
  sources: { source: string; views: number; share: number }[];
  weeks: { week: string; total: number; bySource: Record<string, number> }[];
}

export interface WaitlistReport {
  total: number; confirmed: number; invited: number; confirmRate: number | null; inviteRate: number | null;
  byLanguage: Record<string, number>; weeks: { week: string; joined: number }[];
}

export interface Campaign {
  id: string; slug: string; name: string; channel: string; status: string; startsOn: string | null; endsOn: string | null; notes: string | null;
  createdAt: string; clicks: number; installs: number; signups: number; spend: number;
}
export interface CampaignItem { campaign: Campaign; link: string; }
export interface CampaignInput { slug?: string; name: string; channel: string; status: string; startsOn: string | null; endsOn: string | null; notes: string | null; }
export interface SpendEntry { id: string; day: string; amount: number; note: string | null; createdBy: string | null; }
export interface CampaignDetail {
  campaign: Campaign; row: SegmentRow; spend: SpendEntry[];
  days: { day: string; clicks: number; installs: number; signups: number; spend: number }[];
}

export interface DownloadWeek { week: string; downloads: number | null; signups: number; downloadToSignup: number | null; note: string | null; }
export interface DownloadsReport {
  weeks: DownloadWeek[]; downloads: number; signupsInThoseWeeks: number; downloadToSignup: number | null; unregisteredInstalls: number;
  weeksWithData: number; weeksMissing: number;
}

export interface EconomicsRow {
  kind: string; key: string; name: string; signups: number; payers: number; spend: number; netRevenue: number; paidMonths: number;
  cac: number | null; netPerMonth: number | null; monthlyChurn: number | null; lifetimeMonths: number | null; ltv: number | null;
  ltvToCac: number | null; paybackMonths: number | null; reliable: boolean;
}
export interface EconomicsReport {
  minPayers: number; lifetimeCapMonths: number; overall: EconomicsRow; rows: EconomicsRow[];
  survival: { months: number; retained: number | null; eligible: number }[]; insights: Insight[];
}

export interface MarketingOptions { channels: string[]; statuses: string[]; minReliable: number; retentionWeeks: number; }
