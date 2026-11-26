import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('sv');

const content: LandingContent = {
  title: 'TRAZO · Styrkemotorn som anpassar sig efter dig',
  description: 'TRAZO är ingen mall: det är en motor som skapar din makrocykel, justerar vikter och set efter varje pass utifrån hur du svarar och förklarar varje ändring. Manuell planering är gratis.',
  languageLabel: 'Språk',
  eyebrow: 'Adaptiv träningsmotor',
  headA: 'Din plan är ingen mall.', headB: 'Det är en motor som anpassar sig efter dig',
  lead: 'TRAZO bygger hela makrocykeln, justerar vikter och set efter varje pass utifrån hur du svarar och förklarar varje ändring. Med fasta dagar eller utan.',
  cta: 'Ställ dig i kön',
  loopTitle: 'En loop som sluts efter varje pass',
  loopLead: 'Manuell planering är gratis. Automatisk generering och anpassning är Premium.',
  loop: [
    { title: 'Skapar', text: 'Mål, nivå, utrustning och besvär. Motorn utformar makrocykeln: faser, avlastningar, övningar och volym per muskel.' },
    { title: 'Träna', text: 'Du loggar set, vikter och RIR. Du ser vad du gjorde förra gången och vilken vikt som gäller i dag.' },
    { title: 'Lyssnar', text: 'När du är klar berättar du hur det går: återhämtning, ansträngning, smärta och varje muskel.' },
    { title: 'Anpassar', text: 'Höjer vikten, tar bort eller lägger till set, planerar en avlastning. Varje nytt block utgår från vad du faktiskt lyfte.' }
  ],
  pillarsTitle: 'Det som skiljer motorn åt',
  pillars: [
    { title: 'Dubbel progression per övning', text: 'Först reps, sedan vikt: när du når toppen av intervallet med marginal går du upp ett steg. Är det inte dags ännu ligger vikten kvar.' },
    { title: 'Volym per muskel', text: 'Seten fördelas mot den veckovolym varje muskel behöver, utifrån din nivå och din tid.' },
    { title: 'Återhämtning per muskel', text: 'Innan nästa pass föreslås kontrollerar den att musklerna som ska arbeta har återhämtat sig.', premium: true },
    { title: 'Avlastning när det behövs', text: 'Planerade lätta veckor, och en extra avlastning om tröttheten byggs upp eller prestationen faller.' },
    { title: 'Smärtan bestämmer', text: 'Rapporterar du smärta höjs vikten aldrig och inget sker automatiskt: du uppmanas att se över det. Och dina ledbesvär utesluter övningar som förvärrar dem.' },
    { title: 'Varje ändring förklarad', text: 'Ingen svart låda: varje justering kommer med sitt skäl och sparas i planens historik.' }
  ],
  showcase: [
    {
      kicker: 'Igång på en minut', title: 'Berätta hur du tränar',
      text: 'Mål, nivå, utrustning och besvär. Inga oändliga formulär: motorn gör resten.',
      points: ['Hypertrofi, styrka eller kondition', 'Din utrustning och det du måste skona', 'Planens längd och avlastningsveckor, fritt valt'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Mål och nivå'), shot('12-wizard-details', 'Utrustning och besvär')]
    },
    {
      kicker: 'Din vecka, dina regler', title: 'Med eller utan fasta dagar',
      text: 'Välj kalender eller löpande cykel: TRAZO visar nästa pass och du kör det när det passar.',
      points: ['Förseningar går inte förlorade och inget hoppas över av sig själv', 'Besked om återhämtning innan du börjar', 'Efter veckor borta börjar du lugnare'],
      shots: [shot('11-wizard-flexible', 'Utan fasta dagar'), shot('14-plan-sequence', 'Nästa pass')]
    },
    {
      kicker: 'Träna', title: 'Den vet vad du gjorde förra gången',
      text: 'Varje övning visar förra resultatet och föreslagen vikt. Att logga ett set tar två tryck.',
      points: ['Förra resultatet och målvikt per övning', 'Inbyggd vilotimer', 'Fungerar även utan täckning'],
      shots: [shot('16-session', 'Passets detaljer'), shot('17-workout', 'Loggning av set')]
    },
    {
      kicker: 'Anpassa', title: 'En bättre plan efter varje pass',
      text: 'När du avslutar berättar motorn vad den justerade och varför. Dina framsteg syns alltid.',
      points: ['Vad som ändrats, med skälet', 'Personliga rekord, sviter och set per muskel', 'Hela blocket, där vikterna finslipas vecka för vecka'],
      premium: true,
      shots: [shot('18-adaptation', 'Så anpassas din plan'), shot('19-progress', 'Dina framsteg'), shot('15-block', 'Ditt block')]
    }
  ],
  featuresTitle: 'Allt du behöver för att träna med omdöme',
  features: [
    { title: 'Planera på ditt sätt', text: 'Skapa makrocykler, mesocykler och rutiner, gratis och utan konstlade gränser.' },
    { title: 'Varje pass, tydligt', text: 'Set, reps, RIR och vila för varje övning, med bild och teknik nära till hands.' },
    { title: 'Byt övningar med omtanke', text: 'Alternativ som passar din nivå, utrustning och begränsningar, utan att förstöra planen.' },
    { title: 'Enkel loggning', text: 'Anteckna varje set och använd timern. Fungerar även om uppkopplingen bryts.' },
    { title: 'Övningskatalog', text: 'Över 140 övningar med sökning, filter och kort med bilder.' },
    { title: 'Skyddat konto', text: 'Logga in med Google eller e-post, tvåfaktorsverifiering och kryptering på enheten.' }
  ],
  pricingTitle: 'Börja gratis. Automatisera när du vill.',
  pricingLead: 'Manuell planering är gratis, utan konstlade gränser. Premium är för dig som vill att TRAZO ska skapa och anpassa åt dig.',
  freeName: 'Gratis',
  freePrice: '0 €',
  freePeriod: 'alltid',
  freeTagline: 'Du utformar och styr.',
  freeFeatures: [
    'Skapa makrocykler för hand',
    'Skapa mesocykler och rutiner för hand',
    'Loggning av pass och historik',
    'Hela övningskatalogen med bilder'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ månad',
  premiumTagline: 'TRAZO bygger åt dig och anpassar sig.',
  premiumFeatures: [
    'Allt i Gratis',
    'Automatisk generering av hela planen: makrocykel, faser och avlastningar',
    'Övningsval efter profil, utrustning och begränsningar',
    'Automatisk anpassning av vikter, set och avlastningar utifrån din återkoppling',
    'Nya block byggda på dina verkliga framsteg'
  ],
  premiumAnnual: 'eller 49,99 € per år (4,17 € i månaden)',
  pricingNote: 'Planerade priser vid lanseringen. Manuell planering förblir gratis; automatisk generering och anpassning är Premium. Premium har 14 dagars gratis provperiod.',
  faqTitle: 'Vanliga frågor',
  faq: [
    { q: 'Är det artificiell intelligens?', a: 'Nej. Motorn är deterministisk och bygger på träningsprinciper: dubbel progression, veckovolym per muskel, återhämtning och avlastning. Inga påhittade texter eller dold slump: den tillämpar kända regler på dina data och varje beslut förklaras.' },
    { q: 'Kan jag använda den utan fasta dagar?', a: 'Ja. I stället för kalender kan du välja en löpande cykel: TRAZO visar nästa pass och du kör det när det passar. Förseningar sparas med faktiskt datum, inget hoppas över av sig själv, och det här är gratis.' },
    { q: 'Vad skiljer Gratis från Premium?', a: 'Gratis: skapa makrocykler, mesocykler och rutiner, loggning, historik och katalog. Premium: automatisk generering av planen och automatisk anpassning utifrån din återkoppling.' },
    { q: 'Vad gör den automatiska genereringen?', a: 'Utifrån mål, nivå, dagar, tid, utrustning och begränsningar bygger den en makrocykel med faser och avlastningar, och skapar passen i varje block utifrån vad du redan gjort.' },
    { q: 'Hur anpassas planen?', a: 'Varje gång du loggar ett pass (set, vikter, RIR, återhämtning, smärta) justerar TRAZO de följande: ändrar vikter, tar bort ett set, lägger till marginal eller planerar avlastning. Den berättar alltid vad den ändrar och varför.' },
    { q: 'Anpassar den även planer jag gör själv?', a: 'Ja: med Premium fungerar automatisk anpassning även på manuella planer. Utan Premium sparas loggarna och planen ändras inte av sig själv.' },
    { q: 'Vad händer om jag säger upp Premium?', a: 'Du behåller historiken och dina manuella planer. Automatiskt skapade planer går att se, men att använda dem vidare och skapa nya block kräver Premium. Du kan arkivera dem och göra manuella planer.' },
    { q: 'Kan jag säga upp när som helst?', a: 'Ja. Prenumerationen hanteras i Google Play och du kan säga upp när du vill.' },
    { q: 'Ersätter TRAZO en fackperson?', a: 'Nej. TRAZO ger vägledande träningsplaner och ersätter inte råd från vårdpersonal eller tränare.' }
  ],
  shotAlt: 'Skärmbild från TRAZO-appen',
  howTitle: 'Så fungerar det',
  steps: [
    { title: 'Berätta hur du tränar', text: 'Mål, nivå, lediga dagar och utrustning.' },
    { title: 'Utforma din plan eller generera den', text: 'Bygg den för hand, eller låt TRAZO skapa hela planen med Premium.' },
    { title: 'Träna och logga', text: 'Logga dina pass. Med Premium justeras de följande efter dina resultat.' }
  ],
  waitTitle: 'Vi förbereder lanseringen',
  waitText: 'Lämna din e-post så hör vi av oss när appen finns på Google Play. Vi bjuder också in dig till det slutna testet dessförinnan. Vi använder den inte till något annat.',
  emailLabel: 'E-postadressen till ditt Google-konto',
  emailHint: 'Använd adressen till Google-kontot på telefonen: Google Play behöver den för att ge åtkomst till testet. Vi skickar ett meddelande så att du kan bekräfta den.',
  consent: 'Jag godkänner att TRAZO använder min e-postadress för att meddela om lanseringen och bjuda in mig till det slutna testet på Google Play. Jag kan återkalla samtycket när som helst. Mer i',
  privacyLabel: 'integritetspolicyn (på engelska)',
  send: 'Skriv upp mig',
  note: 'TRAZO ger vägledande träningsplaner och ersätter inte råd från vårdpersonal.',
  ok: 'Nästan klart: vi har skickat ett meddelande. Öppna det och bekräfta adressen för att slutföra.',
  invalid: 'Kontrollera e-postadressen och kryssa i samtycket.',
  later: 'För många försök. Försök igen om en stund.',
  fail: 'Vi kunde inte spara din adress. Försök igen om några minuter.',
  terms: 'Villkor (på engelska)', contact: 'Kontakt', admin: 'Inloggning för administratör', skip: 'Hoppa till innehållet',
  waitlistPages: {
    confirm: {
      title: 'Bekräfta din e-postadress',
      text: 'Tryck på knappen för att bekräfta att du vill stå på TRAZO-listan: vi meddelar om lanseringen och bjuder in dig att prova appen.',
      button: 'Bekräfta adressen',
      done: 'Adressen är bekräftad',
      doneText: 'Tack. Vi skriver när vi har nytt om testet och lanseringen.'
    },
    unsubscribe: {
      title: 'Gå ur listan',
      text: 'När du trycker på knappen raderar vi din adress från TRAZO-listan och skriver inte till dig mer.',
      button: 'Radera mina uppgifter',
      done: 'Raderat',
      doneText: 'Vi har raderat din adress från listan. Du får inga fler meddelanden.'
    },
    failed: 'Länken är ogiltig eller har gått ut',
    failedText: 'Länken är ogiltig, har gått ut eller redan använts. Fyll i formuläret igen för att anmäla dig.',
    missing: 'Länken är ofullständig',
    missingText: 'Bekräftelsekoden saknas i länken. Använd länken i meddelandet precis som den är.',
    wait: 'Ett ögonblick…',
    home: 'Till TRAZO'
  }
};

export default content;
