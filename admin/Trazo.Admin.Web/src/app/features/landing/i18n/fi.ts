import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('fi');

const content: LandingContent = {
  title: 'TRAZO · Voimaharjoittelun moottori, joka mukautuu sinuun',
  description: 'TRAZO ei ole valmis pohja: se on moottori, joka luo makrosyklisi, säätää painot ja setit jokaisen treenin jälkeen vasteesi mukaan ja selittää jokaisen muutoksen. Itse suunnittelu on ilmaista.',
  languageLabel: 'Kieli',
  eyebrow: 'Mukautuva harjoitusmoottori',
  headA: 'Ohjelmasi ei ole valmis pohja.', headB: 'Se on moottori, joka mukautuu sinuun',
  lead: 'TRAZO rakentaa koko makrosyklin, säätää painot ja setit jokaisen treenin jälkeen vasteesi mukaan ja selittää jokaisen muutoksen. Kiinteillä päivillä tai ilman.',
  cta: 'Liity jonotuslistalle',
  loopTitle: 'Kierto, joka sulkeutuu joka treenissä',
  loopLead: 'Itse suunnittelu on ilmaista. Automaattinen luonti ja mukautus ovat Premiumia.',
  loop: [
    { title: 'Luo', text: 'Tavoite, taso, välineet ja vaivat. Moottori suunnittelee makrosyklin: vaiheet, kevennykset, liikkeet ja volyymin lihasta kohti.' },
    { title: 'Treenaa', text: 'Kirjaat setit, painot ja RIR:n. Näet, mitä teit viimeksi ja mikä paino kuuluu tälle päivälle.' },
    { title: 'Kuuntelee', text: 'Lopuksi kerrot, miten sujuu: palautuminen, rasitus, kipu ja kukin lihas.' },
    { title: 'Mukautuu', text: 'Nostaa painoa, poistaa tai lisää settejä, ajoittaa kevennyksen. Jokainen uusi jakso lähtee siitä, mitä oikeasti nostit.' }
  ],
  pillarsTitle: 'Mikä tekee moottorista erilaisen',
  pillars: [
    { title: 'Kaksoisprogressio liikkeittäin', text: 'Ensin toistot, sitten paino: kun saavutat alueen ylärajan vara jäljellä, siirryt portaan ylös. Jos ei vielä, paino pysyy.' },
    { title: 'Volyymi lihasta kohti', text: 'Setit jaetaan kohti viikkovolyymia, jota kukin lihas tarvitsee tasosi ja aikasi mukaan.' },
    { title: 'Palautuminen lihasta kohti', text: 'Ennen seuraavan treenin ehdottamista se tarkistaa, että työtä tekevät lihakset ovat palautuneet.', premium: true },
    { title: 'Kevennys silloin kun tarvitaan', text: 'Suunnitellut kevyet viikot sekä ylimääräinen kevennys, jos väsymys kertyy tai suorituskyky laskee.' },
    { title: 'Kipu määrää', text: 'Jos ilmoitat kivusta, painoa ei koskaan nosteta eikä mitään automatisoida: saat kehotuksen tarkistaa asia. Nivelvaivat sulkevat pois niitä pahentavat liikkeet.' },
    { title: 'Jokainen muutos selitetty', text: 'Ei mustaa laatikkoa: jokaisella säädöllä on perustelu, ja se tallentuu ohjelman historiaan.' }
  ],
  showcase: [
    {
      kicker: 'Alkuun minuutissa', title: 'Kerro, miten treenaat',
      text: 'Tavoite, taso, välineet ja vaivat. Ei loputtomia lomakkeita: moottori hoitaa loput.',
      points: ['Lihaskasvu, voima tai kunto', 'Välineesi ja se, mitä pitää varoa', 'Ohjelman kesto ja kevennysviikot vapaasti'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Tavoite ja taso'), shot('12-wizard-details', 'Välineet ja vaivat')]
    },
    {
      kicker: 'Sinun viikkosi, sinun sääntösi', title: 'Kiinteillä päivillä tai ilman',
      text: 'Valitse kalenteri tai jatkuva kierto: TRAZO näyttää seuraavan treenin ja teet sen, kun sinulle sopii.',
      points: ['Viivästyminen ei hävitä mitään eikä mitään ohiteta itsestään', 'Palautumisilmoitus ennen aloitusta', 'Viikkojen tauon jälkeen aloitat kevyemmin'],
      shots: [shot('11-wizard-flexible', 'Ilman kiinteitä päiviä'), shot('14-plan-sequence', 'Seuraava treeni')]
    },
    {
      kicker: 'Treenaa', title: 'Se tietää, mitä teit viimeksi',
      text: 'Jokainen liike näyttää edellisen tuloksen ja ehdotetun painon. Setin kirjaus vie kaksi napautusta.',
      points: ['Edellinen tulos ja tavoitepaino liikkeittäin', 'Sisäänrakennettu lepoajastin', 'Toimii, vaikka kuuluvuus katkeaa'],
      shots: [shot('16-session', 'Treenin tiedot'), shot('17-workout', 'Settien kirjaus')]
    },
    {
      kicker: 'Mukauta', title: 'Parempi ohjelma jokaisen treenin jälkeen',
      text: 'Kun lopetat, moottori kertoo, mitä se säätää ja miksi. Edistymisesi on aina näkyvissä.',
      points: ['Mikä muuttui ja miksi', 'Ennätykset, putket ja setit lihasta kohti', 'Koko jakso, jossa painot hioutuvat viikko viikolta'],
      premium: true,
      shots: [shot('18-adaptation', 'Näin ohjelmasi mukautuu'), shot('19-progress', 'Edistymisesi'), shot('15-block', 'Jaksosi')]
    }
  ],
  featuresTitle: 'Kaikki, mitä järkevään treeniin tarvitset',
  features: [
    { title: 'Suunnittele omalla tavallasi', text: 'Luo makrosyklejä, mesosyklejä ja rutiineja ilmaiseksi ja ilman keinotekoisia rajoja.' },
    { title: 'Jokainen treeni selkeänä', text: 'Setit, toistot, RIR ja lepo jokaiselle liikkeelle, kuva ja tekniikka käden ulottuvilla.' },
    { title: 'Vaihda liikkeitä järkevästi', text: 'Tasoosi, välineisiisi ja rajoituksiisi sopivat vaihtoehdot rikkomatta ohjelmaa.' },
    { title: 'Vaivaton kirjaus', text: 'Merkitse jokainen setti ja käytä ajastinta. Toimii, vaikka yhteys katkeaa.' },
    { title: 'Liikeluettelo', text: 'Yli 140 liikettä haulla, suodattimilla ja kuvallisilla korteilla.' },
    { title: 'Suojattu tili', text: 'Kirjaudu Googlella tai sähköpostilla, kaksivaiheinen vahvistus ja salaus laitteella.' }
  ],
  pricingTitle: 'Aloita ilmaiseksi. Automatisoi, kun haluat.',
  pricingLead: 'Itse suunnittelu on ilmaista ilman keinotekoisia rajoja. Premium on niille, jotka haluavat TRAZOn luovan ja mukauttavan puolestaan.',
  freeName: 'Ilmainen',
  freePrice: '0 €',
  freePeriod: 'aina',
  freeTagline: 'Sinä suunnittelet ja johdat.',
  freeFeatures: [
    'Makrosyklien luonti itse',
    'Mesosyklien ja rutiinien luonti itse',
    'Treenien kirjaus ja historia',
    'Koko liikeluettelo kuvineen'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ kk',
  premiumTagline: 'TRAZO rakentaa puolestasi ja mukautuu sinuun.',
  premiumFeatures: [
    'Kaikki ilmaisen ominaisuudet',
    'Koko ohjelman automaattinen luonti: makrosykli, vaiheet ja kevennykset',
    'Liikkeiden valinta profiilin, välineiden ja rajoitusten mukaan',
    'Painojen, settien ja kevennysten automaattinen mukautus palautteesi perusteella',
    'Uudet jaksot todellisen edistymisesi pohjalta'
  ],
  premiumAnnual: 'tai 49,99 € vuodessa (4,17 € kuukaudessa)',
  pricingNote: 'Julkaisuun suunnitellut hinnat. Itse suunnittelu pysyy ilmaisena; automaattinen luonti ja mukautus ovat Premiumia. Premiumissa on 14 päivän ilmainen kokeilu.',
  faqTitle: 'Usein kysyttyä',
  faq: [
    { q: 'Onko se tekoälyä?', a: 'Ei. Moottori on deterministinen ja perustuu harjoittelun periaatteisiin: kaksoisprogressioon, viikkovolyymiin lihasta kohti, palautumiseen ja kevennyksiin. Ei keksittyä tekstiä eikä piilevää sattumaa: se soveltaa tunnettuja sääntöjä tietoihisi, ja jokainen päätös selitetään.' },
    { q: 'Voinko käyttää sitä ilman kiinteitä päiviä?', a: 'Kyllä. Kalenterin sijaan voit valita jatkuvan kierron: TRAZO näyttää seuraavan treenin ja teet sen, kun sopii. Viivästykset tallentuvat todellisella päivämäärällä, mitään ei ohiteta itsestään, ja tämä on ilmaista.' },
    { q: 'Miten ilmainen ja Premium eroavat?', a: 'Ilmainen: makrosyklien, mesosyklien ja rutiinien luonti, kirjaus, historia ja luettelo. Premium: ohjelman automaattinen luonti ja automaattinen mukautus palautteesi mukaan.' },
    { q: 'Mitä automaattinen luonti tekee?', a: 'Tavoitteen, tason, päivien, ajan, välineiden ja rajoitusten pohjalta se rakentaa makrosyklin vaiheineen ja kevennyksineen ja tekee kunkin jakson treenit sen perusteella, mitä olet jo tehnyt.' },
    { q: 'Miten ohjelma mukautuu?', a: 'Aina kun kirjaat treenin (setit, painot, RIR, palautuminen, kipu), TRAZO säätää seuraavia: muuttaa painoja, poistaa setin, lisää varaa tai ajoittaa kevennyksen. Se kertoo aina, mitä muuttuu ja miksi.' },
    { q: 'Mukauttaako se myös itse tekemäni ohjelmat?', a: 'Kyllä: Premiumilla automaattinen mukautus toimii myös itse tehdyissä ohjelmissa. Ilman Premiumia kirjaukset tallentuvat, eikä ohjelma muutu itsestään.' },
    { q: 'Mitä tapahtuu, jos peruutan Premiumin?', a: 'Historia ja itse tehdyt ohjelmat säilyvät. Automaattisesti luotuja ohjelmia voi katsella, mutta niiden käyttö ja uudet jaksot vaativat Premiumin. Voit arkistoida ne ja tehdä omia ohjelmia.' },
    { q: 'Voinko peruuttaa milloin tahansa?', a: 'Kyllä. Tilausta hallitaan Google Playssa, ja voit peruuttaa sen milloin vain.' },
    { q: 'Korvaako TRAZO ammattilaisen?', a: 'Ei. TRAZO tarjoaa suuntaa antavia harjoitusohjelmia eikä korvaa terveydenhuollon ammattilaisen tai valmentajan neuvoja.' }
  ],
  shotAlt: 'Näyttökuva TRAZO-sovelluksesta',
  howTitle: 'Näin se toimii',
  steps: [
    { title: 'Kerro, miten treenaat', text: 'Tavoite, taso, vapaat päivät ja välineet.' },
    { title: 'Suunnittele tai luo ohjelma', text: 'Rakenna se itse tai anna TRAZOn luoda koko ohjelma Premiumilla.' },
    { title: 'Treenaa ja kirjaa', text: 'Kirjaa treenisi. Premiumilla seuraavat säädetään tulostesi mukaan.' }
  ],
  waitTitle: 'Valmistelemme julkaisua',
  waitText: 'Jätä sähköpostiosoitteesi, niin ilmoitamme, kun sovellus tulee Google Playhin. Kutsumme sinut myös sitä edeltävään suljettuun testiin. Emme käytä osoitetta muuhun.',
  emailLabel: 'Google-tilisi sähköpostiosoite',
  emailHint: 'Käytä puhelimesi Google-tilin osoitetta: Google Play tarvitsee sen testiin pääsyä varten. Lähetämme viestin, jolla vahvistat sen.',
  consent: 'Hyväksyn, että TRAZO käyttää sähköpostiosoitettani julkaisusta ilmoittamiseen ja kutsuakseen minut Google Playn suljettuun testiin. Voin perua suostumuksen milloin tahansa. Lisätietoja:',
  privacyLabel: 'tietosuojakäytäntö (englanniksi)',
  send: 'Lisää minut listalle',
  note: 'TRAZO tarjoaa suuntaa antavia harjoitusohjelmia eikä korvaa terveydenhuollon ammattilaisen neuvoja.',
  ok: 'Melkein valmista: lähetimme sinulle viestin. Avaa se ja vahvista osoitteesi.',
  invalid: 'Tarkista sähköpostiosoite ja rastita suostumus.',
  later: 'Liian monta yritystä. Yritä hetken kuluttua uudelleen.',
  fail: 'Osoitetta ei voitu tallentaa. Yritä muutaman minuutin kuluttua uudelleen.',
  terms: 'Käyttöehdot (englanniksi)', contact: 'Yhteystiedot', admin: 'Ylläpidon kirjautuminen', skip: 'Siirry sisältöön',
  waitlistPages: {
    confirm: {
      title: 'Vahvista sähköpostiosoitteesi',
      text: 'Vahvista painamalla painiketta, että haluat TRAZOn listalle: kerromme julkaisusta ja kutsumme sinut kokeilemaan sovellusta.',
      button: 'Vahvista osoite',
      done: 'Osoite vahvistettu',
      doneText: 'Kiitos. Kirjoitamme, kun testistä ja julkaisusta on uutta.'
    },
    unsubscribe: {
      title: 'Poistu listalta',
      text: 'Painikkeella poistamme osoitteesi TRAZOn listalta emmekä kirjoita sinulle enää.',
      button: 'Poista tietoni',
      done: 'Poistettu',
      doneText: 'Poistimme osoitteesi listalta. Et saa enää viestejä.'
    },
    failed: 'Linkki on virheellinen tai vanhentunut',
    failedText: 'Linkki ei kelpaa, on vanhentunut tai jo käytetty. Ilmoittaudu täyttämällä lomake uudelleen.',
    missing: 'Linkki on puutteellinen',
    missingText: 'Linkistä puuttuu vahvistuskoodi. Käytä viestin linkkiä sellaisenaan.',
    wait: 'Hetkinen…',
    home: 'TRAZOon'
  }
};

export default content;
