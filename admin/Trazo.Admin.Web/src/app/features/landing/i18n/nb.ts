import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('nb');

const content: LandingContent = {
  title: 'TRAZO · Styrkemotoren som tilpasser seg deg',
  description: 'TRAZO er ikke en mal: det er en motor som lager makrosyklusen din, justerer vekter og sett etter hver økt ut fra hvordan du svarer, og forklarer hver endring. Manuell planlegging er gratis.',
  languageLabel: 'Språk',
  eyebrow: 'Adaptiv treningsmotor',
  headA: 'Planen din er ikke en mal.', headB: 'Det er en motor som tilpasser seg deg',
  lead: 'TRAZO setter sammen hele makrosyklusen, justerer vekter og sett etter hver økt ut fra hvordan du svarer, og forklarer hver endring. Med faste dager eller uten.',
  cta: 'Sett deg på ventelisten',
  loopTitle: 'En sløyfe som lukkes ved hver økt',
  loopLead: 'Manuell planlegging er gratis. Automatisk generering og tilpasning er Premium.',
  loop: [
    { title: 'Lager', text: 'Mål, nivå, utstyr og plager. Motoren utformer makrosyklusen: faser, avlastninger, øvelser og volum per muskel.' },
    { title: 'Tren', text: 'Du logger sett, vekter og RIR. Du ser hva du gjorde sist og hvilken vekt som gjelder i dag.' },
    { title: 'Lytter', text: 'Når du er ferdig, forteller du hvordan det går: restitusjon, anstrengelse, smerte og hver muskel.' },
    { title: 'Tilpasser', text: 'Øker vekten, fjerner eller legger til sett, planlegger en avlastning. Hver ny blokk bygger på det du faktisk løftet.' }
  ],
  pillarsTitle: 'Det som skiller motoren',
  pillars: [
    { title: 'Dobbel progresjon per øvelse', text: 'Først repetisjoner, så vekt: når du når toppen av intervallet med margin, går du opp et trinn. Er det ikke tid ennå, blir vekten stående.' },
    { title: 'Volum per muskel', text: 'Settene fordeles mot ukevolumet hver muskel trenger, ut fra nivået og tiden din.' },
    { title: 'Restitusjon per muskel', text: 'Før neste økt foreslås, sjekker den at musklene som skal jobbe, er restituert.', premium: true },
    { title: 'Avlastning når det trengs', text: 'Planlagte lette uker, og en ekstra avlastning hvis trettheten hoper seg opp eller prestasjonen faller.' },
    { title: 'Smerten bestemmer', text: 'Melder du om smerte, økes vekten aldri og ingenting automatiseres: du får beskjed om å se på det. Og leddplagene dine utelukker øvelser som forverrer dem.' },
    { title: 'Hver endring forklart', text: 'Ingen svart boks: hver justering har en begrunnelse og lagres i planens historikk.' }
  ],
  showcase: [
    {
      kicker: 'I gang på et minutt', title: 'Fortell hvordan du trener',
      text: 'Mål, nivå, utstyr og plager. Ingen endeløse skjemaer: motoren gjør resten.',
      points: ['Hypertrofi, styrke eller kondisjon', 'Utstyret ditt og det du må skåne', 'Planens lengde og avlastningsuker, fritt valg'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Mål og nivå'), shot('12-wizard-details', 'Utstyr og plager')]
    },
    {
      kicker: 'Din uke, dine regler', title: 'Med eller uten faste dager',
      text: 'Velg kalender eller løpende syklus: TRAZO viser neste økt, og du tar den når det passer.',
      points: ['Forsinkelser går ikke tapt, og ingenting hoppes over av seg selv', 'Restitusjonsvarsel før du starter', 'Etter uker borte begynner du roligere'],
      shots: [shot('11-wizard-flexible', 'Uten faste dager'), shot('14-plan-sequence', 'Neste økt')]
    },
    {
      kicker: 'Tren', title: 'Den vet hva du gjorde sist',
      text: 'Hver øvelse viser forrige resultat og foreslått vekt. Å logge et sett tar to trykk.',
      points: ['Forrige resultat og målvekt per øvelse', 'Innebygd pauseklokke', 'Virker selv om dekningen forsvinner'],
      shots: [shot('16-session', 'Øktdetaljer'), shot('17-workout', 'Logging av sett')]
    },
    {
      kicker: 'Tilpass', title: 'En bedre plan etter hver økt',
      text: 'Når du avslutter, forteller motoren hva den justerte og hvorfor. Fremgangen din er alltid synlig.',
      points: ['Hva som er endret, med begrunnelse', 'Personlige rekorder, rekker og sett per muskel', 'Hele blokken, der vekten finpusses uke for uke'],
      premium: true,
      shots: [shot('18-adaptation', 'Slik tilpasses planen'), shot('19-progress', 'Din fremgang'), shot('15-block', 'Blokken din')]
    }
  ],
  featuresTitle: 'Alt du trenger for å trene fornuftig',
  features: [
    { title: 'Planlegg på din måte', text: 'Lag makrosykluser, mesosykluser og rutiner, gratis og uten kunstige grenser.' },
    { title: 'Hver økt, tydelig', text: 'Sett, repetisjoner, RIR og pause for hver øvelse, med bilde og teknikk for hånden.' },
    { title: 'Bytt øvelser med omtanke', text: 'Alternativer som passer nivå, utstyr og begrensninger, uten å ødelegge planen.' },
    { title: 'Enkel logging', text: 'Noter hvert sett og bruk klokken. Virker selv om nettet faller ut.' },
    { title: 'Øvelseskatalog', text: 'Over 140 øvelser med søk, filtre og kort med bilder.' },
    { title: 'Trygg konto', text: 'Logg inn med Google eller e-post, tofaktorbekreftelse og kryptering på enheten.' }
  ],
  pricingTitle: 'Start gratis. Automatiser når du vil.',
  pricingLead: 'Manuell planlegging er gratis, uten kunstige grenser. Premium er for dem som vil at TRAZO skal lage og tilpasse for dem.',
  freeName: 'Gratis',
  freePrice: '0 €',
  freePeriod: 'alltid',
  freeTagline: 'Du utformer og styrer.',
  freeFeatures: [
    'Lag makrosykluser for hånd',
    'Lag mesosykluser og rutiner for hånd',
    'Logging av økter og historikk',
    'Komplett øvelseskatalog med bilder'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ måned',
  premiumTagline: 'TRAZO bygger for deg og tilpasser seg.',
  premiumFeatures: [
    'Alt i Gratis',
    'Automatisk generering av hele planen: makrosyklus, faser og avlastninger',
    'Øvelsesvalg etter profil, utstyr og begrensninger',
    'Automatisk tilpasning av vekter, sett og avlastninger ut fra tilbakemeldingene dine',
    'Nye blokker bygget på din faktiske fremgang'
  ],
  premiumAnnual: 'eller 49,99 € i året (4,17 € i måneden)',
  pricingNote: 'Planlagte priser ved lansering. Manuell planlegging forblir gratis; automatisk generering og tilpasning er Premium. Premium har 14 dagers gratis prøveperiode.',
  faqTitle: 'Vanlige spørsmål',
  faq: [
    { q: 'Er det kunstig intelligens?', a: 'Nei. Motoren er deterministisk og bygger på treningsprinsipper: dobbel progresjon, ukevolum per muskel, restitusjon og avlastning. Ingen oppdiktede tekster eller skjult tilfeldighet: den bruker kjente regler på dine data, og hver beslutning forklares.' },
    { q: 'Kan jeg bruke den uten faste dager?', a: 'Ja. I stedet for en kalender kan du velge en løpende syklus: TRAZO viser neste økt, og du tar den når det passer. Forsinkelser lagres med faktisk dato, ingenting hoppes over av seg selv, og dette er gratis.' },
    { q: 'Hva er forskjellen på Gratis og Premium?', a: 'Gratis: å lage makrosykluser, mesosykluser og rutiner, logge, historikk og katalog. Premium: automatisk generering av planen og automatisk tilpasning etter tilbakemeldingene dine.' },
    { q: 'Hva gjør den automatiske genereringen?', a: 'Ut fra mål, nivå, dager, tid, utstyr og begrensninger bygger den en makrosyklus med faser og avlastninger, og lager øktene i hver blokk basert på det du allerede har gjort.' },
    { q: 'Hvordan tilpasses planen?', a: 'Hver gang du logger en økt (sett, vekter, RIR, restitusjon, smerte), justerer TRAZO de neste: endrer vekter, fjerner et sett, legger til margin eller planlegger avlastning. Den forteller alltid hva den endrer og hvorfor.' },
    { q: 'Tilpasser den også planene jeg lager selv?', a: 'Ja: med Premium virker automatisk tilpasning også på manuelle planer. Uten Premium lagres loggene, og planen endres ikke av seg selv.' },
    { q: 'Hva skjer hvis jeg sier opp Premium?', a: 'Du beholder historikken og de manuelle planene. Automatisk genererte planer kan ses, men videre bruk og nye blokker krever Premium. Du kan arkivere dem og lage manuelle planer.' },
    { q: 'Kan jeg si opp når som helst?', a: 'Ja. Abonnementet styres i Google Play, og du kan si opp når du vil.' },
    { q: 'Erstatter TRAZO en fagperson?', a: 'Nei. TRAZO tilbyr veiledende treningsplaner og erstatter ikke råd fra helsepersonell eller trener.' }
  ],
  shotAlt: 'Skjermbilde fra TRAZO-appen',
  howTitle: 'Slik virker det',
  steps: [
    { title: 'Fortell hvordan du trener', text: 'Mål, nivå, ledige dager og utstyr.' },
    { title: 'Utform planen eller generer den', text: 'Bygg den for hånd, eller la TRAZO lage hele planen med Premium.' },
    { title: 'Tren og logg', text: 'Logg øktene. Med Premium justeres de neste etter resultatene dine.' }
  ],
  waitTitle: 'Vi gjør klar lanseringen',
  waitText: 'Legg igjen e-posten din, så sier vi fra når appen kommer på Google Play. Vi inviterer deg også til den lukkede testen før den tid. Vi bruker den ikke til noe annet.',
  emailLabel: 'E-posten til Google-kontoen din',
  emailHint: 'Bruk e-posten til Google-kontoen på telefonen: Google Play trenger den for å gi tilgang til testen. Vi sender deg en e-post for å bekrefte den.',
  consent: 'Jeg godtar at TRAZO bruker e-posten min til å varsle om lanseringen og invitere meg til den lukkede testen på Google Play. Jeg kan trekke tilbake samtykket når som helst. Mer i',
  privacyLabel: 'personvernerklæringen (på engelsk)',
  send: 'Sett meg på listen',
  note: 'TRAZO tilbyr veiledende treningsplaner og erstatter ikke råd fra helsepersonell.',
  ok: 'Nesten klart: vi har sendt deg en e-post. Åpne den og bekreft adressen for å fullføre.',
  invalid: 'Sjekk e-postadressen og kryss av for samtykke.',
  later: 'For mange forsøk. Prøv igjen senere.',
  fail: 'Vi klarte ikke å lagre e-posten. Prøv igjen om noen minutter.',
  terms: 'Vilkår (på engelsk)', contact: 'Kontakt', admin: 'Administratorinnlogging', skip: 'Hopp til innholdet',
  waitlistPages: {
    confirm: {
      title: 'Bekreft e-posten din',
      text: 'Trykk på knappen for å bekrefte at du vil stå på TRAZO-listen: vi sier fra om lanseringen og inviterer deg til å prøve appen.',
      button: 'Bekreft e-posten',
      done: 'E-posten er bekreftet',
      doneText: 'Takk. Vi skriver til deg når vi har nytt om testen og lanseringen.'
    },
    unsubscribe: {
      title: 'Meld deg av listen',
      text: 'Trykker du på knappen, sletter vi e-posten din fra TRAZO-listen og skriver ikke til deg igjen.',
      button: 'Slett opplysningene mine',
      done: 'Slettet',
      doneText: 'Vi har slettet e-posten din fra listen. Du får ikke flere meldinger.'
    },
    failed: 'Lenken er ugyldig eller utløpt',
    failedText: 'Lenken er ugyldig, utløpt eller allerede brukt. Fyll ut skjemaet på nytt for å melde deg på.',
    missing: 'Lenken er ufullstendig',
    missingText: 'Bekreftelseskoden mangler i lenken. Bruk lenken fra e-posten nøyaktig slik den står.',
    wait: 'Et øyeblikk…',
    home: 'Gå til TRAZO'
  }
};

export default content;
