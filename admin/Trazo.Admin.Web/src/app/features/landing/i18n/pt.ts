import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('pt');

const content: LandingContent = {
  title: 'TRAZO · O motor de treino de força que se adapta a você',
  description: 'O TRAZO não é um modelo pronto: é um motor que gera seu macrociclo, ajusta cargas e séries sessão a sessão conforme sua resposta e explica cada mudança. Planejar manualmente é grátis.',
  languageLabel: 'Idioma',
  eyebrow: 'MOTOR DE TREINO ADAPTATIVO',
  headA: 'Seu plano não é um modelo pronto.', headB: 'É um motor que se adapta a você',
  lead: 'O TRAZO monta seu macrociclo completo, ajusta cargas e séries sessão a sessão conforme sua resposta e explica cada mudança. Com dias fixos ou sem eles.',
  cta: 'Quero entrar na lista de espera',
  loopTitle: 'Um ciclo que se fecha a cada sessão',
  loopLead: 'Planejar manualmente é grátis. A geração e a adaptação automáticas são Premium.',
  loop: [
    { title: 'Gera', text: 'Objetivo, nível, equipamento e desconfortos. O motor desenha o macrociclo: fases, deloads, exercícios e volume por músculo.' },
    { title: 'Treina', text: 'Você registra séries, cargas e RIR. Vê o que fez da última vez e a carga de agora.' },
    { title: 'Escuta', text: 'Ao terminar, você conta como foi: recuperação, esforço, dor e como cada músculo respondeu.' },
    { title: 'Adapta', text: 'Aumenta a carga, tira ou acrescenta séries, programa um deload. Cada novo bloco parte do que você realmente levantou.' }
  ],
  pillarsTitle: 'O que torna este motor diferente',
  pillars: [
    { title: 'Dupla progressão por exercício', text: 'Primeiro repetições, depois carga: quando você chega ao topo da faixa com folga, ela sobe um degrau. Se ainda não é hora, mantém.' },
    { title: 'Volume por músculo', text: 'As séries são distribuídas rumo ao volume semanal que cada músculo precisa, conforme seu nível e seu tempo.' },
    { title: 'Recuperação por músculo', text: 'Antes de propor a próxima sessão, ele verifica se os músculos envolvidos já se recuperaram.', premium: true },
    { title: 'Deload na hora certa', text: 'Semanas leves programadas e um deload extra se aparecer fadiga acumulada ou queda de desempenho.' },
    { title: 'A dor manda', text: 'Se você relatar dor, ele nunca aumenta a carga nem automatiza nada: avisa para você revisar. E seus desconfortos articulares excluem os exercícios que os agravam.' },
    { title: 'Cada mudança, explicada', text: 'Não é uma caixa-preta: cada ajuste vem com o motivo e fica no histórico do plano.' }
  ],
  showcase: [
    {
      kicker: 'Comece em um minuto', title: 'Conte como você treina',
      text: 'Objetivo, nível, equipamento e desconfortos. Sem questionários intermináveis: o motor faz o resto.',
      points: ['Hipertrofia, força ou condicionamento geral', 'O equipamento que você tem e as áreas a proteger', 'Duração do plano e semanas de deload do seu jeito'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Objetivo e nível'), shot('12-wizard-details', 'Equipamento e desconfortos')]
    },
    {
      kicker: 'Sua semana, suas regras', title: 'Com dias fixos ou sem eles',
      text: 'Escolha um calendário ou um ciclo contínuo. O TRAZO diz qual é a próxima sessão e você treina quando puder.',
      points: ['Sessões atrasadas não se perdem nem são puladas sozinhas', 'Aviso de recuperação antes de começar', 'Sessão de retorno mais leve se você voltar depois de semanas'],
      shots: [shot('11-wizard-flexible', 'Sem dias fixos'), shot('14-plan-sequence', 'Próxima sessão')]
    },
    {
      kicker: 'Treine', title: 'Ele sabe o que você fez da última vez',
      text: 'Cada exercício mostra seu desempenho anterior e a carga sugerida. Registrar uma série leva dois toques.',
      points: ['Última vez e carga alvo em cada exercício', 'Cronômetro de descanso integrado', 'Funciona mesmo sem conexão'],
      shots: [shot('16-session', 'A sessão em detalhes'), shot('17-workout', 'Registro de séries')]
    },
    {
      kicker: 'Adapte', title: 'Depois de cada sessão, um plano melhor',
      text: 'Termine a sessão e o motor conta o que ajustou e por quê. Seu progresso sempre à vista.',
      points: ['O que mudou e o motivo de cada mudança', 'Recordes, sequência e séries por músculo', 'O bloco completo, com cargas refinadas semana a semana'],
      premium: true,
      shots: [shot('18-adaptation', 'Como seu plano se adapta'), shot('19-progress', 'Seu progresso'), shot('15-block', 'Seu bloco')]
    }
  ],
  featuresTitle: 'Tudo o que você precisa para treinar com critério',
  features: [
    { title: 'Planeje do seu jeito', text: 'Crie macrociclos, mesociclos e rotinas, de graça e sem limites artificiais.' },
    { title: 'Cada sessão, clara', text: 'Séries, repetições, RIR e descanso de cada exercício, com imagem e técnica à mão.' },
    { title: 'Troque exercícios com critério', text: 'Alternativas válidas para seu nível, equipamento e limitações, sem quebrar o plano.' },
    { title: 'Registro sem atrito', text: 'Anote cada série e use o cronômetro de descanso. Funciona mesmo sem conexão.' },
    { title: 'Catálogo de exercícios', text: 'Mais de 140 exercícios com busca, filtros e fichas com imagens.' },
    { title: 'Sua conta, protegida', text: 'Acesso com Google ou e-mail, verificação em duas etapas e dados criptografados no aparelho.' }
  ],
  pricingTitle: 'Comece grátis. Automatize quando quiser.',
  pricingLead: 'O planejamento manual é grátis e sem limites artificiais. O Premium é para quem quer que o TRAZO programe e adapte o treino.',
  freeName: 'Grátis',
  freePrice: '€ 0',
  freePeriod: 'para sempre',
  freeTagline: 'Você desenha e controla seu treino.',
  freeFeatures: [
    'Criar macrociclos manualmente',
    'Criar mesociclos e rotinas manualmente',
    'Registrar treinos e consultar o histórico',
    'Catálogo completo de exercícios com imagens'
  ],
  premiumName: 'Premium',
  premiumPrice: '€ 6,99',
  premiumPeriod: '/ mês',
  premiumTagline: 'O TRAZO programa por você e se adapta a você.',
  premiumFeatures: [
    'Tudo do plano grátis',
    'Geração automática do plano completo: macrociclo, fases e deloads',
    'Exercícios escolhidos para seu perfil, equipamento e limitações',
    'Adaptação automática de cargas, séries e deloads com base no seu feedback',
    'Cada novo bloco gerado a partir do seu progresso real'
  ],
  premiumAnnual: 'Ou € 49,99 por ano · equivale a € 4,17/mês',
  pricingNote: 'Preços previstos para o lançamento. O planejamento manual continuará grátis; a geração e a adaptação automáticas serão Premium. O Premium inclui 14 dias de teste grátis.',
  faqTitle: 'Perguntas frequentes',
  faq: [
    { q: 'É inteligência artificial?', a: 'Não. O motor é determinístico e se baseia em regras de treino: dupla progressão, volume semanal por músculo, recuperação e deload. Não há texto inventado nem acaso escondido: ele aplica regras conhecidas aos seus dados e cada decisão vem com explicação.' },
    { q: 'Posso treinar sem dias fixos?', a: 'Sim. Você pode escolher um ciclo contínuo em vez de um calendário: o TRAZO diz qual é a próxima sessão e você treina quando puder. Sessões atrasadas são registradas com a data real e nunca são puladas sozinhas. Essa opção é grátis.' },
    { q: 'O que é grátis e o que é Premium?', a: 'Grátis: criar seus macrociclos, mesociclos e rotinas, registrar treinos, consultar o histórico e usar o catálogo. Premium: a geração automática do plano e a adaptação automática dos treinos com base no seu feedback.' },
    { q: 'O que exatamente faz a geração automática?', a: 'A partir do seu objetivo, nível, dias, tempo, equipamento e limitações, o TRAZO monta um macrociclo com fases e deloads e gera as sessões de cada bloco a partir do que você já treinou.' },
    { q: 'Como meu plano se adapta?', a: 'Cada vez que você registra uma sessão (séries, cargas, RIR, recuperação e dor), o TRAZO ajusta as seguintes: muda a carga, reduz séries, dá mais folga ou programa um deload se preciso. Sempre explica o que mudou e por quê.' },
    { q: 'Os planos que eu crio também se adaptam?', a: 'Sim. Com o Premium, a adaptação automática também funciona nos planos manuais. Sem Premium, seus treinos continuam salvos, mas o plano não se ajusta sozinho.' },
    { q: 'O que acontece se eu cancelar o Premium?', a: 'Você mantém o histórico e os planos manuais. Um plano gerado automaticamente continua disponível para consulta, mas é preciso Premium para seguir usando e gerar o próximo bloco. Você pode arquivá-lo e criar um manual.' },
    { q: 'Posso cancelar quando quiser?', a: 'Sim, a assinatura é gerenciada pelo Google Play e pode ser cancelada a qualquer momento.' },
    { q: 'O TRAZO substitui um profissional?', a: 'Não. O TRAZO oferece planos de treino orientativos e não substitui o conselho de um profissional de saúde ou de um treinador.' }
  ],
  shotAlt: 'Captura do app TRAZO',
  howTitle: 'Como funciona',
  steps: [
    { title: 'Conte como você treina', text: 'Objetivo, nível, dias disponíveis e equipamento.' },
    { title: 'Desenhe ou gere seu plano', text: 'Monte o planejamento manualmente ou deixe o TRAZO gerar o plano completo com o Premium.' },
    { title: 'Treine e registre', text: 'Registre as sessões. Com o Premium, o TRAZO adapta as próximas conforme o resultado.' }
  ],
  waitTitle: 'Estamos preparando o lançamento',
  waitText: 'Deixe seu e-mail e avisaremos quando o TRAZO estiver no Google Play. Também convidaremos você a testar antes, no teste fechado. Só escreveremos sobre isso.',
  emailLabel: 'E-mail da sua conta do Google',
  emailHint: 'Use o e-mail da conta do Google do seu celular: o Google Play vai usá-la para dar acesso ao teste. Enviaremos uma mensagem para confirmar.',
  consent: 'Concordo que o TRAZO use meu e-mail para avisar sobre o lançamento e me convidar a testar o app no teste fechado do Google Play. Posso retirar o consentimento quando quiser. Mais informações na',
  privacyLabel: 'política de privacidade (em inglês)',
  send: 'Entrar na lista',
  note: 'O TRAZO oferece planos de treino orientativos e não substitui o conselho de um profissional de saúde.',
  ok: 'Quase lá: enviamos um e-mail. Abra e confirme seu endereço para entrar na lista.',
  invalid: 'Confira o e-mail e aceite o consentimento.',
  later: 'Muitas tentativas. Tente novamente mais tarde.',
  fail: 'Não conseguimos salvar seu e-mail. Tente novamente em alguns minutos.',
  terms: 'Termos de uso (em inglês)', contact: 'Contato', admin: 'Acesso administrativo', skip: 'Ir para o conteúdo',
  waitlistPages: {
    confirm: {
      title: 'Confirme seu e-mail',
      text: 'Toque no botão para confirmar que quer estar na lista do TRAZO: avisaremos do lançamento e convidaremos você a testar o app.',
      button: 'Confirmar',
      done: 'E-mail confirmado',
      doneText: 'Obrigado. Escreveremos quando houver novidades sobre o teste e o lançamento.'
    },
    unsubscribe: {
      title: 'Sair da lista',
      text: 'Toque no botão para apagar seu e-mail da lista do TRAZO. Não escreveremos mais.',
      button: 'Apagar meus dados',
      done: 'Dados apagados',
      doneText: 'Apagamos seu e-mail da lista. Você não receberá mais mensagens.'
    },
    failed: 'Link inválido ou expirado',
    failedText: 'Este link não é válido, expirou ou já foi usado. Se queria entrar na lista, preencha o formulário novamente.',
    missing: 'Link incompleto',
    missingText: 'Falta o código de segurança no link. Use o link do e-mail exatamente como recebeu.',
    wait: 'Um momento…',
    home: 'Ir para o TRAZO'
  }
};

export default content;
