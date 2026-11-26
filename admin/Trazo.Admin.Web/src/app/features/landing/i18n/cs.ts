import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('cs');

const content: LandingContent = {
  title: 'TRAZO · Silový motor, který se ti přizpůsobí',
  description: 'TRAZO není šablona: je to motor, který sestaví tvůj makrocyklus, po každém tréninku upraví váhy a série podle tvojí odezvy a každou změnu vysvětlí. Ruční plánování je zdarma.',
  languageLabel: 'Jazyk',
  eyebrow: 'Adaptivní tréninkový motor',
  headA: 'Tvůj plán není šablona.', headB: 'Je to motor, který se ti přizpůsobí',
  lead: 'TRAZO sestaví celý makrocyklus, po každém tréninku upraví váhy a série podle tvojí odezvy a každou změnu vysvětlí. S pevnými dny i bez nich.',
  cta: 'Zapsat se na čekací listinu',
  loopTitle: 'Smyčka, která se uzavírá každým tréninkem',
  loopLead: 'Ruční plánování je zdarma. Automatické generování a přizpůsobení jsou Premium.',
  loop: [
    { title: 'Vytvoří', text: 'Cíl, úroveň, vybavení a potíže. Motor navrhne makrocyklus: fáze, odlehčení, cviky a objem na sval.' },
    { title: 'Trénuj', text: 'Zapisuješ série, váhy a RIR. Vidíš, co jsi dělal(a) minule, a váhu na dnešek.' },
    { title: 'Naslouchá', text: 'Po skončení řekneš, jak to jde: regenerace, úsilí, bolest a jednotlivé svaly.' },
    { title: 'Přizpůsobí', text: 'Zvýší váhu, ubere nebo přidá série, zařadí odlehčení. Každý nový blok vychází z toho, co jsi opravdu zvedl(a).' }
  ],
  pillarsTitle: 'Čím se motor liší',
  pillars: [
    { title: 'Dvojitá progrese u každého cviku', text: 'Nejdřív opakování, potom váha: když s rezervou dosáhneš horní hranice rozsahu, postoupíš o stupeň. Pokud ještě ne, váha zůstává.' },
    { title: 'Objem na sval', text: 'Série se rozdělují k týdennímu objemu, který každý sval potřebuje podle tvojí úrovně a času.' },
    { title: 'Regenerace po svalech', text: 'Než navrhne další trénink, ověří, že svaly, které budou pracovat, jsou zregenerované.', premium: true },
    { title: 'Odlehčení, když je potřeba', text: 'Plánované lehké týdny a mimořádné odlehčení, pokud se hromadí únava nebo klesá výkon.' },
    { title: 'Rozhoduje bolest', text: 'Nahlásíš-li bolest, váha se nikdy nezvýší a nic se neděje automaticky: dostaneš upozornění, ať to prověříš. A potíže s klouby vyřadí cviky, které je zhoršují.' },
    { title: 'Každá změna vysvětlená', text: 'Žádná černá skříňka: každá úprava má své zdůvodnění a ukládá se do historie plánu.' }
  ],
  showcase: [
    {
      kicker: 'Začátek za minutu', title: 'Řekni, jak trénuješ',
      text: 'Cíl, úroveň, vybavení a potíže. Žádné nekonečné formuláře: zbytek udělá motor.',
      points: ['Hypertrofie, síla nebo kondice', 'Tvoje vybavení a to, co musíš šetřit', 'Délka plánu a odlehčovací týdny podle tebe'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Cíl a úroveň'), shot('12-wizard-details', 'Vybavení a potíže')]
    },
    {
      kicker: 'Tvůj týden, tvoje pravidla', title: 'S pevnými dny i bez nich',
      text: 'Zvol kalendář nebo plynulý cyklus: TRAZO ukáže další trénink a ty jej odcvičíš, kdy ti to vyhovuje.',
      points: ['Zpoždění se neztrácí a nic se samo nepřeskočí', 'Upozornění na regeneraci před začátkem', 'Po týdnech pauzy začneš zvolna'],
      shots: [shot('11-wizard-flexible', 'Bez pevných dnů'), shot('14-plan-sequence', 'Další trénink')]
    },
    {
      kicker: 'Trénuj', title: 'Ví, co jsi dělal(a) minule',
      text: 'Každý cvik ukazuje předchozí výsledek a navrženou váhu. Zápis série zabere dvě klepnutí.',
      points: ['Předchozí výsledek a cílová váha u každého cviku', 'Zabudovaný časovač pauzy', 'Funguje i bez signálu'],
      shots: [shot('16-session', 'Detail tréninku'), shot('17-workout', 'Zápis sérií')]
    },
    {
      kicker: 'Přizpůsob', title: 'Po každém tréninku lepší plán',
      text: 'Po skončení ti motor řekne, co upravil a proč. Svůj pokrok máš pořád na očích.',
      points: ['Co se změnilo a z jakého důvodu', 'Osobní rekordy, série bez přerušení a série na sval', 'Celý blok, v němž se váhy ladí týden po týdnu'],
      premium: true,
      shots: [shot('18-adaptation', 'Takto se plán přizpůsobí'), shot('19-progress', 'Tvůj pokrok'), shot('15-block', 'Tvůj blok')]
    }
  ],
  featuresTitle: 'Vše pro trénink s rozumem',
  features: [
    { title: 'Plánuj po svém', text: 'Tvoř makrocykly, mezocykly a rutiny zdarma a bez umělých omezení.' },
    { title: 'Každý trénink přehledně', text: 'Série, opakování, RIR a pauza u každého cviku, s obrázkem a technikou po ruce.' },
    { title: 'Vyměňuj cviky s rozmyslem', text: 'Alternativy odpovídající úrovni, vybavení a omezením, aniž by se plán rozpadl.' },
    { title: 'Snadný zápis', text: 'Zapiš každou sérii a použij časovač. Funguje i při výpadku připojení.' },
    { title: 'Katalog cviků', text: 'Přes 140 cviků s vyhledáváním, filtry a kartami s obrázky.' },
    { title: 'Chráněný účet', text: 'Přihlášení přes Google nebo e-mail, dvoufázové ověření a šifrování v zařízení.' }
  ],
  pricingTitle: 'Začni zdarma. Automatizuj, až budeš chtít.',
  pricingLead: 'Ruční plánování je zdarma a bez umělých omezení. Premium je pro ty, kdo chtějí, aby TRAZO tvořilo a přizpůsobovalo za ně.',
  freeName: 'Zdarma',
  freePrice: '0 €',
  freePeriod: 'navždy',
  freeTagline: 'Navrhuješ a řídíš ty.',
  freeFeatures: [
    'Ruční tvorba makrocyklů',
    'Ruční tvorba mezocyklů a rutin',
    'Zápis tréninků a historie',
    'Kompletní katalog cviků s obrázky'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ měsíc',
  premiumTagline: 'TRAZO staví za tebe a přizpůsobuje se ti.',
  premiumFeatures: [
    'Vše z verze zdarma',
    'Automatické vytvoření celého plánu: makrocyklus, fáze a odlehčení',
    'Výběr cviků podle profilu, vybavení a omezení',
    'Automatické přizpůsobení vah, sérií a odlehčení podle tvojí zpětné vazby',
    'Nové bloky postavené na skutečném pokroku'
  ],
  premiumAnnual: 'nebo 49,99 € ročně (4,17 € měsíčně)',
  pricingNote: 'Plánované ceny při spuštění. Ruční plánování zůstává zdarma; automatické generování a přizpůsobení jsou Premium. Premium má 14denní zkušební dobu zdarma.',
  faqTitle: 'Časté otázky',
  faq: [
    { q: 'Je to umělá inteligence?', a: 'Ne. Motor je deterministický a stojí na tréninkových principech: dvojitá progrese, týdenní objem na sval, regenerace a odlehčení. Žádné vymyšlené texty ani skrytá náhoda: na tvoje data uplatňuje známá pravidla a každé rozhodnutí vysvětlí.' },
    { q: 'Dá se používat bez pevných dnů?', a: 'Ano. Místo kalendáře můžeš zvolit plynulý cyklus: TRAZO ukáže další trénink a ty jej odcvičíš, kdy chceš. Zpoždění se ukládají se skutečným datem, nic se samo nepřeskakuje a tato funkce je zdarma.' },
    { q: 'Jaký je rozdíl mezi verzí zdarma a Premium?', a: 'Zdarma: tvorba makrocyklů, mezocyklů a rutin, zápis, historie a katalog. Premium: automatické vytvoření plánu a jeho automatické přizpůsobení podle zpětné vazby.' },
    { q: 'Co dělá automatické generování?', a: 'Z cíle, úrovně, dnů, času, vybavení a omezení sestaví makrocyklus s fázemi a odlehčeními a tréninky každého bloku tvoří podle toho, co už máš za sebou.' },
    { q: 'Jak se plán přizpůsobuje?', a: 'Pokaždé, když zapíšeš trénink (série, váhy, RIR, regenerace, bolest), TRAZO upraví další: změní váhy, ubere sérii, přidá rezervu nebo zařadí odlehčení. Vždy řekne, co mění a proč.' },
    { q: 'Přizpůsobí i plány, které si vytvořím sám?', a: 'Ano: s Premium funguje automatické přizpůsobení i u ručních plánů. Bez Premium se záznamy ukládají a plán se sám nemění.' },
    { q: 'Co se stane, když Premium zruším?', a: 'Historie a ruční plány ti zůstanou. Automaticky vytvořené plány si můžeš prohlížet, ale jejich další používání a nové bloky vyžadují Premium. Můžeš je archivovat a vytvořit ruční.' },
    { q: 'Mohu zrušit kdykoli?', a: 'Ano. Předplatné se spravuje v Google Play a zrušit jej můžeš kdykoli.' },
    { q: 'Nahrazuje TRAZO odborníka?', a: 'Ne. TRAZO nabízí orientační tréninkové plány a nenahrazuje radu zdravotníka ani trenéra.' }
  ],
  shotAlt: 'Snímek obrazovky aplikace TRAZO',
  howTitle: 'Jak to funguje',
  steps: [
    { title: 'Řekni, jak trénuješ', text: 'Cíl, úroveň, volné dny a vybavení.' },
    { title: 'Navrhni plán, nebo jej vygeneruj', text: 'Sestav jej ručně, nebo nech TRAZO vytvořit celý plán s Premium.' },
    { title: 'Trénuj a zapisuj', text: 'Zapisuj tréninky. S Premium se další upraví podle tvých výsledků.' }
  ],
  waitTitle: 'Připravujeme spuštění',
  waitText: 'Nech nám e-mail a dáme ti vědět, až bude aplikace na Google Play. Předtím tě také pozveme do uzavřeného testu. K ničemu jinému jej nepoužijeme.',
  emailLabel: 'E-mail tvého účtu Google',
  emailHint: 'Použij e-mail účtu Google ve svém telefonu: Google Play jej potřebuje k udělení přístupu do testu. Pošleme ti zprávu k jeho potvrzení.',
  consent: 'Souhlasím, aby TRAZO použilo můj e-mail k oznámení o spuštění a k pozvání do uzavřeného testu na Google Play. Souhlas mohu kdykoli odvolat. Více v',
  privacyLabel: 'zásadách ochrany osobních údajů (anglicky)',
  send: 'Zapsat mě',
  note: 'TRAZO nabízí orientační tréninkové plány a nenahrazuje radu zdravotníka.',
  ok: 'Už jen krok: poslali jsme ti e-mail. Otevři jej a potvrď adresu.',
  invalid: 'Zkontroluj e-mail a zaškrtni souhlas.',
  later: 'Příliš mnoho pokusů. Zkus to za chvíli.',
  fail: 'E-mail se nepodařilo uložit. Zkus to za pár minut.',
  terms: 'Podmínky (anglicky)', contact: 'Kontakt', admin: 'Přihlášení správce', skip: 'Přejít na obsah',
  waitlistPages: {
    confirm: {
      title: 'Potvrď svůj e-mail',
      text: 'Stisknutím tlačítka potvrdíš, že chceš být na seznamu TRAZO: dáme vědět o spuštění a pozveme tě aplikaci vyzkoušet.',
      button: 'Potvrdit e-mail',
      done: 'E-mail potvrzen',
      doneText: 'Děkujeme. Ozveme se, až budou novinky o testu a spuštění.'
    },
    unsubscribe: {
      title: 'Odhlásit se ze seznamu',
      text: 'Stisknutím tlačítka smažeme tvůj e-mail ze seznamu TRAZO a už ti nebudeme psát.',
      button: 'Smazat mé údaje',
      done: 'Smazáno',
      doneText: 'Tvůj e-mail jsme ze seznamu smazali. Další zprávy nedostaneš.'
    },
    failed: 'Neplatný nebo prošlý odkaz',
    failedText: 'Odkaz je neplatný, vypršel nebo už byl použit. Chceš-li se přihlásit, vyplň formulář znovu.',
    missing: 'Neúplný odkaz',
    missingText: 'V odkazu chybí potvrzovací kód. Použij odkaz z e-mailu přesně tak, jak je.',
    wait: 'Okamžik…',
    home: 'Přejít na TRAZO'
  }
};

export default content;
