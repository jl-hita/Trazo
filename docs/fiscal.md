# Tax management (gestión fiscal)

Component of the administration console for the **owner (autónomo)**: books of income and expenses, VAT and income-tax
figures per quarter, and a **"close month"** button that freezes the month and produces the package the accountant needs.
**Superadmin and accountant** (role `accountant`, shown as "gestor"): administrators, the app and any other role get `403`. The accountant is created by the superadmin in Users, signs in to the admin console and sees **only** this page (no overview, users, plan reviews, logs or settings; the API refuses every other admin route). It can enter entries and close or reopen months like the superadmin.

> The figures are **orientative**. They prepare the work of the accountant; they file nothing and are not tax advice.
> Have the gestor review the VAT treatments, the 303/130/349 numbers and the Google Play income before any filing.

## How TRAZO income works (default model)

Google Play is the seller to the consumer and collects the VAT; the developer *supplies a service to Google*. Therefore:

- Google Play income is booked against **Google Ireland Limited** (VAT id `IE6388047V`, verify it on a current Google
  invoice/earnings report) as a **business-to-business service to an EU customer**: **no Spanish VAT on the invoice**,
  reverse charge, declared in the intra-EU return (**modelo 349**, key `S`) and as an operation not subject by location rules
  in **modelo 303**.
- What counts is the amount in **Google Play's monthly earnings report**. TRAZO shows an **estimate** computed from the
  purchases the backend verified (first purchases, renewals and refunds, monthly or annual by the gap between charges) and
  lets the owner book it in one click (`google_estimate` entries, flagged until edited), to be replaced by the real figure.
- Estimate = (gross price − consumer VAT) − Google fee. Prices, the VAT included in the consumer price (21 %) and the fee
  (15 %) are editable in the fiscal profile. It ignores per-country prices, currencies and test purchases.

## Entries

`fiscal_entries`: one line of the income or expense book. Amounts in euros; VAT, withholding and total are always computed by the server.

| Field | Notes |
|---|---|
| `kind` | `income` / `expense` |
| `vatTreatment` | `domestic` (Spanish VAT at `vatRatePct`), `eu_reverse_charge` (EU business, no VAT on the invoice), `no_vat` (exempt / not subject: self-employed fee, insurance, bank charges, non-EU suppliers) |
| expense with `eu_reverse_charge` | VAT is **self-assessed**: counted as output VAT *and* deductible input VAT (net 0) |
| `withholdingPct` (income) | IRPF withholding applied by the payer (retención soportada) |
| `deductiblePct` (expense) | Share deductible for mixed-use costs; the non-deductible VAT is kept as cost for income tax |
| `category` | Income: `google_play`, `other_income`. Expense: `hosting`, `software`, `store_fees`, `domains`, `advertising`, `professional_services`, `self_employed_fee`, `equipment`, `office`, `training`, `taxes_fees`, `other` |

A negative base is a credit note (abono). The period (`yyyy-MM`) always comes from the date.

## Quarter figures

Computed from the entries, never stored:

- **Modelo 303 (VAT)**: VAT charged on domestic income + self-assessed VAT, minus deductible input VAT; plus the base of EU B2B income (not subject).
- **Modelo 130 (income-tax instalment, direct estimation)**: percentage (default 20 %) of the net result **year to date**
  (income − deductible costs), minus withholdings suffered, minus the instalments of previous quarters; never negative. Can be switched off in the profile.
- **Modelo 349**: EU B2B operations per counterparty (`S` services provided, `A` acquisitions).
- Due dates: 20 April, 20 July, 20 October and 30 January (fourth quarter). Overdue quarters are flagged.

Not covered (ask the gestor): modelo 111/190 for withholdings you apply to others, the 5 % difficult-to-justify expenses of
the annual return, regional taxes, social-security fee scales, and VAT regimes other than the general one.

## Closing a month

`POST /months/{period}/close`:

1. The month must have finished (it cannot be the current or a future month) and the **fiscal profile** must have the legal name and tax id (`422 profile_incomplete`).
2. **Warnings** (non-blocking, require `acknowledgeWarnings: true`, otherwise `409 warnings` with the list): empty month,
   purchases recorded but no Google Play income booked, earlier months with entries still open, Spanish-VAT entries without an
   invoice number, intra-EU entries without the counterparty VAT number.
3. The totals are snapshotted in `fiscal_closures` and the closing is written to `fiscal_audit`.
4. From then on entries of that month **cannot be created, edited or deleted** (`409 month_closed`).

`POST /months/{period}/reopen` needs a reason (10-300 characters) and is audited. The console downloads the package
automatically after closing and offers it again afterwards.

## The package (ZIP)

`GET /months/{period}/package` (closed months only) and `GET /quarters/{year}/{quarter}/package` (the three months closed):

- `LEEME.txt` — owner, closures, contents and the disclaimer.
- `libro_ingresos_*.csv`, `libro_gastos_*.csv` — the books, one line per entry.
- `resumen_*.csv` — totals of the period.
- `resumen_trimestre_{yyyy}T{q}.csv` — orientative 303 / 130 figures and the due date.
- `modelo349_{yyyy}T{q}.csv` — intra-EU operations, when there are any.

CSV: `;` separator, decimal comma, `dd/MM/yyyy`, UTF-8 with BOM (opens directly in Spanish Excel). Text starting with
`=`, `+`, `@` is prefixed with `'` so that a hand-typed concept can never run as a spreadsheet formula.

## API (`/api/v1/admin/fiscal`, superadmin)

| Route | Purpose |
|---|---|
| `GET/PUT /profile` | Owner data, VAT/IRPF rules, Google Play prices, VAT in the consumer price and Google fee |
| `GET /options` | Categories and VAT treatments |
| `GET /years/{year}` | 12 months (status, totals), 4 quarters, year totals |
| `GET /months/{yyyy-MM}` | Entries, totals, Google Play estimate, warnings, `canClose` |
| `POST /months/{yyyy-MM}/book-google-estimate` | Books the estimate as income (once per month) |
| `POST /months/{yyyy-MM}/close` · `/reopen` | Close (body `{acknowledgeWarnings}`) / reopen (body `{reason}`) |
| `GET /months/{yyyy-MM}/package` | ZIP for the accountant |
| `GET /quarters/{year}/{q}` · `/package` | Quarter summary / ZIP |
| `POST /entries` · `PUT/DELETE /entries/{id}` | Ledger lines (refused in closed months) |
| `GET /audit` | Last 200 closings and reopenings |

The fiscal profile lives in `app_settings` (`Fiscal:Profile`); tables are `fiscal_entries`, `fiscal_closures`,
`fiscal_audit` (migration `Fiscal`). Fiscal data is business data of the owner and holds **no user personal data**.

## Pending / ideas

- Attach the invoice file (justificante) to each expense; today only the invoice number is stored.
- Import Google Play's earnings CSV to book the real figure automatically and reconcile it with the estimate.
- Recurring expenses (hosting, self-employed fee) generated every month.
- Modelo 111/190, annual summary (390/100 help) and e-mail delivery of the package to the accountant.
- Review of the rules with the gestor (OSS, exemptions, regimes) before using the numbers in a real return.
