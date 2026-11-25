import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('eu');

const content: LandingContent = {
  title: 'TRAZO · Zure arabera egokitzen den indar-entrenamenduko motorra',
  description: 'TRAZO ez da txantiloi bat: zure makrozikloa sortzen duen, kargak eta seriak saioz saio zure erantzunaren arabera doitzen dituen eta aldaketa bakoitza azaltzen dizun motorra da. Eskuz planifikatzea doan da.',
  languageLabel: 'Hizkuntza',
  eyebrow: 'ENTRENAMENDU-MOTOR EGOKITZAILEA',
  headA: 'Zure plana ez da txantiloi bat.', headB: 'Zure arabera egokitzen den motorra da',
  lead: 'TRAZOk zure makroziklo osoa eraikitzen du, kargak eta seriak saioz saio zure erantzunaren arabera doitzen ditu eta aldaketa bakoitza azaltzen dizu. Egun finkoekin edo gabe.',
  cta: 'Itxaron-zerrendan sartu nahi dut',
  loopTitle: 'Saio bakoitzean ixten den zikloa',
  loopLead: 'Eskuz planifikatzea doan da. Sorrera eta egokitzapen automatikoak Premium dira.',
  loop: [
    { title: 'Sortu', text: 'Helburua, maila, ekipamendua eta ondoezak. Motorrak makrozikloa diseinatzen du: faseak, arintzeak, ariketak eta bolumena muskuluka.' },
    { title: 'Entrenatu', text: 'Seriak, kargak eta RIRa erregistratzen dituzu. Azken aldian zer egin zenuen eta orain dagokion karga ikusten duzu.' },
    { title: 'Entzun', text: 'Amaitzean nola zoazen kontatzen duzu: errekuperazioa, ahalegina, mina eta muskulu bakoitza nola joan den.' },
    { title: 'Egokitu', text: 'Karga igotzen du, seriak kentzen edo gehitzen ditu, arintze bat programatzen du. Bloke berri bakoitza benetan altxatu zenuenetik abiatzen da.' }
  ],
  pillarsTitle: 'Motor hau desberdin egiten duena',
  pillars: [
    { title: 'Progresio bikoitza ariketaka', text: 'Lehenik errepikapenak, gero karga: tartearen goiko mugara marjinarekin iristen zarenean, maila bat igotzen du. Oraindik ez badagokio, mantendu egiten da.' },
    { title: 'Bolumena muskuluka', text: 'Seriak muskulu bakoitzak behar duen asteko bolumenerantz banatzen dira, zure mailaren eta duzun denboraren arabera.' },
    { title: 'Errekuperazioa muskuluka', text: 'Hurrengo saioa proposatu aurretik, landuko dituen muskuluak errekuperatu diren egiaztatzen du.', premium: true },
    { title: 'Arintzeak behar denean', text: 'Programatutako aste arinak eta arintze gehigarri bat neke metatua edo errendimendu beherakorra agertzen bada.' },
    { title: 'Minak agintzen du', text: 'Mina adierazten baduzu, ez du inoiz karga igotzen ezta ezer automatizatzen ere: abisatu egiten dizu berrikus dezazun. Eta zure artikulazioetako ondoezek okerrera eragiten dieten ariketak kanpoan uzten dituzte.' },
    { title: 'Aldaketa bakoitza, azalduta', text: 'Ez da kutxa beltz bat: doikuntza bakoitza bere arrazoiarekin iristen da eta planaren historian geratzen da.' }
  ],
  showcase: [
    {
      kicker: 'Hasi minutu batean', title: 'Kontatu nola entrenatzen duzun',
      text: 'Helburua, maila, ekipamendua eta ondoezak. Amaigabeko galdetegirik gabe: motorrak egiten du gainerakoa.',
      points: ['Hipertrofia, indarra edo forma orokorra', 'Duzun ekipamendua eta babestu beharreko eremuak', 'Planaren iraupena eta arintze-asteak zure gustura'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Helburua eta maila'), shot('12-wizard-details', 'Ekipamendua eta ondoezak')]
    },
    {
      kicker: 'Zure astea, zure arauak', title: 'Egun finkoekin edo gabe',
      text: 'Aukeratu egutegi bat edo etengabeko ziklo bat. TRAZOk hurrengo saioa zein den esaten dizu eta zuk ahal duzunean entrenatzen duzu.',
      points: ['Atzeratutako saioak ez dira galtzen ezta bere kabuz saltatzen ere', 'Errekuperazio-abisua hasi aurretik', 'Itzulera-saio arinagoa asteak pasa ondoren bazatoz'],
      shots: [shot('11-wizard-flexible', 'Egun finkorik gabe'), shot('14-plan-sequence', 'Hurrengo saioa')]
    },
    {
      kicker: 'Entrenatu', title: 'Badaki azken aldian zer egin zenuen',
      text: 'Ariketa bakoitzak zure aurreko errendimendua eta proposatutako karga erakusten ditu. Seria bat erregistratzea bi ukitu dira.',
      points: ['Azken aldia eta helburu-karga ariketa bakoitzean', 'Atseden-tenporizadore integratua', 'Konexioa galdu arren funtzionatzen du'],
      shots: [shot('16-session', 'Saioa xehetasunez'), shot('17-workout', 'Serien erregistroa')]
    },
    {
      kicker: 'Egokitu', title: 'Saio bakoitzaren ondoren, plan hobea',
      text: 'Amaitu saioa eta motorrak zer doitu duen eta zergatik esaten dizu. Zure aurrerapena, beti agerian.',
      points: ['Zer aldatu den eta aldaketa bakoitzaren arrazoia', 'Errekorrak, bolada eta seriak muskuluka', 'Bloke osoa, kargak astez aste finduta'],
      premium: true,
      shots: [shot('18-adaptation', 'Horrela egokitzen da zure plana'), shot('19-progress', 'Zure aurrerapena'), shot('15-block', 'Zure blokea')]
    }
  ],
  featuresTitle: 'Irizpidez entrenatzeko behar duzun guztia',
  features: [
    { title: 'Planifikatu zure erara', text: 'Sortu makrozikloak, mesozikloak eta errutinak, doan eta muga artifizialik gabe.' },
    { title: 'Saio bakoitza, argi', text: 'Ariketa bakoitzaren seriak, errepikapenak, RIRa eta atsedena, irudia eta teknika eskura.' },
    { title: 'Aldatu ariketak irizpidez', text: 'Zure mailarako, ekipamendurako eta mugetarako balio duten alternatibak, plana hautsi gabe.' },
    { title: 'Erregistro erraza', text: 'Idatzi seria bakoitza eta erabili tenporizadorea. Konexioa galdu arren funtzionatzen du.' },
    { title: 'Ariketa-katalogoa', text: '140 ariketa baino gehiago bilaketarekin, iragazkiekin eta irudidun fitxekin.' },
    { title: 'Zure kontua, babestuta', text: 'Sarbidea Google edo posta bidez, bi urratseko egiaztapena eta datu zifratuak gailuan.' }
  ],
  pricingTitle: 'Hasi doan. Automatizatu nahi duzunean.',
  pricingLead: 'Eskuzko planifikazioa doan eta muga artifizialik gabe dago. Premium TRAZOk entrenamendua programatu eta egokitzea nahi duenarentzat da.',
  freeName: 'Doan',
  freePrice: '0 €',
  freePeriod: 'betiko',
  freeTagline: 'Zuk diseinatzen eta kontrolatzen duzu entrenamendua.',
  freeFeatures: [
    'Makrozikloak eskuz sortzea',
    'Mesozikloak eta errutinak eskuz sortzea',
    'Entrenamenduak erregistratzea eta historia kontsultatzea',
    'Irudidun ariketa-katalogo osoa'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ hilean',
  premiumTagline: 'TRAZOk zure ordez programatzen du eta zure arabera egokitzen da.',
  premiumFeatures: [
    'Doako planeko guztia',
    'Plan osoaren sorrera automatikoa: makrozikloa, faseak eta arintzeak',
    'Zure profil, ekipamendu eta mugen arabera aukeratutako ariketak',
    'Kargen, serien eta arintzeen egokitzapen automatikoa zure erantzunen arabera',
    'Bloke berri bakoitza, zure benetako aurrerapenetik sortua'
  ],
  premiumAnnual: 'Edo 49,99 € urtean · 4,17 €/hilean',
  pricingNote: 'Abiarazterako aurreikusitako prezioak. Eskuzko planifikazioak doan izaten jarraituko du; sorrera eta egokitzapen automatikoak Premium izango dira. Premiumek 14 eguneko doako proba dakar.',
  faqTitle: 'Ohiko galderak',
  faq: [
    { q: 'Adimen artifiziala da?', a: 'Ez. Motorra determinista da eta entrenamendu-arauetan oinarritzen da: progresio bikoitza, asteko bolumena muskuluka, errekuperazioa eta arintzeak. Ez dago asmatutako testurik ez ezkutuko zoririk: arau ezagunak aplikatzen dizkie zure datuei eta erabaki bakoitza azalduta iristen da.' },
    { q: 'Entrena dezaket egun finkorik gabe?', a: 'Bai. Egutegi baten ordez etengabeko ziklo bat aukera dezakezu: TRAZOk hurrengo saioa esaten dizu eta ahal duzunean entrenatzen duzu. Atzeratutako saioak benetako datarekin erregistratzen dira eta inoiz ez dira bere kabuz saltatzen. Aukera hau doan da.' },
    { q: 'Zer da doan eta zer Premium?', a: 'Doan: makrozikloak, mesozikloak eta errutinak sortzea, entrenamenduak erregistratzea, historia kontsultatzea eta katalogoa erabiltzea. Premium: planaren sorrera automatikoa eta zure erantzunen araberako egokitzapen automatikoa.' },
    { q: 'Zer egiten du zehazki sorrera automatikoak?', a: 'Zure helburu, maila, egun, denbora, ekipamendu eta mugetatik abiatuta, TRAZOk faseak eta arintzeak dituen makroziklo bat eraikitzen du eta bloke bakoitzeko saioak jada entrenatu duzunetik sortzen ditu.' },
    { q: 'Nola egokitzen da nire plana?', a: 'Saio bat erregistratzen duzun bakoitzean (seriak, kargak, RIRa, errekuperazioa eta mina), TRAZOk hurrengoak doitzen ditu: karga aldatzen du, seriak murrizten, marjina gehitzen edo arintze bat programatzen. Beti azaltzen dizu zer eta zergatik.' },
    { q: 'Nik sortutako planak ere egokitzen dira?', a: 'Bai. Premiumekin, egokitzapen automatikoa zure eskuzko planetan ere dabil. Premium gabe zure entrenamenduak berdin gordetzen dira, baina plana ez da bere kabuz doitzen.' },
    { q: 'Zer gertatzen da Premium bertan behera uzten badut?', a: 'Zure historia eta eskuzko planak mantentzen dituzu. Automatikoki sortutako plan bat kontsulta daiteke, baina Premium behar da hura erabiltzen jarraitzeko eta hurrengo blokea sortzeko. Artxiba dezakezu eta eskuzko bat sortu.' },
    { q: 'Nahi dudanean utz dezaket bertan behera?', a: 'Bai, harpidetza Google Playtik kudeatzen da eta edozein unetan utz dezakezu bertan behera.' },
    { q: 'TRAZOk profesional bat ordezkatzen du?', a: 'Ez. TRAZOk orientaziozko entrenamendu-planak eskaintzen ditu eta ez du osasun-profesional baten edo entrenatzaile baten aholkua ordezkatzen.' }
  ],
  shotAlt: 'TRAZO aplikazioaren pantaila-argazkia',
  howTitle: 'Nola dabil',
  steps: [
    { title: 'Kontatu nola entrenatzen duzun', text: 'Helburua, maila, egun erabilgarriak eta ekipamendua.' },
    { title: 'Diseinatu edo sortu zure plana', text: 'Eraiki eskuz edo utzi TRAZOri osorik sortzen Premiumekin.' },
    { title: 'Entrenatu eta erregistratu', text: 'Erregistratu zure saioak. Premiumekin, TRAZOk hurrengoak egokitzen ditu nola joan zaizunaren arabera.' }
  ],
  waitTitle: 'Abiaraztea prestatzen ari gara',
  waitText: 'Utzi zure posta eta TRAZO Google Playn dagoenean abisatuko dizugu. Lehenago probatzera ere gonbidatuko zaitugu, proba itxian. Horretarako bakarrik idatziko dizugu.',
  emailLabel: 'Zure Google kontuaren posta',
  emailHint: 'Erabili zure mugikorreko Google kontuaren posta: Google Playk hura erabiliko du probarako sarbidea emateko. Mezu bat bidaliko dizugu baieztatzeko.',
  consent: 'Onartzen dut TRAZOk nire posta erabiltzea abiaraztearen berri emateko eta aplikazioa Google Playko proba itxian probatzera gonbidatzeko. Nahi dudanean ken dezaket. Informazio gehiago:',
  privacyLabel: 'pribatutasun-politika (ingelesez)',
  send: 'Apuntatu',
  note: 'TRAZOk orientaziozko entrenamendu-planak eskaintzen ditu eta ez du osasun-profesional baten aholkua ordezkatzen.',
  ok: 'Ia prest: mezu bat bidali dizugu. Ireki eta baieztatu zure helbidea apuntatuta geratzeko.',
  invalid: 'Berrikusi posta eta onartu baimena.',
  later: 'Saiakera gehiegi. Saiatu berriro geroago.',
  fail: 'Ezin izan dugu zure posta gorde. Saiatu berriro minutu batzuk barru.',
  terms: 'Erabilera-baldintzak (ingelesez)', contact: 'Kontaktua', admin: 'Administrazio-sarbidea', skip: 'Joan edukira',
  waitlistPages: {
    confirm: {
      title: 'Baieztatu zure posta',
      text: 'Sakatu botoia TRAZOren zerrendan egon nahi duzula baieztatzeko: abiaraztearen berri emango dizugu eta aplikazioa probatzera gonbidatuko zaitugu.',
      button: 'Baieztatu',
      done: 'Posta baieztatuta',
      doneText: 'Eskerrik asko. Probari eta abiarazteari buruzko berriak ditugunean idatziko dizugu.'
    },
    unsubscribe: {
      title: 'Eman baja',
      text: 'Sakatu botoia zure posta TRAZOren zerrendatik ezabatzeko. Ez dizugu gehiago idatziko.',
      button: 'Ezabatu nire datuak',
      done: 'Datuak ezabatuta',
      doneText: 'Zure posta zerrendatik ezabatu dugu. Ez duzu mezu gehiagorik jasoko.'
    },
    failed: 'Esteka baliogabea edo iraungia',
    failedText: 'Esteka hau ez da baliozkoa, iraungi egin da edo erabili da jada. Apuntatu nahi bazenuen, bete berriro inprimakia.',
    missing: 'Esteka osatugabea',
    missingText: 'Estekari segurtasun-kodea falta zaio. Erabili mezuko esteka jaso zenuen bezala.',
    wait: 'Une bat…',
    home: 'Joan TRAZOra'
  }
};

export default content;
