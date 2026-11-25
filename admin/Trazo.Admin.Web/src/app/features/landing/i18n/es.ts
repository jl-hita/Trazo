import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('es');

const content: LandingContent = {
  title: 'TRAZO · El motor de entrenamiento de fuerza que se adapta a ti',
  description: 'TRAZO no es una plantilla: es un motor que genera tu macrociclo, ajusta cargas y series sesión a sesión según cómo respondes y te explica cada cambio. Planificar a mano es gratis.',
  languageLabel: 'Idioma',
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
      shots: [shot('10-wizard-goal', 'Objetivo y nivel'), shot('12-wizard-details', 'Material y molestias')]
    },
    {
      kicker: 'Tu semana, tus reglas', title: 'Con días fijos o sin ellos',
      text: 'Elige un calendario o un ciclo continuo. TRAZO te dice cuál es la siguiente sesión y tú entrenas cuando puedas.',
      points: ['Las sesiones atrasadas no se pierden ni se omiten solas', 'Aviso de recuperación antes de empezar', 'Sesión de reentrada más ligera si vuelves tras semanas fuera'],
      shots: [shot('11-wizard-flexible', 'Sin días fijos'), shot('14-plan-sequence', 'Siguiente sesión')]
    },
    {
      kicker: 'Entrena', title: 'Sabe qué hiciste la última vez',
      text: 'Cada ejercicio muestra tu rendimiento anterior y la carga propuesta. Registrar una serie son dos toques.',
      points: ['Última vez y carga objetivo en cada ejercicio', 'Temporizador de descanso integrado', 'Funciona aunque pierdas la conexión'],
      shots: [shot('16-session', 'La sesión, al detalle'), shot('17-workout', 'Registro de series')]
    },
    {
      kicker: 'Adapta', title: 'Después de cada sesión, un plan mejor',
      text: 'Termina la sesión y el motor te dice qué ha ajustado y por qué. Tu progreso, siempre a la vista.',
      points: ['Qué se ha cambiado y la razón de cada cambio', 'Récords, racha y series por músculo', 'El bloque completo, con las cargas afinadas semana a semana'],
      premium: true,
      shots: [shot('18-adaptation', 'Así se adapta tu plan'), shot('19-progress', 'Tu progreso'), shot('15-block', 'Tu bloque')]
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
  terms: 'Condiciones de uso', contact: 'Contacto', admin: 'Acceso administración', skip: 'Saltar al contenido',
  waitlistPages: {
    confirm: {
      title: 'Confirma tu correo',
      text: 'Pulsa el botón para confirmar que quieres estar en la lista de TRAZO: te avisaremos del lanzamiento y te invitaremos a probar la app.',
      button: 'Confirmar',
      done: 'Correo confirmado',
      doneText: 'Gracias. Te escribiremos cuando haya novedades sobre la prueba y el lanzamiento.'
    },
    unsubscribe: {
      title: 'Darte de baja',
      text: 'Pulsa el botón para borrar tu correo de la lista de TRAZO. No te escribiremos más.',
      button: 'Borrar mis datos',
      done: 'Datos borrados',
      doneText: 'Hemos borrado tu correo de la lista. No recibirás más mensajes.'
    },
    failed: 'Enlace no válido o caducado',
    failedText: 'Este enlace no es válido, ha caducado o ya se ha usado. Si querías apuntarte, vuelve a rellenar el formulario.',
    missing: 'Enlace incompleto',
    missingText: 'Al enlace le falta su código de seguridad. Usa el enlace del correo tal como lo recibiste.',
    wait: 'Un momento…',
    home: 'Ir a TRAZO'
  }
};

export default content;
