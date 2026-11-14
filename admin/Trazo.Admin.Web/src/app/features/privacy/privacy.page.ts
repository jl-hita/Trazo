import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Meta, Title } from '@angular/platform-browser';

@Component({
  selector: 'trazo-privacy-page',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './privacy.page.html',
  styleUrl: './privacy.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PrivacyPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly doc = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly lang: 'es' | 'en' = this.route.snapshot.data['lang'] === 'en' ? 'en' : 'es';

  ngOnInit(): void {
    const previousLang = this.doc.documentElement.lang;
    const previousTitle = this.title.getTitle();
    this.doc.documentElement.lang = this.lang;
    this.title.setTitle(this.lang === 'es' ? 'Política de privacidad · TRAZO' : 'Privacy policy · TRAZO');
    this.meta.updateTag({
      name: 'description',
      content: this.lang === 'es'
        ? 'Información sobre el tratamiento de datos personales en TRAZO.'
        : 'How TRAZO processes personal data.'
    });
    this.destroyRef.onDestroy(() => {
      this.doc.documentElement.lang = previousLang;
      this.title.setTitle(previousTitle);
      this.meta.removeTag("name='description'");
    });
  }
}
