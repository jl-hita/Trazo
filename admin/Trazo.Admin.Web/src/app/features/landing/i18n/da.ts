import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('da');

const content: LandingContent = {
  title: 'TRAZO · Styrketræningsmotoren, der tilpasser sig dig',
  description: 'TRAZO er ikke en skabelon: det er en motor, der genererer din makrocyklus, justerer vægte og sæt pas for pas efter, hvordan du reagerer, og forklarer hver ændring. Manuel planlægning er gratis.',
  languageLabel: 'Sprog',
  eyebrow: 'ADAPTIV TRÆNINGSMOTOR',
  headA: 'Din plan er ikke en skabelon.', headB: 'Det er en motor, der tilpasser sig dig',
  lead: 'TRAZO bygger hele din makrocyklus, justerer vægte og sæt pas for pas efter, hvordan du reagerer, og forklarer hver ændring. Med eller uden faste dage.',
  cta: 'Jeg vil på ventelisten',
  loopTitle: 'Et kredsløb, der lukkes i hvert pas',
  loopLead: 'Manuel planlægning er gratis. Automatisk generering og tilpasning er Premium.',
  loop: [
    { title: 'Genererer', text: 'Mål, niveau, udstyr og gener. Motoren designer makrocyklussen: faser, deloads, øvelser og volumen pr. muskel.' },
    { title: 'Træn', text: 'Du registrerer sæt, vægte og RIR. Du ser, hvad du lavede sidst, og vægten for i dag.' },
    { title: 'Lytter', text: 'Når du er færdig, fortæller du, hvordan det går: restitution, anstrengelse, smerte og hver muskel.' },
    { title: 'Tilpasser', text: 'Hæver vægten, fjerner eller tilføjer sæt, planlægger en deload. Hver ny blok tager udgangspunkt i det, du faktisk løftede.' }
  ],
  pillarsTitle: 'Det, der gør motoren anderledes',
  pillars: [
    { title: 'Dobbelt progression pr. øvelse', text: 'Først gentagelser, så vægt: når du når toppen af intervallet med margen, går du et trin op. Er det ikke tid endnu, bliver den.' },
    { title: 'Volumen pr. muskel', text: 'Sættene fordeles mod den ugevolumen, hver muskel har brug for, efter dit niveau og din tid.' },
    { title: 'Restitution pr. muskel', text: 'Før næste pas foreslås, tjekker den, at musklerne, der skal arbejde, er restitueret.', premium: true },
    { title: 'Deloads, når det trængs', text: 'Planlagte lette uger og en ekstra deload, hvis træthed hober sig op, eller præstationen falder.' },
    { title: 'Smerten bestemmer', text: 'Melder du smerte, hæves vægten aldrig, og intet automatiseres: du får besked om at se på det. Og dine ledgener udelukker øvelser, der forværrer dem.' },
    { title: 'Hver ændring forklaret', text: 'Ingen sort boks: hver justering kommer med begrundelsen og gemmes i planens historik.' }
  ],
  showcase: [
    {
      kicker: 'I gang på et minut', title: 'Fortæl, hvordan du træner',
      text: 'Mål, niveau, udstyr og gener. Ingen endeløse spørgeskemaer: motoren klarer resten.',
      points: ['Muskelvækst, styrke eller generel form', 'Dit udstyr og de områder, du vil skåne', 'Planens længde og deload-uger, som du vil'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Mål og niveau'), shot('12-wizard-details', 'Udstyr og gener')]
    },
    {
      kicker: 'Din uge, dine regler', title: 'Med eller uden faste dage',
      text: 'Vælg en kalender eller en løbende cyklus. TRAZO viser næste pas, og du træner, når du kan.',
      points: ['Forsinkede pas går ikke tabt og springes ikke over af sig selv', 'Besked om restitution, før du går i gang', 'Lettere comeback-pas, hvis du vender tilbage efter uger'],
      shots: [shot('11-wizard-flexible', 'Uden faste dage'), shot('14-plan-sequence', 'Næste pas')]
    },
    {
      kicker: 'Træn', title: 'Den ved, hvad du lavede sidst',
      text: 'Hver øvelse viser din tidligere præstation og den foreslåede vægt. Et sæt registreres med to tryk.',
      points: ['Sidste gang og målvægt ved hver øvelse', 'Indbygget hvilepause-ur', 'Virker, selv om forbindelsen ryger'],
      shots: [shot('16-session', 'Passet i detaljer'), shot('17-workout', 'Registrering af sæt')]
    },
    {
      kicker: 'Tilpas', title: 'En bedre plan efter hvert pas',
      text: 'Afslut passet, og motoren fortæller, hvad den justerede og hvorfor. Din fremgang er altid synlig.',
      points: ['Hvad der ændrede sig, og hvorfor', 'Rekorder, stime og sæt pr. muskel', 'Hele blokken med vægte, der finpudses uge for uge'],
      premium: true,
      shots: [shot('18-adaptation', 'Sådan tilpasses din plan'), shot('19-progress', 'Din fremgang'), shot('15-block', 'Din blok')]
    }
  ],
  featuresTitle: 'Alt, du skal bruge for at træne med omtanke',
  features: [
    { title: 'Planlæg på din måde', text: 'Opret makrocykler, mesocykler og rutiner, gratis og uden kunstige grænser.' },
    { title: 'Hvert pas, tydeligt', text: 'Sæt, gentagelser, RIR og hvile for hver øvelse, med billede og teknik ved hånden.' },
    { title: 'Skift øvelser med omtanke', text: 'Alternativer, der passer til niveau, udstyr og begrænsninger, uden at ødelægge planen.' },
    { title: 'Nem registrering', text: 'Notér hvert sæt, og brug uret. Virker, selv om forbindelsen ryger.' },
    { title: 'Øvelseskatalog', text: 'Over 140 øvelser med søgning, filtre og kort med billeder.' },
    { title: 'Din konto, beskyttet', text: 'Login med Google eller e-mail, totrinsbekræftelse og krypterede data på enheden.' }
  ],
  pricingTitle: 'Start gratis. Automatisér, når du vil.',
  pricingLead: 'Manuel planlægning er gratis og uden kunstige grænser. Premium er for dem, der vil have TRAZO til at programmere og tilpasse træningen.',
  freeName: 'Gratis',
  freePrice: '0 €',
  freePeriod: 'for altid',
  freeTagline: 'Du designer og styrer træningen.',
  freeFeatures: [
    'Opret makrocykler manuelt',
    'Opret mesocykler og rutiner manuelt',
    'Registrér træning og se historik',
    'Hele øvelseskataloget med billeder'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ md.',
  premiumTagline: 'TRAZO programmerer for dig og tilpasser sig dig.',
  premiumFeatures: [
    'Alt fra den gratis plan',
    'Automatisk generering af hele planen: makrocyklus, faser og deloads',
    'Øvelser valgt efter profil, udstyr og begrænsninger',
    'Automatisk tilpasning af vægte, sæt og deloads efter din feedback',
    'Hver ny blok genereret ud fra din reelle fremgang'
  ],
  premiumAnnual: 'Eller 49,99 € om året · svarer til 4,17 €/md.',
  pricingNote: 'Forventede priser ved lancering. Manuel planlægning forbliver gratis; automatisk generering og tilpasning bliver Premium. Premium inkluderer 14 dages gratis prøve.',
  faqTitle: 'Ofte stillede spørgsmål',
  faq: [
    { q: 'Er det kunstig intelligens?', a: 'Nej. Motoren er deterministisk og bygger på træningsregler: dobbelt progression, ugevolumen pr. muskel, restitution og deloads. Ingen opdigtet tekst og ingen skjult tilfældighed: den anvender kendte regler på dine data, og hver beslutning forklares.' },
    { q: 'Kan jeg træne uden faste dage?', a: 'Ja. Du kan vælge en løbende cyklus i stedet for en kalender: TRAZO viser næste pas, og du træner, når du kan. Forsinkede pas registreres med den rigtige dato og springes aldrig over af sig selv. Muligheden er gratis.' },
    { q: 'Hvad er gratis, og hvad er Premium?', a: 'Gratis: oprette makrocykler, mesocykler og rutiner, registrere træning, se historik og bruge kataloget. Premium: automatisk generering af planen og automatisk tilpasning efter din feedback.' },
    { q: 'Hvad gør den automatiske generering præcist?', a: 'Ud fra mål, niveau, dage, tid, udstyr og begrænsninger bygger TRAZO en makrocyklus med faser og deloads og genererer hver bloks pas ud fra det, du allerede har trænet.' },
    { q: 'Hvordan tilpasses min plan?', a: 'Hver gang du registrerer et pas (sæt, vægte, RIR, restitution og smerte), justerer TRAZO de næste: ændrer vægten, fjerner sæt, giver mere margen eller planlægger en deload. Du får altid at vide hvad og hvorfor.' },
    { q: 'Tilpasses planer, jeg selv laver, også?', a: 'Ja. Med Premium virker den automatiske tilpasning også i dine manuelle planer. Uden Premium gemmes træningen stadig, men planen justeres ikke af sig selv.' },
    { q: 'Hvad sker der, hvis jeg opsiger Premium?', a: 'Du beholder historikken og dine manuelle planer. En automatisk genereret plan kan ses, men det kræver Premium at bruge den videre og generere næste blok. Du kan arkivere den og lave en manuel.' },
    { q: 'Kan jeg opsige, når jeg vil?', a: 'Ja, abonnementet administreres i Google Play, og du kan opsige når som helst.' },
    { q: 'Erstatter TRAZO en fagperson?', a: 'Nej. TRAZO tilbyder vejledende træningsplaner og erstatter ikke råd fra en sundhedsfaglig person eller træner.' }
  ],
  shotAlt: 'Skærmbillede fra TRAZO-appen',
  howTitle: 'Sådan virker det',
  steps: [
    { title: 'Fortæl, hvordan du træner', text: 'Mål, niveau, ledige dage og udstyr.' },
    { title: 'Design eller generér din plan', text: 'Byg den selv, eller lad TRAZO generere hele planen med Premium.' },
    { title: 'Træn og registrér', text: 'Registrér dine pas. Med Premium tilpasser TRAZO de næste efter, hvordan det gik.' }
  ],
  waitTitle: 'Vi forbereder lanceringen',
  waitText: 'Skriv din e-mail, så giver vi besked, når TRAZO er i Google Play. Vi inviterer dig også til at prøve den før i den lukkede test. Vi skriver kun om det.',
  emailLabel: 'E-mail til din Google-konto',
  emailHint: 'Brug e-mailen til telefonens Google-konto: Google Play bruger den til at give dig adgang til testen. Vi sender en mail, så du kan bekræfte den.',
  consent: 'Jeg accepterer, at TRAZO bruger min e-mail til at give besked om lanceringen og invitere mig til at prøve appen i den lukkede test i Google Play. Jeg kan altid trække det tilbage. Læs mere i',
  privacyLabel: 'privatlivspolitikken (på engelsk)',
  send: 'Skriv mig op',
  note: 'TRAZO tilbyder vejledende træningsplaner og erstatter ikke råd fra en sundhedsfaglig person.',
  ok: 'Næsten færdig: vi har sendt dig en mail. Åbn den, og bekræft adressen for at komme på listen.',
  invalid: 'Tjek e-mailen, og acceptér samtykket.',
  later: 'For mange forsøg. Prøv igen senere.',
  fail: 'Vi kunne ikke gemme din e-mail. Prøv igen om et par minutter.',
  terms: 'Vilkår (på engelsk)', contact: 'Kontakt', admin: 'Administratoradgang', skip: 'Spring til indholdet',
  waitlistPages: {
    confirm: {
      title: 'Bekræft din e-mail',
      text: 'Tryk på knappen for at bekræfte, at du vil på TRAZOs liste: vi giver besked om lanceringen og inviterer dig til at prøve appen.',
      button: 'Bekræft',
      done: 'E-mail bekræftet',
      doneText: 'Tak. Vi skriver, når der er nyt om testen og lanceringen.'
    },
    unsubscribe: {
      title: 'Frameld dig',
      text: 'Tryk på knappen for at slette din e-mail fra TRAZOs liste. Vi skriver ikke til dig igen.',
      button: 'Slet mine data',
      done: 'Data slettet',
      doneText: 'Vi har slettet din e-mail fra listen. Du får ikke flere beskeder.'
    },
    failed: 'Ugyldigt eller udløbet link',
    failedText: 'Linket er ugyldigt, udløbet eller allerede brugt. Ville du skrive dig op, så udfyld formularen igen.',
    missing: 'Ufuldstændigt link',
    missingText: 'Linket mangler sikkerhedskoden. Brug linket fra mailen, præcis som du modtog det.',
    wait: 'Et øjeblik…',
    home: 'Gå til TRAZO'
  }
};

export default content;
