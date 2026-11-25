import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('it');

const content: LandingContent = {
  title: 'TRAZO · Il motore di allenamento della forza che si adatta a te',
  description: 'TRAZO non è un modello: è un motore che genera il tuo macrociclo, regola carichi e serie sessione per sessione in base a come rispondi e ti spiega ogni modifica. Pianificare a mano è gratis.',
  languageLabel: 'Lingua',
  eyebrow: 'MOTORE DI ALLENAMENTO ADATTIVO',
  headA: 'Il tuo piano non è un modello.', headB: 'È un motore che si adatta a te',
  lead: 'TRAZO costruisce il tuo macrociclo completo, regola carichi e serie sessione per sessione in base a come rispondi e ti spiega ogni modifica. Con giorni fissi o senza.',
  cta: 'Voglio entrare in lista d’attesa',
  loopTitle: 'Un ciclo che si chiude a ogni sessione',
  loopLead: 'Pianificare a mano è gratis. La generazione e l’adattamento automatici sono Premium.',
  loop: [
    { title: 'Genera', text: 'Obiettivo, livello, attrezzi e fastidi. Il motore disegna il macrociclo: fasi, scarichi, esercizi e volume per muscolo.' },
    { title: 'Allena', text: 'Registri serie, carichi e RIR. Vedi cosa hai fatto l’ultima volta e il carico di oggi.' },
    { title: 'Ascolta', text: 'Alla fine racconti com’è andata: recupero, sforzo, dolore e risposta di ogni muscolo.' },
    { title: 'Adatta', text: 'Aumenta il carico, toglie o aggiunge serie, programma uno scarico. Ogni nuovo blocco parte da quello che hai davvero sollevato.' }
  ],
  pillarsTitle: 'Cosa rende diverso questo motore',
  pillars: [
    { title: 'Doppia progressione per esercizio', text: 'Prima le ripetizioni, poi il carico: arrivato in cima all’intervallo con margine, sale di un gradino. Se non è ora, resta.' },
    { title: 'Volume per muscolo', text: 'Le serie vengono distribuite verso il volume settimanale che serve a ogni muscolo, secondo livello e tempo.' },
    { title: 'Recupero per muscolo', text: 'Prima di proporti la sessione successiva controlla che i muscoli coinvolti abbiano recuperato.', premium: true },
    { title: 'Scarichi quando servono', text: 'Settimane leggere programmate e uno scarico extra se si accumula fatica o cala la prestazione.' },
    { title: 'Il dolore comanda', text: 'Se segnali dolore non aumenta mai il carico né automatizza nulla: ti avvisa. E i tuoi fastidi articolari escludono gli esercizi che li peggiorano.' },
    { title: 'Ogni modifica spiegata', text: 'Non è una scatola nera: ogni regolazione ha il suo motivo e resta nella cronologia del piano.' }
  ],
  showcase: [
    {
      kicker: 'Parti in un minuto', title: 'Digli come ti alleni',
      text: 'Obiettivo, livello, attrezzi e fastidi. Niente questionari infiniti: il resto lo fa il motore.',
      points: ['Ipertrofia, forza o forma generale', 'Gli attrezzi che hai e le zone da proteggere', 'Durata del piano e scarichi come preferisci'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Obiettivo e livello'), shot('12-wizard-details', 'Attrezzi e fastidi')]
    },
    {
      kicker: 'La tua settimana, le tue regole', title: 'Con giorni fissi o senza',
      text: 'Scegli un calendario o un ciclo continuo. TRAZO ti dice qual è la prossima sessione e ti alleni quando puoi.',
      points: ['Le sessioni in ritardo non si perdono né si saltano da sole', 'Avviso sul recupero prima di iniziare', 'Sessione di rientro più leggera dopo settimane di stop'],
      shots: [shot('11-wizard-flexible', 'Senza giorni fissi'), shot('14-plan-sequence', 'Prossima sessione')]
    },
    {
      kicker: 'Allena', title: 'Sa cosa hai fatto l’ultima volta',
      text: 'Ogni esercizio mostra la prestazione precedente e il carico proposto. Registrare una serie richiede due tocchi.',
      points: ['Ultima volta e carico obiettivo per ogni esercizio', 'Timer di recupero integrato', 'Funziona anche senza connessione'],
      shots: [shot('16-session', 'La sessione nel dettaglio'), shot('17-workout', 'Registrazione delle serie')]
    },
    {
      kicker: 'Adatta', title: 'Dopo ogni sessione, un piano migliore',
      text: 'Chiudi la sessione e il motore ti dice cosa ha regolato e perché. I progressi sempre in vista.',
      points: ['Cosa è cambiato e il motivo di ogni modifica', 'Record, serie di settimane e serie per muscolo', 'Il blocco completo, con carichi affinati settimana dopo settimana'],
      premium: true,
      shots: [shot('18-adaptation', 'Come si adatta il tuo piano'), shot('19-progress', 'I tuoi progressi'), shot('15-block', 'Il tuo blocco')]
    }
  ],
  featuresTitle: 'Tutto per allenarti con criterio',
  features: [
    { title: 'Pianifica a modo tuo', text: 'Crea macrocicli, mesocicli e routine, gratis e senza limiti artificiali.' },
    { title: 'Ogni sessione, chiara', text: 'Serie, ripetizioni, RIR e recupero di ogni esercizio, con immagine e tecnica a portata di mano.' },
    { title: 'Cambia esercizi con criterio', text: 'Alternative valide per livello, attrezzi e limitazioni, senza rompere il piano.' },
    { title: 'Registrazione senza attriti', text: 'Annota ogni serie e usa il timer di recupero. Funziona anche senza connessione.' },
    { title: 'Catalogo esercizi', text: 'Oltre 140 esercizi con ricerca, filtri e schede con immagini.' },
    { title: 'Il tuo account, protetto', text: 'Accesso con Google o email, verifica in due passaggi e dati cifrati sul dispositivo.' }
  ],
  pricingTitle: 'Inizia gratis. Automatizza quando vuoi.',
  pricingLead: 'La pianificazione manuale è gratis e senza limiti artificiali. Premium è per chi vuole che TRAZO programmi e adatti l’allenamento.',
  freeName: 'Gratis',
  freePrice: '0 €',
  freePeriod: 'per sempre',
  freeTagline: 'Disegni e controlli tu il tuo allenamento.',
  freeFeatures: [
    'Creare macrocicli manualmente',
    'Creare mesocicli e routine manualmente',
    'Registrare allenamenti e consultare la cronologia',
    'Catalogo completo di esercizi con immagini'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ mese',
  premiumTagline: 'TRAZO programma per te e si adatta a te.',
  premiumFeatures: [
    'Tutto il piano gratuito',
    'Generazione automatica del piano completo: macrociclo, fasi e scarichi',
    'Esercizi scelti per profilo, attrezzi e limitazioni',
    'Adattamento automatico di carichi, serie e scarichi in base al tuo feedback',
    'Ogni nuovo blocco generato dai tuoi progressi reali'
  ],
  premiumAnnual: 'Oppure 49,99 € l’anno · pari a 4,17 €/mese',
  pricingNote: 'Prezzi previsti al lancio. La pianificazione manuale resterà gratuita; generazione e adattamento automatici saranno Premium. Premium include 14 giorni di prova gratuita.',
  faqTitle: 'Domande frequenti',
  faq: [
    { q: 'È intelligenza artificiale?', a: 'No. Il motore è deterministico e basato su regole di allenamento: doppia progressione, volume settimanale per muscolo, recupero e scarichi. Niente testo inventato né casualità nascosta: applica regole note ai tuoi dati e spiega ogni decisione.' },
    { q: 'Posso allenarmi senza giorni fissi?', a: 'Sì. Puoi scegliere un ciclo continuo invece del calendario: TRAZO ti dice la prossima sessione e ti alleni quando puoi. Le sessioni in ritardo si registrano con la data reale e non vengono mai saltate da sole. Questa opzione è gratuita.' },
    { q: 'Cosa è gratis e cosa è Premium?', a: 'Gratis: creare macrocicli, mesocicli e routine, registrare allenamenti, consultare la cronologia e usare il catalogo. Premium: generazione automatica del piano e adattamento automatico degli allenamenti in base al tuo feedback.' },
    { q: 'Cosa fa esattamente la generazione automatica?', a: 'Da obiettivo, livello, giorni, tempo, attrezzi e limitazioni, TRAZO costruisce un macrociclo con fasi e scarichi e genera le sessioni di ogni blocco da quello che hai già allenato.' },
    { q: 'Come si adatta il mio piano?', a: 'Ogni volta che registri una sessione (serie, carichi, RIR, recupero e dolore) TRAZO regola le successive: cambia il carico, toglie serie, aggiunge margine o programma uno scarico. Ti spiega sempre cosa e perché.' },
    { q: 'Si adattano anche i piani creati da me?', a: 'Sì. Con Premium l’adattamento automatico funziona anche sui piani manuali. Senza Premium gli allenamenti si salvano comunque, ma il piano non si regola da solo.' },
    { q: 'Cosa succede se disdico Premium?', a: 'Conservi cronologia e piani manuali. Un piano generato automaticamente resta consultabile, ma serve Premium per continuare a usarlo e generare il blocco successivo. Puoi archiviarlo e crearne uno manuale.' },
    { q: 'Posso disdire quando voglio?', a: 'Sì, l’abbonamento si gestisce da Google Play e puoi annullarlo in qualsiasi momento.' },
    { q: 'TRAZO sostituisce un professionista?', a: 'No. TRAZO offre piani orientativi e non sostituisce il parere di un professionista sanitario o di un allenatore.' }
  ],
  shotAlt: 'Schermata dell’app TRAZO',
  howTitle: 'Come funziona',
  steps: [
    { title: 'Raccontaci come ti alleni', text: 'Obiettivo, livello, giorni disponibili e attrezzi.' },
    { title: 'Disegna o genera il piano', text: 'Costruiscilo a mano oppure lascia che TRAZO lo generi completo con Premium.' },
    { title: 'Allenati e registra', text: 'Registra le sessioni. Con Premium TRAZO adatta le successive a com’è andata.' }
  ],
  waitTitle: 'Stiamo preparando il lancio',
  waitText: 'Lasciaci la tua email e ti avviseremo quando TRAZO sarà su Google Play. Ti inviteremo anche a provarlo prima, nel test chiuso. Ti scriveremo solo per questo.',
  emailLabel: 'Email del tuo account Google',
  emailHint: 'Usa l’email dell’account Google del telefono: Google Play la userà per darti accesso al test. Ti invieremo un messaggio per confermarla.',
  consent: 'Accetto che TRAZO usi la mia email per avvisarmi del lancio e invitarmi a provare l’app nel test chiuso su Google Play. Posso revocare il consenso quando voglio. Maggiori informazioni nell’',
  privacyLabel: 'informativa sulla privacy (in inglese)',
  send: 'Iscrivimi',
  note: 'TRAZO offre piani di allenamento orientativi e non sostituisce il parere di un professionista sanitario.',
  ok: 'Ci siamo quasi: ti abbiamo inviato un’email. Aprila e conferma l’indirizzo per essere iscritto.',
  invalid: 'Controlla l’email e accetta il consenso.',
  later: 'Troppi tentativi. Riprova più tardi.',
  fail: 'Non siamo riusciti a salvare la tua email. Riprova tra qualche minuto.',
  terms: 'Condizioni d’uso (in inglese)', contact: 'Contatti', admin: 'Accesso amministrazione', skip: 'Vai al contenuto',
  waitlistPages: {
    confirm: {
      title: 'Conferma la tua email',
      text: 'Premi il pulsante per confermare che vuoi essere nella lista TRAZO: ti avviseremo del lancio e ti inviteremo a provare l’app.',
      button: 'Conferma',
      done: 'Email confermata',
      doneText: 'Grazie. Ti scriveremo quando ci saranno novità su test e lancio.'
    },
    unsubscribe: {
      title: 'Annulla l’iscrizione',
      text: 'Premi il pulsante per cancellare la tua email dalla lista TRAZO. Non ti scriveremo più.',
      button: 'Cancella i miei dati',
      done: 'Dati cancellati',
      doneText: 'Abbiamo cancellato la tua email dalla lista. Non riceverai altri messaggi.'
    },
    failed: 'Link non valido o scaduto',
    failedText: 'Questo link non è valido, è scaduto o è già stato usato. Se volevi iscriverti, compila di nuovo il modulo.',
    missing: 'Link incompleto',
    missingText: 'Al link manca il codice di sicurezza. Usa il link dell’email esattamente come l’hai ricevuto.',
    wait: 'Un momento…',
    home: 'Vai a TRAZO'
  }
};

export default content;
