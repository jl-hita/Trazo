import { LandingContent, shotsOf } from '../landing.content';

const shot = shotsOf('en');

const content: LandingContent = {
  title: 'TRAZO · The strength training engine that adapts to you',
  description: 'TRAZO is not a template: it is an engine that builds your macrocycle, adjusts loads and sets session by session to how you respond, and explains every change. Planning by hand is free.',
  languageLabel: 'Language',
  eyebrow: 'ADAPTIVE TRAINING ENGINE',
  headA: 'Your plan is not a template.', headB: 'It is an engine that adapts to you',
  lead: 'TRAZO builds your full macrocycle, adjusts loads and sets session by session to how you respond, and explains every change. With fixed days or without them.',
  cta: 'Join the waitlist',
  loopTitle: 'A loop that closes every session',
  loopLead: 'Planning by hand is free. Automatic generation and adaptation are Premium.',
  loop: [
    { title: 'Generate', text: 'Goal, level, equipment and aches. The engine designs the macrocycle: phases, deloads, exercises and volume per muscle.' },
    { title: 'Train', text: 'You log sets, loads and RIR. You see what you did last time and the load that is due now.' },
    { title: 'Listen', text: 'When you finish you say how it went: recovery, effort, pain and how each muscle responded.' },
    { title: 'Adapt', text: 'It raises the load, cuts or adds sets, schedules a deload. Every new block starts from what you really lifted.' }
  ],
  pillarsTitle: 'What makes this engine different',
  pillars: [
    { title: 'Double progression per exercise', text: 'Reps first, then load: when you reach the top of the range with margin to spare, it goes up one step. If it is not time yet, it holds.' },
    { title: 'Volume per muscle', text: 'Sets are distributed towards the weekly volume each muscle needs, for your level and the time you have.' },
    { title: 'Recovery per muscle', text: 'Before suggesting your next session, it checks whether the muscles it will work have recovered.', premium: true },
    { title: 'Deloads when due', text: 'Scheduled light weeks, plus an extra deload if accumulated fatigue or falling performance shows up.' },
    { title: 'Pain comes first', text: 'If you report pain, it never raises the load and automates nothing: it flags it for you to review. And your joint aches rule out the exercises that aggravate them.' },
    { title: 'Every change, explained', text: 'It is not a black box: each adjustment comes with its reason and stays in your plan history.' }
  ],
  showcase: [
    {
      kicker: 'Start in a minute', title: 'Tell it how you train',
      text: 'Goal, level, equipment and aches. No endless questionnaires: the engine does the rest.',
      points: ['Muscle growth, strength or general fitness', 'The equipment you have and the areas to protect', 'Plan length and deload weeks the way you like'],
      premium: true,
      shots: [shot('10-wizard-goal', 'Goal and level'), shot('12-wizard-details', 'Equipment and aches')]
    },
    {
      kicker: 'Your week, your rules', title: 'With fixed days or without them',
      text: 'Pick a calendar or a continuous cycle. TRAZO tells you which session comes next and you train when you can.',
      points: ['Late sessions are never lost or skipped on their own', 'Recovery advice before you start', 'A lighter re-entry session if you come back after weeks away'],
      shots: [shot('11-wizard-flexible', 'No fixed days'), shot('14-plan-sequence', 'Next session')]
    },
    {
      kicker: 'Train', title: 'It knows what you did last time',
      text: 'Every exercise shows your previous performance and the suggested load. Logging a set takes two taps.',
      points: ['Last time and target load on every exercise', 'Built-in rest timer', 'Works even when you lose connection'],
      shots: [shot('16-session', 'Session in detail'), shot('17-workout', 'Set logging')]
    },
    {
      kicker: 'Adapt', title: 'After every session, a better plan',
      text: 'Finish the session and the engine tells you what it adjusted and why. Your progress, always in view.',
      points: ['What changed and the reason for each change', 'Records, streak and sets per muscle', 'The whole block, with loads fine-tuned week by week'],
      premium: true,
      shots: [shot('18-adaptation', 'How your plan adapts'), shot('19-progress', 'Your progress'), shot('15-block', 'Your block')]
    }
  ],
  featuresTitle: 'Everything you need to train with purpose',
  features: [
    { title: 'Plan your way', text: 'Build your macrocycles, mesocycles and routines, free and with no artificial limits.' },
    { title: 'Every session, clear', text: 'Sets, reps, RIR and rest for each exercise, with its image and technique at hand.' },
    { title: 'Swap exercises wisely', text: 'Valid alternatives for your level, equipment and limitations, without breaking the plan.' },
    { title: 'Frictionless logging', text: 'Log each set and use the built-in rest timer. It works even when you lose connection.' },
    { title: 'Exercise catalogue', text: 'Over 140 exercises with search, filters and illustrated detail pages.' },
    { title: 'Your account, protected', text: 'Sign in with Google or email, two-step verification and data encrypted on your device.' }
  ],
  pricingTitle: 'Start free. Automate when you are ready.',
  pricingLead: 'Manual planning is free, with no artificial limits. Premium is for people who want TRAZO to programme and adapt their training for them.',
  freeName: 'Free',
  freePrice: '€0',
  freePeriod: 'forever',
  freeTagline: 'You design and control your training.',
  freeFeatures: [
    'Build macrocycles manually',
    'Build mesocycles and routines manually',
    'Log workouts and view your history',
    'Full exercise catalogue with images'
  ],
  premiumName: 'Premium',
  premiumPrice: '€6.99',
  premiumPeriod: '/ month',
  premiumTagline: 'TRAZO programmes for you and adapts to you.',
  premiumFeatures: [
    'Everything in the free plan',
    'Automatic generation of your full plan: macrocycle, phases and deloads',
    'Exercises chosen for your profile, equipment and limitations',
    'Automatic adaptation of loads, sets and deloads from your feedback',
    'Every new block generated from your real progress'
  ],
  premiumAnnual: 'Or €49.99 per year · equivalent to €4.17/month',
  pricingNote: 'Planned launch pricing. Manual planning will remain free; automatic generation and adaptation will be Premium. Premium includes a 14-day free trial.',
  faqTitle: 'Frequently asked questions',
  faq: [
    { q: 'Is it artificial intelligence?', a: 'No. The engine is deterministic and built on training rules: double progression, weekly volume per muscle, recovery and deloads. There is no made-up text or hidden randomness: it applies known rules to your data and every decision comes with its explanation.' },
    { q: 'Can I train without fixed days?', a: 'Yes. You can choose a continuous cycle instead of a calendar: TRAZO tells you which session comes next and you train when you can. Late sessions are logged with their real date and are never skipped on their own. This option is free.' },
    { q: 'What is free and what is Premium?', a: 'Free: build your own macrocycles, mesocycles and routines, log workouts, view your history and use the catalogue. Premium: automatic generation of your plan and automatic adaptation of your workouts from your feedback.' },
    { q: 'What exactly does automatic generation do?', a: 'From your goal, level, days, time, equipment and limitations, TRAZO builds a macrocycle with its phases and deloads and generates the sessions of each block from what you have already trained.' },
    { q: 'How does my plan adapt?', a: 'Each time you log a session (sets, loads, RIR, recovery and pain), TRAZO adjusts the next ones: it changes the load, cuts sets, adds margin or schedules a deload when needed. It always explains what changed and why.' },
    { q: 'Do the plans I build myself adapt too?', a: 'Yes. With Premium, automatic adaptation also works on your manual plans. Without Premium your workouts are still saved, but the plan does not adjust by itself.' },
    { q: 'What happens if I cancel Premium?', a: 'You keep your history and your manual plans. A plan generated automatically stays readable, but you need Premium to keep using it and to generate the next block. You can archive it and build a manual one.' },
    { q: 'Can I cancel any time?', a: 'Yes, the subscription is managed through Google Play and you can cancel it at any time.' },
    { q: 'Does TRAZO replace a professional?', a: 'No. TRAZO provides general training plans and does not replace the advice of a healthcare professional or a coach.' }
  ],
  shotAlt: 'TRAZO app screenshot',
  howTitle: 'How it works',
  steps: [
    { title: 'Tell us how you train', text: 'Goal, level, available days and equipment.' },
    { title: 'Build or generate your plan', text: 'Create your plan manually, or let TRAZO generate your complete plan with Premium.' },
    { title: 'Train and log', text: 'Log your sessions. With Premium, TRAZO adapts the next ones to how it went.' }
  ],
  waitTitle: "We're getting ready to launch",
  waitText: "Leave your email and we'll let you know when TRAZO is available on Google Play. We'll also invite you to try it early, in the closed test. We'll only write to you about that.",
  emailLabel: 'Email of your Google account',
  emailHint: 'Use the email of the Google account on your phone: Google Play will use it to give you access to the test. We will send you a message to confirm it.',
  consent: 'I agree that TRAZO may use my email to notify me about the launch and invite me to try the app in the closed test on Google Play. I can withdraw this at any time. More in the',
  privacyLabel: 'privacy policy',
  send: 'Join waitlist',
  note: 'TRAZO provides general training plans and does not replace the advice of a healthcare professional.',
  ok: "Almost done: we've sent you an email. Open it and confirm your address to complete the sign-up.",
  invalid: 'Check your email and accept the consent.',
  later: 'Too many attempts. Please try again later.',
  fail: "We couldn't save your email. Please try again in a few minutes.",
  terms: 'Terms of use', contact: 'Contact', admin: 'Admin sign-in', skip: 'Skip to content',
  waitlistPages: {
    confirm: {
      title: 'Confirm your email',
      text: 'Press the button to confirm you want to be on the TRAZO list: we will tell you about the launch and invite you to try the app.',
      button: 'Confirm',
      done: 'Email confirmed',
      doneText: 'Thank you. We will write to you when there is news about the test and the launch.'
    },
    unsubscribe: {
      title: 'Unsubscribe',
      text: 'Press the button to delete your email from the TRAZO list. We will not write to you again.',
      button: 'Delete my data',
      done: 'Data deleted',
      doneText: 'We have deleted your email from the list. You will not get any more messages.'
    },
    failed: 'Link invalid or expired',
    failedText: 'This link is invalid, has expired or was already used. If you wanted to join, fill in the form again.',
    missing: 'Incomplete link',
    missingText: 'The link is missing its security code. Use the link in the email exactly as you received it.',
    wait: 'One moment…',
    home: 'Go to TRAZO'
  }
};

export default content;
