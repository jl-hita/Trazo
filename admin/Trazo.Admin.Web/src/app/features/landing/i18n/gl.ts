import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('gl');

const content: LandingContent = {
  title: 'TRAZO · O motor de adestramento de forza que se adapta a ti',
  description: 'TRAZO non é un modelo prefabricado: é un motor que xera o teu macrociclo, axusta cargas e series sesión a sesión segundo como respondes e explícache cada cambio. Planificar a man é gratis.',
  languageLabel: 'Idioma',
  eyebrow: 'MOTOR DE ADESTRAMENTO ADAPTATIVO',
  headA: 'O teu plan non é un modelo prefabricado.', headB: 'É un motor que se adapta a ti',
  lead: 'TRAZO constrúe o teu macrociclo completo, axusta cargas e series sesión a sesión segundo como respondes e explícache cada cambio. Con días fixos ou sen eles.',
  cta: 'Quero entrar na lista de espera',
  loopTitle: 'Un ciclo que se pecha en cada sesión',
  loopLead: 'Planificar a man é gratis. A xeración e a adaptación automáticas son Premium.',
  loop: [
    { title: 'Xera', text: 'Obxectivo, nivel, material e molestias. O motor deseña o macrociclo: fases, descargas, exercicios e volume por músculo.' },
    { title: 'Adestra', text: 'Rexistras series, cargas e RIR. Ves o que fixeches a última vez e a carga que toca agora.' },
    { title: 'Escoita', text: 'Ao rematar contas como vas: recuperación, esforzo, dor e como foi cada músculo.' },
    { title: 'Adapta', text: 'Sobe a carga, retira ou engade series, programa unha descarga. Cada bloque novo parte do que de verdade levantaches.' }
  ],
  pillarsTitle: 'O que fai distinto a este motor',
  pillars: [
    { title: 'Dobre progresión por exercicio', text: 'Primeiro repeticións, despois carga: cando chegas ao tope do rango con marxe, sobe un chanzo. Se aínda non toca, mantense.' },
    { title: 'Volume por músculo', text: 'As series repártense cara ao volume semanal que precisa cada músculo, segundo o teu nivel e o tempo que tes.' },
    { title: 'Recuperación por músculo', text: 'Antes de proporche a seguinte sesión, comproba se os músculos que vai traballar xa se recuperaron.', premium: true },
    { title: 'Descargas cando toca', text: 'Semanas lixeiras programadas e unha descarga extra se aparece fatiga acumulada ou rendemento á baixa.' },
    { title: 'A dor manda', text: 'Se indicas dor, nunca sobe a carga nin automatiza nada: avísate para que o revises. E as túas molestias articulares deixan fóra os exercicios que as agravan.' },
    { title: 'Cada cambio, explicado', text: 'Non é unha caixa negra: cada axuste chega co seu motivo e queda no historial do plan.' }
  ],
  showcase: [
    {
      kicker: 'Comeza nun minuto', title: 'Cóntalle como adestras',
      text: 'Obxectivo, nivel, material e molestias. Sen cuestionarios eternos: o motor fai o resto.',
      points: ['Hipertrofia, forza ou forma xeral', 'O material que tes e as zonas a protexer', 'Duración do plan e semanas de descarga ao teu gusto'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Obxectivo e nivel'), shot('12-wizard-details', 'Material e molestias')]
    },
    {
      kicker: 'A túa semana, as túas regras', title: 'Con días fixos ou sen eles',
      text: 'Escolle un calendario ou un ciclo continuo. TRAZO dicha cal é a seguinte sesión e ti adestras cando poidas.',
      points: ['As sesións atrasadas non se perden nin se omiten soas', 'Aviso de recuperación antes de comezar', 'Sesión de volta máis lixeira se regresas tras semanas'],
      shots: [shot('11-wizard-flexible', 'Sen días fixos'), shot('14-plan-sequence', 'Seguinte sesión')]
    },
    {
      kicker: 'Adestra', title: 'Sabe o que fixeches a última vez',
      text: 'Cada exercicio amosa o teu rendemento anterior e a carga proposta. Rexistrar unha serie son dous toques.',
      points: ['Última vez e carga obxectivo en cada exercicio', 'Temporizador de descanso integrado', 'Funciona aínda que perdas a conexión'],
      shots: [shot('16-session', 'A sesión en detalle'), shot('17-workout', 'Rexistro de series')]
    },
    {
      kicker: 'Adapta', title: 'Despois de cada sesión, un plan mellor',
      text: 'Remata a sesión e o motor dicha que axustou e por que. O teu progreso, sempre á vista.',
      points: ['Que cambiou e a razón de cada cambio', 'Marcas, refacho e series por músculo', 'O bloque completo, coas cargas afinadas semana a semana'],
      premium: true,
      shots: [shot('18-adaptation', 'Así se adapta o teu plan'), shot('19-progress', 'O teu progreso'), shot('15-block', 'O teu bloque')]
    }
  ],
  featuresTitle: 'Todo o que precisas para adestrar con criterio',
  features: [
    { title: 'Planifica ao teu xeito', text: 'Crea macrociclos, mesociclos e rutinas, gratis e sen límites artificiais.' },
    { title: 'Cada sesión, clara', text: 'Series, repeticións, RIR e descanso de cada exercicio, con imaxe e técnica á man.' },
    { title: 'Cambia exercicios con criterio', text: 'Alternativas válidas para o teu nivel, material e limitacións, sen romper o plan.' },
    { title: 'Rexistro sen fricción', text: 'Anota cada serie e usa o temporizador. Funciona aínda que perdas a conexión.' },
    { title: 'Catálogo de exercicios', text: 'Máis de 140 exercicios con busca, filtros e fichas con imaxes.' },
    { title: 'A túa conta, protexida', text: 'Acceso con Google ou correo, verificación en dous pasos e datos cifrados no dispositivo.' }
  ],
  pricingTitle: 'Comeza gratis. Automatiza cando queiras.',
  pricingLead: 'A planificación manual é gratis e sen límites artificiais. Premium é para quen quere que TRAZO programe e adapte o adestramento.',
  freeName: 'Gratis',
  freePrice: '0 €',
  freePeriod: 'para sempre',
  freeTagline: 'Ti deseñas e controlas o teu adestramento.',
  freeFeatures: [
    'Crear macrociclos manualmente',
    'Crear mesociclos e rutinas manualmente',
    'Rexistrar adestramentos e consultar o historial',
    'Catálogo completo de exercicios con imaxes'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ mes',
  premiumTagline: 'TRAZO programa por ti e adáptase a ti.',
  premiumFeatures: [
    'Todo o do plan gratuíto',
    'Xeración automática do plan completo: macrociclo, fases e descargas',
    'Exercicios escollidos segundo o teu perfil, material e limitacións',
    'Adaptación automática de cargas, series e descargas segundo o teu feedback',
    'Cada novo bloque, xerado a partir do teu progreso real'
  ],
  premiumAnnual: 'Ou 49,99 € ao ano · equivale a 4,17 €/mes',
  pricingNote: 'Prezos previstos para o lanzamento. A planificación manual seguirá sendo gratuíta; a xeración e a adaptación automáticas serán Premium. Premium inclúe 14 días de proba gratuíta.',
  faqTitle: 'Preguntas frecuentes',
  faq: [
    { q: 'É intelixencia artificial?', a: 'Non. O motor é determinista e baséase en regras de adestramento: dobre progresión, volume semanal por músculo, recuperación e descargas. Non hai texto inventado nin azar oculto: aplica regras coñecidas aos teus datos e cada decisión chega explicada.' },
    { q: 'Podo adestrar sen días fixos?', a: 'Si. Podes escoller un ciclo continuo en lugar dun calendario: TRAZO dicha a seguinte sesión e adestras cando poidas. As sesións atrasadas rexístranse coa súa data real e nunca se omiten soas. Esta opción é gratuíta.' },
    { q: 'Que é gratis e que é Premium?', a: 'Gratis: crear macrociclos, mesociclos e rutinas, rexistrar adestramentos, consultar o historial e usar o catálogo. Premium: a xeración automática do plan e a súa adaptación automática segundo o teu feedback.' },
    { q: 'Que fai exactamente a xeración automática?', a: 'A partir do teu obxectivo, nivel, días, tempo, material e limitacións, TRAZO constrúe un macrociclo con fases e descargas e xera as sesións de cada bloque a partir do que xa adestraches.' },
    { q: 'Como se adapta o meu plan?', a: 'Cada vez que rexistras unha sesión (series, cargas, RIR, recuperación e dor), TRAZO axusta as seguintes: cambia a carga, reduce series, engade marxe ou programa unha descarga. Sempre che explica que e por que.' },
    { q: 'Tamén se adaptan os plans que creo eu?', a: 'Si. Con Premium, a adaptación automática tamén funciona nos teus plans manuais. Sen Premium os teus adestramentos gárdanse igual, pero o plan non se axusta só.' },
    { q: 'Que pasa se cancelo Premium?', a: 'Conservas o teu historial e os teus plans manuais. Un plan xerado automaticamente pode consultarse, pero cómpre Premium para seguir usándoo e xerar o seguinte bloque. Podes arquivalo e crear un manual.' },
    { q: 'Podo cancelar cando queira?', a: 'Si, a subscrición xestiónase desde Google Play e podes cancelala en calquera momento.' },
    { q: 'TRAZO substitúe un profesional?', a: 'Non. TRAZO ofrece plans de adestramento orientativos e non substitúe o consello dun profesional sanitario nin dun adestrador.' }
  ],
  shotAlt: 'Captura da app TRAZO',
  howTitle: 'Como funciona',
  steps: [
    { title: 'Cóntanos como adestras', text: 'Obxectivo, nivel, días dispoñibles e material.' },
    { title: 'Deseña ou xera o teu plan', text: 'Constrúeo manualmente ou deixa que TRAZO o xere completo con Premium.' },
    { title: 'Adestra e rexistra', text: 'Rexistra as túas sesións. Con Premium, TRAZO adapta as seguintes segundo como che foi.' }
  ],
  waitTitle: 'Estamos preparando o lanzamento',
  waitText: 'Déixanos o teu correo e avisarémoste cando TRAZO estea en Google Play. Tamén te convidaremos a probalo antes, na proba pechada. Só che escribiremos para iso.',
  emailLabel: 'Correo da túa conta de Google',
  emailHint: 'Usa o correo da conta de Google do teu móbil: Google Play usarao para darche acceso á proba. Enviarémosche unha mensaxe para confirmalo.',
  consent: 'Acepto que TRAZO use o meu correo para avisarme do lanzamento e convidarme a probar a app na proba pechada de Google Play. Podo retiralo cando queira. Máis información na',
  privacyLabel: 'política de privacidade (en inglés)',
  send: 'Apuntarme',
  note: 'TRAZO ofrece plans de adestramento orientativos e non substitúe o consello dun profesional sanitario.',
  ok: 'Case listo: enviámosche un correo. Ábreo e confirma o teu enderezo para quedar apuntado.',
  invalid: 'Revisa o correo e acepta o consentimento.',
  later: 'Demasiados intentos. Téntao de novo máis tarde.',
  fail: 'Non puidemos gardar o teu correo. Téntao de novo nuns minutos.',
  terms: 'Condicións de uso (en inglés)', contact: 'Contacto', admin: 'Acceso de administración', skip: 'Saltar ao contido',
  waitlistPages: {
    confirm: {
      title: 'Confirma o teu correo',
      text: 'Preme o botón para confirmar que queres estar na lista de TRAZO: avisarémoste do lanzamento e convidarémoste a probar a app.',
      button: 'Confirmar',
      done: 'Correo confirmado',
      doneText: 'Grazas. Escribirémosche cando haxa novidades sobre a proba e o lanzamento.'
    },
    unsubscribe: {
      title: 'Darte de baixa',
      text: 'Preme o botón para borrar o teu correo da lista de TRAZO. Non che escribiremos máis.',
      button: 'Borrar os meus datos',
      done: 'Datos borrados',
      doneText: 'Borramos o teu correo da lista. Non recibirás máis mensaxes.'
    },
    failed: 'Ligazón non válida ou caducada',
    failedText: 'Esta ligazón non é válida, caducou ou xa se usou. Se querías apuntarte, volve encher o formulario.',
    missing: 'Ligazón incompleta',
    missingText: 'Á ligazón fáltalle o código de seguridade. Usa a ligazón do correo tal como a recibiches.',
    wait: 'Un momento…',
    home: 'Ir a TRAZO'
  }
};

export default content;
