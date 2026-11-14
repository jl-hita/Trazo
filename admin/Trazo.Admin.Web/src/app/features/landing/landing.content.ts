export type LandingLang = 'es' | 'en';

export interface LandingContent {
  title: string;
  description: string;
  otherLang: LandingLang;
  otherPath: string;
  otherLabel: string;
  otherAria: string;
  eyebrow: string;
  headA: string;
  headB: string;
  lead: string;
  cta: string;
  featuresTitle: string;
  features: { title: string; text: string }[];
  shotsTitle: string;
  shots: { file: string; caption: string }[];
  shotAlt: string;
  howTitle: string;
  steps: { title: string; text: string }[];
  waitTitle: string;
  waitText: string;
  emailLabel: string;
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
}

// Política y condiciones: de momento en el sitio legal público de GitHub Pages.
export const PRIVACY_URL = 'https://jl-hita.github.io/trazo-legal/privacidad.html';
export const TERMS_URL = 'https://jl-hita.github.io/trazo-legal/condiciones.html';

const shots = (lang: LandingLang, captions: string[]) =>
  ['02-plan', '03-session', '04-workout', '06-catalog'].map((f, i) => ({ file: `/landing/screenshots/${lang}/${f}.png`, caption: captions[i] }));

export const LANDING: Record<LandingLang, LandingContent> = {
  es: {
    title: 'TRAZO · Entrenamiento de fuerza con plan y progresión',
    description: 'TRAZO genera un plan de fuerza personalizado con progresión semanal y te guía sesión a sesión. Apúntate a la lista de espera.',
    otherLang: 'en', otherPath: '/en', otherLabel: 'English', otherAria: 'Open the English version',
    eyebrow: 'Entrenamiento de fuerza',
    headA: 'Tu plan de fuerza,', headB: 'semana a semana',
    lead: 'TRAZO crea un plan personalizado con progresión, te dice qué hacer en cada sesión y lo ajusta según entrenas. Sin hojas de cálculo.',
    cta: 'Quiero entrar en la lista de espera',
    featuresTitle: 'Todo lo que necesitas para entrenar con criterio',
    features: [
      { title: 'Plan con progresión', text: 'Fases, semanas de descarga y cargas objetivo pensadas para que avances sin estancarte.' },
      { title: 'Cada sesión, clara', text: 'Series, repeticiones, RIR y descanso de cada ejercicio, con su imagen y la técnica a mano.' },
      { title: 'Se adapta a ti', text: 'Tu nivel, tus días y tus limitaciones. El plan se genera y se ajusta a medida que registras tus sesiones.' },
      { title: 'Registro sin fricción', text: 'Anota cada serie y usa el temporizador de descanso integrado. Funciona aunque pierdas la conexión.' },
      { title: 'Catálogo de ejercicios', text: 'Más de 140 ejercicios con búsqueda, filtros y fichas con imágenes.' },
      { title: 'Tu cuenta, protegida', text: 'Acceso con Google o correo, verificación en dos pasos y datos cifrados en el dispositivo.' }
    ],
    shotsTitle: 'Así se ve',
    shots: shots('es', ['Tu semana', 'La sesión, al detalle', 'Registro de series', 'Catálogo']),
    shotAlt: 'Captura de la app TRAZO',
    howTitle: 'Cómo funciona',
    steps: [
      { title: 'Cuéntanos cómo entrenas', text: 'Objetivo, nivel, días disponibles y material.' },
      { title: 'Recibe tu plan', text: 'Un macrociclo con fases y la semana en curso al detalle.' },
      { title: 'Entrena y registra', text: 'El plan aprende de tus sesiones y se ajusta.' }
    ],
    waitTitle: 'Estamos preparando el lanzamiento',
    waitText: 'Déjanos tu correo y te avisaremos cuando TRAZO esté disponible en Google Play. Solo te escribiremos por eso.',
    emailLabel: 'Correo electrónico',
    consent: 'Acepto que TRAZO use mi correo para avisarme del lanzamiento. Puedo retirarlo cuando quiera. Más información en la',
    privacyLabel: 'política de privacidad',
    send: 'Apuntarme',
    note: 'TRAZO ofrece planes de entrenamiento orientativos y no sustituye el consejo de un profesional sanitario.',
    ok: '¡Listo! Te avisaremos cuando TRAZO esté disponible.',
    invalid: 'Revisa el correo y acepta el consentimiento.',
    later: 'Demasiados intentos. Inténtalo más tarde.',
    fail: 'No hemos podido guardar tu correo. Inténtalo de nuevo en unos minutos.',
    terms: 'Condiciones de uso', contact: 'Contacto', admin: 'Acceso administración', skip: 'Saltar al contenido'
  },
  en: {
    title: 'TRAZO · Strength training with a plan and progression',
    description: 'TRAZO builds a personalised strength plan with weekly progression and guides you session by session. Join the waitlist.',
    otherLang: 'es', otherPath: '/', otherLabel: 'Español', otherAria: 'Abrir la versión en español',
    eyebrow: 'Strength training',
    headA: 'Your strength plan,', headB: 'week by week',
    lead: 'TRAZO builds a personalised plan with progression, tells you what to do each session and adjusts it as you train. No spreadsheets.',
    cta: 'Join the waitlist',
    featuresTitle: 'Everything you need to train with purpose',
    features: [
      { title: 'Plan with progression', text: 'Phases, deload weeks and target loads designed so you keep progressing.' },
      { title: 'Every session, clear', text: 'Sets, reps, RIR and rest for each exercise, with its image and technique at hand.' },
      { title: 'Adapts to you', text: 'Your level, your days and your limitations. The plan is generated and adjusted as you log sessions.' },
      { title: 'Frictionless logging', text: 'Log each set and use the built-in rest timer. It works even when you lose connection.' },
      { title: 'Exercise catalogue', text: 'Over 140 exercises with search, filters and illustrated detail pages.' },
      { title: 'Your account, protected', text: 'Sign in with Google or email, two-step verification and data encrypted on your device.' }
    ],
    shotsTitle: 'See it in action',
    shots: shots('en', ['Your week', 'Session in detail', 'Set logging', 'Catalogue']),
    shotAlt: 'TRAZO app screenshot',
    howTitle: 'How it works',
    steps: [
      { title: 'Tell us how you train', text: 'Goal, level, available days and equipment.' },
      { title: 'Get your plan', text: 'A macrocycle with phases and the current week in detail.' },
      { title: 'Train and log', text: 'The plan learns from your sessions and adjusts.' }
    ],
    waitTitle: "We're getting ready to launch",
    waitText: "Leave your email and we'll let you know when TRAZO is available on Google Play. We'll only write to you about that.",
    emailLabel: 'Email address',
    consent: 'I agree that TRAZO may use my email to notify me about the launch. I can withdraw this at any time. More in the',
    privacyLabel: 'privacy policy (Spanish)',
    send: 'Join waitlist',
    note: 'TRAZO provides general training plans and does not replace the advice of a healthcare professional.',
    ok: "Done! We'll let you know when TRAZO is available.",
    invalid: 'Check your email and accept the consent.',
    later: 'Too many attempts. Please try again later.',
    fail: "We couldn't save your email. Please try again in a few minutes.",
    terms: 'Terms of use', contact: 'Contact', admin: 'Admin sign-in', skip: 'Skip to content'
  }
};
