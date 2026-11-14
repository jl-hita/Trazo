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
  autoTitle: string;
  autoLead: string;
  autoBlocks: { badge: string; title: string; text: string; points: string[] }[];
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
export const PRIVACY_URL = '/privacidad';
export const TERMS_URL = 'https://jl-hita.github.io/trazo-legal/condiciones.html';

const shots = (lang: LandingLang, captions: string[]) =>
  ['02-plan', '03-session', '04-workout', '06-catalog'].map((f, i) => ({ file: `/landing/screenshots/${lang}/${f}.png`, caption: captions[i] }));

export const LANDING: Record<LandingLang, LandingContent> = {
  es: {
    title: 'TRAZO · Entrenamiento de fuerza con plan y progresión',
    description: 'Planifica gratis a tu manera, o deja que TRAZO genere tu plan completo y lo adapte automáticamente a tu progreso con Premium.',
    otherLang: 'en', otherPath: '/en', otherLabel: 'English', otherAria: 'Open the English version',
    eyebrow: 'ADVANCED STRENGTH TRAINING',
    headA: 'Tu entrenamiento,', headB: 'construido con criterio',
    lead: 'Planifica gratis a tu manera. Con Premium, TRAZO genera tu plan completo y lo adapta sesión a sesión según cómo respondes al entrenamiento.',
    cta: 'Quiero entrar en la lista de espera',
    autoTitle: 'Tu plan, generado y adaptado automáticamente',
    autoLead: 'Dos funciones Premium que se encargan del trabajo duro de la programación.',
    autoBlocks: [
      {
        badge: 'Premium', title: 'Generación automática',
        text: 'Responde a unas preguntas y recibe un macrociclo completo, no solo una rutina suelta.',
        points: [
          'Fases, semanas de descarga y progresión de cargas ya planificadas',
          'Ejercicios elegidos según tu objetivo, nivel, material y limitaciones',
          'Sesiones listas, con series, repeticiones, RIR y descanso',
          'Cada bloque nuevo se genera a partir de lo que ya has hecho'
        ]
      },
      {
        badge: 'Premium', title: 'Adaptación automática',
        text: 'Registra la sesión y TRAZO ajusta las siguientes según cómo te ha ido, sin que tengas que rehacer el plan.',
        points: [
          'Sube la carga cuando progresas y la mantiene cuando todavía no toca',
          'Reduce series o añade margen si la recuperación o el rendimiento bajan',
          'Frena la progresión y puede programar una descarga si aparecen fatiga o dolor',
          'Te explica cada cambio: sabes qué se ha ajustado y por qué'
        ]
      }
    ],
    featuresTitle: 'Todo lo que necesitas para entrenar con criterio',
    features: [
      { title: 'Planificación a tu manera', text: 'Crea tus macrociclos, mesociclos y rutinas, gratis y sin límites artificiales.' },
      { title: 'Cada sesión, clara', text: 'Series, repeticiones, RIR y descanso de cada ejercicio, con su imagen y la técnica a mano.' },
      { title: 'Cambia ejercicios con criterio', text: 'Alternativas válidas para tu nivel, tu material y tus limitaciones, sin romper el plan.' },
      { title: 'Registro sin fricción', text: 'Anota cada serie y usa el temporizador de descanso integrado. Funciona aunque pierdas la conexión.' },
      { title: 'Catálogo de ejercicios', text: 'Más de 140 ejercicios con búsqueda, filtros y fichas con imágenes.' },
      { title: 'Tu cuenta, protegida', text: 'Acceso con Google o correo, verificación en dos pasos y datos cifrados en el dispositivo.' }
    ],
    pricingTitle: 'Empieza gratis. Automatiza cuando quieras.',
    pricingLead: 'La planificación manual es gratis y sin límites artificiales. Premium es para quien quiere que TRAZO programe y adapte el entrenamiento por él.',
    freeName: 'Gratis',
    freePrice: '0 €',
    freePeriod: 'para siempre',
    freeTagline: 'Tú diseñas y controlas tu entrenamiento.',
    freeFeatures: [
      'Crear macrociclos manualmente',
      'Crear mesociclos y rutinas manualmente',
      'Registrar entrenamientos y consultar el historial',
      'Catálogo completo de ejercicios con imágenes'
    ],
    premiumName: 'Premium',
    premiumPrice: '6,99 €',
    premiumPeriod: '/ mes',
    premiumTagline: 'TRAZO programa por ti y se adapta a ti.',
    premiumFeatures: [
      'Todo lo del plan gratuito',
      'Generación automática de tu plan completo: macrociclo, fases y descargas',
      'Ejercicios elegidos según tu perfil, material y limitaciones',
      'Adaptación automática de cargas, series y descargas según tu feedback',
      'Cada bloque nuevo, generado a partir de tu progreso real'
    ],
    premiumAnnual: 'O 49,99 € al año · equivale a 4,17 €/mes',
    pricingNote: 'Precios previstos para el lanzamiento. La planificación manual seguirá siendo gratuita; la generación y la adaptación automáticas serán Premium. Premium incluye 14 días de prueba gratuita.',
    faqTitle: 'Preguntas frecuentes',
    faq: [
      { q: '¿Qué es gratis y qué es Premium?', a: 'Gratis: crear tus propios macrociclos, mesociclos y rutinas, registrar entrenamientos, consultar el historial y usar el catálogo. Premium: la generación automática de tu plan y la adaptación automática de tus entrenamientos según tu feedback.' },
      { q: '¿Qué hace exactamente la generación automática?', a: 'A partir de tu objetivo, nivel, días, tiempo, material y limitaciones, TRAZO construye un macrociclo con sus fases y descargas y genera las sesiones de cada bloque a partir de lo que ya has entrenado.' },
      { q: '¿Cómo se adapta mi plan?', a: 'Cada vez que registras una sesión (series, cargas, RIR, recuperación y dolor), TRAZO ajusta las siguientes: modifica la carga, reduce series, añade margen o programa una descarga si hace falta. Siempre te explica qué ha cambiado y por qué.' },
      { q: '¿Se adaptan también los planes que creo yo?', a: 'Sí. Con Premium, la adaptación automática también funciona en tus planes manuales. Sin Premium, tus entrenamientos se guardan igual, pero el plan no se ajusta solo.' },
      { q: '¿Qué pasa si cancelo Premium?', a: 'Conservas tu historial y tus planes manuales. Un plan generado automáticamente sigue siendo consultable, pero necesitas Premium para seguir usándolo y para generar el siguiente bloque. Puedes archivarlo y crear uno manual.' },
      { q: '¿Puedo cancelar cuando quiera?', a: 'Sí, la suscripción se gestiona desde Google Play y puedes cancelarla en cualquier momento.' },
      { q: '¿TRAZO sustituye a un profesional?', a: 'No. TRAZO ofrece planes de entrenamiento orientativos y no sustituye el consejo de un profesional sanitario ni de un entrenador.' }
    ],
    shotsTitle: 'Así se ve',
    shots: shots('es', ['Tu semana', 'La sesión, al detalle', 'Registro de series', 'Catálogo']),
    shotAlt: 'Captura de la app TRAZO',
    howTitle: 'Cómo funciona',
    steps: [
      { title: 'Cuéntanos cómo entrenas', text: 'Objetivo, nivel, días disponibles y material.' },
      { title: 'Diseña o genera tu plan', text: 'Construye tu planificación manualmente, o deja que TRAZO genere tu plan completo con Premium.' },
      { title: 'Entrena y registra', text: 'Registra tus sesiones. Con Premium, TRAZO adapta las siguientes según cómo te ha ido.' }
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
    description: 'Plan your training for free, or let TRAZO generate your full plan and adapt it automatically to your progress with Premium.',
    otherLang: 'es', otherPath: '/', otherLabel: 'Español', otherAria: 'Abrir la versión en español',
    eyebrow: 'ADVANCED STRENGTH TRAINING',
    headA: 'Your training,', headB: 'engineered with purpose',
    lead: 'Plan your way for free. With Premium, TRAZO generates your complete plan and adapts it session by session to how you respond to training.',
    cta: 'Join the waitlist',
    autoTitle: 'Your plan, generated and adapted automatically',
    autoLead: 'Two Premium features that take care of the hard work of programming.',
    autoBlocks: [
      {
        badge: 'Premium', title: 'Automatic generation',
        text: 'Answer a few questions and get a complete macrocycle, not just a loose routine.',
        points: [
          'Phases, deload weeks and load progression already planned',
          'Exercises chosen for your goal, level, equipment and limitations',
          'Ready-made sessions with sets, reps, RIR and rest',
          'Every new block is generated from what you have already done'
        ]
      },
      {
        badge: 'Premium', title: 'Automatic adaptation',
        text: 'Log your session and TRAZO adjusts the next ones to how it went, without you having to rebuild the plan.',
        points: [
          'Raises the load when you progress and holds it when it is not time yet',
          'Cuts sets or adds margin when recovery or performance drops',
          'Slows progression and can schedule a deload when fatigue or pain show up',
          'Explains every change: you know what was adjusted and why'
        ]
      }
    ],
    featuresTitle: 'Everything you need to train with purpose',
    features: [
      { title: 'Plan your way', text: 'Build your macrocycles, mesocycles and routines, free and with no artificial limits.' },
      { title: 'Every session, clear', text: 'Sets, reps, RIR and rest for each exercise, with its image and technique at hand.' },
      { title: 'Swap exercises wisely', text: 'Valid alternatives for your level, equipment and limitations, without breaking the plan.' },
      { title: 'Frictionless logging', text: 'Log each set and use the built-in rest timer. It works even when you lose connection.' },
      { title: 'Exercise catalogue', text: 'Over 140 exercises with search, filters and illustrated detail pages.' },
      { title: 'Your account, protected', text: 'Sign in with Google or email, two-step verification and data encrypted on your device.' }
    ],
    pricingTitle: 'Start free. Automate when you are ready.',
    pricingLead: 'Manual planning is free, with no artificial limits. Premium is for people who want TRAZO to programme and adapt their training for them.',
    freeName: 'Free',
    freePrice: '€0',
    freePeriod: 'forever',
    freeTagline: 'You design and control your training.',
    freeFeatures: [
      'Build macrocycles manually',
      'Build mesocycles and routines manually',
      'Log workouts and view your history',
      'Full exercise catalogue with images'
    ],
    premiumName: 'Premium',
    premiumPrice: '€6.99',
    premiumPeriod: '/ month',
    premiumTagline: 'TRAZO programmes for you and adapts to you.',
    premiumFeatures: [
      'Everything in the free plan',
      'Automatic generation of your full plan: macrocycle, phases and deloads',
      'Exercises chosen for your profile, equipment and limitations',
      'Automatic adaptation of loads, sets and deloads from your feedback',
      'Every new block generated from your real progress'
    ],
    premiumAnnual: 'Or €49.99 per year · equivalent to €4.17/month',
    pricingNote: 'Planned launch pricing. Manual planning will remain free; automatic generation and adaptation will be Premium. Premium includes a 14-day free trial.',
    faqTitle: 'Frequently asked questions',
    faq: [
      { q: 'What is free and what is Premium?', a: 'Free: build your own macrocycles, mesocycles and routines, log workouts, view your history and use the catalogue. Premium: automatic generation of your plan and automatic adaptation of your workouts from your feedback.' },
      { q: 'What exactly does automatic generation do?', a: 'From your goal, level, days, time, equipment and limitations, TRAZO builds a macrocycle with its phases and deloads and generates the sessions of each block from what you have already trained.' },
      { q: 'How does my plan adapt?', a: 'Each time you log a session (sets, loads, RIR, recovery and pain), TRAZO adjusts the next ones: it changes the load, cuts sets, adds margin or schedules a deload when needed. It always explains what changed and why.' },
      { q: 'Do the plans I build myself adapt too?', a: 'Yes. With Premium, automatic adaptation also works on your manual plans. Without Premium your workouts are still saved, but the plan does not adjust by itself.' },
      { q: 'What happens if I cancel Premium?', a: 'You keep your history and your manual plans. A plan generated automatically stays readable, but you need Premium to keep using it and to generate the next block. You can archive it and build a manual one.' },
      { q: 'Can I cancel any time?', a: 'Yes, the subscription is managed through Google Play and you can cancel it at any time.' },
      { q: 'Does TRAZO replace a professional?', a: 'No. TRAZO provides general training plans and does not replace the advice of a healthcare professional or a coach.' }
    ],
    shotsTitle: 'See it in action',
    shots: shots('en', ['Your week', 'Session in detail', 'Set logging', 'Catalogue']),
    shotAlt: 'TRAZO app screenshot',
    howTitle: 'How it works',
    steps: [
      { title: 'Tell us how you train', text: 'Goal, level, available days and equipment.' },
      { title: 'Build or generate your plan', text: 'Create your plan manually, or let TRAZO generate your complete plan with Premium.' },
      { title: 'Train and log', text: 'Log your sessions. With Premium, TRAZO adapts the next ones to how it went.' }
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
