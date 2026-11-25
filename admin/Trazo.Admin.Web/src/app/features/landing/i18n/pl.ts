import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('pl');

const content: LandingContent = {
  title: 'TRAZO · Silnik treningu siłowego, który dopasowuje się do Ciebie',
  description: 'TRAZO to nie szablon: to silnik, który generuje Twój makrocykl, koryguje ciężary i serie z sesji na sesję według Twoich reakcji i wyjaśnia każdą zmianę. Planowanie ręczne jest darmowe.',
  languageLabel: 'Język',
  eyebrow: 'ADAPTACYJNY SILNIK TRENINGOWY',
  headA: 'Twój plan to nie szablon.', headB: 'To silnik, który dopasowuje się do Ciebie',
  lead: 'TRAZO buduje Twój pełny makrocykl, koryguje ciężary i serie z sesji na sesję według Twoich reakcji i wyjaśnia każdą zmianę. Ze stałymi dniami lub bez.',
  cta: 'Chcę na listę oczekujących',
  loopTitle: 'Cykl, który domyka się w każdej sesji',
  loopLead: 'Planowanie ręczne jest darmowe. Automatyczne generowanie i adaptacja to Premium.',
  loop: [
    { title: 'Generuje', text: 'Cel, poziom, sprzęt i dolegliwości. Silnik projektuje makrocykl: fazy, odciążenia, ćwiczenia i objętość na mięsień.' },
    { title: 'Trenujesz', text: 'Zapisujesz serie, ciężary i RIR. Widzisz, co było ostatnio, i ciężar na teraz.' },
    { title: 'Słucha', text: 'Po treningu mówisz, jak Ci idzie: regeneracja, wysiłek, ból i jak poszło każdemu mięśniowi.' },
    { title: 'Dostosowuje', text: 'Zwiększa ciężar, odejmuje lub dodaje serie, planuje odciążenie. Każdy nowy blok zaczyna się od tego, co naprawdę udało Ci się podnieść.' }
  ],
  pillarsTitle: 'Co wyróżnia ten silnik',
  pillars: [
    { title: 'Podwójna progresja w każdym ćwiczeniu', text: 'Najpierw powtórzenia, potem ciężar: gdy dochodzisz do górnej granicy zakresu z zapasem, ciężar rośnie o stopień. Jeśli jeszcze nie czas, zostaje.' },
    { title: 'Objętość na mięsień', text: 'Serie rozkładają się w kierunku tygodniowej objętości, jakiej potrzebuje każdy mięsień, zależnie od poziomu i czasu, który masz.' },
    { title: 'Regeneracja na mięsień', text: 'Zanim zaproponuje kolejną sesję, sprawdza, czy mięśnie, które będą pracować, są już zregenerowane.', premium: true },
    { title: 'Odciążenia wtedy, gdy trzeba', text: 'Zaplanowane lżejsze tygodnie i dodatkowe odciążenie, jeśli pojawia się skumulowane zmęczenie lub spadek wyników.' },
    { title: 'Ból decyduje', text: 'Jeśli zgłosisz ból, nigdy nie zwiększa ciężaru ani niczego nie automatyzuje: ostrzega, abyś to sprawdził(a). A dolegliwości stawów wykluczają ćwiczenia, które je nasilają.' },
    { title: 'Każda zmiana wyjaśniona', text: 'To nie czarna skrzynka: każda korekta przychodzi z powodem i zostaje w historii planu.' }
  ],
  showcase: [
    {
      kicker: 'Zacznij w minutę', title: 'Opowiedz, jak trenujesz',
      text: 'Cel, poziom, sprzęt i dolegliwości. Bez niekończących się ankiet: resztę robi silnik.',
      points: ['Hipertrofia, siła lub ogólna forma', 'Sprzęt, który masz, i obszary do ochrony', 'Długość planu i tygodnie odciążenia według uznania'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Cel i poziom'), shot('12-wizard-details', 'Sprzęt i dolegliwości')]
    },
    {
      kicker: 'Twój tydzień, Twoje zasady', title: 'Ze stałymi dniami lub bez',
      text: 'Wybierz kalendarz albo ciągły cykl. TRAZO podpowiada następną sesję, a Ty trenujesz, kiedy możesz.',
      points: ['Zaległe sesje nie przepadają i same się nie pomijają', 'Ostrzeżenie o regeneracji przed startem', 'Lżejsza sesja powrotna po kilku tygodniach przerwy'],
      shots: [shot('11-wizard-flexible', 'Bez stałych dni'), shot('14-plan-sequence', 'Następna sesja')]
    },
    {
      kicker: 'Trening', title: 'Wie, co było ostatnio',
      text: 'Każde ćwiczenie pokazuje poprzedni wynik i proponowany ciężar. Zapisanie serii to dwa dotknięcia.',
      points: ['Ostatni raz i ciężar docelowy w każdym ćwiczeniu', 'Wbudowany minutnik przerw', 'Działa nawet po utracie połączenia'],
      shots: [shot('16-session', 'Szczegóły sesji'), shot('17-workout', 'Zapisywanie serii')]
    },
    {
      kicker: 'Adaptacja', title: 'Po każdej sesji lepszy plan',
      text: 'Kończysz sesję, a silnik mówi, co skorygował i dlaczego. Twoje postępy zawsze na widoku.',
      points: ['Co się zmieniło i powód każdej zmiany', 'Rekordy, seria tygodni i serie na mięsień', 'Cały blok z ciężarami dopracowywanymi co tydzień'],
      premium: true,
      shots: [shot('18-adaptation', 'Tak dopasowuje się Twój plan'), shot('19-progress', 'Twoje postępy'), shot('15-block', 'Twój blok')]
    }
  ],
  featuresTitle: 'Wszystko, czego potrzebujesz, by trenować z głową',
  features: [
    { title: 'Planuj po swojemu', text: 'Twórz makrocykle, mezocykle i rutyny — za darmo i bez sztucznych limitów.' },
    { title: 'Każda sesja jasna', text: 'Serie, powtórzenia, RIR i przerwy dla każdego ćwiczenia, z obrazkiem i techniką pod ręką.' },
    { title: 'Zamieniaj ćwiczenia z sensem', text: 'Zamienniki zgodne z poziomem, sprzętem i ograniczeniami, bez psucia planu.' },
    { title: 'Zapis bez tarcia', text: 'Notuj każdą serię i korzystaj z minutnika. Działa nawet po utracie połączenia.' },
    { title: 'Katalog ćwiczeń', text: 'Ponad 140 ćwiczeń z wyszukiwarką, filtrami i kartami z obrazkami.' },
    { title: 'Chronione konto', text: 'Logowanie przez Google lub e-mail, weryfikacja dwuetapowa i zaszyfrowane dane na urządzeniu.' }
  ],
  pricingTitle: 'Zacznij za darmo. Zautomatyzuj, kiedy chcesz.',
  pricingLead: 'Planowanie ręczne jest darmowe i bez sztucznych limitów. Premium jest dla tych, którzy chcą, by TRAZO programowało i dopasowywało trening.',
  freeName: 'Za darmo',
  freePrice: '0 €',
  freePeriod: 'na zawsze',
  freeTagline: 'Ty projektujesz i kontrolujesz trening.',
  freeFeatures: [
    'Ręczne tworzenie makrocykli',
    'Ręczne tworzenie mezocykli i rutyn',
    'Zapisywanie treningów i przegląd historii',
    'Pełny katalog ćwiczeń z obrazkami'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ mies.',
  premiumTagline: 'TRAZO programuje za Ciebie i dopasowuje się do Ciebie.',
  premiumFeatures: [
    'Wszystko z planu darmowego',
    'Automatyczne generowanie całego planu: makrocykl, fazy i odciążenia',
    'Ćwiczenia dobrane do profilu, sprzętu i ograniczeń',
    'Automatyczna adaptacja ciężarów, serii i odciążeń na podstawie Twoich odpowiedzi',
    'Każdy nowy blok generowany z Twoich rzeczywistych postępów'
  ],
  premiumAnnual: 'Lub 49,99 € rocznie · to 4,17 €/mies.',
  pricingNote: 'Ceny planowane na premierę. Planowanie ręczne pozostanie darmowe; automatyczne generowanie i adaptacja będą Premium. Premium obejmuje 14 dni darmowego okresu próbnego.',
  faqTitle: 'Częste pytania',
  faq: [
    { q: 'Czy to sztuczna inteligencja?', a: 'Nie. Silnik jest deterministyczny i opiera się na zasadach treningu: podwójnej progresji, tygodniowej objętości na mięsień, regeneracji i odciążeniach. Bez zmyślonego tekstu i ukrytej losowości: stosuje znane reguły do Twoich danych, a każda decyzja jest wyjaśniona.' },
    { q: 'Czy można trenować bez stałych dni?', a: 'Tak. Zamiast kalendarza możesz wybrać ciągły cykl: TRAZO podpowiada następną sesję, a Ty trenujesz, kiedy możesz. Zaległe sesje zapisują się z rzeczywistą datą i nigdy nie pomijają się same. Ta opcja jest darmowa.' },
    { q: 'Co jest darmowe, a co w Premium?', a: 'Darmowe: tworzenie makrocykli, mezocykli i rutyn, zapisywanie treningów, historia i katalog. Premium: automatyczne generowanie planu i jego automatyczna adaptacja na podstawie Twoich odpowiedzi.' },
    { q: 'Co dokładnie robi automatyczne generowanie?', a: 'Na podstawie celu, poziomu, dni, czasu, sprzętu i ograniczeń TRAZO buduje makrocykl z fazami i odciążeniami oraz generuje sesje każdego bloku na bazie tego, co już udało Ci się wytrenować.' },
    { q: 'Jak dopasowuje się mój plan?', a: 'Za każdym razem, gdy zapisujesz sesję (serie, ciężary, RIR, regenerację i ból), TRAZO koryguje kolejne: zmienia ciężar, odejmuje serie, dodaje zapas lub planuje odciążenie. Zawsze wyjaśnia co i dlaczego.' },
    { q: 'Czy moje własne plany też się dopasowują?', a: 'Tak. W Premium automatyczna adaptacja działa także w planach ręcznych. Bez Premium treningi nadal się zapisują, ale plan sam się nie koryguje.' },
    { q: 'Co się stanie po anulowaniu Premium?', a: 'Zachowujesz historię i plany ręczne. Plan wygenerowany automatycznie można przeglądać, ale do dalszego korzystania i generowania kolejnego bloku potrzebne jest Premium. Możesz go zarchiwizować i utworzyć ręczny.' },
    { q: 'Czy mogę anulować w każdej chwili?', a: 'Tak, subskrypcją zarządza się w Google Play i można ją anulować w dowolnym momencie.' },
    { q: 'Czy TRAZO zastępuje specjalistę?', a: 'Nie. TRAZO oferuje orientacyjne plany treningowe i nie zastępuje porady pracownika ochrony zdrowia ani trenera.' }
  ],
  shotAlt: 'Zrzut ekranu aplikacji TRAZO',
  howTitle: 'Jak to działa',
  steps: [
    { title: 'Opowiedz, jak trenujesz', text: 'Cel, poziom, dostępne dni i sprzęt.' },
    { title: 'Zaprojektuj lub wygeneruj plan', text: 'Zbuduj go ręcznie albo w Premium pozwól TRAZO wygenerować całość.' },
    { title: 'Trenuj i zapisuj', text: 'Zapisuj sesje. W Premium TRAZO dopasowuje kolejne do tego, jak Ci poszło.' }
  ],
  waitTitle: 'Przygotowujemy premierę',
  waitText: 'Zostaw swój e-mail, a damy znać, gdy TRAZO pojawi się w Google Play. Zaprosimy Cię też do wcześniejszego, zamkniętego testu. Napiszemy tylko w tej sprawie.',
  emailLabel: 'E-mail Twojego konta Google',
  emailHint: 'Użyj adresu konta Google z telefonu: Google Play wykorzysta go, aby dać Ci dostęp do testu. Wyślemy wiadomość z prośbą o potwierdzenie.',
  consent: 'Zgadzam się, aby TRAZO użyło mojego adresu e-mail do powiadomienia o premierze i zaproszenia do zamkniętego testu aplikacji w Google Play. Mogę wycofać zgodę w każdej chwili. Więcej informacji:',
  privacyLabel: 'polityka prywatności (po angielsku)',
  send: 'Zapisz mnie',
  note: 'TRAZO oferuje orientacyjne plany treningowe i nie zastępuje porady pracownika ochrony zdrowia.',
  ok: 'Prawie gotowe: wysłaliśmy Ci e-mail. Otwórz go i potwierdź adres, aby się zapisać.',
  invalid: 'Sprawdź e-mail i zaakceptuj zgodę.',
  later: 'Zbyt wiele prób. Spróbuj ponownie później.',
  fail: 'Nie udało się zapisać Twojego adresu. Spróbuj ponownie za kilka minut.',
  terms: 'Warunki korzystania (po angielsku)', contact: 'Kontakt', admin: 'Logowanie administratora', skip: 'Przejdź do treści',
  waitlistPages: {
    confirm: {
      title: 'Potwierdź swój e-mail',
      text: 'Naciśnij przycisk, aby potwierdzić, że chcesz być na liście TRAZO: damy znać o premierze i zaprosimy do wypróbowania aplikacji.',
      button: 'Potwierdź',
      done: 'E-mail potwierdzony',
      doneText: 'Dziękujemy. Napiszemy, gdy będą wieści o teście i premierze.'
    },
    unsubscribe: {
      title: 'Wypisz się',
      text: 'Naciśnij przycisk, aby usunąć swój e-mail z listy TRAZO. Więcej nie napiszemy.',
      button: 'Usuń moje dane',
      done: 'Dane usunięte',
      doneText: 'Usunęliśmy Twój adres z listy. Nie otrzymasz więcej wiadomości.'
    },
    failed: 'Nieprawidłowy lub wygasły link',
    failedText: 'Ten link jest nieprawidłowy, wygasł lub został już użyty. Jeśli chodziło o zapis, wypełnij formularz ponownie.',
    missing: 'Niepełny link',
    missingText: 'W linku brakuje kodu zabezpieczającego. Użyj linku z wiadomości dokładnie tak, jak go otrzymano.',
    wait: 'Chwileczkę…',
    home: 'Przejdź do TRAZO'
  }
};

export default content;
