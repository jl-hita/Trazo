import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('sk');

const content: LandingContent = {
  title: 'TRAZO · Silový motor, ktorý sa ti prispôsobí',
  description: 'TRAZO nie je šablóna: je to motor, ktorý zostaví tvoj makrocyklus, po každom tréningu upraví váhy a série podľa tvojej odozvy a každú zmenu vysvetlí. Ručné plánovanie je zadarmo.',
  languageLabel: 'Jazyk',
  eyebrow: 'Adaptívny tréningový motor',
  headA: 'Tvoj plán nie je šablóna.', headB: 'Je to motor, ktorý sa ti prispôsobí',
  lead: 'TRAZO zostaví celý makrocyklus, po každom tréningu upraví váhy a série podľa tvojej odozvy a každú zmenu vysvetlí. S pevnými dňami aj bez nich.',
  cta: 'Zapísať sa na čakaciu listinu',
  loopTitle: 'Slučka, ktorá sa uzatvára každým tréningom',
  loopLead: 'Ručné plánovanie je zadarmo. Automatické generovanie a prispôsobenie sú Premium.',
  loop: [
    { title: 'Vytvorí', text: 'Cieľ, úroveň, vybavenie a ťažkosti. Motor navrhne makrocyklus: fázy, odľahčenia, cviky a objem na sval.' },
    { title: 'Trénuj', text: 'Zapisuješ série, váhy a RIR. Vidíš, čo si robil(a) naposledy, a váhu na dnešok.' },
    { title: 'Počúva', text: 'Po skončení povieš, ako to ide: regenerácia, úsilie, bolesť a jednotlivé svaly.' },
    { title: 'Prispôsobí', text: 'Zvýši váhu, uberie alebo pridá série, zaradí odľahčenie. Každý nový blok vychádza z toho, čo si naozaj zdvihol(a).' }
  ],
  pillarsTitle: 'Čím sa motor líši',
  pillars: [
    { title: 'Dvojitá progresia pri každom cviku', text: 'Najprv opakovania, potom váha: keď s rezervou dosiahneš hornú hranicu rozsahu, postúpiš o stupeň. Ak ešte nie, váha zostáva.' },
    { title: 'Objem na sval', text: 'Série sa rozdeľujú k týždennému objemu, ktorý každý sval potrebuje podľa tvojej úrovne a času.' },
    { title: 'Regenerácia po svaloch', text: 'Skôr než navrhne ďalší tréning, overí, že svaly, ktoré budú pracovať, sú zregenerované.', premium: true },
    { title: 'Odľahčenie, keď treba', text: 'Plánované ľahké týždne a mimoriadne odľahčenie, ak sa hromadí únava alebo klesá výkon.' },
    { title: 'Rozhoduje bolesť', text: 'Ak nahlásiš bolesť, váha sa nikdy nezvýši a nič sa nedeje automaticky: dostaneš upozornenie, aby si to preveril(a). A ťažkosti s kĺbmi vyradia cviky, ktoré ich zhoršujú.' },
    { title: 'Každá zmena vysvetlená', text: 'Žiadna čierna skrinka: každá úprava má svoje zdôvodnenie a ukladá sa do histórie plánu.' }
  ],
  showcase: [
    {
      kicker: 'Začiatok za minútu', title: 'Povedz, ako trénuješ',
      text: 'Cieľ, úroveň, vybavenie a ťažkosti. Žiadne nekonečné formuláre: zvyšok urobí motor.',
      points: ['Hypertrofia, sila alebo kondícia', 'Tvoje vybavenie a to, čo musíš šetriť', 'Dĺžka plánu a odľahčovacie týždne podľa teba'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Cieľ a úroveň'), shot('12-wizard-details', 'Vybavenie a ťažkosti')]
    },
    {
      kicker: 'Tvoj týždeň, tvoje pravidlá', title: 'S pevnými dňami aj bez nich',
      text: 'Zvoľ kalendár alebo plynulý cyklus: TRAZO ukáže ďalší tréning a ty ho odcvičíš, keď ti to vyhovuje.',
      points: ['Meškanie sa nestráca a nič sa samo nepreskočí', 'Upozornenie na regeneráciu pred začiatkom', 'Po týždňoch prestávky začneš zľahka'],
      shots: [shot('11-wizard-flexible', 'Bez pevných dní'), shot('14-plan-sequence', 'Ďalší tréning')]
    },
    {
      kicker: 'Trénuj', title: 'Vie, čo si robil(a) naposledy',
      text: 'Každý cvik ukazuje predchádzajúci výsledok a navrhnutú váhu. Zápis série zaberie dve ťuknutia.',
      points: ['Predchádzajúci výsledok a cieľová váha pri každom cviku', 'Zabudovaný časovač pauzy', 'Funguje aj bez signálu'],
      shots: [shot('16-session', 'Detail tréningu'), shot('17-workout', 'Zápis sérií')]
    },
    {
      kicker: 'Prispôsob', title: 'Po každom tréningu lepší plán',
      text: 'Po skončení ti motor povie, čo upravil a prečo. Svoj pokrok máš stále na očiach.',
      points: ['Čo sa zmenilo a z akého dôvodu', 'Osobné rekordy, neprerušené rady a série na sval', 'Celý blok, v ktorom sa váhy ladia týždeň po týždni'],
      premium: true,
      shots: [shot('18-adaptation', 'Takto sa plán prispôsobí'), shot('19-progress', 'Tvoj pokrok'), shot('15-block', 'Tvoj blok')]
    }
  ],
  featuresTitle: 'Všetko na tréning s rozumom',
  features: [
    { title: 'Plánuj po svojom', text: 'Tvor makrocykly, mezocykly a rutiny zadarmo a bez umelých obmedzení.' },
    { title: 'Každý tréning prehľadne', text: 'Série, opakovania, RIR a pauza pri každom cviku, s obrázkom a technikou po ruke.' },
    { title: 'Vymieňaj cviky s rozmyslom', text: 'Alternatívy zodpovedajúce úrovni, vybaveniu a obmedzeniam bez rozpadnutia plánu.' },
    { title: 'Jednoduchý zápis', text: 'Zapíš každú sériu a použi časovač. Funguje aj pri výpadku pripojenia.' },
    { title: 'Katalóg cvikov', text: 'Vyše 140 cvikov s vyhľadávaním, filtrami a kartami s obrázkami.' },
    { title: 'Chránený účet', text: 'Prihlásenie cez Google alebo e-mail, dvojfázové overenie a šifrovanie v zariadení.' }
  ],
  pricingTitle: 'Začni zadarmo. Automatizuj, keď budeš chcieť.',
  pricingLead: 'Ručné plánovanie je zadarmo a bez umelých obmedzení. Premium je pre tých, ktorí chcú, aby TRAZO tvorilo a prispôsobovalo za nich.',
  freeName: 'Zadarmo',
  freePrice: '0 €',
  freePeriod: 'navždy',
  freeTagline: 'Navrhuješ a riadiš ty.',
  freeFeatures: [
    'Ručná tvorba makrocyklov',
    'Ručná tvorba mezocyklov a rutín',
    'Zápis tréningov a história',
    'Kompletný katalóg cvikov s obrázkami'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ mesiac',
  premiumTagline: 'TRAZO stavia za teba a prispôsobuje sa ti.',
  premiumFeatures: [
    'Všetko z bezplatnej verzie',
    'Automatické vytvorenie celého plánu: makrocyklus, fázy a odľahčenia',
    'Výber cvikov podľa profilu, vybavenia a obmedzení',
    'Automatické prispôsobenie váh, sérií a odľahčení podľa tvojej spätnej väzby',
    'Nové bloky postavené na skutočnom pokroku'
  ],
  premiumAnnual: 'alebo 49,99 € ročne (4,17 € mesačne)',
  pricingNote: 'Plánované ceny pri spustení. Ručné plánovanie zostáva zadarmo; automatické generovanie a prispôsobenie sú Premium. Premium má 14-dňovú skúšobnú dobu zadarmo.',
  faqTitle: 'Časté otázky',
  faq: [
    { q: 'Je to umelá inteligencia?', a: 'Nie. Motor je deterministický a stojí na tréningových princípoch: dvojitá progresia, týždenný objem na sval, regenerácia a odľahčenie. Žiadne vymyslené texty ani skrytá náhoda: na tvoje údaje uplatňuje známe pravidlá a každé rozhodnutie vysvetlí.' },
    { q: 'Dá sa používať bez pevných dní?', a: 'Áno. Namiesto kalendára si môžeš zvoliť plynulý cyklus: TRAZO ukáže ďalší tréning a ty ho odcvičíš, kedy chceš. Meškanie sa ukladá so skutočným dátumom, nič sa samo nepreskakuje a táto funkcia je zadarmo.' },
    { q: 'Aký je rozdiel medzi bezplatnou verziou a Premium?', a: 'Zadarmo: tvorba makrocyklov, mezocyklov a rutín, zápis, história a katalóg. Premium: automatické vytvorenie plánu a jeho automatické prispôsobenie podľa spätnej väzby.' },
    { q: 'Čo robí automatické generovanie?', a: 'Z cieľa, úrovne, dní, času, vybavenia a obmedzení zostaví makrocyklus s fázami a odľahčeniami a tréningy každého bloku tvorí podľa toho, čo už máš za sebou.' },
    { q: 'Ako sa plán prispôsobuje?', a: 'Zakaždým, keď zapíšeš tréning (série, váhy, RIR, regenerácia, bolesť), TRAZO upraví ďalšie: zmení váhy, uberie sériu, pridá rezervu alebo zaradí odľahčenie. Vždy povie, čo mení a prečo.' },
    { q: 'Prispôsobí aj plány, ktoré si vytvorím sám?', a: 'Áno: s Premium funguje automatické prispôsobenie aj pri ručných plánoch. Bez Premium sa záznamy ukladajú a plán sa sám nemení.' },
    { q: 'Čo sa stane, keď Premium zruším?', a: 'História a ručné plány ti zostanú. Automaticky vytvorené plány si môžeš prezerať, ale ich ďalšie používanie a nové bloky vyžadujú Premium. Môžeš ich archivovať a vytvoriť ručné.' },
    { q: 'Môžem zrušiť kedykoľvek?', a: 'Áno. Predplatné sa spravuje v Google Play a zrušiť ho môžeš kedykoľvek.' },
    { q: 'Nahrádza TRAZO odborníka?', a: 'Nie. TRAZO ponúka orientačné tréningové plány a nenahrádza radu zdravotníka ani trénera.' }
  ],
  shotAlt: 'Snímka obrazovky aplikácie TRAZO',
  howTitle: 'Ako to funguje',
  steps: [
    { title: 'Povedz, ako trénuješ', text: 'Cieľ, úroveň, voľné dni a vybavenie.' },
    { title: 'Navrhni plán alebo ho vygeneruj', text: 'Zostav ho ručne alebo nechaj TRAZO vytvoriť celý plán s Premium.' },
    { title: 'Trénuj a zapisuj', text: 'Zapisuj tréningy. S Premium sa ďalšie upravia podľa tvojich výsledkov.' }
  ],
  waitTitle: 'Pripravujeme spustenie',
  waitText: 'Nechaj nám e-mail a dáme ti vedieť, keď bude aplikácia na Google Play. Predtým ťa tiež pozveme do uzavretého testu. Na nič iné ho nepoužijeme.',
  emailLabel: 'E-mail tvojho účtu Google',
  emailHint: 'Použi e-mail účtu Google vo svojom telefóne: Google Play ho potrebuje na udelenie prístupu do testu. Pošleme ti správu na jeho potvrdenie.',
  consent: 'Súhlasím, aby TRAZO použilo môj e-mail na oznámenie o spustení a na pozvanie do uzavretého testu na Google Play. Súhlas môžem kedykoľvek odvolať. Viac v',
  privacyLabel: 'zásadách ochrany osobných údajov (po anglicky)',
  send: 'Zapísať ma',
  note: 'TRAZO ponúka orientačné tréningové plány a nenahrádza radu zdravotníka.',
  ok: 'Už len krok: poslali sme ti e-mail. Otvor ho a potvrď adresu.',
  invalid: 'Skontroluj e-mail a zaškrtni súhlas.',
  later: 'Príliš veľa pokusov. Skús to o chvíľu.',
  fail: 'E-mail sa nepodarilo uložiť. Skús to o pár minút.',
  terms: 'Podmienky (po anglicky)', contact: 'Kontakt', admin: 'Prihlásenie správcu', skip: 'Prejsť na obsah',
  waitlistPages: {
    confirm: {
      title: 'Potvrď svoj e-mail',
      text: 'Stlačením tlačidla potvrdíš, že chceš byť v zozname TRAZO: dáme vedieť o spustení a pozveme ťa aplikáciu vyskúšať.',
      button: 'Potvrdiť e-mail',
      done: 'E-mail potvrdený',
      doneText: 'Ďakujeme. Ozveme sa, keď budú novinky o teste a spustení.'
    },
    unsubscribe: {
      title: 'Odhlásiť sa zo zoznamu',
      text: 'Stlačením tlačidla zmažeme tvoj e-mail zo zoznamu TRAZO a už ti nebudeme písať.',
      button: 'Zmazať moje údaje',
      done: 'Zmazané',
      doneText: 'Tvoj e-mail sme zo zoznamu zmazali. Ďalšie správy nedostaneš.'
    },
    failed: 'Neplatný alebo prepadnutý odkaz',
    failedText: 'Odkaz je neplatný, vypršal alebo už bol použitý. Ak sa chceš prihlásiť, vyplň formulár znova.',
    missing: 'Neúplný odkaz',
    missingText: 'V odkaze chýba potvrdzovací kód. Použi odkaz z e-mailu presne tak, ako je.',
    wait: 'Okamih…',
    home: 'Prejsť na TRAZO'
  }
};

export default content;
