import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('fr');

const content: LandingContent = {
  title: 'TRAZO · Le moteur de musculation qui s’adapte à toi',
  description: 'TRAZO n’est pas un modèle : c’est un moteur qui génère ton macrocycle, ajuste charges et séries séance après séance selon tes réponses et explique chaque changement. Planifier à la main est gratuit.',
  languageLabel: 'Langue',
  eyebrow: 'MOTEUR D’ENTRAÎNEMENT ADAPTATIF',
  headA: 'Ton programme n’est pas un modèle.', headB: 'C’est un moteur qui s’adapte à toi',
  lead: 'TRAZO construit ton macrocycle complet, ajuste charges et séries séance après séance selon tes réponses et explique chaque changement. Avec ou sans jours fixes.',
  cta: 'Rejoindre la liste d’attente',
  loopTitle: 'Une boucle qui se referme à chaque séance',
  loopLead: 'Planifier à la main est gratuit. La génération et l’adaptation automatiques sont Premium.',
  loop: [
    { title: 'Générer', text: 'Objectif, niveau, équipement et gênes. Le moteur conçoit le macrocycle : phases, décharges, exercices et volume par muscle.' },
    { title: 'S’entraîner', text: 'Tu notes séries, charges et RIR. Tu vois ce que tu as fait la dernière fois et la charge du jour.' },
    { title: 'Écouter', text: 'À la fin, tu dis comment ça s’est passé : récupération, effort, douleur et réponse de chaque muscle.' },
    { title: 'Adapter', text: 'Il augmente la charge, retire ou ajoute des séries, programme une décharge. Chaque nouveau bloc part de ce que tu as réellement soulevé.' }
  ],
  pillarsTitle: 'Ce qui rend ce moteur différent',
  pillars: [
    { title: 'Double progression par exercice', text: 'D’abord les répétitions, puis la charge : en haut de la fourchette avec de la marge, elle monte d’un palier. Sinon, elle reste.' },
    { title: 'Volume par muscle', text: 'Les séries sont réparties vers le volume hebdomadaire nécessaire à chaque muscle, selon ton niveau et ton temps.' },
    { title: 'Récupération par muscle', text: 'Avant de proposer la séance suivante, il vérifie que les muscles concernés ont récupéré.', premium: true },
    { title: 'Des décharges au bon moment', text: 'Semaines légères programmées, plus une décharge si la fatigue s’accumule ou la performance baisse.' },
    { title: 'La douleur prime', text: 'Si tu signales une douleur, il n’augmente jamais la charge et n’automatise rien : il te prévient. Tes gênes articulaires écartent les exercices qui les aggravent.' },
    { title: 'Chaque changement expliqué', text: 'Pas de boîte noire : chaque ajustement a sa raison et reste dans l’historique du programme.' }
  ],
  showcase: [
    {
      kicker: 'Démarre en une minute', title: 'Dis-lui comment tu t’entraînes',
      text: 'Objectif, niveau, équipement et gênes. Pas de questionnaires sans fin : le moteur fait le reste.',
      points: ['Prise de muscle, force ou forme générale', 'Ton matériel et les zones à protéger', 'Durée du programme et décharges à ton goût'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Objectif et niveau'), shot('12-wizard-details', 'Matériel et gênes')]
    },
    {
      kicker: 'Ta semaine, tes règles', title: 'Avec ou sans jours fixes',
      text: 'Choisis un calendrier ou un cycle continu. TRAZO indique la prochaine séance et tu t’entraînes quand tu peux.',
      points: ['Les séances en retard ne sont ni perdues ni passées toutes seules', 'Avis de récupération avant de commencer', 'Séance de reprise allégée après plusieurs semaines d’arrêt'],
      shots: [shot('11-wizard-flexible', 'Sans jours fixes'), shot('14-plan-sequence', 'Séance suivante')]
    },
    {
      kicker: 'S’entraîner', title: 'Il sait ce que tu as fait la dernière fois',
      text: 'Chaque exercice montre ta performance précédente et la charge proposée. Noter une série prend deux gestes.',
      points: ['Dernière fois et charge cible sur chaque exercice', 'Minuteur de repos intégré', 'Fonctionne même sans connexion'],
      shots: [shot('16-session', 'La séance en détail'), shot('17-workout', 'Saisie des séries')]
    },
    {
      kicker: 'Adapter', title: 'Après chaque séance, un meilleur programme',
      text: 'Termine la séance : le moteur dit ce qu’il a ajusté et pourquoi. Tes progrès toujours visibles.',
      points: ['Ce qui a changé et pourquoi', 'Records, série en cours et séries par muscle', 'Le bloc complet, charges affinées semaine après semaine'],
      premium: true,
      shots: [shot('18-adaptation', 'Comment ton programme s’adapte'), shot('19-progress', 'Tes progrès'), shot('15-block', 'Ton bloc')]
    }
  ],
  featuresTitle: 'Tout pour toi entraîner avec méthode',
  features: [
    { title: 'Planifie à ta façon', text: 'Crée macrocycles, mésocycles et routines, gratuitement et sans limites artificielles.' },
    { title: 'Des séances claires', text: 'Séries, répétitions, RIR et repos de chaque exercice, avec image et technique à portée de main.' },
    { title: 'Change d’exercice intelligemment', text: 'Des alternatives adaptées à ton niveau, ton matériel et tes limites, sans casser le programme.' },
    { title: 'Saisie sans friction', text: 'Note chaque série et utilise le minuteur de repos. Fonctionne même hors connexion.' },
    { title: 'Catalogue d’exercices', text: 'Plus de 140 exercices avec recherche, filtres et fiches illustrées.' },
    { title: 'Un compte protégé', text: 'Connexion Google ou e-mail, validation en deux étapes et données chiffrées sur l’appareil.' }
  ],
  pricingTitle: 'Commence gratuitement. Automatise quand tu veux.',
  pricingLead: 'La planification manuelle est gratuite et sans limites artificielles. Premium est pour ceux qui veulent que TRAZO programme et adapte leur entraînement.',
  freeName: 'Gratuit',
  freePrice: '0 €',
  freePeriod: 'pour toujours',
  freeTagline: 'Tu conçois et pilote ton entraînement.',
  freeFeatures: [
    'Créer des macrocycles manuellement',
    'Créer des mésocycles et des routines manuellement',
    'Enregistrer les entraînements et consulter l’historique',
    'Catalogue complet d’exercices illustrés'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ mois',
  premiumTagline: 'TRAZO programme pour toi et s’adapte à toi.',
  premiumFeatures: [
    'Tout le plan gratuit',
    'Génération automatique du programme complet : macrocycle, phases et décharges',
    'Exercices choisis selon ton profil, ton matériel et tes limites',
    'Adaptation automatique des charges, séries et décharges selon tes retours',
    'Chaque nouveau bloc généré à partir de tes progrès réels'
  ],
  premiumAnnual: 'Ou 49,99 € par an · soit 4,17 €/mois',
  pricingNote: 'Tarifs prévus au lancement. La planification manuelle restera gratuite ; la génération et l’adaptation automatiques seront Premium. Premium inclut 14 jours d’essai gratuit.',
  faqTitle: 'Questions fréquentes',
  faq: [
    { q: 'Est-ce de l’intelligence artificielle ?', a: 'Non. Le moteur est déterministe et fondé sur des règles d’entraînement : double progression, volume hebdomadaire par muscle, récupération et décharges. Pas de texte inventé ni de hasard caché : il applique des règles connues à tes données et explique chaque décision.' },
    { q: 'Puis-je m’entraîner sans jours fixes ?', a: 'Oui. Tu peux choisir un cycle continu au lieu d’un calendrier : TRAZO indique la prochaine séance et tu t’entraînes quand tu peux. Les séances en retard sont enregistrées à leur date réelle et jamais passées toutes seules. Cette option est gratuite.' },
    { q: 'Qu’est-ce qui est gratuit et qu’est-ce qui est Premium ?', a: 'Gratuit : créer tes macrocycles, mésocycles et routines, enregistrer tes entraînements, consulter l’historique et utiliser le catalogue. Premium : la génération automatique du programme et l’adaptation automatique selon tes retours.' },
    { q: 'Que fait exactement la génération automatique ?', a: 'À partir de ton objectif, niveau, jours, temps, matériel et limites, TRAZO construit un macrocycle avec phases et décharges et génère les séances de chaque bloc à partir de ce que tu as déjà fait.' },
    { q: 'Comment mon programme s’adapte-t-il ?', a: 'À chaque séance enregistrée (séries, charges, RIR, récupération et douleur), TRAZO ajuste les suivantes : il change la charge, retire des séries, ajoute de la marge ou programme une décharge si nécessaire. Il explique toujours quoi et pourquoi.' },
    { q: 'Les programmes que je crée s’adaptent-ils aussi ?', a: 'Oui. Avec Premium, l’adaptation automatique fonctionne aussi sur tes programmes manuels. Sans Premium, tes entraînements sont conservés mais le programme ne s’ajuste pas seul.' },
    { q: 'Que se passe-t-il si je résilie Premium ?', a: 'Tu gardes ton historique et tes programmes manuels. Un programme généré automatiquement reste consultable, mais il faut Premium pour continuer à l’utiliser et générer le bloc suivant. Tu peux l’archiver et en créer un manuel.' },
    { q: 'Puis-je résilier à tout moment ?', a: 'Oui, l’abonnement est géré par Google Play et résiliable à tout moment.' },
    { q: 'TRAZO remplace-t-il un professionnel ?', a: 'Non. TRAZO propose des programmes indicatifs et ne remplace pas l’avis d’un professionnel de santé ou d’un entraîneur.' }
  ],
  shotAlt: 'Capture de l’application TRAZO',
  howTitle: 'Comment ça marche',
  steps: [
    { title: 'Dis-nous comment tu t’entraînes', text: 'Objectif, niveau, jours disponibles et matériel.' },
    { title: 'Crée ou génère ton programme', text: 'Construis-le à la main ou laisse TRAZO le générer entièrement avec Premium.' },
    { title: 'Entraîne-toi et note', text: 'Enregistre tes séances. Avec Premium, TRAZO adapte les suivantes selon le résultat.' }
  ],
  waitTitle: 'Nous préparons le lancement',
  waitText: 'Laisse-nous ton e-mail et nous te préviendrons quand TRAZO sera sur Google Play. Nous t’inviterons aussi à l’essayer avant, en test fermé. Nous ne t’écrirons que pour cela.',
  emailLabel: 'E-mail de ton compte Google',
  emailHint: 'Utilise l’adresse du compte Google de ton téléphone : Google Play s’en servira pour te donner accès au test. Nous t’enverrons un message de confirmation.',
  consent: 'J’accepte que TRAZO utilise mon e-mail pour m’informer du lancement et m’inviter à essayer l’application en test fermé sur Google Play. Je peux retirer mon accord à tout moment. Plus d’informations dans la',
  privacyLabel: 'politique de confidentialité (en anglais)',
  send: 'M’inscrire',
  note: 'TRAZO propose des programmes indicatifs et ne remplace pas l’avis d’un professionnel de santé.',
  ok: 'Presque fini : nous t’avons envoyé un e-mail. Ouvre-le et confirme ton adresse pour être inscrit.',
  invalid: 'Vérifie l’e-mail et accepte le consentement.',
  later: 'Trop de tentatives. Réessaie plus tard.',
  fail: 'Impossible d’enregistrer ton e-mail. Réessaie dans quelques minutes.',
  terms: 'Conditions d’utilisation (en anglais)', contact: 'Contact', admin: 'Accès administration', skip: 'Aller au contenu',
  waitlistPages: {
    confirm: {
      title: 'Confirme ton e-mail',
      text: 'Appuie sur le bouton pour confirmer que tu veux figurer sur la liste TRAZO : nous te préviendrons du lancement et t’inviterons à essayer l’application.',
      button: 'Confirmer',
      done: 'E-mail confirmé',
      doneText: 'Merci. Nous t’écrirons dès qu’il y aura du nouveau sur le test et le lancement.'
    },
    unsubscribe: {
      title: 'Se désinscrire',
      text: 'Appuie sur le bouton pour effacer ton e-mail de la liste TRAZO. Nous ne t’écrirons plus.',
      button: 'Effacer mes données',
      done: 'Données effacées',
      doneText: 'Nous avons effacé ton e-mail de la liste. Tu ne recevras plus de messages.'
    },
    failed: 'Lien non valide ou expiré',
    failedText: 'Ce lien n’est pas valide, a expiré ou a déjà servi. Si tu voulais t’inscrire, remplis à nouveau le formulaire.',
    missing: 'Lien incomplet',
    missingText: 'Il manque le code de sécurité. Utilise le lien de l’e-mail tel quel.',
    wait: 'Un instant…',
    home: 'Aller sur TRAZO'
  }
};

export default content;
