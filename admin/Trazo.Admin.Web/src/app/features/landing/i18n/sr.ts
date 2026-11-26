import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('sr');

const content: LandingContent = {
  title: 'TRAZO · Motor za trening snage koji se prilagođava tebi',
  description: 'TRAZO nije predložak: to je motor koji stvara tvoj makrociklus, nakon svakog treninga prilagođava težine i serije prema tvom odgovoru i objašnjava svaku promenu. Ručno planiranje je besplatno.',
  languageLabel: 'Jezik',
  eyebrow: 'Prilagodljivi motor treninga',
  headA: 'Tvoj plan nije predložak.', headB: 'To je motor koji se prilagođava tebi',
  lead: 'TRAZO slaže celi makrociklus, nakon svakog treninga prilagođava težine i serije prema tvom odgovoru i objašnjava svaku promenu. S fiksnim danima ili bez njih.',
  cta: 'Prijavi se na listu čekanja',
  loopTitle: 'Petlja koja se zatvara svakim treningom',
  loopLead: 'Ručno planiranje je besplatno. Automatsko stvaranje i prilagodba su Premium.',
  loop: [
    { title: 'Stvara', text: 'Cilj, nivo, oprema i tegobe. Motor osmišljava makrociklus: faze, rasterećenja, vežbe i volumen po mišiću.' },
    { title: 'Treniraj', text: 'Beležiš serije, težine i RIR. Vidiš šta si radio prošli put i težinu za danas.' },
    { title: 'Sluša', text: 'Na kraju kažeš kako ide: oporavak, napor, bol i svaki mišić.' },
    { title: 'Prilagođava', text: 'Povećava težinu, uklanja ili dodaje serije, smešta rasterećenje. Svaki novi blok polazi od onoga što si stvarno podigao.' }
  ],
  pillarsTitle: 'Po čemu se motor razlikuje',
  pillars: [
    { title: 'Dvostruko napredovanje po vežbi', text: 'Prvo ponavljanja, zatim težina: kad s rezervom dosegneš gornju granicu raspona, penješ se stepenicu više. Ako još nije vreme, težina ostaje.' },
    { title: 'Volumen po mišiću', text: 'Serije se raspoređuju prema nedeljnom volumenu koji svaki mišić treba prema tvom nivou i vremenu.' },
    { title: 'Oporavak po mišiću', text: 'Pre nego što predloži sledeći trening, proverava jesu li mišići koji će raditi oporavljeni.', premium: true },
    { title: 'Rasterećenje kad treba', text: 'Planirane lagane nedelje i dodatno rasterećenje ako se umor nakuplja ili izvedba pada.' },
    { title: 'Bol odlučuje', text: 'Ako prijaviš bol, težina se nikad ne povećava i ništa se ne radi automatski: dobivaš upozorenje da to proveriš. A tegobe sa zglobovima isključuju vežbe koje ih pogoršavaju.' },
    { title: 'Svaka promena objašnjena', text: 'Bez crne kutije: svaka prilagodba ima svoj razlog i sprema se u istorij plana.' }
  ],
  showcase: [
    {
      kicker: 'Počni za minutu', title: 'Reci kako treniraš',
      text: 'Cilj, nivo, oprema i tegobe. Bez beskrajnih obrazaca: ostalo radi motor.',
      points: ['Hipertrofija, snaga ili kondicija', 'Tvoja oprema i ono što moraš čuvati', 'Trajanje plana i nedelje rasterećenja po izboru'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Cilj i nivo'), shot('12-wizard-details', 'Oprema i tegobe')]
    },
    {
      kicker: 'Tvoja nedelja, tvoja pravila', title: 'S fiksnim danima ili bez njih',
      text: 'Odaberi kalendar ili kontinuirani ciklus: TRAZO pokazuje sledeći trening, a ti ga odradiš kad ti odgovara.',
      points: ['Kašnjenje se ne gubi i ništa se samo ne preskače', 'Obaveštenje o oporavku pre početka', 'Nakon nedelja pauze krećeš blaže'],
      shots: [shot('11-wizard-flexible', 'Bez fiksnih dana'), shot('14-plan-sequence', 'Sledeći trening')]
    },
    {
      kicker: 'Treniraj', title: 'Zna šta si radio prošli put',
      text: 'Svaka vežba pokazuje prethodni rezultat i predloženu težinu. Za zapis serije dovoljna su dva dodira.',
      points: ['Prethodni rezultat i ciljna težina po vežbi', 'Ugrađen merač odmora', 'Radi i bez signala'],
      shots: [shot('16-session', 'Detalji treninga'), shot('17-workout', 'Beleženje serija')]
    },
    {
      kicker: 'Prilagodi', title: 'Bolji plan nakon svakog treninga',
      text: 'Kad završiš, motor ti kaže šta je prilagodio i zašto. Napredak ti je uvek pred očima.',
      points: ['Šta se promenilo i iz kojeg razloga', 'Osobni rekordi, nizovi i serije po mišiću', 'Celi blok u kojem se težine bruse iz nedelje u nedelju'],
      premium: true,
      shots: [shot('18-adaptation', 'Ovako se plan prilagođava'), shot('19-progress', 'Tvoj napredak'), shot('15-block', 'Tvoj blok')]
    }
  ],
  featuresTitle: 'Sve za promišljen trening',
  features: [
    { title: 'Planiraj po svome', text: 'Stvaraj makrocikluse, mezocikluse i rutine besplatno i bez veštačkih ograničenja.' },
    { title: 'Svaki trening pregledno', text: 'Serije, ponavljanja, RIR i odmor za svaku vežbu, sa slikom i tehnikom pri ruci.' },
    { title: 'Promišljene zamene vežbi', text: 'Zamene usklađene s nivoom, opremom i ograničenjima, bez rušenja plana.' },
    { title: 'Jednostavno beleženje', text: 'Zapiši svaku seriju i koristi merač. Radi i kad veza padne.' },
    { title: 'Katalog vežbi', text: 'Više od 140 vežbi s pretragom, filtrima i karticama sa slikama.' },
    { title: 'Zaštićen račun', text: 'Prijava Googleom ili e-poštom, dvofaktorska provera i šifrovanje na uređaju.' }
  ],
  pricingTitle: 'Počni besplatno. Automatizuj kad poželiš.',
  pricingLead: 'Ručno planiranje je besplatno i bez veštačkih ograničenja. Premium je za one koji žele da TRAZO stvara i prilagođava umesto njih.',
  freeName: 'Besplatno',
  freePrice: '0 €',
  freePeriod: 'zauvek',
  freeTagline: 'Ti osmišljavaš i vodiš.',
  freeFeatures: [
    'Ručno stvaranje makrociklusa',
    'Ručno stvaranje mezociklusa i rutina',
    'Beleženje treninga i istorij',
    'Celi katalog vežbi sa slikama'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ mesečno',
  premiumTagline: 'TRAZO gradi za tebe i prilagođava se.',
  premiumFeatures: [
    'Sve iz besplatne verzije',
    'Automatsko stvaranje celog plana: makrociklus, faze i rasterećenja',
    'Odabir vežbi prema profilu, opremi i ograničenjima',
    'Automatska prilagodba težina, serija i rasterećenja prema tvojim povratnim informacijama',
    'Novi blokovi izgrađeni na stvarnom napretku'
  ],
  premiumAnnual: 'ili 49,99 € godišnje (4,17 € mesečno)',
  pricingNote: 'Planirane cene pri izlasku. Ručno planiranje ostaje besplatno; automatsko stvaranje i prilagodba su Premium. Premium ima 14 dana besplatnog probnog razdoblja.',
  faqTitle: 'Česta pitanja',
  faq: [
    { q: 'Je li to veštačka inteligencija?', a: 'Ne. Motor je deterministički i temelji se na načelima treninga: dvostrukom napredovanju, nedeljnom volumenu po mišiću, oporavku i rasterećenju. Nema izmišljenih tekstova ni skrivene slučajnosti: na tvoje podatke primenjuje poznata pravila i svaku odluku objašnjava.' },
    { q: 'Mogu li ga koristiti bez fiksnih dana?', a: 'Da. Umesto kalendara možeš odabrati kontinuirani ciklus: TRAZO pokazuje sledeći trening, a ti ga odradiš kad želiš. Kašnjenja se beleže sa stvarnim datumom, ništa se samo ne preskače, a ta je mogućnost besplatna.' },
    { q: 'Koja je razlika između besplatne verzije i Premiuma?', a: 'Besplatno: stvaranje makrociklusa, mezociklusa i rutina, beleženje, istorij i katalog. Premium: automatsko stvaranje plana i automatska prilagodba prema tvojim povratnim informacijama.' },
    { q: 'Šta radi automatsko stvaranje?', a: 'Na temelju cilja, nivoa, dana, vremena, opreme i ograničenja slaže makrociklus s fazama i rasterećenjima, a treninge svakog bloka stvara prema onome što si već odradio.' },
    { q: 'Kako se plan prilagođava?', a: 'Svaki put kad zabeležiš trening (serije, težine, RIR, oporavak, bol), TRAZO prilagodi sledeće: promeni težine, ukloni seriju, doda rezervu ili smesti rasterećenje. Uvek kaže šta menja i zašto.' },
    { q: 'Prilagođava li i planove koje sam napravim?', a: 'Da: uz Premium automatska prilagodba radi i na ručnim planovima. Bez Premiuma zapisi se spremaju, a plan se sam ne menja.' },
    { q: 'Šta se događa ako otkažem Premium?', a: 'Istorij i ručni planovi ostaju ti. Automatski stvorene planove možeš gledati, ali za dalje korišćenje i nove blokove treba Premium. Možeš ih arhivirati i napraviti ručne.' },
    { q: 'Mogu li otkazati bilo kada?', a: 'Da. Pretplatom se upravlja na Google Playu i možeš je otkazati kad god želiš.' },
    { q: 'Zamenjuje li TRAZO stručnjaka?', a: 'Ne. TRAZO nudi okvirne planove treninga i ne zamenjuje savet zdravstvenog radnika ni trenera.' }
  ],
  shotAlt: 'Snimka ekrana aplikacije TRAZO',
  howTitle: 'Kako radi',
  steps: [
    { title: 'Reci kako treniraš', text: 'Cilj, nivo, slobodni dani i oprema.' },
    { title: 'Osmisli plan ili ga stvori', text: 'Složi ga ručno ili pusti da TRAZO uz Premium stvori celi plan.' },
    { title: 'Treniraj i beleži', text: 'Beleži treninge. Uz Premium sledeći se prilagođavaju tvojim rezultatima.' }
  ],
  waitTitle: 'Pripremamo izlazak',
  waitText: 'Ostavi nam e-poštu i javićemo ti kad aplikacija bude na Google Playu. Pre toga pozvaćemo te i u zatvoreno testiranje. Ni za što drugo je nećemo koristiti.',
  emailLabel: 'E-pošta tvog Google računa',
  emailHint: 'Upotrebi e-poštu Google računa na svom telefonu: Google Playu treba za pristup testiranju. Poslaćemo ti poruku za potvrdu.',
  consent: 'Pristajem da TRAZO koristi moju e-poštu kako bi me obavestio o izlasku i pozvao u zatvoreno testiranje na Google Playu. Pristanak mogu povući bilo kada. Više u',
  privacyLabel: 'pravilima o privatnosti (na engleskom)',
  send: 'Dodaj me',
  note: 'TRAZO nudi okvirne planove treninga i ne zamenjuje savet zdravstvenog radnika.',
  ok: 'Još samo korak: poslali smo ti poruku. Otvori je i potvrdi adresu.',
  invalid: 'Proveri e-poštu i označi pristanak.',
  later: 'Previše pokušaja. Pokušaj malo kasnije.',
  fail: 'E-poštu nije bilo moguće spremiti. Pokušaj za nekoliko minuta.',
  terms: 'Uslovi (na engleskom)', contact: 'Kontakt', admin: 'Prijava administratora', skip: 'Preskoči na sadržaj',
  waitlistPages: {
    confirm: {
      title: 'Potvrdi svoju e-poštu',
      text: 'Pritiskom na dugme potvrdi da želiš biti na TRAZO popisu: javićemo ti o izlasku i pozvati te da isprobaš aplikaciju.',
      button: 'Potvrdi e-poštu',
      done: 'E-pošta potvrđena',
      doneText: 'Hvala. Pisaćemo ti kad bude novosti o testiranju i izlasku.'
    },
    unsubscribe: {
      title: 'Odjava s popisa',
      text: 'Pritiskom na dugme brišemo tvoju e-poštu s TRAZO popisa i više ti nećemo pisati.',
      button: 'Izbriši moje podatke',
      done: 'Izbrisano',
      doneText: 'Izbrisali smo tvoju e-poštu s popisa. Više nećeš primati poruke.'
    },
    failed: 'Nevaljana ili istekla veza',
    failedText: 'Veza nije valjana, istekla je ili je već upotrebljena. Za prijavu ponovo ispuni obrazac.',
    missing: 'Nepotpuna veza',
    missingText: 'U vezi nedostaje kod za potvrdu. Upotrebi vezu iz poruke tačno onakvu kakva jest.',
    wait: 'Trenutak…',
    home: 'Na TRAZO'
  }
};

export default content;
