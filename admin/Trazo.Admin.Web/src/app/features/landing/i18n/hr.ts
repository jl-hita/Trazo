import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('hr');

const content: LandingContent = {
  title: 'TRAZO · Motor za trening snage koji se prilagođava tebi',
  description: 'TRAZO nije predložak: to je motor koji stvara tvoj makrociklus, nakon svakog treninga prilagođava težine i serije prema tvom odgovoru i objašnjava svaku promjenu. Ručno planiranje je besplatno.',
  languageLabel: 'Jezik',
  eyebrow: 'Prilagodljivi motor treninga',
  headA: 'Tvoj plan nije predložak.', headB: 'To je motor koji se prilagođava tebi',
  lead: 'TRAZO slaže cijeli makrociklus, nakon svakog treninga prilagođava težine i serije prema tvom odgovoru i objašnjava svaku promjenu. S fiksnim danima ili bez njih.',
  cta: 'Prijavi se na listu čekanja',
  loopTitle: 'Petlja koja se zatvara svakim treningom',
  loopLead: 'Ručno planiranje je besplatno. Automatsko stvaranje i prilagodba su Premium.',
  loop: [
    { title: 'Stvara', text: 'Cilj, razina, oprema i tegobe. Motor osmišljava makrociklus: faze, rasterećenja, vježbe i volumen po mišiću.' },
    { title: 'Treniraj', text: 'Bilježiš serije, težine i RIR. Vidiš što si radio prošli put i težinu za danas.' },
    { title: 'Sluša', text: 'Na kraju kažeš kako ide: oporavak, napor, bol i svaki mišić.' },
    { title: 'Prilagođava', text: 'Povećava težinu, uklanja ili dodaje serije, smješta rasterećenje. Svaki novi blok polazi od onoga što si stvarno podigao.' }
  ],
  pillarsTitle: 'Po čemu se motor razlikuje',
  pillars: [
    { title: 'Dvostruko napredovanje po vježbi', text: 'Prvo ponavljanja, zatim težina: kad s rezervom dosegneš gornju granicu raspona, penješ se stepenicu više. Ako još nije vrijeme, težina ostaje.' },
    { title: 'Volumen po mišiću', text: 'Serije se raspoređuju prema tjednom volumenu koji svaki mišić treba prema tvojoj razini i vremenu.' },
    { title: 'Oporavak po mišiću', text: 'Prije nego što predloži sljedeći trening, provjerava jesu li mišići koji će raditi oporavljeni.', premium: true },
    { title: 'Rasterećenje kad treba', text: 'Planirani lagani tjedni i dodatno rasterećenje ako se umor nakuplja ili izvedba pada.' },
    { title: 'Bol odlučuje', text: 'Ako prijaviš bol, težina se nikad ne povećava i ništa se ne radi automatski: dobivaš upozorenje da to provjeriš. A tegobe sa zglobovima isključuju vježbe koje ih pogoršavaju.' },
    { title: 'Svaka promjena objašnjena', text: 'Bez crne kutije: svaka prilagodba ima svoj razlog i sprema se u povijest plana.' }
  ],
  showcase: [
    {
      kicker: 'Počni za minutu', title: 'Reci kako treniraš',
      text: 'Cilj, razina, oprema i tegobe. Bez beskrajnih obrazaca: ostalo radi motor.',
      points: ['Hipertrofija, snaga ili kondicija', 'Tvoja oprema i ono što moraš čuvati', 'Trajanje plana i tjedni rasterećenja po izboru'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Cilj i razina'), shot('12-wizard-details', 'Oprema i tegobe')]
    },
    {
      kicker: 'Tvoj tjedan, tvoja pravila', title: 'S fiksnim danima ili bez njih',
      text: 'Odaberi kalendar ili kontinuirani ciklus: TRAZO pokazuje sljedeći trening, a ti ga odradiš kad ti odgovara.',
      points: ['Kašnjenje se ne gubi i ništa se samo ne preskače', 'Obavijest o oporavku prije početka', 'Nakon tjedana pauze krećeš blaže'],
      shots: [shot('11-wizard-flexible', 'Bez fiksnih dana'), shot('14-plan-sequence', 'Sljedeći trening')]
    },
    {
      kicker: 'Treniraj', title: 'Zna što si radio prošli put',
      text: 'Svaka vježba pokazuje prethodni rezultat i predloženu težinu. Za zapis serije dovoljna su dva dodira.',
      points: ['Prethodni rezultat i ciljna težina po vježbi', 'Ugrađen mjerač odmora', 'Radi i bez signala'],
      shots: [shot('16-session', 'Detalji treninga'), shot('17-workout', 'Bilježenje serija')]
    },
    {
      kicker: 'Prilagodi', title: 'Bolji plan nakon svakog treninga',
      text: 'Kad završiš, motor ti kaže što je prilagodio i zašto. Napredak ti je uvijek pred očima.',
      points: ['Što se promijenilo i iz kojeg razloga', 'Osobni rekordi, nizovi i serije po mišiću', 'Cijeli blok u kojem se težine bruse iz tjedna u tjedan'],
      premium: true,
      shots: [shot('18-adaptation', 'Ovako se plan prilagođava'), shot('19-progress', 'Tvoj napredak'), shot('15-block', 'Tvoj blok')]
    }
  ],
  featuresTitle: 'Sve za promišljen trening',
  features: [
    { title: 'Planiraj po svome', text: 'Stvaraj makrocikluse, mezocikluse i rutine besplatno i bez umjetnih ograničenja.' },
    { title: 'Svaki trening pregledno', text: 'Serije, ponavljanja, RIR i odmor za svaku vježbu, sa slikom i tehnikom pri ruci.' },
    { title: 'Promišljene zamjene vježbi', text: 'Zamjene usklađene s razinom, opremom i ograničenjima, bez rušenja plana.' },
    { title: 'Jednostavno bilježenje', text: 'Zapiši svaku seriju i koristi mjerač. Radi i kad veza padne.' },
    { title: 'Katalog vježbi', text: 'Više od 140 vježbi s pretragom, filtrima i karticama sa slikama.' },
    { title: 'Zaštićen račun', text: 'Prijava Googleom ili e-poštom, dvofaktorska provjera i šifriranje na uređaju.' }
  ],
  pricingTitle: 'Počni besplatno. Automatiziraj kad poželiš.',
  pricingLead: 'Ručno planiranje je besplatno i bez umjetnih ograničenja. Premium je za one koji žele da TRAZO stvara i prilagođava umjesto njih.',
  freeName: 'Besplatno',
  freePrice: '0 €',
  freePeriod: 'zauvijek',
  freeTagline: 'Ti osmišljavaš i vodiš.',
  freeFeatures: [
    'Ručno stvaranje makrociklusa',
    'Ručno stvaranje mezociklusa i rutina',
    'Bilježenje treninga i povijest',
    'Cijeli katalog vježbi sa slikama'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ mjesečno',
  premiumTagline: 'TRAZO gradi za tebe i prilagođava se.',
  premiumFeatures: [
    'Sve iz besplatne verzije',
    'Automatsko stvaranje cijelog plana: makrociklus, faze i rasterećenja',
    'Odabir vježbi prema profilu, opremi i ograničenjima',
    'Automatska prilagodba težina, serija i rasterećenja prema tvojim povratnim informacijama',
    'Novi blokovi izgrađeni na stvarnom napretku'
  ],
  premiumAnnual: 'ili 49,99 € godišnje (4,17 € mjesečno)',
  pricingNote: 'Planirane cijene pri izlasku. Ručno planiranje ostaje besplatno; automatsko stvaranje i prilagodba su Premium. Premium ima 14 dana besplatnog probnog razdoblja.',
  faqTitle: 'Česta pitanja',
  faq: [
    { q: 'Je li to umjetna inteligencija?', a: 'Ne. Motor je deterministički i temelji se na načelima treninga: dvostrukom napredovanju, tjednom volumenu po mišiću, oporavku i rasterećenju. Nema izmišljenih tekstova ni skrivene slučajnosti: na tvoje podatke primjenjuje poznata pravila i svaku odluku objašnjava.' },
    { q: 'Mogu li ga koristiti bez fiksnih dana?', a: 'Da. Umjesto kalendara možeš odabrati kontinuirani ciklus: TRAZO pokazuje sljedeći trening, a ti ga odradiš kad želiš. Kašnjenja se bilježe sa stvarnim datumom, ništa se samo ne preskače, a ta je mogućnost besplatna.' },
    { q: 'Koja je razlika između besplatne verzije i Premiuma?', a: 'Besplatno: stvaranje makrociklusa, mezociklusa i rutina, bilježenje, povijest i katalog. Premium: automatsko stvaranje plana i automatska prilagodba prema tvojim povratnim informacijama.' },
    { q: 'Što radi automatsko stvaranje?', a: 'Na temelju cilja, razine, dana, vremena, opreme i ograničenja slaže makrociklus s fazama i rasterećenjima, a treninge svakog bloka stvara prema onome što si već odradio.' },
    { q: 'Kako se plan prilagođava?', a: 'Svaki put kad zabilježiš trening (serije, težine, RIR, oporavak, bol), TRAZO prilagodi sljedeće: promijeni težine, ukloni seriju, doda rezervu ili smjesti rasterećenje. Uvijek kaže što mijenja i zašto.' },
    { q: 'Prilagođava li i planove koje sam napravim?', a: 'Da: uz Premium automatska prilagodba radi i na ručnim planovima. Bez Premiuma zapisi se spremaju, a plan se sam ne mijenja.' },
    { q: 'Što se događa ako otkažem Premium?', a: 'Povijest i ručni planovi ostaju ti. Automatski stvorene planove možeš gledati, ali za daljnje korištenje i nove blokove treba Premium. Možeš ih arhivirati i napraviti ručne.' },
    { q: 'Mogu li otkazati bilo kada?', a: 'Da. Pretplatom se upravlja na Google Playu i možeš je otkazati kad god želiš.' },
    { q: 'Zamjenjuje li TRAZO stručnjaka?', a: 'Ne. TRAZO nudi okvirne planove treninga i ne zamjenjuje savjet zdravstvenog radnika ni trenera.' }
  ],
  shotAlt: 'Snimka zaslona aplikacije TRAZO',
  howTitle: 'Kako radi',
  steps: [
    { title: 'Reci kako treniraš', text: 'Cilj, razina, slobodni dani i oprema.' },
    { title: 'Osmisli plan ili ga stvori', text: 'Složi ga ručno ili pusti da TRAZO uz Premium stvori cijeli plan.' },
    { title: 'Treniraj i bilježi', text: 'Bilježi treninge. Uz Premium sljedeći se prilagođavaju tvojim rezultatima.' }
  ],
  waitTitle: 'Pripremamo izlazak',
  waitText: 'Ostavi nam e-poštu i javit ćemo ti kad aplikacija bude na Google Playu. Prije toga pozvat ćemo te i u zatvoreno testiranje. Ni za što drugo je nećemo koristiti.',
  emailLabel: 'E-pošta tvog Google računa',
  emailHint: 'Upotrijebi e-poštu Google računa na svom telefonu: Google Playu treba za pristup testiranju. Poslat ćemo ti poruku za potvrdu.',
  consent: 'Pristajem da TRAZO koristi moju e-poštu kako bi me obavijestio o izlasku i pozvao u zatvoreno testiranje na Google Playu. Privolu mogu povući bilo kada. Više u',
  privacyLabel: 'pravilima o privatnosti (na engleskom)',
  send: 'Dodaj me',
  note: 'TRAZO nudi okvirne planove treninga i ne zamjenjuje savjet zdravstvenog radnika.',
  ok: 'Još samo korak: poslali smo ti poruku. Otvori je i potvrdi adresu.',
  invalid: 'Provjeri e-poštu i označi privolu.',
  later: 'Previše pokušaja. Pokušaj malo kasnije.',
  fail: 'E-poštu nije bilo moguće spremiti. Pokušaj za nekoliko minuta.',
  terms: 'Uvjeti (na engleskom)', contact: 'Kontakt', admin: 'Prijava administratora', skip: 'Preskoči na sadržaj',
  waitlistPages: {
    confirm: {
      title: 'Potvrdi svoju e-poštu',
      text: 'Pritiskom na gumb potvrdi da želiš biti na TRAZO popisu: javit ćemo ti o izlasku i pozvati te da isprobaš aplikaciju.',
      button: 'Potvrdi e-poštu',
      done: 'E-pošta potvrđena',
      doneText: 'Hvala. Pisat ćemo ti kad bude novosti o testiranju i izlasku.'
    },
    unsubscribe: {
      title: 'Odjava s popisa',
      text: 'Pritiskom na gumb brišemo tvoju e-poštu s TRAZO popisa i više ti nećemo pisati.',
      button: 'Izbriši moje podatke',
      done: 'Izbrisano',
      doneText: 'Izbrisali smo tvoju e-poštu s popisa. Više nećeš primati poruke.'
    },
    failed: 'Nevaljana ili istekla poveznica',
    failedText: 'Poveznica nije valjana, istekla je ili je već upotrijebljena. Za prijavu ponovno ispuni obrazac.',
    missing: 'Nepotpuna poveznica',
    missingText: 'U poveznici nedostaje kod za potvrdu. Upotrijebi poveznicu iz poruke točno onakvu kakva jest.',
    wait: 'Trenutak…',
    home: 'Na TRAZO'
  }
};

export default content;
