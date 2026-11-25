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
  { code: 'de', path: 'de', name: 'Deutsch', load: () => import('./i18n/de').then(m => m.default) }
];

export const landingLanguage = (code: string | null | undefined): LandingLanguage =>
  LANDING_LANGUAGES.find(l => l.code === code) ?? LANDING_LANGUAGES[0];

/** Páginas legales públicas del propio frontend: existen en español y en inglés. */
export const privacyUrl = (code: string): string => (code === 'es' ? '/privacidad' : '/privacy-policy');
export const termsUrl = (code: string): string => (code === 'es' ? '/condiciones' : '/terms');

export const shotsOf = (code: string) => (file: string, caption: string) => ({ file: `/landing/screenshots/${code}/${file}.webp`, caption });
