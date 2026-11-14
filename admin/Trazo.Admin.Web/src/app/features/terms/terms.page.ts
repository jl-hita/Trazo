import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Meta, Title } from '@angular/platform-browser';

@Component({
  selector: 'trazo-terms-page',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './terms.page.html',
  styleUrl: './terms.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TermsPageComponent implements OnInit {
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
    this.title.setTitle(this.lang === 'es' ? 'Condiciones de uso · TRAZO' : 'Terms of use · TRAZO');
    this.meta.updateTag({
      name: 'description',
      content: this.lang === 'es'
        ? 'Condiciones aplicables al uso de TRAZO y a sus funciones gratuitas y Premium.'
        : 'Terms governing the use of TRAZO and its free and Premium features.'
    });
    this.destroyRef.onDestroy(() => {
      this.doc.documentElement.lang = previousLang;
      this.title.setTitle(previousTitle);
      this.meta.removeTag("name='description'");
    });
  }
}
