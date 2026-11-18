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
}

// Páginas legales públicas del propio frontend.
export const PRIVACY_URL = '/privacidad';
export const TERMS_URL = '/condiciones';

const shot = (lang: LandingLang, file: string, caption: string) => ({ file: `/landing/screenshots/${lang}/${file}.png`, caption });

export const LANDING: Record<LandingLang, LandingContent> = {
  es: {
    title: 'TRAZO · El motor de entrenamiento de fuerza que se adapta a ti',
    description: 'TRAZO no es una plantilla: es un motor que genera tu macrociclo, ajusta cargas y series sesión a sesión según cómo respondes y te explica cada cambio. Planificar a mano es gratis.',
    otherLang: 'en', otherPath: '/en', otherLabel: 'English', otherAria: 'Open the English version',
    eyebrow: 'MOTOR DE ENTRENAMIENTO ADAPTATIVO',
    headA: 'Tu plan no es una plantilla.', headB: 'Es un motor que se adapta a ti',
    lead: 'TRAZO construye tu macrociclo completo, ajusta cargas y series sesión a sesión según cómo respondes, y te explica cada cambio. Con días fijos o sin ellos.',
    cta: 'Quiero entrar en la lista de espera',
    loopTitle: 'Un ciclo que se cierra en cada sesión',
    loopLead: 'Planificar a mano es gratis. La generación y la adaptación automáticas son Premium.',
    loop: [
      { title: 'Genera', text: 'Objetivo, nivel, material y molestias. El motor diseña el macrociclo: fases, descargas, ejercicios y volumen por músculo.' },
      { title: 'Entrena', text: 'Registras series, cargas y RIR. Ves lo que hiciste la última vez y la carga que toca ahora.' },
      { title: 'Escucha', text: 'Al terminar cuentas cómo vas: recuperación, esfuerzo, dolor y cómo ha ido cada músculo.' },
      { title: 'Adapta', text: 'Sube la carga, quita o añade series, programa una descarga. Cada bloque nuevo parte de lo que de verdad levantaste.' }
    ],
    pillarsTitle: 'Lo que hace distinto a este motor',
    pillars: [
      { title: 'Progresión doble por ejercicio', text: 'Primero repeticiones, luego carga: cuando llegas al tope del rango con margen, sube un escalón. Si todavía no toca, mantiene.' },
      { title: 'Volumen por músculo', text: 'Las series se reparten hacia el volumen semanal que necesita cada músculo, según tu nivel y el tiempo que tienes.' },
      { title: 'Recuperación por músculo', text: 'Antes de proponerte la siguiente sesión, comprueba si los músculos que va a trabajar ya están recuperados.', premium: true },
      { title: 'Descargas cuando toca', text: 'Semanas ligeras programadas y una descarga extra si aparecen fatiga acumulada o un rendimiento a la baja.' },
      { title: 'El dolor manda', text: 'Si indicas dolor, nunca sube la carga y no automatiza nada: te avisa para que lo revises. Y tus molestias articulares dejan fuera los ejercicios que las agravan.' },
      { title: 'Cada cambio, explicado', text: 'No es una caja negra: cada ajuste llega con su motivo y queda en el historial de tu plan.' }
    ],
    showcase: [
      {
        kicker: 'Empieza en un minuto', title: 'Cuéntale cómo entrenas',
        text: 'Objetivo, nivel, material y molestias. Sin cuestionarios eternos: el motor hace el resto.',
        points: ['Hipertrofia, fuerza o forma general', 'El material que tienes y las zonas a proteger', 'Duración del plan y semanas de descarga a tu gusto'],
        premium: true,
        shots: [shot('es', '10-wizard-goal', 'Objetivo y nivel'), shot('es', '12-wizard-details', 'Material y molestias')]
      },
      {
        kicker: 'Tu semana, tus reglas', title: 'Con días fijos o sin ellos',
        text: 'Elige un calendario o un ciclo continuo. TRAZO te dice cuál es la siguiente sesión y tú entrenas cuando puedas.',
        points: ['Las sesiones atrasadas no se pierden ni se omiten solas', 'Aviso de recuperación antes de empezar', 'Sesión de reentrada más ligera si vuelves tras semanas fuera'],
        shots: [shot('es', '11-wizard-flexible', 'Sin días fijos'), shot('es', '14-plan-sequence', 'Siguiente sesión')]
      },
      {
        kicker: 'Entrena', title: 'Sabe qué hiciste la última vez',
        text: 'Cada ejercicio muestra tu rendimiento anterior y la carga propuesta. Registrar una serie son dos toques.',
        points: ['Última vez y carga objetivo en cada ejercicio', 'Temporizador de descanso integrado', 'Funciona aunque pierdas la conexión'],
        shots: [shot('es', '16-session', 'La sesión, al detalle'), shot('es', '17-workout', 'Registro de series')]
      },
      {
        kicker: 'Adapta', title: 'Después de cada sesión, un plan mejor',
        text: 'Termina la sesión y el motor te dice qué ha ajustado y por qué. Tu progreso, siempre a la vista.',
        points: ['Qué se ha cambiado y la razón de cada cambio', 'Récords, racha y series por músculo', 'El bloque completo, con las cargas afinadas semana a semana'],
        premium: true,
        shots: [shot('es', '18-adaptation', 'Así se adapta tu plan'), shot('es', '19-progress', 'Tu progreso'), shot('es', '15-block', 'Tu bloque')]
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
      { q: '¿Es inteligencia artificial?', a: 'No. El motor es determinista y se basa en reglas de entrenamiento: progresión doble, volumen semanal por músculo, recuperación y descargas. No hay texto inventado ni azar oculto: aplica reglas conocidas a tus datos y cada decisión llega con su explicación.' },
      { q: '¿Puedo entrenar sin días fijos?', a: 'Sí. Puedes elegir un ciclo continuo en lugar de un calendario: TRAZO te dice cuál es la siguiente sesión y entrenas cuando puedas. Las sesiones atrasadas se registran con su fecha real y nunca se omiten solas. Esta opción es gratuita.' },
      { q: '¿Qué es gratis y qué es Premium?', a: 'Gratis: crear tus propios macrociclos, mesociclos y rutinas, registrar entrenamientos, consultar el historial y usar el catálogo. Premium: la generación automática de tu plan y la adaptación automática de tus entrenamientos según tu feedback.' },
      { q: '¿Qué hace exactamente la generación automática?', a: 'A partir de tu objetivo, nivel, días, tiempo, material y limitaciones, TRAZO construye un macrociclo con sus fases y descargas y genera las sesiones de cada bloque a partir de lo que ya has entrenado.' },
      { q: '¿Cómo se adapta mi plan?', a: 'Cada vez que registras una sesión (series, cargas, RIR, recuperación y dolor), TRAZO ajusta las siguientes: modifica la carga, reduce series, añade margen o programa una descarga si hace falta. Siempre te explica qué ha cambiado y por qué.' },
      { q: '¿Se adaptan también los planes que creo yo?', a: 'Sí. Con Premium, la adaptación automática también funciona en tus planes manuales. Sin Premium, tus entrenamientos se guardan igual, pero el plan no se ajusta solo.' },
      { q: '¿Qué pasa si cancelo Premium?', a: 'Conservas tu historial y tus planes manuales. Un plan generado automáticamente sigue siendo consultable, pero necesitas Premium para seguir usándolo y para generar el siguiente bloque. Puedes archivarlo y crear uno manual.' },
      { q: '¿Puedo cancelar cuando quiera?', a: 'Sí, la suscripción se gestiona desde Google Play y puedes cancelarla en cualquier momento.' },
      { q: '¿TRAZO sustituye a un profesional?', a: 'No. TRAZO ofrece planes de entrenamiento orientativos y no sustituye el consejo de un profesional sanitario ni de un entrenador.' }
    ],
    shotAlt: 'Captura de la app TRAZO',
    howTitle: 'Cómo funciona',
    steps: [
      { title: 'Cuéntanos cómo entrenas', text: 'Objetivo, nivel, días disponibles y material.' },
      { title: 'Diseña o genera tu plan', text: 'Construye tu planificación manualmente, o deja que TRAZO genere tu plan completo con Premium.' },
      { title: 'Entrena y registra', text: 'Registra tus sesiones. Con Premium, TRAZO adapta las siguientes según cómo te ha ido.' }
    ],
    waitTitle: 'Estamos preparando el lanzamiento',
    waitText: 'Déjanos tu correo y te avisaremos cuando TRAZO esté disponible en Google Play. También te invitaremos a probarlo antes, en la prueba cerrada. Solo te escribiremos por eso.',
    emailLabel: 'Correo de tu cuenta de Google',
    emailHint: 'Usa el correo de la cuenta de Google que tienes en el móvil: Google Play la usará para darte acceso a la prueba. Te enviaremos un mensaje para confirmarlo.',
    consent: 'Acepto que TRAZO use mi correo para avisarme del lanzamiento e invitarme a probar la app en la prueba cerrada de Google Play. Puedo retirarlo cuando quiera. Más información en la',
    privacyLabel: 'política de privacidad',
    send: 'Apuntarme',
    note: 'TRAZO ofrece planes de entrenamiento orientativos y no sustituye el consejo de un profesional sanitario.',
    ok: 'Casi listo: te hemos enviado un correo. Ábrelo y confirma tu dirección para quedar apuntado.',
    invalid: 'Revisa el correo y acepta el consentimiento.',
    later: 'Demasiados intentos. Inténtalo más tarde.',
    fail: 'No hemos podido guardar tu correo. Inténtalo de nuevo en unos minutos.',
    terms: 'Condiciones de uso', contact: 'Contacto', admin: 'Acceso administración', skip: 'Saltar al contenido'
  },
  en: {
    title: 'TRAZO · The strength training engine that adapts to you',
    description: 'TRAZO is not a template: it is an engine that builds your macrocycle, adjusts loads and sets session by session to how you respond, and explains every change. Planning by hand is free.',
    otherLang: 'es', otherPath: '/', otherLabel: 'Español', otherAria: 'Abrir la versión en español',
    eyebrow: 'ADAPTIVE TRAINING ENGINE',
    headA: 'Your plan is not a template.', headB: 'It is an engine that adapts to you',
    lead: 'TRAZO builds your full macrocycle, adjusts loads and sets session by session to how you respond, and explains every change. With fixed days or without them.',
    cta: 'Join the waitlist',
    loopTitle: 'A loop that closes every session',
    loopLead: 'Planning by hand is free. Automatic generation and adaptation are Premium.',
    loop: [
      { title: 'Generate', text: 'Goal, level, equipment and aches. The engine designs the macrocycle: phases, deloads, exercises and volume per muscle.' },
      { title: 'Train', text: 'You log sets, loads and RIR. You see what you did last time and the load that is due now.' },
      { title: 'Listen', text: 'When you finish you say how it went: recovery, effort, pain and how each muscle responded.' },
      { title: 'Adapt', text: 'It raises the load, cuts or adds sets, schedules a deload. Every new block starts from what you really lifted.' }
    ],
    pillarsTitle: 'What makes this engine different',
    pillars: [
      { title: 'Double progression per exercise', text: 'Reps first, then load: when you reach the top of the range with margin to spare, it goes up one step. If it is not time yet, it holds.' },
      { title: 'Volume per muscle', text: 'Sets are distributed towards the weekly volume each muscle needs, for your level and the time you have.' },
      { title: 'Recovery per muscle', text: 'Before suggesting your next session, it checks whether the muscles it will work have recovered.', premium: true },
      { title: 'Deloads when due', text: 'Scheduled light weeks, plus an extra deload if accumulated fatigue or falling performance shows up.' },
      { title: 'Pain comes first', text: 'If you report pain, it never raises the load and automates nothing: it flags it for you to review. And your joint aches rule out the exercises that aggravate them.' },
      { title: 'Every change, explained', text: 'It is not a black box: each adjustment comes with its reason and stays in your plan history.' }
    ],
    showcase: [
      {
        kicker: 'Start in a minute', title: 'Tell it how you train',
        text: 'Goal, level, equipment and aches. No endless questionnaires: the engine does the rest.',
        points: ['Muscle growth, strength or general fitness', 'The equipment you have and the areas to protect', 'Plan length and deload weeks the way you like'],
        premium: true,
        shots: [shot('en', '10-wizard-goal', 'Goal and level'), shot('en', '12-wizard-details', 'Equipment and aches')]
      },
      {
        kicker: 'Your week, your rules', title: 'With fixed days or without them',
        text: 'Pick a calendar or a continuous cycle. TRAZO tells you which session comes next and you train when you can.',
        points: ['Late sessions are never lost or skipped on their own', 'Recovery advice before you start', 'A lighter re-entry session if you come back after weeks away'],
        shots: [shot('en', '11-wizard-flexible', 'No fixed days'), shot('en', '14-plan-sequence', 'Next session')]
      },
      {
        kicker: 'Train', title: 'It knows what you did last time',
        text: 'Every exercise shows your previous performance and the suggested load. Logging a set takes two taps.',
        points: ['Last time and target load on every exercise', 'Built-in rest timer', 'Works even when you lose connection'],
        shots: [shot('en', '16-session', 'Session in detail'), shot('en', '17-workout', 'Set logging')]
      },
      {
        kicker: 'Adapt', title: 'After every session, a better plan',
        text: 'Finish the session and the engine tells you what it adjusted and why. Your progress, always in view.',
        points: ['What changed and the reason for each change', 'Records, streak and sets per muscle', 'The whole block, with loads fine-tuned week by week'],
        premium: true,
        shots: [shot('en', '18-adaptation', 'How your plan adapts'), shot('en', '19-progress', 'Your progress'), shot('en', '15-block', 'Your block')]
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
      { q: 'Is it artificial intelligence?', a: 'No. The engine is deterministic and built on training rules: double progression, weekly volume per muscle, recovery and deloads. There is no made-up text or hidden randomness: it applies known rules to your data and every decision comes with its explanation.' },
      { q: 'Can I train without fixed days?', a: 'Yes. You can choose a continuous cycle instead of a calendar: TRAZO tells you which session comes next and you train when you can. Late sessions are logged with their real date and are never skipped on their own. This option is free.' },
      { q: 'What is free and what is Premium?', a: 'Free: build your own macrocycles, mesocycles and routines, log workouts, view your history and use the catalogue. Premium: automatic generation of your plan and automatic adaptation of your workouts from your feedback.' },
      { q: 'What exactly does automatic generation do?', a: 'From your goal, level, days, time, equipment and limitations, TRAZO builds a macrocycle with its phases and deloads and generates the sessions of each block from what you have already trained.' },
      { q: 'How does my plan adapt?', a: 'Each time you log a session (sets, loads, RIR, recovery and pain), TRAZO adjusts the next ones: it changes the load, cuts sets, adds margin or schedules a deload when needed. It always explains what changed and why.' },
      { q: 'Do the plans I build myself adapt too?', a: 'Yes. With Premium, automatic adaptation also works on your manual plans. Without Premium your workouts are still saved, but the plan does not adjust by itself.' },
      { q: 'What happens if I cancel Premium?', a: 'You keep your history and your manual plans. A plan generated automatically stays readable, but you need Premium to keep using it and to generate the next block. You can archive it and build a manual one.' },
      { q: 'Can I cancel any time?', a: 'Yes, the subscription is managed through Google Play and you can cancel it at any time.' },
      { q: 'Does TRAZO replace a professional?', a: 'No. TRAZO provides general training plans and does not replace the advice of a healthcare professional or a coach.' }
    ],
    shotAlt: 'TRAZO app screenshot',
    howTitle: 'How it works',
    steps: [
      { title: 'Tell us how you train', text: 'Goal, level, available days and equipment.' },
      { title: 'Build or generate your plan', text: 'Create your plan manually, or let TRAZO generate your complete plan with Premium.' },
      { title: 'Train and log', text: 'Log your sessions. With Premium, TRAZO adapts the next ones to how it went.' }
    ],
    waitTitle: "We're getting ready to launch",
    waitText: "Leave your email and we'll let you know when TRAZO is available on Google Play. We'll also invite you to try it early, in the closed test. We'll only write to you about that.",
    emailLabel: 'Email of your Google account',
    emailHint: 'Use the email of the Google account on your phone: Google Play will use it to give you access to the test. We will send you a message to confirm it.',
    consent: 'I agree that TRAZO may use my email to notify me about the launch and invite me to try the app in the closed test on Google Play. I can withdraw this at any time. More in the',
    privacyLabel: 'privacy policy (Spanish)',
    send: 'Join waitlist',
    note: 'TRAZO provides general training plans and does not replace the advice of a healthcare professional.',
    ok: "Almost done: we've sent you an email. Open it and confirm your address to complete the sign-up.",
    invalid: 'Check your email and accept the consent.',
    later: 'Too many attempts. Please try again later.',
    fail: "We couldn't save your email. Please try again in a few minutes.",
    terms: 'Terms of use', contact: 'Contact', admin: 'Admin sign-in', skip: 'Skip to content'
  }
};
