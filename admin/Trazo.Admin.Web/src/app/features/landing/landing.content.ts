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
    description: 'Planifica tu entrenamiento manualmente gratis o deja que TRAZO genere tu programación automática con Premium.',
    otherLang: 'en', otherPath: '/en', otherLabel: 'English', otherAria: 'Open the English version',
    eyebrow: 'ADVANCED STRENGTH TRAINING',
    headA: 'Tu entrenamiento,', headB: 'construido con criterio',
    lead: 'Diseña tus propios macrociclos, mesociclos y rutinas gratis. O deja que TRAZO estructure tu entrenamiento y su progresión automáticamente con Premium.',
    cta: 'Quiero entrar en la lista de espera',
    featuresTitle: 'Todo lo que necesitas para entrenar con criterio',
    features: [
      { title: 'Planificación a tu manera', text: 'Crea manualmente macrociclos, mesociclos y rutinas, o utiliza la generación automática de TRAZO.' },
      { title: 'Cada sesión, clara', text: 'Series, repeticiones, RIR y descanso de cada ejercicio, con su imagen y la técnica a mano.' },
      { title: 'Progresión estructurada', text: 'Organiza fases, semanas de descarga y cargas objetivo para avanzar con una planificación coherente.' },
      { title: 'Registro sin fricción', text: 'Anota cada serie y usa el temporizador de descanso integrado. Funciona aunque pierdas la conexión.' },
      { title: 'Catálogo de ejercicios', text: 'Más de 140 ejercicios con búsqueda, filtros y fichas con imágenes.' },
      { title: 'Tu cuenta, protegida', text: 'Acceso con Google o correo, verificación en dos pasos y datos cifrados en el dispositivo.' }
    ],
    pricingTitle: 'Empieza gratis. Automatiza cuando quieras.',
    pricingLead: 'La planificación manual no tiene coste ni límites artificiales. Premium está pensado para quien quiere que TRAZO haga el trabajo de programación.',
    freeName: 'Gratis',
    freePrice: '0 €',
    freePeriod: 'para siempre',
    freeTagline: 'Tú diseñas y controlas tu entrenamiento.',
    freeFeatures: [
      'Crear macrociclos manualmente',
      'Crear mesociclos y rutinas manualmente',
      'Registrar entrenamientos y consultar el historial',
      'Seguimiento y gráficas básicas de progreso'
    ],
    premiumName: 'Premium',
    premiumPrice: '6,99 €',
    premiumPeriod: '/ mes',
    premiumTagline: 'TRAZO diseña la planificación contigo.',
    premiumFeatures: [
      'Generación automática de rutinas y mesociclos',
      'Planificación automática de macrociclos',
      'Selección y distribución de ejercicios según tu perfil',
      'Progresión planificada de volumen e intensidad',
      'Análisis avanzado del volumen y del progreso'
    ],
    premiumAnnual: 'O 49,99 € al año · equivale a 4,17 €/mes',
    pricingNote: 'Precios previstos para el lanzamiento. La planificación manual seguirá siendo gratuita; las funciones automáticas y avanzadas serán Premium.',
    shotsTitle: 'Así se ve',
    shots: shots('es', ['Tu semana', 'La sesión, al detalle', 'Registro de series', 'Catálogo']),
    shotAlt: 'Captura de la app TRAZO',
    howTitle: 'Cómo funciona',
    steps: [
      { title: 'Cuéntanos cómo entrenas', text: 'Objetivo, nivel, días disponibles y material.' },
      { title: 'Diseña o genera tu plan', text: 'Construye tu planificación manualmente o deja que TRAZO te proponga una estructura automática.' },
      { title: 'Entrena y registra', text: 'Registra tus sesiones y consulta cómo evoluciona tu rendimiento.' }
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
    description: 'Plan your training manually for free or let TRAZO build your programme automatically with Premium.',
    otherLang: 'es', otherPath: '/', otherLabel: 'Español', otherAria: 'Abrir la versión en español',
    eyebrow: 'ADVANCED STRENGTH TRAINING',
    headA: 'Your training,', headB: 'engineered with purpose',
    lead: 'Build your own macrocycles, mesocycles and routines for free. Or let TRAZO structure your training and progression automatically with Premium.',
    cta: 'Join the waitlist',
    featuresTitle: 'Everything you need to train with purpose',
    features: [
      { title: 'Plan your way', text: 'Build macrocycles, mesocycles and routines manually, or use TRAZO automatic generation.' },
      { title: 'Every session, clear', text: 'Sets, reps, RIR and rest for each exercise, with its image and technique at hand.' },
      { title: 'Structured progression', text: 'Organise phases, deload weeks and target loads into a coherent plan.' },
      { title: 'Frictionless logging', text: 'Log each set and use the built-in rest timer. It works even when you lose connection.' },
      { title: 'Exercise catalogue', text: 'Over 140 exercises with search, filters and illustrated detail pages.' },
      { title: 'Your account, protected', text: 'Sign in with Google or email, two-step verification and data encrypted on your device.' }
    ],
    pricingTitle: 'Start free. Automate when you are ready.',
    pricingLead: 'Manual planning is free, with no artificial limits. Premium is for people who want TRAZO to do the programming work.',
    freeName: 'Free',
    freePrice: '€0',
    freePeriod: 'forever',
    freeTagline: 'You design and control your training.',
    freeFeatures: [
      'Build macrocycles manually',
      'Build mesocycles and routines manually',
      'Log workouts and view your history',
      'Basic progress tracking and charts'
    ],
    premiumName: 'Premium',
    premiumPrice: '€6.99',
    premiumPeriod: '/ month',
    premiumTagline: 'TRAZO helps design your programme.',
    premiumFeatures: [
      'Automatic routine and mesocycle generation',
      'Automatic macrocycle planning',
      'Exercise selection and distribution for your profile',
      'Planned progression of volume and intensity',
      'Advanced volume and progress analysis'
    ],
    premiumAnnual: 'Or €49.99 per year · equivalent to €4.17/month',
    pricingNote: 'Planned launch pricing. Manual planning will remain free; automatic and advanced features will be part of Premium.',
    shotsTitle: 'See it in action',
    shots: shots('en', ['Your week', 'Session in detail', 'Set logging', 'Catalogue']),
    shotAlt: 'TRAZO app screenshot',
    howTitle: 'How it works',
    steps: [
      { title: 'Tell us how you train', text: 'Goal, level, available days and equipment.' },
      { title: 'Build or generate your plan', text: 'Create your plan manually or let TRAZO suggest an automatic structure.' },
      { title: 'Train and log', text: 'Log your sessions and track how your performance evolves.' }
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
