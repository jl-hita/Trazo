import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('nl');

const content: LandingContent = {
  title: 'TRAZO · De krachttrainingsmotor die zich aan jou aanpast',
  description: 'TRAZO is geen sjabloon: het is een motor die je macrocyclus maakt, gewichten en sets per training aanpast aan hoe je reageert en elke wijziging uitlegt. Zelf plannen is gratis.',
  languageLabel: 'Taal',
  eyebrow: 'ADAPTIEVE TRAININGSMOTOR',
  headA: 'Je plan is geen sjabloon.', headB: 'Het is een motor die zich aan jou aanpast',
  lead: 'TRAZO bouwt je volledige macrocyclus, past gewichten en sets per training aan hoe je reageert en legt elke wijziging uit. Met vaste dagen of zonder.',
  cta: 'Zet me op de wachtlijst',
  loopTitle: 'Een cyclus die elke training rond wordt',
  loopLead: 'Zelf plannen is gratis. Automatisch maken en aanpassen is Premium.',
  loop: [
    { title: 'Maken', text: 'Doel, niveau, materiaal en klachten. De motor ontwerpt de macrocyclus: fasen, deloads, oefeningen en volume per spier.' },
    { title: 'Trainen', text: 'Je registreert sets, gewichten en RIR. Je ziet wat je vorige keer deed en welk gewicht nu aan de beurt is.' },
    { title: 'Luisteren', text: 'Na afloop vertel je hoe het ging: herstel, inspanning, pijn en hoe elke spier reageerde.' },
    { title: 'Aanpassen', text: 'Hij verhoogt het gewicht, schrapt of voegt sets toe, plant een deload. Elk nieuw blok begint bij wat je echt tilde.' }
  ],
  pillarsTitle: 'Wat deze motor anders maakt',
  pillars: [
    { title: 'Dubbele progressie per oefening', text: 'Eerst herhalingen, dan gewicht: haal je de bovenkant van het bereik met marge, dan gaat het een stap omhoog. Nog niet? Dan blijft het.' },
    { title: 'Volume per spier', text: 'Sets worden verdeeld naar het weekvolume dat elke spier nodig heeft, passend bij je niveau en tijd.' },
    { title: 'Herstel per spier', text: 'Voor het voorstellen van de volgende training controleert hij of de betrokken spieren hersteld zijn.', premium: true },
    { title: 'Deloads wanneer nodig', text: 'Geplande lichte weken, plus een extra deload bij opgebouwde vermoeidheid of dalende prestaties.' },
    { title: 'Pijn gaat voor', text: 'Meld je pijn, dan verhoogt hij nooit het gewicht en automatiseert niets: hij waarschuwt je. Je gewrichtsklachten sluiten belastende oefeningen uit.' },
    { title: 'Elke wijziging uitgelegd', text: 'Geen black box: elke aanpassing komt met de reden en blijft in de geschiedenis van je plan.' }
  ],
  showcase: [
    {
      kicker: 'Binnen een minuut aan de slag', title: 'Vertel hoe je traint',
      text: 'Doel, niveau, materiaal en klachten. Geen eindeloze vragenlijsten: de motor doet de rest.',
      points: ['Spiergroei, kracht of algemene fitheid', 'Je materiaal en de zones die je wilt ontzien', 'Planlengte en deloadweken zoals jij wilt'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Doel en niveau'), shot('12-wizard-details', 'Materiaal en klachten')]
    },
    {
      kicker: 'Jouw week, jouw regels', title: 'Met vaste dagen of zonder',
      text: 'Kies een kalender of een doorlopende cyclus. TRAZO zegt welke training volgt en jij traint wanneer je kunt.',
      points: ['Achterstallige trainingen gaan niet verloren en worden nooit vanzelf overgeslagen', 'Hersteladvies voor je begint', 'Een lichtere herintrede na weken weg'],
      shots: [shot('11-wizard-flexible', 'Zonder vaste dagen'), shot('14-plan-sequence', 'Volgende training')]
    },
    {
      kicker: 'Trainen', title: 'Hij weet wat je vorige keer deed',
      text: 'Elke oefening toont je vorige prestatie en het voorgestelde gewicht. Een set registreren kost twee tikken.',
      points: ['Vorige keer en doelgewicht bij elke oefening', 'Ingebouwde rusttimer', 'Werkt ook zonder verbinding'],
      shots: [shot('16-session', 'De training in detail'), shot('17-workout', 'Sets registreren')]
    },
    {
      kicker: 'Aanpassen', title: 'Na elke training een beter plan',
      text: 'Rond de training af en de motor vertelt wat hij aanpaste en waarom. Je vooruitgang altijd in beeld.',
      points: ['Wat veranderde en waarom', 'Records, reeks en sets per spier', 'Het hele blok, met gewichten die week na week scherper worden'],
      premium: true,
      shots: [shot('18-adaptation', 'Zo past je plan zich aan'), shot('19-progress', 'Je vooruitgang'), shot('15-block', 'Je blok')]
    }
  ],
  featuresTitle: 'Alles om doordacht te trainen',
  features: [
    { title: 'Plan op jouw manier', text: 'Maak macrocycli, mesocycli en routines, gratis en zonder kunstmatige grenzen.' },
    { title: 'Elke training helder', text: 'Sets, herhalingen, RIR en rust per oefening, met afbeelding en techniek bij de hand.' },
    { title: 'Wissel slim van oefening', text: 'Geldige alternatieven voor je niveau, materiaal en beperkingen, zonder het plan te breken.' },
    { title: 'Moeiteloos registreren', text: 'Noteer elke set en gebruik de rusttimer. Werkt ook zonder verbinding.' },
    { title: 'Oefeningencatalogus', text: 'Meer dan 140 oefeningen met zoeken, filters en geïllustreerde pagina’s.' },
    { title: 'Je account beschermd', text: 'Inloggen met Google of e-mail, tweestapsverificatie en versleutelde gegevens op je toestel.' }
  ],
  pricingTitle: 'Begin gratis. Automatiseer wanneer je wilt.',
  pricingLead: 'Handmatig plannen is gratis en zonder kunstmatige grenzen. Premium is voor wie TRAZO de training wil laten programmeren en aanpassen.',
  freeName: 'Gratis',
  freePrice: '€ 0',
  freePeriod: 'voor altijd',
  freeTagline: 'Jij ontwerpt en stuurt je training.',
  freeFeatures: [
    'Macrocycli handmatig maken',
    'Mesocycli en routines handmatig maken',
    'Trainingen registreren en de geschiedenis bekijken',
    'Volledige oefeningencatalogus met afbeeldingen'
  ],
  premiumName: 'Premium',
  premiumPrice: '€ 6,99',
  premiumPeriod: '/ maand',
  premiumTagline: 'TRAZO programmeert voor jou en past zich aan.',
  premiumFeatures: [
    'Alles uit het gratis plan',
    'Automatisch je hele plan: macrocyclus, fasen en deloads',
    'Oefeningen gekozen op profiel, materiaal en beperkingen',
    'Automatische aanpassing van gewichten, sets en deloads op je feedback',
    'Elk nieuw blok gemaakt uit je echte vooruitgang'
  ],
  premiumAnnual: 'Of € 49,99 per jaar · komt neer op € 4,17/maand',
  pricingNote: 'Verwachte prijzen bij lancering. Handmatig plannen blijft gratis; automatisch maken en aanpassen wordt Premium. Premium bevat 14 dagen gratis proberen.',
  faqTitle: 'Veelgestelde vragen',
  faq: [
    { q: 'Is het kunstmatige intelligentie?', a: 'Nee. De motor is deterministisch en gebouwd op trainingsregels: dubbele progressie, weekvolume per spier, herstel en deloads. Geen verzonnen tekst of verborgen toeval: hij past bekende regels toe op jouw gegevens en legt elke keuze uit.' },
    { q: 'Kan ik zonder vaste dagen trainen?', a: 'Ja. Je kunt een doorlopende cyclus kiezen in plaats van een kalender: TRAZO zegt welke training volgt en jij traint wanneer je kunt. Late trainingen worden met hun echte datum geregistreerd en nooit vanzelf overgeslagen. Deze optie is gratis.' },
    { q: 'Wat is gratis en wat is Premium?', a: 'Gratis: eigen macrocycli, mesocycli en routines maken, trainingen registreren, de geschiedenis bekijken en de catalogus gebruiken. Premium: automatisch je plan maken en je trainingen aanpassen op je feedback.' },
    { q: 'Wat doet het automatisch maken precies?', a: 'Uit je doel, niveau, dagen, tijd, materiaal en beperkingen bouwt TRAZO een macrocyclus met fasen en deloads en maakt de trainingen van elk blok uit wat je al trainde.' },
    { q: 'Hoe past mijn plan zich aan?', a: 'Bij elke geregistreerde training (sets, gewichten, RIR, herstel en pijn) past TRAZO de volgende aan: ander gewicht, minder sets, meer marge of een deload als dat nodig is. Hij legt altijd uit wat en waarom.' },
    { q: 'Passen zelfgemaakte plannen zich ook aan?', a: 'Ja. Met Premium werkt de automatische aanpassing ook op je handmatige plannen. Zonder Premium worden je trainingen wel bewaard, maar past het plan zich niet vanzelf aan.' },
    { q: 'Wat gebeurt er als ik Premium opzeg?', a: 'Je houdt je geschiedenis en handmatige plannen. Een automatisch gemaakt plan blijft leesbaar, maar je hebt Premium nodig om het te blijven gebruiken en het volgende blok te maken. Je kunt het archiveren en een handmatig plan maken.' },
    { q: 'Kan ik altijd opzeggen?', a: 'Ja, het abonnement loopt via Google Play en je kunt het op elk moment opzeggen.' },
    { q: 'Vervangt TRAZO een professional?', a: 'Nee. TRAZO geeft algemene trainingsplannen en vervangt geen advies van een zorgprofessional of trainer.' }
  ],
  shotAlt: 'Schermafbeelding van de TRAZO-app',
  howTitle: 'Zo werkt het',
  steps: [
    { title: 'Vertel hoe je traint', text: 'Doel, niveau, beschikbare dagen en materiaal.' },
    { title: 'Ontwerp of laat je plan maken', text: 'Bouw je planning zelf of laat TRAZO met Premium je volledige plan maken.' },
    { title: 'Train en registreer', text: 'Registreer je trainingen. Met Premium past TRAZO de volgende aan op hoe het ging.' }
  ],
  waitTitle: 'We bereiden de lancering voor',
  waitText: 'Laat je e-mail achter en we melden het zodra TRAZO op Google Play staat. We nodigen je ook uit om eerder te testen, in de gesloten test. Alleen daarvoor schrijven we je.',
  emailLabel: 'E-mail van je Google-account',
  emailHint: 'Gebruik het e-mailadres van het Google-account op je telefoon: Google Play gebruikt het om je toegang tot de test te geven. We sturen een bericht ter bevestiging.',
  consent: 'Ik ga ermee akkoord dat TRAZO mijn e-mail gebruikt om me over de lancering te informeren en uit te nodigen voor de gesloten test op Google Play. Ik kan dit altijd intrekken. Meer in het',
  privacyLabel: 'privacybeleid (Engels)',
  send: 'Aanmelden',
  note: 'TRAZO geeft algemene trainingsplannen en vervangt geen advies van een zorgprofessional.',
  ok: 'Bijna klaar: we hebben je een e-mail gestuurd. Open hem en bevestig je adres om op de lijst te komen.',
  invalid: 'Controleer het e-mailadres en geef toestemming.',
  later: 'Te veel pogingen. Probeer het later opnieuw.',
  fail: 'We konden je e-mail niet opslaan. Probeer het over een paar minuten opnieuw.',
  terms: 'Gebruiksvoorwaarden (Engels)', contact: 'Contact', admin: 'Beheerlogin', skip: 'Naar de inhoud',
  waitlistPages: {
    confirm: {
      title: 'Bevestig je e-mail',
      text: 'Tik op de knop om te bevestigen dat je op de TRAZO-lijst wilt: we melden de lancering en nodigen je uit de app te testen.',
      button: 'Bevestigen',
      done: 'E-mail bevestigd',
      doneText: 'Bedankt. We schrijven je zodra er nieuws is over de test en de lancering.'
    },
    unsubscribe: {
      title: 'Uitschrijven',
      text: 'Tik op de knop om je e-mail van de TRAZO-lijst te wissen. We schrijven je niet meer.',
      button: 'Mijn gegevens wissen',
      done: 'Gegevens gewist',
      doneText: 'We hebben je e-mail van de lijst gewist. Je ontvangt geen berichten meer.'
    },
    failed: 'Link ongeldig of verlopen',
    failedText: 'Deze link is ongeldig, verlopen of al gebruikt. Wilde je je aanmelden, vul het formulier dan opnieuw in.',
    missing: 'Onvolledige link',
    missingText: 'In de link ontbreekt de beveiligingscode. Gebruik de link uit de e-mail precies zoals je hem kreeg.',
    wait: 'Een ogenblik…',
    home: 'Naar TRAZO'
  }
};

export default content;
