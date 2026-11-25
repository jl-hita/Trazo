import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('de');

const content: LandingContent = {
  title: 'TRAZO · Der Krafttrainings-Motor, der sich dir anpasst',
  description: 'TRAZO ist keine Vorlage: Es ist ein Motor, der deinen Makrozyklus erstellt, Lasten und Sätze Einheit für Einheit an deine Reaktion anpasst und jede Änderung erklärt. Selbst planen ist kostenlos.',
  languageLabel: 'Sprache',
  eyebrow: 'ADAPTIVER TRAININGSMOTOR',
  headA: 'Dein Plan ist keine Vorlage.', headB: 'Er ist ein Motor, der sich dir anpasst',
  lead: 'TRAZO baut deinen kompletten Makrozyklus, passt Lasten und Sätze Einheit für Einheit daran an, wie du reagierst, und erklärt jede Änderung. Mit festen Tagen oder ohne.',
  cta: 'Auf die Warteliste',
  loopTitle: 'Ein Kreislauf, der sich mit jeder Einheit schließt',
  loopLead: 'Selbst planen ist kostenlos. Automatisches Erstellen und Anpassen sind Premium.',
  loop: [
    { title: 'Erstellen', text: 'Ziel, Niveau, Ausrüstung und Beschwerden. Der Motor entwirft den Makrozyklus: Phasen, Entlastungen, Übungen und Volumen pro Muskel.' },
    { title: 'Trainieren', text: 'Du protokollierst Sätze, Lasten und RIR. Du siehst, was du letztes Mal geschafft hast und welche Last jetzt ansteht.' },
    { title: 'Zuhören', text: 'Zum Schluss sagst du, wie es lief: Erholung, Anstrengung, Schmerzen und wie jeder Muskel reagiert hat.' },
    { title: 'Anpassen', text: 'Er erhöht die Last, streicht oder ergänzt Sätze, plant eine Entlastung. Jeder neue Block beginnt bei dem, was du wirklich gehoben hast.' }
  ],
  pillarsTitle: 'Was diesen Motor anders macht',
  pillars: [
    { title: 'Doppelte Progression pro Übung', text: 'Erst Wiederholungen, dann Last: Erreichst du das obere Ende des Bereichs mit Reserve, geht es eine Stufe höher. Ist es noch nicht so weit, bleibt die Last.' },
    { title: 'Volumen pro Muskel', text: 'Die Sätze werden auf das Wochenvolumen verteilt, das jeder Muskel braucht – passend zu deinem Niveau und deiner Zeit.' },
    { title: 'Erholung pro Muskel', text: 'Bevor er dir die nächste Einheit vorschlägt, prüft er, ob die beanspruchten Muskeln schon erholt sind.', premium: true },
    { title: 'Entlastung, wenn sie fällig ist', text: 'Geplante leichte Wochen und eine zusätzliche Entlastung, wenn sich Ermüdung ansammelt oder die Leistung sinkt.' },
    { title: 'Schmerz geht vor', text: 'Gibst du Schmerzen an, erhöht er nie die Last und automatisiert nichts: Er weist dich darauf hin. Und deine Gelenkbeschwerden schließen belastende Übungen aus.' },
    { title: 'Jede Änderung erklärt', text: 'Keine Blackbox: Jede Anpassung kommt mit ihrer Begründung und bleibt im Verlauf deines Plans.' }
  ],
  showcase: [
    {
      kicker: 'In einer Minute startklar', title: 'Sag ihm, wie du trainierst',
      text: 'Ziel, Niveau, Ausrüstung und Beschwerden. Keine endlosen Fragebögen: Den Rest macht der Motor.',
      points: ['Muskelaufbau, Kraft oder allgemeine Fitness', 'Deine Ausrüstung und die Bereiche, die du schonen willst', 'Plandauer und Entlastungswochen, wie du magst'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Ziel und Niveau'), shot('12-wizard-details', 'Ausrüstung und Beschwerden')]
    },
    {
      kicker: 'Deine Woche, deine Regeln', title: 'Mit festen Tagen oder ohne',
      text: 'Wähle einen Kalender oder einen fortlaufenden Zyklus. TRAZO sagt dir, welche Einheit als Nächstes kommt, und du trainierst, wann du kannst.',
      points: ['Überfällige Einheiten gehen nicht verloren und werden nie von selbst übersprungen', 'Hinweis zur Erholung, bevor du startest', 'Leichtere Wiedereinstiegseinheit nach Wochen Pause'],
      shots: [shot('11-wizard-flexible', 'Ohne feste Tage'), shot('14-plan-sequence', 'Nächste Einheit')]
    },
    {
      kicker: 'Trainieren', title: 'Er weiß, was du letztes Mal geschafft hast',
      text: 'Jede Übung zeigt deine letzte Leistung und die vorgeschlagene Last. Einen Satz protokollierst du mit zwei Fingertipps.',
      points: ['Letztes Mal und Ziellast bei jeder Übung', 'Integrierter Pausentimer', 'Funktioniert auch ohne Verbindung'],
      shots: [shot('16-session', 'Die Einheit im Detail'), shot('17-workout', 'Sätze protokollieren')]
    },
    {
      kicker: 'Anpassen', title: 'Nach jeder Einheit ein besserer Plan',
      text: 'Beende die Einheit, und der Motor sagt dir, was er angepasst hat und warum. Dein Fortschritt immer im Blick.',
      points: ['Was sich geändert hat und der Grund dafür', 'Rekorde, Serie und Sätze pro Muskel', 'Der ganze Block mit Woche für Woche verfeinerten Lasten'],
      premium: true,
      shots: [shot('18-adaptation', 'So passt sich dein Plan an'), shot('19-progress', 'Dein Fortschritt'), shot('15-block', 'Dein Block')]
    }
  ],
  featuresTitle: 'Alles, was du für durchdachtes Training brauchst',
  features: [
    { title: 'Planen auf deine Art', text: 'Erstelle Makrozyklen, Mesozyklen und Routinen – kostenlos und ohne künstliche Grenzen.' },
    { title: 'Jede Einheit klar', text: 'Sätze, Wiederholungen, RIR und Pause jeder Übung, mit Bild und Technik zur Hand.' },
    { title: 'Übungen sinnvoll tauschen', text: 'Gültige Alternativen für dein Niveau, deine Ausrüstung und deine Einschränkungen, ohne den Plan zu zerstören.' },
    { title: 'Protokoll ohne Reibung', text: 'Notiere jeden Satz und nutze den Pausentimer. Funktioniert auch ohne Verbindung.' },
    { title: 'Übungskatalog', text: 'Über 140 Übungen mit Suche, Filtern und bebilderten Detailseiten.' },
    { title: 'Dein Konto, geschützt', text: 'Anmeldung mit Google oder E-Mail, Zwei-Faktor-Authentifizierung und verschlüsselte Daten auf dem Gerät.' }
  ],
  pricingTitle: 'Kostenlos starten. Automatisieren, wann du willst.',
  pricingLead: 'Manuelles Planen ist kostenlos und ohne künstliche Grenzen. Premium ist für alle, die TRAZO das Programmieren und Anpassen überlassen möchten.',
  freeName: 'Kostenlos',
  freePrice: '0 €',
  freePeriod: 'für immer',
  freeTagline: 'Du entwirfst und steuerst dein Training.',
  freeFeatures: [
    'Makrozyklen manuell erstellen',
    'Mesozyklen und Routinen manuell erstellen',
    'Trainings protokollieren und den Verlauf ansehen',
    'Vollständiger Übungskatalog mit Bildern'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ Monat',
  premiumTagline: 'TRAZO programmiert für dich und passt sich dir an.',
  premiumFeatures: [
    'Alles aus dem kostenlosen Tarif',
    'Automatische Erstellung deines ganzen Plans: Makrozyklus, Phasen und Entlastungen',
    'Übungen passend zu deinem Profil, deiner Ausrüstung und deinen Einschränkungen',
    'Automatische Anpassung von Lasten, Sätzen und Entlastungen nach deinem Feedback',
    'Jeder neue Block aus deinem echten Fortschritt erstellt'
  ],
  premiumAnnual: 'Oder 49,99 € pro Jahr · entspricht 4,17 €/Monat',
  pricingNote: 'Geplante Preise zum Start. Manuelles Planen bleibt kostenlos; automatisches Erstellen und Anpassen sind Premium. Premium enthält eine 14-tägige kostenlose Testphase.',
  faqTitle: 'Häufige Fragen',
  faq: [
    { q: 'Ist das künstliche Intelligenz?', a: 'Nein. Der Motor ist deterministisch und beruht auf Trainingsregeln: doppelte Progression, Wochenvolumen pro Muskel, Erholung und Entlastungen. Es gibt keinen erfundenen Text und keinen versteckten Zufall: Er wendet bekannte Regeln auf deine Daten an, und jede Entscheidung wird erklärt.' },
    { q: 'Kann ich ohne feste Tage trainieren?', a: 'Ja. Statt eines Kalenders kannst du einen fortlaufenden Zyklus wählen: TRAZO sagt dir, welche Einheit als Nächstes kommt, und du trainierst, wann du kannst. Überfällige Einheiten werden mit ihrem echten Datum protokolliert und nie von selbst übersprungen. Diese Option ist kostenlos.' },
    { q: 'Was ist kostenlos und was Premium?', a: 'Kostenlos: eigene Makrozyklen, Mesozyklen und Routinen erstellen, Trainings protokollieren, den Verlauf ansehen und den Katalog nutzen. Premium: die automatische Erstellung deines Plans und die automatische Anpassung deiner Trainings nach deinem Feedback.' },
    { q: 'Was genau macht die automatische Erstellung?', a: 'Aus Ziel, Niveau, Tagen, Zeit, Ausrüstung und Einschränkungen baut TRAZO einen Makrozyklus mit Phasen und Entlastungen und erstellt die Einheiten jedes Blocks aus dem, was du bereits trainiert hast.' },
    { q: 'Wie passt sich mein Plan an?', a: 'Jedes Mal, wenn du eine Einheit protokollierst (Sätze, Lasten, RIR, Erholung und Schmerzen), passt TRAZO die folgenden an: Er ändert die Last, streicht Sätze, gibt mehr Reserve oder plant bei Bedarf eine Entlastung. Er erklärt immer, was sich geändert hat und warum.' },
    { q: 'Passen sich auch selbst erstellte Pläne an?', a: 'Ja. Mit Premium funktioniert die automatische Anpassung auch bei deinen manuellen Plänen. Ohne Premium werden deine Trainings trotzdem gespeichert, aber der Plan passt sich nicht von selbst an.' },
    { q: 'Was passiert, wenn ich Premium kündige?', a: 'Dein Verlauf und deine manuellen Pläne bleiben erhalten. Ein automatisch erstellter Plan bleibt lesbar, aber du brauchst Premium, um ihn weiter zu nutzen und den nächsten Block zu erstellen. Du kannst ihn archivieren und einen manuellen anlegen.' },
    { q: 'Kann ich jederzeit kündigen?', a: 'Ja, das Abo wird über Google Play verwaltet, und du kannst es jederzeit kündigen.' },
    { q: 'Ersetzt TRAZO eine Fachperson?', a: 'Nein. TRAZO bietet allgemeine Trainingspläne und ersetzt weder medizinischen Rat noch eine Trainerin oder einen Trainer.' }
  ],
  shotAlt: 'Screenshot der TRAZO-App',
  howTitle: 'So funktioniert es',
  steps: [
    { title: 'Sag uns, wie du trainierst', text: 'Ziel, Niveau, verfügbare Tage und Ausrüstung.' },
    { title: 'Plan entwerfen oder erstellen lassen', text: 'Baue deine Planung selbst oder lass TRAZO mit Premium deinen kompletten Plan erstellen.' },
    { title: 'Trainieren und protokollieren', text: 'Protokolliere deine Einheiten. Mit Premium passt TRAZO die nächsten daran an, wie es lief.' }
  ],
  waitTitle: 'Wir bereiten den Start vor',
  waitText: 'Hinterlass uns deine E-Mail, und wir sagen dir Bescheid, sobald TRAZO bei Google Play verfügbar ist. Außerdem laden wir dich ein, die App vorab im geschlossenen Test auszuprobieren. Nur dafür schreiben wir dir.',
  emailLabel: 'E-Mail deines Google-Kontos',
  emailHint: 'Nutze die E-Mail des Google-Kontos auf deinem Handy: Google Play verwendet sie, um dir Zugang zum Test zu geben. Wir schicken dir eine Nachricht zur Bestätigung.',
  consent: 'Ich bin einverstanden, dass TRAZO meine E-Mail verwendet, um mich über den Start zu informieren und mich zum Testen der App im geschlossenen Test bei Google Play einzuladen. Ich kann das jederzeit widerrufen. Mehr dazu in der',
  privacyLabel: 'Datenschutzerklärung (Englisch)',
  send: 'Eintragen',
  note: 'TRAZO bietet allgemeine Trainingspläne und ersetzt keinen medizinischen Rat.',
  ok: 'Fast geschafft: Wir haben dir eine E-Mail geschickt. Öffne sie und bestätige deine Adresse, um dich einzutragen.',
  invalid: 'Prüfe die E-Mail und stimme der Einwilligung zu.',
  later: 'Zu viele Versuche. Bitte versuche es später erneut.',
  fail: 'Wir konnten deine E-Mail nicht speichern. Bitte versuche es in ein paar Minuten erneut.',
  terms: 'Nutzungsbedingungen (Englisch)', contact: 'Kontakt', admin: 'Admin-Anmeldung', skip: 'Zum Inhalt springen',
  waitlistPages: {
    confirm: {
      title: 'Bestätige deine E-Mail',
      text: 'Tippe auf die Schaltfläche, um zu bestätigen, dass du auf der TRAZO-Liste stehen möchtest: Wir informieren dich über den Start und laden dich zum Testen der App ein.',
      button: 'Bestätigen',
      done: 'E-Mail bestätigt',
      doneText: 'Danke. Wir schreiben dir, sobald es Neues zum Test und zum Start gibt.'
    },
    unsubscribe: {
      title: 'Abmelden',
      text: 'Tippe auf die Schaltfläche, um deine E-Mail aus der TRAZO-Liste zu löschen. Wir schreiben dir dann nicht mehr.',
      button: 'Meine Daten löschen',
      done: 'Daten gelöscht',
      doneText: 'Wir haben deine E-Mail aus der Liste gelöscht. Du erhältst keine Nachrichten mehr.'
    },
    failed: 'Link ungültig oder abgelaufen',
    failedText: 'Dieser Link ist ungültig, abgelaufen oder wurde bereits verwendet. Wenn du dich eintragen wolltest, fülle das Formular erneut aus.',
    missing: 'Unvollständiger Link',
    missingText: 'Dem Link fehlt der Sicherheitscode. Verwende den Link aus der E-Mail genau so, wie du ihn erhalten hast.',
    wait: 'Einen Moment…',
    home: 'Zu TRAZO'
  }
};

export default content;
