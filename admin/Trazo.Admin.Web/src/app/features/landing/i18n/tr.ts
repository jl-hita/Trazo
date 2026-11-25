import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('tr');

const content: LandingContent = {
  title: 'TRAZO · Sana uyum sağlayan kuvvet antrenmanı motoru',
  description: 'TRAZO bir şablon değil: makrodöngünü oluşturan, verdiğin yanıta göre yükleri ve setleri seans seans ayarlayan ve her değişikliği açıklayan bir motor. Elle planlama ücretsiz.',
  languageLabel: 'Dil',
  eyebrow: 'UYARLANABİLİR ANTRENMAN MOTORU',
  headA: 'Planın bir şablon değil.', headB: 'Sana uyum sağlayan bir motor',
  lead: 'TRAZO tüm makrodöngünü kurar, verdiğin yanıta göre yükleri ve setleri seans seans ayarlar ve her değişikliği açıklar. Sabit günlerle veya günsüz.',
  cta: 'Bekleme listesine katılmak istiyorum',
  loopTitle: 'Her seansta kapanan bir döngü',
  loopLead: 'Elle planlama ücretsiz. Otomatik oluşturma ve uyarlama Premium.',
  loop: [
    { title: 'Oluşturur', text: 'Hedef, seviye, ekipman ve rahatsızlıklar. Motor makrodöngüyü tasarlar: evreler, hafifletmeler, egzersizler ve kas başına hacim.' },
    { title: 'Antrenman yaparsın', text: 'Setleri, yükleri ve RIR’ı kaydedersin. Geçen sefer ne yaptığını ve şimdiki yükü görürsün.' },
    { title: 'Dinler', text: 'Bitirince gidişatını anlatırsın: toparlanma, zorlanma, ağrı ve her kasın nasıl gittiği.' },
    { title: 'Uyarlar', text: 'Yükü artırır, set çıkarır veya ekler, hafifletme planlar. Her yeni blok gerçekten kaldırdığından başlar.' }
  ],
  pillarsTitle: 'Bu motoru farklı kılan',
  pillars: [
    { title: 'Egzersiz başına çift ilerleme', text: 'Önce tekrar, sonra yük: aralığın üst sınırına payla ulaştığında bir basamak yükselir. Henüz zamanı değilse aynı kalır.' },
    { title: 'Kas başına hacim', text: 'Setler, seviyene ve zamanına göre her kasın ihtiyaç duyduğu haftalık hacme doğru dağıtılır.' },
    { title: 'Kas başına toparlanma', text: 'Sıradaki seansı önermeden önce çalışacak kasların toparlanıp toparlanmadığını kontrol eder.', premium: true },
    { title: 'Gerektiğinde hafifletme', text: 'Planlı hafif haftalar ve birikmiş yorgunluk ya da düşen performans görülürse ek hafifletme.' },
    { title: 'Karar ağrının', text: 'Ağrı bildirirsen yükü asla artırmaz ve hiçbir şeyi otomatikleştirmez: kontrol etmen için uyarır. Eklem rahatsızlıkların da onları artıran egzersizleri dışarıda bırakır.' },
    { title: 'Her değişiklik açıklanır', text: 'Kapalı kutu değil: her ayarlama nedeniyle gelir ve plan geçmişinde kalır.' }
  ],
  showcase: [
    {
      kicker: 'Bir dakikada başla', title: 'Nasıl antrenman yaptığını anlat',
      text: 'Hedef, seviye, ekipman ve rahatsızlıklar. Bitmeyen anketler yok: gerisini motor yapar.',
      points: ['Hipertrofi, kuvvet veya genel form', 'Elindeki ekipman ve korunacak bölgeler', 'Plan süresi ve hafifletme haftaları sana göre'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Hedef ve seviye'), shot('12-wizard-details', 'Ekipman ve rahatsızlıklar')]
    },
    {
      kicker: 'Haftan, kuralların', title: 'Sabit günlerle veya günsüz',
      text: 'Takvim ya da sürekli döngü seç. TRAZO sıradaki seansı söyler, sen uygun olduğunda antrenman yaparsın.',
      points: ['Gecikmiş seanslar kaybolmaz, kendiliğinden atlanmaz', 'Başlamadan önce toparlanma uyarısı', 'Haftalar sonra dönersen daha hafif dönüş seansı'],
      shots: [shot('11-wizard-flexible', 'Sabit gün olmadan'), shot('14-plan-sequence', 'Sıradaki seans')]
    },
    {
      kicker: 'Antrenman', title: 'Geçen sefer ne yaptığını bilir',
      text: 'Her egzersiz önceki performansını ve önerilen yükü gösterir. Set kaydetmek iki dokunuş.',
      points: ['Her egzersizde son sefer ve hedef yük', 'Yerleşik dinlenme sayacı', 'Bağlantı kopsa da çalışır'],
      shots: [shot('16-session', 'Seans ayrıntıları'), shot('17-workout', 'Set kaydı')]
    },
    {
      kicker: 'Uyarlama', title: 'Her seanstan sonra daha iyi bir plan',
      text: 'Seansı bitir, motor neyi neden ayarladığını söylesin. İlerlemen hep göz önünde.',
      points: ['Ne değişti ve her değişikliğin nedeni', 'Rekorlar, seri ve kas başına setler', 'Yükleri haftadan haftaya incelen tüm blok'],
      premium: true,
      shots: [shot('18-adaptation', 'Planın böyle uyarlanır'), shot('19-progress', 'İlerlemen'), shot('15-block', 'Bloğun')]
    }
  ],
  featuresTitle: 'Bilinçli antrenman için gereken her şey',
  features: [
    { title: 'Kendi tarzında planla', text: 'Makrodöngü, mezodöngü ve rutin oluştur; ücretsiz ve yapay sınır olmadan.' },
    { title: 'Her seans net', text: 'Her egzersizin seti, tekrarı, RIR’ı ve dinlenmesi; görsel ve teknik elinin altında.' },
    { title: 'Egzersizleri bilinçli değiştir', text: 'Planı bozmadan seviyene, ekipmanına ve kısıtlamalarına uygun alternatifler.' },
    { title: 'Zahmetsiz kayıt', text: 'Her seti not et ve sayacı kullan. Bağlantı kopsa da çalışır.' },
    { title: 'Egzersiz kataloğu', text: 'Arama, filtre ve görselli kartlarla 140’tan fazla egzersiz.' },
    { title: 'Korunan hesabın', text: 'Google veya e-posta ile giriş, iki adımlı doğrulama ve cihazda şifreli veri.' }
  ],
  pricingTitle: 'Ücretsiz başla. İstediğinde otomatikleştir.',
  pricingLead: 'Elle planlama ücretsiz ve yapay sınır yok. Premium, antrenmanı TRAZO’nun programlayıp uyarlamasını isteyenler için.',
  freeName: 'Ücretsiz',
  freePrice: '0 €',
  freePeriod: 'sonsuza dek',
  freeTagline: 'Antrenmanı sen tasarlar ve yönetirsin.',
  freeFeatures: [
    'Elle makrodöngü oluşturma',
    'Elle mezodöngü ve rutin oluşturma',
    'Antrenman kaydı ve geçmiş',
    'Görselli tam egzersiz kataloğu'
  ],
  premiumName: 'Premium',
  premiumPrice: '6,99 €',
  premiumPeriod: '/ ay',
  premiumTagline: 'TRAZO senin için programlar ve sana uyum sağlar.',
  premiumFeatures: [
    'Ücretsiz plandaki her şey',
    'Tüm planın otomatik oluşturulması: makrodöngü, evreler ve hafifletmeler',
    'Profiline, ekipmanına ve kısıtlamalarına göre seçilen egzersizler',
    'Geri bildirimine göre yük, set ve hafifletmelerin otomatik uyarlanması',
    'Gerçek ilerlemenden oluşturulan her yeni blok'
  ],
  premiumAnnual: 'Veya yıllık 49,99 € · aylık 4,17 €’ya denk',
  pricingNote: 'Lansman için öngörülen fiyatlar. Elle planlama ücretsiz kalacak; otomatik oluşturma ve uyarlama Premium olacak. Premium 14 gün ücretsiz deneme içerir.',
  faqTitle: 'Sık sorulan sorular',
  faq: [
    { q: 'Bu yapay zekâ mı?', a: 'Hayır. Motor deterministiktir ve antrenman kurallarına dayanır: çift ilerleme, kas başına haftalık hacim, toparlanma ve hafifletmeler. Uydurulmuş metin veya gizli rastlantı yok: bilinen kuralları verilerine uygular ve her karar açıklanır.' },
    { q: 'Sabit gün olmadan antrenman yapabilir miyim?', a: 'Evet. Takvim yerine sürekli döngü seçebilirsin: TRAZO sıradaki seansı söyler, uygun olduğunda antrenman yaparsın. Gecikmiş seanslar gerçek tarihleriyle kaydedilir ve kendiliğinden atlanmaz. Bu seçenek ücretsiz.' },
    { q: 'Ne ücretsiz, ne Premium?', a: 'Ücretsiz: makrodöngü, mezodöngü ve rutin oluşturma, antrenman kaydı, geçmiş ve katalog. Premium: planın otomatik oluşturulması ve geri bildirimine göre otomatik uyarlanması.' },
    { q: 'Otomatik oluşturma tam olarak ne yapar?', a: 'Hedefine, seviyene, günlerine, süreye, ekipmana ve kısıtlamalarına göre TRAZO evreli ve hafifletmeli bir makrodöngü kurar ve her bloğun seanslarını şimdiye kadar yaptıklarından oluşturur.' },
    { q: 'Planım nasıl uyarlanır?', a: 'Bir seans kaydettiğin her seferinde (setler, yükler, RIR, toparlanma ve ağrı) TRAZO sonrakileri ayarlar: yükü değiştirir, set azaltır, pay ekler veya hafifletme planlar. Neyi neden yaptığını her zaman açıklar.' },
    { q: 'Kendi oluşturduğum planlar da uyarlanır mı?', a: 'Evet. Premium ile otomatik uyarlama elle yaptığın planlarda da çalışır. Premium olmadan antrenmanların yine kaydedilir ama plan kendiliğinden ayarlanmaz.' },
    { q: 'Premium’u iptal edersem ne olur?', a: 'Geçmişin ve elle yaptığın planlar kalır. Otomatik oluşturulan plan görüntülenebilir, ancak kullanmaya devam etmek ve sonraki bloğu oluşturmak için Premium gerekir. Onu arşivleyip elle bir plan yapabilirsin.' },
    { q: 'İstediğim zaman iptal edebilir miyim?', a: 'Evet, abonelik Google Play’den yönetilir ve istediğin an iptal edebilirsin.' },
    { q: 'TRAZO bir uzmanın yerini tutar mı?', a: 'Hayır. TRAZO yol gösterici antrenman planları sunar; bir sağlık uzmanının veya antrenörün tavsiyesinin yerini tutmaz.' }
  ],
  shotAlt: 'TRAZO uygulaması ekran görüntüsü',
  howTitle: 'Nasıl çalışır',
  steps: [
    { title: 'Nasıl antrenman yaptığını anlat', text: 'Hedef, seviye, uygun günler ve ekipman.' },
    { title: 'Planını tasarla veya oluştur', text: 'Elle kur ya da Premium ile tamamını TRAZO’ya bırak.' },
    { title: 'Antrenman yap ve kaydet', text: 'Seanslarını kaydet. Premium ile TRAZO sonrakileri gidişatına göre uyarlar.' }
  ],
  waitTitle: 'Lansmana hazırlanıyoruz',
  waitText: 'E-postanı bırak, TRAZO Google Play’e geldiğinde haber verelim. Ayrıca daha önce kapalı testte denemeye davet edeceğiz. Yalnızca bunun için yazacağız.',
  emailLabel: 'Google hesabının e-postası',
  emailHint: 'Telefonundaki Google hesabının e-postasını kullan: Google Play teste erişimi bununla verecek. Onaylaman için bir mesaj göndereceğiz.',
  consent: 'TRAZO’nun e-postamı lansmanı bildirmek ve uygulamayı Google Play’deki kapalı testte denemeye davet etmek için kullanmasını kabul ediyorum. Onayımı istediğim zaman geri çekebilirim. Ayrıntılar:',
  privacyLabel: 'gizlilik politikası (İngilizce)',
  send: 'Listeye katıl',
  note: 'TRAZO yol gösterici antrenman planları sunar; bir sağlık uzmanının tavsiyesinin yerini tutmaz.',
  ok: 'Neredeyse tamam: sana bir e-posta gönderdik. Aç ve listeye katılmak için adresini onayla.',
  invalid: 'E-postayı kontrol et ve onayı kabul et.',
  later: 'Çok fazla deneme. Daha sonra tekrar dene.',
  fail: 'E-postanı kaydedemedik. Birkaç dakika sonra tekrar dene.',
  terms: 'Kullanım koşulları (İngilizce)', contact: 'İletişim', admin: 'Yönetici girişi', skip: 'İçeriğe geç',
  waitlistPages: {
    confirm: {
      title: 'E-postanı onayla',
      text: 'TRAZO listesinde olmak istediğini onaylamak için düğmeye bas: lansmanı haber vereceğiz ve uygulamayı denemeye davet edeceğiz.',
      button: 'Onayla',
      done: 'E-posta onaylandı',
      doneText: 'Teşekkürler. Test ve lansmanla ilgili haber olunca yazacağız.'
    },
    unsubscribe: {
      title: 'Listeden ayrıl',
      text: 'E-postanı TRAZO listesinden silmek için düğmeye bas. Sana artık yazmayacağız.',
      button: 'Verilerimi sil',
      done: 'Veriler silindi',
      doneText: 'E-postanı listeden sildik. Artık mesaj almayacaksın.'
    },
    failed: 'Geçersiz veya süresi dolmuş bağlantı',
    failedText: 'Bu bağlantı geçersiz, süresi dolmuş veya zaten kullanılmış. Listeye katılmak istiyorsan formu yeniden doldur.',
    missing: 'Eksik bağlantı',
    missingText: 'Bağlantıda güvenlik kodu eksik. E-postadaki bağlantıyı aldığın gibi kullan.',
    wait: 'Bir saniye…',
    home: 'TRAZO’ya git'
  }
};

export default content;
