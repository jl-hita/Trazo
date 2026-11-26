import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('sl');

const content: LandingContent = {
  title: 'TRAZO · Motor za vadbo moči, ki se prilagaja ti',
  description: 'TRAZO ni predloga: je motor, ki ustvari tvoj makrocikel, po vsakem treningu prilagodi teže in serije glede na tvoj odziv in razloži vsako spremembo. Ročno načrtovanje je brezplačno.',
  languageLabel: 'Jezik',
  eyebrow: 'Prilagodljivi vadbeni motor',
  headA: 'Tvoj načrt ni predloga.', headB: 'Je motor, ki se prilagaja ti',
  lead: 'TRAZO sestavi celoten makrocikel, po vsakem treningu prilagodi teže in serije glede na tvoj odziv in razloži vsako spremembo. S stalnimi dnevi ali brez njih.',
  cta: 'Pridruži se čakalni vrsti',
  loopTitle: 'Zanka, ki se sklene z vsakim treningom',
  loopLead: 'Ročno načrtovanje je brezplačno. Samodejno ustvarjanje in prilagajanje sta Premium.',
  loop: [
    { title: 'Ustvari', text: 'Cilj, raven, oprema in težave. Motor zasnuje makrocikel: faze, razbremenitve, vaje in obseg po mišicah.' },
    { title: 'Treniraj', text: 'Beleži serije, teže in RIR. Vidiš, kaj si naredil zadnjič, in težo za danes.' },
    { title: 'Posluša', text: 'Ko končaš, poveš, kako gre: regeneracija, napor, bolečina in posamezne mišice.' },
    { title: 'Prilagodi', text: 'Poveča težo, odvzame ali doda serije, umesti razbremenitev. Vsak nov blok izhaja iz tega, kar si res dvignil.' }
  ],
  pillarsTitle: 'Po čem se motor razlikuje',
  pillars: [
    { title: 'Dvojno napredovanje pri vsaki vaji', text: 'Najprej ponovitve, nato teža: ko z rezervo dosežeš zgornjo mejo razpona, napreduješ za stopnjo. Če še ni čas, teža ostane.' },
    { title: 'Obseg po mišicah', text: 'Serije se razporedijo k tedenskemu obsegu, ki ga vsaka mišica potrebuje glede na tvojo raven in čas.' },
    { title: 'Regeneracija po mišicah', text: 'Preden predlaga naslednji trening, preveri, ali so mišice, ki bodo delale, regenerirane.', premium: true },
    { title: 'Razbremenitev, ko je potrebna', text: 'Načrtovani lažji tedni in izredna razbremenitev, če se kopiči utrujenost ali pada zmogljivost.' },
    { title: 'Odloča bolečina', text: 'Če sporočiš bolečino, se teža nikoli ne poveča in nič se ne zgodi samodejno: dobiš opozorilo, da to preveriš. Težave s sklepi izločijo vaje, ki jih poslabšajo.' },
    { title: 'Vsaka sprememba razložena', text: 'Brez črne skrinjice: vsaka prilagoditev ima razlago in se shrani v zgodovino načrta.' }
  ],
  showcase: [
    {
      kicker: 'Začetek v minuti', title: 'Povej, kako treniraš',
      text: 'Cilj, raven, oprema in težave. Brez neskončnih obrazcev: ostalo opravi motor.',
      points: ['Hipertrofija, moč ali kondicija', 'Tvoja oprema in to, kar moraš varovati', 'Trajanje načrta in tedni razbremenitve po tvoji izbiri'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Cilj in raven'), shot('12-wizard-details', 'Oprema in težave')]
    },
    {
      kicker: 'Tvoj teden, tvoja pravila', title: 'S stalnimi dnevi ali brez njih',
      text: 'Izberi koledar ali tekoči cikel: TRAZO pokaže naslednji trening, ti pa ga opravi, ko ti ustreza.',
      points: ['Zamude se ne izgubijo in nič se ne preskoči samo', 'Opozorilo o regeneraciji pred začetkom', 'Po tednih premora začneš zložneje'],
      shots: [shot('11-wizard-flexible', 'Brez stalnih dni'), shot('14-plan-sequence', 'Naslednji trening')]
    },
    {
      kicker: 'Treniraj', title: 'Ve, kaj si naredil zadnjič',
      text: 'Vsaka vaja pokaže prejšnji rezultat in predlagano težo. Beleženje serije sta dva dotika.',
      points: ['Prejšnji rezultat in ciljna teža pri vsaki vaji', 'Vgrajen časovnik počitka', 'Deluje tudi brez signala'],
      shots: [shot('16-session', 'Podrobnosti treninga'), shot('17-workout', 'Beleženje serij')]
    },
    {
      kicker: 'Prilagodi', title: 'Po vsakem treningu boljši načrt',
      text: 'Ko končaš, ti motor pove, kaj je prilagodil in zakaj. Svoj napredek imaš vedno pred očmi.',
      points: ['Kaj se je spremenilo in zakaj', 'Osebni rekordi, nizi in serije po mišicah', 'Celoten blok, v katerem se teže pilijo iz tedna v teden'],
      premium: true,
      shots: [shot('18-adaptation', 'Tako se načrt prilagodi'), shot('19-progress', 'Tvoj napredek'), shot('15-block', 'Tvoj blok')]
    }
  ],
  featuresTitle: 'Vse za premišljeno vadbo',
  features: [
    { title: 'Načrtuj po svoje', text: 'Ustvarjaj makrocikle, mezocikle in rutine brezplačno in brez umetnih omejitev.' },
    { title: 'Vsak trening pregledno', text: 'Serije, ponovitve, RIR in počitek pri vsaki vaji, s sliko in tehniko pri roki.' },
    { title: 'Premišljene zamenjave vaj', text: 'Alternative, skladne z ravnjo, opremo in omejitvami, ne da bi se načrt podrl.' },
    { title: 'Preprosto beleženje', text: 'Zabeleži vsako serijo in uporabi časovnik. Deluje tudi ob izpadu povezave.' },
    { title: 'Katalog vaj', text: 'Več kot 140 vaj z iskanjem, filtri in karticami s slikami.' },
    { title: 'Zaščiten račun', text: 'Prijava z Google ali e-pošto, dvostopenjsko preverjanje in šifriranje v napravi.' }
  ],
  pricingTitle: 'Začni brezplačno. Avtomatiziraj, ko boš želel.',
  pricingLead: 'Ročno načrtovanje je brezplačno in brez umetnih omejitev. Premium je za tiste, ki želijo, da TRAZO ustvarja in prilagaja namesto njih.',
  freeName: 'Brezplačno',
  freePrice: '0 €',
  freePeriod: 'za vedno',
  freeTagline: 'Ti zasnuješ in vodi.',
  freeFeatures: [
    'Ročno ustvarjanje makrociklov',
    'Ročno ustvarjanje mezociklov in rutin',
    'Beleženje treningov in zgodovina',
    'Celoten katalog vaj s slikami'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ mesec',
  premiumTagline: 'TRAZO gradi namesto te in se ti prilagaja.',
  premiumFeatures: [
    'Vse iz brezplačne različice',
    'Samodejno ustvarjanje celotnega načrta: makrocikel, faze in razbremenitve',
    'Izbira vaj po profilu, opremi in omejitvah',
    'Samodejno prilagajanje tež, serij in razbremenitev po tvojih povratnih informacijah',
    'Novi bloki, zgrajeni na dejanskem napredku'
  ],
  premiumAnnual: 'ali 49,99 € na leto (4,17 € na mesec)',
  pricingNote: 'Predvidene cene ob izidu. Ročno načrtovanje ostane brezplačno; samodejno ustvarjanje in prilagajanje sta Premium. Premium ima 14-dnevno brezplačno preizkusno obdobje.',
  faqTitle: 'Pogosta vprašanja',
  faq: [
    { q: 'Je to umetna inteligenca?', a: 'Ne. Motor je determinističen in temelji na načelih vadbe: dvojno napredovanje, tedenski obseg po mišicah, regeneracija in razbremenitev. Brez izmišljenih besedil in skritih naključij: na tvoje podatke uporabi znana pravila in vsako odločitev razloži.' },
    { q: 'Ga lahko uporabljam brez stalnih dni?', a: 'Da. Namesto koledarja lahko izbereš tekoči cikel: TRAZO pokaže naslednji trening, ti pa ga opravi, ko želiš. Zamude se zabeležijo z dejanskim datumom, nič se ne preskoči samo, ta možnost pa je brezplačna.' },
    { q: 'Kakšna je razlika med brezplačno različico in Premium?', a: 'Brezplačno: ustvarjanje makrociklov, mezociklov in rutin, beleženje, zgodovina in katalog. Premium: samodejno ustvarjanje načrta in samodejno prilagajanje po tvojih povratnih informacijah.' },
    { q: 'Kaj naredi samodejno ustvarjanje?', a: 'Iz cilja, ravni, dni, časa, opreme in omejitev sestavi makrocikel s fazami in razbremenitvami, treninge vsakega bloka pa ustvari na podlagi že opravljenega.' },
    { q: 'Kako se načrt prilagaja?', a: 'Vsakič, ko zabeležiš trening (serije, teže, RIR, regeneracija, bolečina), TRAZO prilagodi naslednje: spremeni teže, odvzame serijo, doda rezervo ali umesti razbremenitev. Vedno pove, kaj spreminja in zakaj.' },
    { q: 'Ali prilagaja tudi načrte, ki jih ustvarim sam?', a: 'Da: s Premium samodejno prilagajanje deluje tudi pri ročnih načrtih. Brez Premium se zapisi shranijo in načrt se sam ne spreminja.' },
    { q: 'Kaj se zgodi, če prekličem Premium?', a: 'Zgodovina in ročni načrti ostanejo. Samodejno ustvarjene načrte si lahko ogledaš, za nadaljnjo uporabo in nove bloke pa potrebuješ Premium. Lahko jih arhiviraš in ustvari ročne.' },
    { q: 'Lahko prekličem kadar koli?', a: 'Da. Naročnino upravljaš v Google Play in jo lahko prekličeš kadar koli.' },
    { q: 'Ali TRAZO nadomešča strokovnjaka?', a: 'Ne. TRAZO ponuja okvirne načrte vadbe in ne nadomešča nasveta zdravstvenega delavca ali trenerja.' }
  ],
  shotAlt: 'Posnetek zaslona aplikacije TRAZO',
  howTitle: 'Kako deluje',
  steps: [
    { title: 'Povej, kako treniraš', text: 'Cilj, raven, prosti dnevi in oprema.' },
    { title: 'Zasnuj načrt ali ga ustvari', text: 'Sestavi ga ročno ali pusti, da TRAZO s Premium ustvari celoten načrt.' },
    { title: 'Treniraj in beleži', text: 'Beleži treninge. S Premium se naslednji prilagodijo tvojim rezultatom.' }
  ],
  waitTitle: 'Pripravljamo izid',
  waitText: 'Pusti nam e-naslov in sporočili ti bomo, ko bo aplikacija v Google Play. Pred tem te bomo povabili tudi v zaprto testiranje. Za nič drugega ga ne bomo uporabili.',
  emailLabel: 'E-naslov tvojega računa Google',
  emailHint: 'Uporabi e-naslov računa Google v svojem telefonu: Google Play ga potrebuje za dostop do testiranja. Poslali ti bomo sporočilo za potrditev.',
  consent: 'Soglašam, da TRAZO uporabi moj e-naslov za obvestilo o izidu in povabilo v zaprto testiranje v Google Play. Soglasje lahko kadar koli prekličem. Več v',
  privacyLabel: 'pravilniku o zasebnosti (v angleščini)',
  send: 'Dodaj me',
  note: 'TRAZO ponuja okvirne načrte vadbe in ne nadomešča nasveta zdravstvenega delavca.',
  ok: 'Še en korak: poslali smo ti sporočilo. Odpri ga in potrdi naslov.',
  invalid: 'Preveri e-naslov in označi soglasje.',
  later: 'Preveč poskusov. Poskusi čez nekaj časa.',
  fail: 'E-naslova ni bilo mogoče shraniti. Poskusi čez nekaj minut.',
  terms: 'Pogoji (v angleščini)', contact: 'Stik', admin: 'Prijava skrbnika', skip: 'Preskoči na vsebino',
  waitlistPages: {
    confirm: {
      title: 'Potrdi svoj e-naslov',
      text: 'S pritiskom na gumb potrdi, da želiš biti na seznamu TRAZO: sporočili ti bomo o izidu in te povabili k preizkusu aplikacije.',
      button: 'Potrdi e-naslov',
      done: 'E-naslov potrjen',
      doneText: 'Hvala. Pisali ti bomo, ko bodo novice o testiranju in izidu.'
    },
    unsubscribe: {
      title: 'Odjava s seznama',
      text: 'S pritiskom na gumb izbrišemo tvoj e-naslov s seznama TRAZO in ti ne bomo več pisali.',
      button: 'Izbriši moje podatke',
      done: 'Izbrisano',
      doneText: 'Tvoj e-naslov smo izbrisali s seznama. Sporočil ne boš več prejemal.'
    },
    failed: 'Neveljavna ali potekla povezava',
    failedText: 'Povezava ni veljavna, je potekla ali je že bila uporabljena. Za prijavo znova izpolni obrazec.',
    missing: 'Nepopolna povezava',
    missingText: 'V povezavi manjka potrditvena koda. Uporabi povezavo iz sporočila natanko takšno, kot je.',
    wait: 'Trenutek…',
    home: 'Na TRAZO'
  }
};

export default content;
