export interface LandingLanguage {
  code: string;
  /** Route of the landing in this language. */
  path: string;
  /** Name of the language, in the language itself. */
  name: string;
  rtl?: boolean;
  load: () => Promise<LandingContent>;
}

export interface WaitlistActionText {
  title: string;
  text: string;
  button: string;
  done: string;
  doneText: string;
}

export interface WaitlistPagesText {
  confirm: WaitlistActionText;
  unsubscribe: WaitlistActionText;
  failed: string;
  failedText: string;
  missing: string;
  missingText: string;
  wait: string;
  home: string;
}

export interface LandingContent {
  title: string;
  description: string;
  languageLabel: string;
  eyebrow: string;
  headA: string;
  headB: string;
  lead: string;
  cta: string;
  loopTitle: string;
  loopLead: string;
  loop: { title: string; text: string }[];
  pillarsTitle: string;
  pillars: { title: string; text: string; premium?: boolean }[];
  showcase: { kicker: string; title: string; text: string; points: string[]; premium?: boolean; shots: { file: string; caption: string }[] }[];
  featuresTitle: string;
  features: { title: string; text: string }[];
  pricingTitle: string;
  pricingLead: string;
  freeName: string;
  freePrice: string;
  freePeriod: string;
  freeTagline: string;
  freeFeatures: string[];
  premiumName: string;
  premiumPrice: string;
  premiumPeriod: string;
  premiumTagline: string;
  premiumFeatures: string[];
  premiumAnnual: string;
  pricingNote: string;
  faqTitle: string;
  faq: { q: string; a: string }[];
  shotAlt: string;
  howTitle: string;
  steps: { title: string; text: string }[];
  waitTitle: string;
  waitText: string;
  emailLabel: string;
  emailHint: string;
  consent: string;
  privacyLabel: string;
  send: string;
  note: string;
  ok: string;
  invalid: string;
  later: string;
  fail: string;
  terms: string;
  contact: string;
  admin: string;
  skip: string;
  /** The pages behind the links of the waitlist emails. */
  waitlistPages: WaitlistPagesText;
}

// Un fichero por idioma en `i18n/`; se descarga solo el que se muestra. Para añadir uno: su fichero, sus capturas en
// `public/landing/screenshots/{código}/` y una línea aquí (el servidor debe tener también ese idioma para la lista de espera).
export const LANDING_LANGUAGES: LandingLanguage[] = [
  { code: 'es', path: '', name: 'Español', load: () => import('./i18n/es').then(m => m.default) },
  { code: 'en', path: 'en', name: 'English', load: () => import('./i18n/en').then(m => m.default) },
  { code: 'de', path: 'de', name: 'Deutsch', load: () => import('./i18n/de').then(m => m.default) },
  { code: 'pt', path: 'pt-br', name: 'Português (Brasil)', load: () => import('./i18n/pt').then(m => m.default) },
  { code: 'fr', path: 'fr', name: 'Français', load: () => import('./i18n/fr').then(m => m.default) },
  { code: 'it', path: 'it', name: 'Italiano', load: () => import('./i18n/it').then(m => m.default) },
  { code: 'nl', path: 'nl', name: 'Nederlands', load: () => import('./i18n/nl').then(m => m.default) },
  { code: 'ca', path: 'ca', name: 'Català', load: () => import('./i18n/ca').then(m => m.default) },
  { code: 'gl', path: 'gl', name: 'Galego', load: () => import('./i18n/gl').then(m => m.default) },
  { code: 'eu', path: 'eu', name: 'Euskara', load: () => import('./i18n/eu').then(m => m.default) },
  { code: 'pl', path: 'pl', name: 'Polski', load: () => import('./i18n/pl').then(m => m.default) },
  { code: 'ru', path: 'ru', name: 'Русский', load: () => import('./i18n/ru').then(m => m.default) },
  { code: 'tr', path: 'tr', name: 'Türkçe', load: () => import('./i18n/tr').then(m => m.default) },
  { code: 'ko', path: 'ko', name: '한국어', load: () => import('./i18n/ko').then(m => m.default) },
  { code: 'ja', path: 'ja', name: '日本語', load: () => import('./i18n/ja').then(m => m.default) },
  { code: 'zh', path: 'zh', name: '中文', load: () => import('./i18n/zh').then(m => m.default) },
  { code: 'ar', path: 'ar', name: 'العربية', rtl: true, load: () => import('./i18n/ar').then(m => m.default) },
  { code: 'da', path: 'da', name: 'Dansk', load: () => import('./i18n/da').then(m => m.default) },
  { code: 'nb', path: 'nb', name: 'Norsk bokmål', load: () => import('./i18n/nb').then(m => m.default) },
  { code: 'sv', path: 'sv', name: 'Svenska', load: () => import('./i18n/sv').then(m => m.default) },
  { code: 'fi', path: 'fi', name: 'Suomi', load: () => import('./i18n/fi').then(m => m.default) },
  { code: 'cs', path: 'cs', name: 'Čeština', load: () => import('./i18n/cs').then(m => m.default) },
  { code: 'sk', path: 'sk', name: 'Slovenčina', load: () => import('./i18n/sk').then(m => m.default) },
  { code: 'sl', path: 'sl', name: 'Slovenščina', load: () => import('./i18n/sl').then(m => m.default) },
  { code: 'hr', path: 'hr', name: 'Hrvatski', load: () => import('./i18n/hr').then(m => m.default) },
  { code: 'sr', path: 'sr', name: 'Srpski', load: () => import('./i18n/sr').then(m => m.default) },
  { code: 'bs', path: 'bs', name: 'Bosanski', load: () => import('./i18n/bs').then(m => m.default) }
];

export const landingLanguage = (code: string | null | undefined): LandingLanguage =>
  LANDING_LANGUAGES.find(l => l.code === code) ?? LANDING_LANGUAGES[0];

/** Páginas legales públicas del propio frontend: existen en español y en inglés. */
export const privacyUrl = (code: string): string => (code === 'es' ? '/privacidad' : '/privacy-policy');
export const termsUrl = (code: string): string => (code === 'es' ? '/condiciones' : '/terms');

export const shotsOf = (code: string) => (file: string, caption: string) => ({ file: `/landing/screenshots/${code}/${file}.webp`, caption });
