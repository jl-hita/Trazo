# Marketing panel and the commercial role

Component of the administration console for whoever runs marketing: it answers **"which acquisition strategies bring people who sign up,
train, pay and stay, and what does each one cost?"**. Open to the **superadmin** and to the **commercial** role (`commercial`): the
commercial is created by the superadmin in Users, signs in to the admin console and sees **only** the Marketing page. Every other route
of the console (users, subscriptions, tickets, settings, logs, tax management, plan reviews, even the referral audit) answers `403`.

**Aggregates only.** Nothing in the panel names, lists or lets you pick a person: no e-mail, name, id, device or IP, and the waitlist
appears as counts. Health data (pain, limitations) is not read at all.

## Where people come from (attribution)

| Strategy | How a person is attributed |
|---|---|
| **Campaign** | A campaign has a permanent tracking link `https://<api host>/c/<link name>`. Opening it counts a click and sends the visitor to Google Play with `referrer=utm_source=<channel>&utm_medium=trazo&utm_campaign=<link name>&trazo_cmp=<link name>`. After the install the app reads the Play Install Referrer, reports one install (`POST /api/v1/campaigns/install-referrer`, anonymous) and sends `campaignCode` when the account is created. |
| **Referral** | A friend's invitation code (see `docs/referrals.md`). |
| **Organic** | Everybody else: search, word of mouth, sideloaded builds, another device, a link that never reached Google Play. |

A person counts in **one** strategy: the referral code wins over a campaign, and a campaign wins over organic. First touch is kept
(the first campaign stays), and the campaign can only be attached while the account is created (never to an existing one: a user older
than 10 minutes is refused). The row `campaign_attributions` is deleted with the account; the campaign, its anonymous daily counters
(`campaign_daily_stats`: clicks and installs per day) and the spend entries hold no user.

Link-preview robots (WhatsApp, Slack, Telegram, curl...) and requests without a user agent are redirected but **not counted**. The
`utm_*` values in the referrer are standard, so Google Play Console's own acquisition reports agree with this panel.

Limits to keep in mind (the panel's "How to read" box says the same): installs outside Google Play, on another device from the click, or
after the Install Referrer window (Google documents 90 days) appear as organic. Clicks are not people: one person can open a link many times.

## What is measured per strategy

From the link click to the paid renewal, over the **accounts created in the period** (a cohort): clicks, installs, signups, verified
e-mail, finished onboarding, first workout (activation), free trial, first real payment, renewal, cancellation, refund. Derived:
step-to-step rates, **signup → payer with its 95 % Wilson interval**, trial → payer, renewed share, **retention** (trained in the week that
starts 7 / 30 days after signing up, only counting people old enough), spend, **cost per install / signup / trial / payer**, **estimated
net revenue** and **return on spend**.

* Economic events come from purchases the backend verified with Google Play. **Test purchases and promotional time (welcome period,
  referral rewards) are excluded.**
* Estimated revenue reuses the fiscal estimator and the prices, VAT and Google fee of the tax settings (the commercial never sees that
  profile, only the resulting figure). It ignores per-country prices and currencies: orientative, the truth is in Play Console.
* A strategy with fewer than **20 signups** is flagged *small sample*; the interval is shown next to every rate so luck is not read as skill.
* The server writes plain-language insights: best at turning signups into payers, best retention, cheapest payer, spend without payers,
  clicks that do not become installs, trials that do not convert, high refund rate.

## Pages (admin console → Marketing)

* **Overview**: flows against the previous period (signups, trials, new payers, renewals, cancellations, refunds), conversion of the
  cohort, subscribers right now, where signups come from, week by week.
* **Strategies**: insights, ranking by any metric, channel and campaign comparison tables, CSV download.
* **Campaigns**: create / edit / pause / archive campaigns, copy the link, daily clicks-installs-signups chart, spend entries per day.
* **Funnel & retention**: the funnel and a weekly retention heat map for everybody or one strategy.
* **Other levers**: where the Premium offer is opened (anonymous counter), the invitation programme and the landing waitlist.

## API (`/api/v1/admin/marketing`, policy `marketing` = superadmin + commercial)

| Route | Purpose |
|---|---|
| `GET /options` | Channel types, statuses, reliability threshold. |
| `GET /summary?from&to` | KPIs with previous period, cohort rates, subscribers now, signup mix, weekly series. |
| `GET /strategies?from&to` | Overall, channel rows, campaign rows and insights. |
| `GET /strategies.csv?from&to` | The same table as CSV (formulas in names are defused with a leading `'`). |
| `GET /funnel?from&to&segment` / `GET /retention?from&to&segment` | `segment` = `all`, `organic`, `referral`, `channel:<type>` or `campaign:<link name>`. |
| `GET /paywall?from&to` / `GET /waitlist` / `GET /referrals?from&to` | Offer openings by source, waitlist counts, invitation programme report. |
| `GET/POST /campaigns`, `PUT /campaigns/{id}`, `GET /campaigns/{id}?from&to` | Campaign management and detail (the link name is fixed at creation). |
| `POST /campaigns/{id}/spend`, `DELETE /campaigns/{id}/spend/{spendId}` | Spend per day, in euros. |

The period defaults to the last 90 days and is limited to two years. Public routes (per-IP rate limited, no personal data):
`GET /c/{slug}` and `POST /api/v1/campaigns/install-referrer`. Registration and Google sign-in accept an optional `campaignCode`.

Channel types: `ads`, `social`, `influencer`, `email`, `partner`, `content`, `press`, `event`, `other`.

## Using it to learn what works

1. One campaign per idea and per channel (a creative, an influencer, an ad set): the link name is the experiment. Write what you
   changed in the notes.
2. Enter the spend of every day you pay, or at least weekly, so cost per payer is real.
3. Compare on the **same period** and wait for 20+ signups per strategy before deciding; prefer cost per **payer** and 30-day
   retention over cost per install: cheap installs that never train or pay are not growth.
4. Look at the funnel of the winner and of the loser: the biggest drop between two steps is where to work.
5. Keep what pays back (return on spend above 0 % and healthy retention), pause what has spend and no payers.

## Not included (yet)

Per-country or per-device breakdowns, A/B experiment arms (`growth_events.variant` is reserved), automated spend import from ad platforms,
and sending the weekly summary by e-mail (see the conversion backlog).
