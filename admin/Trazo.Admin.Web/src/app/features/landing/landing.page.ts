import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Meta, Title } from '@angular/platform-browser';
import { LANDING, LandingContent, LandingLang, PRIVACY_URL, TERMS_URL } from './landing.content';

type FormState = 'idle' | 'sending' | 'ok' | 'invalid' | 'later' | 'fail';

// Landing pública (lista de espera). El idioma llega por `data.lang` de la ruta.
@Component({
  selector: 'trazo-landing',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './landing.page.html',
  styleUrl: './landing.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LandingPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly doc = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly privacyUrl = PRIVACY_URL;
  protected readonly termsUrl = TERMS_URL;
  protected readonly state = signal<FormState>('idle');
  protected lang: LandingLang = 'es';
  protected t!: LandingContent;

  ngOnInit(): void {
    this.lang = this.route.snapshot.data['lang'] === 'en' ? 'en' : 'es';
    this.t = LANDING[this.lang];
    const previousLang = this.doc.documentElement.lang;
    const previousTitle = this.title.getTitle();
    this.doc.documentElement.lang = this.lang;
    this.title.setTitle(this.t.title);
    this.meta.updateTag({ name: 'description', content: this.t.description });
    this.destroyRef.onDestroy(() => {
      this.doc.documentElement.lang = previousLang;
      this.title.setTitle(previousTitle);
      this.meta.removeTag("name='description'");
    });
  }

  protected message(): string {
    switch (this.state()) {
      case 'ok': return this.t.ok;
      case 'invalid': return this.t.invalid;
      case 'later': return this.t.later;
      case 'fail': return this.t.fail;
      default: return '';
    }
  }

  protected async submit(event: Event, email: HTMLInputElement, consent: HTMLInputElement, trap: HTMLInputElement): Promise<void> {
    event.preventDefault();
    if (trap.value) return; // campo trampa para bots
    this.state.set('sending');
    try {
      const response = await fetch('/api/v1/waitlist', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: email.value.trim(), language: this.lang, consent: consent.checked })
      });
      if (response.ok) {
        email.value = '';
        consent.checked = false;
        this.state.set('ok');
      } else if (response.status === 400 || response.status === 422) {
        this.state.set('invalid');
      } else if (response.status === 429) {
        this.state.set('later');
      } else {
        this.state.set('fail');
      }
    } catch {
      this.state.set('fail');
    }
  }
}
