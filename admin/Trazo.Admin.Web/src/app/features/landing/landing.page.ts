import { ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { UiButtonComponent } from '../../shared/ui/ui-button.component';
import { UiFormFieldComponent } from '../../shared/ui/ui-form-field.component';
import { Meta, Title } from '@angular/platform-browser';
import { LANDING_LANGUAGES, LandingContent, LandingLanguage, landingLanguage, privacyUrl, termsUrl } from './landing.content';

type FormState = 'idle' | 'sending' | 'ok' | 'invalid' | 'later' | 'fail';

// Landing pública (lista de espera). El idioma llega por `data.lang` de la ruta y sus textos, resueltos, por `data.content`.
@Component({
  selector: 'trazo-landing',
  standalone: true,
  imports: [RouterLink, UiButtonComponent, UiFormFieldComponent],
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
  private readonly changes = inject(ChangeDetectorRef);
  private applied = false;

  protected readonly languages = LANDING_LANGUAGES;
  protected readonly state = signal<FormState>('idle');
  protected language!: LandingLanguage;
  protected privacyUrl = '';
  protected termsUrl = '';
  protected t!: LandingContent;

  ngOnInit(): void {
    // El selector de idioma cambia de ruta sin recrear necesariamente el componente: todo sale de los datos de la ruta.
    this.route.data.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(data => this.show(data['lang'], data['content']));
    const previousLang = this.doc.documentElement.lang;
    const previousDir = this.doc.documentElement.dir;
    const previousTitle = this.title.getTitle();
    this.apply();
    this.destroyRef.onDestroy(() => {
      this.doc.documentElement.lang = previousLang;
      this.doc.documentElement.dir = previousDir;
      this.title.setTitle(previousTitle);
      this.meta.removeTag("name='description'");
    });
  }

  private show(code: string, content: LandingContent): void {
    this.language = landingLanguage(code);
    this.t = content;
    this.privacyUrl = privacyUrl(this.language.code);
    this.termsUrl = termsUrl(this.language.code);
    this.state.set('idle');
    if (this.applied) this.apply();
    this.changes.markForCheck();
  }

  private apply(): void {
    this.applied = true;
    this.doc.documentElement.lang = this.language.code;
    this.doc.documentElement.dir = this.language.rtl ? 'rtl' : 'ltr';
    this.title.setTitle(this.t.title);
    this.meta.updateTag({ name: 'description', content: this.t.description });
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
        body: JSON.stringify({ email: email.value.trim(), language: this.language.code, consent: consent.checked })
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
