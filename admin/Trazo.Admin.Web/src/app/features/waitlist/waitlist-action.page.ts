import { Component, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

type Action = 'confirm' | 'unsubscribe';
type Lang = 'es' | 'en';
type Phase = 'ready' | 'working' | 'done' | 'failed' | 'missing';

const TEXT: Record<Lang, Record<Action, { title: string; text: string; button: string; done: string; doneText: string }> & {
  failed: string; failedText: string; missing: string; missingText: string; wait: string; home: string;
}> = {
  es: {
    confirm: {
      title: 'Confirma tu correo',
      text: 'Pulsa el botón para confirmar que quieres estar en la lista de TRAZO: te avisaremos del lanzamiento y te invitaremos a probar la app.',
      button: 'Confirmar',
      done: 'Correo confirmado',
      doneText: 'Gracias. Te escribiremos cuando haya novedades sobre la prueba y el lanzamiento.'
    },
    unsubscribe: {
      title: 'Darte de baja',
      text: 'Pulsa el botón para borrar tu correo de la lista de TRAZO. No te escribiremos más.',
      button: 'Borrar mis datos',
      done: 'Datos borrados',
      doneText: 'Hemos borrado tu correo de la lista. No recibirás más mensajes.'
    },
    failed: 'Enlace no válido o caducado',
    failedText: 'Este enlace no es válido, ha caducado o ya se ha usado. Si querías apuntarte, vuelve a rellenar el formulario.',
    missing: 'Enlace incompleto',
    missingText: 'Al enlace le falta su código de seguridad. Usa el enlace del correo tal como lo recibiste.',
    wait: 'Un momento…',
    home: 'Ir a TRAZO'
  },
  en: {
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

/**
 * The pages behind the links of the waitlist emails (`/waitlist/confirm` and `/waitlist/unsubscribe`). Nothing happens on load:
 * the person presses a button, so a mail scanner that merely opens the link cannot confirm or remove anybody.
 */
@Component({
  selector: 'trazo-waitlist-action-page',
  standalone: true,
  styles: [`
    :host { display:grid; place-items:center; min-height:100vh; padding:24px; background:#0d1119; color:#e8edf5; }
    section { display:grid; gap:14px; width:min(440px,100%); padding:30px; border:1px solid #293444; border-radius:12px; background:#131b27; }
    img { width:130px; } h1 { margin:0; font-size:22px; } p { margin:0; color:#aab6c7; line-height:1.55; }
    a, button { display:inline-block; padding:12px 16px; border:1px solid #6d9bd2; border-radius:7px; background:#3b6ea8; color:#fff; font:inherit; text-align:center; text-decoration:none; cursor:pointer; }
    button:disabled { opacity:.6; cursor:wait; }
  `],
  template: `
    <section>
      <img src="logo-dark.svg" alt="TRAZO">
      <h1>{{ title() }}</h1>
      <p role="status">{{ message() }}</p>
      @if (phase() === 'ready' || phase() === 'working') {
        <button type="button" (click)="run()" [disabled]="phase() === 'working'">{{ phase() === 'working' ? t.wait : t[action].button }}</button>
      } @else {
        <a href="/">{{ t.home }}</a>
      }
    </section>
  `
})
export class WaitlistActionPageComponent {
  private readonly http = inject(HttpClient);
  private token: string | null;

  protected readonly action: Action;
  protected readonly t: (typeof TEXT)[Lang];
  protected readonly phase = signal<Phase>('ready');

  constructor() {
    const route = inject(ActivatedRoute);
    this.action = route.snapshot.data['action'] === 'unsubscribe' ? 'unsubscribe' : 'confirm';
    this.t = TEXT[route.snapshot.queryParamMap.get('lang') === 'en' ? 'en' : 'es'];
    this.token = route.snapshot.queryParamMap.get('token');
    // The token stays in memory only: out of the address bar and the browser history.
    inject(Location).replaceState(`/waitlist/${this.action}`);
    if (!this.token) this.phase.set('missing');
  }

  protected title(): string {
    switch (this.phase()) {
      case 'done': return this.t[this.action].done;
      case 'failed': return this.t.failed;
      case 'missing': return this.t.missing;
      default: return this.t[this.action].title;
    }
  }

  protected message(): string {
    switch (this.phase()) {
      case 'done': return this.t[this.action].doneText;
      case 'failed': return this.t.failedText;
      case 'missing': return this.t.missingText;
      default: return this.t[this.action].text;
    }
  }

  protected async run(): Promise<void> {
    if (!this.token) return;
    this.phase.set('working');
    try {
      await firstValueFrom(this.http.post(`/api/v1/waitlist/${this.action}`, { token: this.token }));
      this.phase.set('done');
    } catch {
      this.phase.set('failed');
    }
  }
}
