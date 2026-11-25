import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('ca');

const content: LandingContent = {
  title: 'TRAZO · El motor d’entrenament de força que s’adapta a tu',
  description: 'TRAZO no és una plantilla: és un motor que genera el teu macrocicle, ajusta càrregues i sèries sessió a sessió segons com respons i t’explica cada canvi. Planificar a mà és gratis.',
  languageLabel: 'Idioma',
  eyebrow: 'MOTOR D’ENTRENAMENT ADAPTATIU',
  headA: 'El teu pla no és una plantilla.', headB: 'És un motor que s’adapta a tu',
  lead: 'TRAZO construeix el teu macrocicle complet, ajusta càrregues i sèries sessió a sessió segons com respons i t’explica cada canvi. Amb dies fixos o sense.',
  cta: 'Vull entrar a la llista d’espera',
  loopTitle: 'Un cicle que es tanca a cada sessió',
  loopLead: 'Planificar a mà és gratis. La generació i l’adaptació automàtiques són Premium.',
  loop: [
    { title: 'Genera', text: 'Objectiu, nivell, material i molèsties. El motor dissenya el macrocicle: fases, descàrregues, exercicis i volum per múscul.' },
    { title: 'Entrena', text: 'Registres sèries, càrregues i RIR. Veus què vas fer l’última vegada i la càrrega que toca ara.' },
    { title: 'Escolta', text: 'En acabar expliques com vas: recuperació, esforç, dolor i com ha anat cada múscul.' },
    { title: 'Adapta', text: 'Puja la càrrega, treu o afegeix sèries, programa una descàrrega. Cada bloc nou parteix del que de veritat vas aixecar.' }
  ],
  pillarsTitle: 'El que fa diferent aquest motor',
  pillars: [
    { title: 'Progressió doble per exercici', text: 'Primer repeticions, després càrrega: quan arribes al límit del rang amb marge, puja un esglaó. Si encara no toca, es manté.' },
    { title: 'Volum per múscul', text: 'Les sèries es reparteixen cap al volum setmanal que necessita cada múscul, segons el teu nivell i el temps que tens.' },
    { title: 'Recuperació per múscul', text: 'Abans de proposar-te la sessió següent, comprova si els músculs que treballarà ja s’han recuperat.', premium: true },
    { title: 'Descàrregues quan toca', text: 'Setmanes lleugeres programades i una descàrrega extra si apareix fatiga acumulada o un rendiment a la baixa.' },
    { title: 'El dolor mana', text: 'Si indiques dolor, no puja mai la càrrega ni automatitza res: t’avisa perquè ho revisis. I les teves molèsties articulars deixen fora els exercicis que les agreugen.' },
    { title: 'Cada canvi, explicat', text: 'No és una caixa negra: cada ajust arriba amb el motiu i queda a l’historial del pla.' }
  ],
  showcase: [
    {
      kicker: 'Comença en un minut', title: 'Explica-li com entrenes',
      text: 'Objectiu, nivell, material i molèsties. Sense qüestionaris eterns: el motor fa la resta.',
      points: ['Hipertròfia, força o forma general', 'El material que tens i les zones a protegir', 'Durada del pla i setmanes de descàrrega al teu gust'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Objectiu i nivell'), shot('12-wizard-details', 'Material i molèsties')]
    },
    {
      kicker: 'La teva setmana, les teves regles', title: 'Amb dies fixos o sense',
      text: 'Tria un calendari o un cicle continu. TRAZO et diu quina és la sessió següent i tu entrenes quan puguis.',
      points: ['Les sessions endarrerides no es perden ni s’ometen soles', 'Avís de recuperació abans de començar', 'Sessió de tornada més lleugera si tornes després de setmanes'],
      shots: [shot('11-wizard-flexible', 'Sense dies fixos'), shot('14-plan-sequence', 'Sessió següent')]
    },
    {
      kicker: 'Entrena', title: 'Sap què vas fer l’última vegada',
      text: 'Cada exercici mostra el rendiment anterior i la càrrega proposada. Registrar una sèrie són dos tocs.',
      points: ['Última vegada i càrrega objectiu a cada exercici', 'Temporitzador de descans integrat', 'Funciona encara que perdis la connexió'],
      shots: [shot('16-session', 'La sessió al detall'), shot('17-workout', 'Registre de sèries')]
    },
    {
      kicker: 'Adapta', title: 'Després de cada sessió, un pla millor',
      text: 'Acaba la sessió i el motor et diu què ha ajustat i per què. El teu progrés, sempre a la vista.',
      points: ['Què ha canviat i la raó de cada canvi', 'Rècords, ratxa i sèries per múscul', 'El bloc complet, amb les càrregues afinades setmana a setmana'],
      premium: true,
      shots: [shot('18-adaptation', 'Així s’adapta el teu pla'), shot('19-progress', 'El teu progrés'), shot('15-block', 'El teu bloc')]
    }
  ],
  featuresTitle: 'Tot el que necessites per entrenar amb criteri',
  features: [
    { title: 'Planifica a la teva manera', text: 'Crea macrocicles, mesocicles i rutines, gratis i sense límits artificials.' },
    { title: 'Cada sessió, clara', text: 'Sèries, repeticions, RIR i descans de cada exercici, amb imatge i tècnica a mà.' },
    { title: 'Canvia exercicis amb criteri', text: 'Alternatives vàlides per al teu nivell, material i limitacions, sense trencar el pla.' },
    { title: 'Registre sense fricció', text: 'Anota cada sèrie i fes servir el temporitzador. Funciona encara que perdis la connexió.' },
    { title: 'Catàleg d’exercicis', text: 'Més de 140 exercicis amb cerca, filtres i fitxes amb imatges.' },
    { title: 'El teu compte, protegit', text: 'Accés amb Google o correu, verificació en dos passos i dades xifrades al dispositiu.' }
  ],
  pricingTitle: 'Comença gratis. Automatitza quan vulguis.',
  pricingLead: 'La planificació manual és gratis i sense límits artificials. Premium és per a qui vol que TRAZO programi i adapti l’entrenament.',
  freeName: 'Gratis',
  freePrice: '0 €',
  freePeriod: 'per sempre',
  freeTagline: 'Tu dissenyes i controles l’entrenament.',
  freeFeatures: [
    'Crear macrocicles manualment',
    'Crear mesocicles i rutines manualment',
    'Registrar entrenaments i consultar l’historial',
    'Catàleg complet d’exercicis amb imatges'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ mes',
  premiumTagline: 'TRAZO programa per tu i s’adapta a tu.',
  premiumFeatures: [
    'Tot el del pla gratuït',
    'Generació automàtica del pla complet: macrocicle, fases i descàrregues',
    'Exercicis triats segons el teu perfil, material i limitacions',
    'Adaptació automàtica de càrregues, sèries i descàrregues segons el teu feedback',
    'Cada bloc nou, generat a partir del teu progrés real'
  ],
  premiumAnnual: 'O 49,99 € a l’any · equival a 4,17 €/mes',
  pricingNote: 'Preus previstos per al llançament. La planificació manual continuarà sent gratuïta; la generació i l’adaptació automàtiques seran Premium. Premium inclou 14 dies de prova gratuïta.',
  faqTitle: 'Preguntes freqüents',
  faq: [
    { q: 'És intel·ligència artificial?', a: 'No. El motor és determinista i es basa en regles d’entrenament: progressió doble, volum setmanal per múscul, recuperació i descàrregues. No hi ha text inventat ni atzar ocult: aplica regles conegudes a les teves dades i cada decisió arriba explicada.' },
    { q: 'Puc entrenar sense dies fixos?', a: 'Sí. Pots triar un cicle continu en lloc d’un calendari: TRAZO et diu la sessió següent i entrenes quan puguis. Les sessions endarrerides es registren amb la data real i mai s’ometen soles. Aquesta opció és gratuïta.' },
    { q: 'Què és gratis i què és Premium?', a: 'Gratis: crear macrocicles, mesocicles i rutines, registrar entrenaments, consultar l’historial i fer servir el catàleg. Premium: la generació automàtica del pla i l’adaptació automàtica segons el teu feedback.' },
    { q: 'Què fa exactament la generació automàtica?', a: 'A partir de l’objectiu, nivell, dies, temps, material i limitacions, TRAZO construeix un macrocicle amb fases i descàrregues i genera les sessions de cada bloc a partir del que ja has entrenat.' },
    { q: 'Com s’adapta el meu pla?', a: 'Cada vegada que registres una sessió (sèries, càrregues, RIR, recuperació i dolor), TRAZO ajusta les següents: canvia la càrrega, redueix sèries, afegeix marge o programa una descàrrega. Sempre t’explica què i per què.' },
    { q: 'També s’adapten els plans que creo jo?', a: 'Sí. Amb Premium, l’adaptació automàtica també funciona als plans manuals. Sense Premium els entrenaments es desen igualment, però el pla no s’ajusta sol.' },
    { q: 'Què passa si cancel·lo Premium?', a: 'Conserves l’historial i els plans manuals. Un pla generat automàticament es pot consultar, però cal Premium per continuar-lo fent servir i generar el bloc següent. Pots arxivar-lo i crear-ne un de manual.' },
    { q: 'Puc cancel·lar quan vulgui?', a: 'Sí, la subscripció es gestiona des de Google Play i la pots cancel·lar en qualsevol moment.' },
    { q: 'TRAZO substitueix un professional?', a: 'No. TRAZO ofereix plans d’entrenament orientatius i no substitueix el consell d’un professional sanitari ni d’un entrenador.' }
  ],
  shotAlt: 'Captura de l’app TRAZO',
  howTitle: 'Com funciona',
  steps: [
    { title: 'Explica’ns com entrenes', text: 'Objectiu, nivell, dies disponibles i material.' },
    { title: 'Dissenya o genera el pla', text: 'Construeix-lo manualment o deixa que TRAZO el generi complet amb Premium.' },
    { title: 'Entrena i registra', text: 'Registra les sessions. Amb Premium, TRAZO adapta les següents segons com t’ha anat.' }
  ],
  waitTitle: 'Estem preparant el llançament',
  waitText: 'Deixa’ns el teu correu i t’avisarem quan TRAZO sigui a Google Play. També et convidarem a provar-lo abans, a la prova tancada. Només t’escriurem per això.',
  emailLabel: 'Correu del teu compte de Google',
  emailHint: 'Fes servir el correu del compte de Google del mòbil: Google Play l’usarà per donar-te accés a la prova. T’enviarem un missatge per confirmar-lo.',
  consent: 'Accepto que TRAZO faci servir el meu correu per avisar-me del llançament i convidar-me a provar l’app a la prova tancada de Google Play. Ho puc retirar quan vulgui. Més informació a la',
  privacyLabel: 'política de privacitat (en anglès)',
  send: 'Apuntar-me',
  note: 'TRAZO ofereix plans d’entrenament orientatius i no substitueix el consell d’un professional sanitari.',
  ok: 'Gairebé fet: t’hem enviat un correu. Obre’l i confirma l’adreça per quedar apuntat.',
  invalid: 'Revisa el correu i accepta el consentiment.',
  later: 'Massa intents. Torna-ho a provar més tard.',
  fail: 'No hem pogut desar el teu correu. Torna-ho a provar d’aquí a uns minuts.',
  terms: 'Condicions d’ús (en anglès)', contact: 'Contacte', admin: 'Accés d’administració', skip: 'Ves al contingut',
  waitlistPages: {
    confirm: {
      title: 'Confirma el teu correu',
      text: 'Prem el botó per confirmar que vols ser a la llista de TRAZO: t’avisarem del llançament i et convidarem a provar l’app.',
      button: 'Confirmar',
      done: 'Correu confirmat',
      doneText: 'Gràcies. T’escriurem quan hi hagi novetats sobre la prova i el llançament.'
    },
    unsubscribe: {
      title: 'Donar-te de baixa',
      text: 'Prem el botó per esborrar el teu correu de la llista de TRAZO. No t’escriurem més.',
      button: 'Esborrar les meves dades',
      done: 'Dades esborrades',
      doneText: 'Hem esborrat el teu correu de la llista. No rebràs més missatges.'
    },
    failed: 'Enllaç no vàlid o caducat',
    failedText: 'Aquest enllaç no és vàlid, ha caducat o ja s’ha fet servir. Si et volies apuntar, torna a omplir el formulari.',
    missing: 'Enllaç incomplet',
    missingText: 'A l’enllaç li falta el codi de seguretat. Fes servir l’enllaç del correu tal com el vas rebre.',
    wait: 'Un moment…',
    home: 'Anar a TRAZO'
  }
};

export default content;
