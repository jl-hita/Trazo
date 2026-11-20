import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { IntelligencePeriodToolbarComponent } from './intelligence-period-toolbar.component';
import { IntelligenceApi } from './intelligence.api';
import { IntelligenceSummary } from './intelligence.models';
import { UiTableComponent } from '../../shared/ui/ui-table.component';

@Component({
  selector: 'trazo-intelligence-page',
  standalone: true,
  imports: [IntelligencePeriodToolbarComponent, UiTableComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './intelligence.page.scss',
  templateUrl: './intelligence.page.html'
})
export class IntelligencePageComponent {
  private readonly api = inject(IntelligenceApi);
  private loadVersion = 0;
  protected readonly presets = [{days:30,label:'30 días'},{days:90,label:'90 días'},{days:365,label:'12 meses'}];
  protected readonly preset = signal<number | null>(90);
  protected readonly from = signal(this.daysAgo(89));
  protected readonly to = signal(this.today());
  protected readonly data = signal<IntelligenceSummary | null>(null);
  protected readonly loading = signal(false);
  protected readonly exporting = signal(false);
  protected readonly loadError = signal('');
  protected readonly exportError = signal('');

  protected usePreset(days: number): void {
    this.preset.set(days); this.from.set(this.daysAgo(days - 1)); this.to.set(this.today()); this.loadError.set(''); this.exportError.set(''); void this.load();
  }
  protected setFrom(value: string): void { this.from.set(value); this.preset.set(null); this.loadError.set(''); this.exportError.set(''); }
  protected setTo(value: string): void { this.to.set(value); this.preset.set(null); this.loadError.set(''); this.exportError.set(''); }
  protected async load(): Promise<void> {
    if (!this.from() || !this.to() || this.from() > this.to()) { this.loadError.set('El periodo no es válido.'); return; }
    const requestVersion = ++this.loadVersion;
    this.loading.set(true); this.loadError.set('');
    try {
      const result = await this.api.summary(this.from(), this.to());
      if (requestVersion === this.loadVersion) this.data.set(result);
    }
    catch (e) { if (requestVersion === this.loadVersion) this.loadError.set((e as {error?:{message?:string}})?.error?.message ?? 'No se han podido cargar los datos de Intelligence.'); }
    finally { if (requestVersion === this.loadVersion) this.loading.set(false); }
  }
  protected async export(): Promise<void> {
    if (!this.from() || !this.to() || this.from() > this.to()) { this.exportError.set('El periodo no es válido.'); return; }
    this.exporting.set(true); this.exportError.set('');
    try {
      const blob = await this.api.export(this.from(), this.to());
      const signature = new Uint8Array(await blob.slice(0, 4).arrayBuffer());
      const hasZipSignature = signature.length === 4 && signature[0] === 0x50 && signature[1] === 0x4b
        && ((signature[2] === 0x03 && signature[3] === 0x04) || (signature[2] === 0x05 && signature[3] === 0x06));
      if (!blob.size || !hasZipSignature) {
        const message = await blob.text();
        let detail = 'La API no ha devuelto un ZIP válido.';
        try { detail = JSON.parse(message)?.message ?? detail; } catch { /* The response was not a JSON error. */ }
        throw new Error(detail);
      }
      const url = URL.createObjectURL(blob); const a = document.createElement('a');
      a.href = url; a.download = 'trazo-intelligence-' + this.from().replaceAll('-','') + '-' + this.to().replaceAll('-','') + '.zip';
      a.style.display = 'none'; document.body.appendChild(a); a.click(); a.remove();
      // Revoke after the browser has had time to start consuming the download.
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      this.exportError.set(e instanceof Error ? e.message : 'No se ha podido generar la exportación.');
    }
    finally { this.exporting.set(false); }
  }
  protected adaptationTotal(): number { return this.data()?.adaptations.reduce((s,x)=>s+x.count,0) ?? 0; }
  protected n(value: number): string { return value.toLocaleString('es-ES'); }
  protected decimal(value: number): string { return value.toLocaleString('es-ES',{maximumFractionDigits:1}); }
  protected pct(a: number,b: number): string { return b ? Math.round(a*100/b)+' %' : '—'; }
  protected pctValue(a: number,b: number): number { return b ? Math.min(100,Math.max(0,a*100/b)) : 0; }
  protected date(value: string): string { return new Date(value).toLocaleDateString('es-ES'); }
  protected dateTime(value: string): string { return new Date(value).toLocaleString('es-ES'); }
  protected adaptationLabel(value: string): string {
    return ({reduction_sets:'Reducción de series',raise_reserve:'Aumento de RIR',restore_sets:'Restauración de series',deload:'Descarga',hold_progression:'Progresión retenida',load_update:'Actualización de cargas'} as Record<string,string>)[value] ?? value;
  }
  protected sourceLabel(value: string | null): string { return value === 'engine' ? 'Automática' : value === 'user' ? 'Usuario' : value === 'admin' ? 'Administrador' : 'Sin dato'; }
  protected qualityLabel(value: string): string { return ({valid:'Válida',partial:'Parcial',excluded:'Excluida'} as Record<string,string>)[value] ?? value; }
  private today(): string { return this.localDate(new Date()); }
  private daysAgo(days: number): string { const date = new Date(); date.setDate(date.getDate() - days); return this.localDate(date); }
  private localDate(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }
  constructor() { void this.load(); }
}
