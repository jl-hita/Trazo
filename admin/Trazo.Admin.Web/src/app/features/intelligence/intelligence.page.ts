import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

interface IntelligenceSummary {
  from: string; to: string; generatedAt: string;
  plans: { total: number; generated: number; manual: number; engineVersions: string[] };
  prescriptions: { sessions: number; withOriginalSnapshot: number; withoutOriginalSnapshot: number };
  workouts: { total: number; completed: number; partial: number; notCompleted: number };
  adaptations: { kind: string; source: string | null; count: number }[];
  observations: {
    muscle: string; quality: string; count: number; prescribedSets: number; performedSets: number;
    performanceSamples: number; fatigueSignals: number; averagePerformanceChange: number | null;
  }[];
  decisions: { action: string; policyVersion: string; count: number }[];
}

@Component({
  selector: 'trazo-intelligence-page',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  styles: [`
    .ti { max-width: 1440px; color: #dbe5f2; }
    .ti-toolbar { display:flex; flex-wrap:wrap; align-items:end; gap:10px; margin:18px 0 20px; }
    .ti-field { display:grid; gap:5px; color:#91a1b6; font-size:11px; }
    .ti input { padding:9px 10px; border:1px solid #34465b; border-radius:6px; background:#0d1119; color:#e8edf5; }
    .ti-btn { padding:9px 13px; border:1px solid #4a6585; border-radius:6px; background:#1b2a3e; color:#e4edf8; font-size:12px; cursor:pointer; }
    .ti-btn.primary { background:#315f91; border-color:#5d8fca; color:white; }
    .ti-btn:disabled { opacity:.5; cursor:progress; }
    .ti-presets { display:flex; gap:5px; }
    .ti-presets button.active { border-color:#79a9dc; background:#315f91; color:white; }
    .ti-error,.ti-note { padding:12px 14px; border:1px solid #654044; border-radius:7px; background:#241719; color:#f0b2b2; font-size:12px; margin:12px 0; }
    .ti-note { border-color:#3a506a; background:#152131; color:#b9cde4; line-height:1.55; }
    .ti-grid { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:12px; margin:18px 0; }
    .ti-card,.ti-panel { border:1px solid #293444; border-radius:8px; background:#131b27; padding:16px; min-width:0; }
    .ti-card .value { display:block; color:#f2f5fa; font-size:28px; font-weight:650; font-variant-numeric:tabular-nums; }
    .ti-card .label { display:block; color:#91a1b6; font-size:11px; margin-top:4px; }
    .ti-section { margin:24px 0 10px; color:#a9c2df; font-size:12px; letter-spacing:.1em; text-transform:uppercase; }
    .ti-columns { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:14px; }
    .ti-panel h2 { margin:0 0 13px; color:#e7edf6; font-size:14px; }
    .ti-table-wrap { overflow-x:auto; }
    .ti table { width:100%; border-collapse:collapse; font-size:12px; }
    .ti th,.ti td { padding:9px 8px; border-bottom:1px solid #263243; text-align:right; white-space:nowrap; }
    .ti th:first-child,.ti td:first-child { text-align:left; }
    .ti th { color:#91a1b6; font-size:10px; text-transform:uppercase; letter-spacing:.06em; }
    .ti .muted { color:#8291a6; }
    .ti .empty { padding:22px 4px; color:#8291a6; text-align:center; font-size:12px; }
    .ti .barlist { display:grid; gap:11px; }
    .ti .barrow { display:grid; grid-template-columns:minmax(100px,150px) 1fr 48px; gap:9px; align-items:center; font-size:11px; }
    .ti .track { height:9px; border-radius:5px; background:#0d1119; overflow:hidden; }
    .ti .fill { height:100%; background:#6595c8; border-radius:5px; }
    .ti .quality { display:inline-block; border:1px solid #40546d; border-radius:99px; padding:2px 7px; color:#b6cce5; font-size:10px; }
    .ti .foot { color:#8291a6; font-size:11px; line-height:1.5; margin-top:12px; }
    .ti button:focus-visible,.ti input:focus-visible { outline:2px solid #9cc0ea; outline-offset:2px; }
    @media(max-width:950px) { .ti-grid { grid-template-columns:repeat(2,minmax(0,1fr)); } }
    @media(max-width:640px) { .ti-grid,.ti-columns { grid-template-columns:1fr; } }
  `],
  template: `
    <div class="page-heading">
      <div><p class="eyebrow">INVESTIGACIÓN Y DATOS</p><h1>Trazo Intelligence</h1>
        <p class="subtitle">Analiza qué prescribe el motor, qué hacen realmente los deportistas y qué señales utiliza para adaptarse.</p></div>
    </div>
    <div class="ti">
      <div class="ti-toolbar">
        <div class="ti-presets" role="group" aria-label="Periodo">
          @for (p of presets; track p.days) {
            <button class="ti-btn" [class.active]="preset() === p.days" type="button" (click)="usePreset(p.days)">{{p.label}}</button>
          }
        </div>
        <label class="ti-field">Desde <input type="date" [value]="from()" (change)="setFrom($any($event.target).value)"></label>
        <label class="ti-field">Hasta (incluido) <input type="date" [value]="to()" (change)="setTo($any($event.target).value)"></label>
        <button class="ti-btn" type="button" (click)="load()" [disabled]="loading()">Actualizar</button>
        <button class="ti-btn primary" type="button" (click)="export()" [disabled]="exporting() || loading()">
          {{ exporting() ? 'Preparando paquete…' : '↓ Exportar investigación (ZIP)' }}
        </button>
      </div>
      <p class="ti-note"><strong>Qué contiene la exportación:</strong> CSV y JSON relacionados de planes, mesociclos, prescripciones originales y actuales, adaptaciones, entrenamientos, series realizadas, observaciones longitudinales y decisiones del motor. Los identificadores se seudonimizan y solo son relacionables dentro del mismo paquete. Se excluyen nombres, correos, notas libres y limitaciones personales. El archivo sigue siendo información seudonimizada y debe tratarse como privada.</p>
      @if (error()) { <p class="ti-error" role="alert">{{error()}}</p> }
      @if (loading() && !data()) { <p class="empty">Cargando datos de investigación…</p> }
      @if (data(); as d) {
        <p class="foot">Periodo {{date(d.from)}} – {{date(d.to)}} · Datos actualizados {{dateTime(d.generatedAt)}}. Las observaciones se cuentan por músculo y periodo; no equivalen a deportistas independientes.</p>
        <div class="ti-grid">
          <article class="ti-card"><span class="value">{{n(d.plans.total)}}</span><span class="label">Planes creados · {{n(d.plans.generated)}} generados / {{n(d.plans.manual)}} manuales</span></article>
          <article class="ti-card"><span class="value">{{n(d.prescriptions.sessions)}}</span><span class="label">Sesiones prescritas · {{pct(d.prescriptions.withOriginalSnapshot,d.prescriptions.sessions)}} con snapshot original</span></article>
          <article class="ti-card"><span class="value">{{n(d.workouts.total)}}</span><span class="label">Entrenamientos realizados · {{pct(d.workouts.completed,d.workouts.total)}} completados</span></article>
          <article class="ti-card"><span class="value">{{n(adaptationTotal())}}</span><span class="label">Adaptaciones registradas en el periodo</span></article>
        </div>

        <h2 class="ti-section">Calidad y cobertura de datos</h2>
        <div class="ti-columns">
          <section class="ti-panel"><h2>Trazabilidad de prescripción</h2>
            <div class="barlist">
              <div class="barrow"><span>Con snapshot original</span><span class="track"><span class="fill" [style.width.%]="pctValue(d.prescriptions.withOriginalSnapshot,d.prescriptions.sessions)"></span></span><b>{{n(d.prescriptions.withOriginalSnapshot)}}</b></div>
              <div class="barrow"><span>Sin snapshot original</span><span class="track"><span class="fill" [style.width.%]="pctValue(d.prescriptions.withoutOriginalSnapshot,d.prescriptions.sessions)"></span></span><b>{{n(d.prescriptions.withoutOriginalSnapshot)}}</b></div>
            </div>
            <p class="foot">Los registros históricos sin snapshot se mantienen como desconocidos; no se reconstruyen usando el catálogo o el motor actual.</p>
            <p class="foot">Versiones de motor: {{d.plans.engineVersions.length ? d.plans.engineVersions.join(', ') : 'No disponibles en este periodo'}}</p>
          </section>
          <section class="ti-panel"><h2>Resultado de los entrenamientos</h2>
            <div class="barlist">
              <div class="barrow"><span>Completados</span><span class="track"><span class="fill" [style.width.%]="pctValue(d.workouts.completed,d.workouts.total)"></span></span><b>{{n(d.workouts.completed)}}</b></div>
              <div class="barrow"><span>Parciales</span><span class="track"><span class="fill" [style.width.%]="pctValue(d.workouts.partial,d.workouts.total)"></span></span><b>{{n(d.workouts.partial)}}</b></div>
              <div class="barrow"><span>No completados</span><span class="track"><span class="fill" [style.width.%]="pctValue(d.workouts.notCompleted,d.workouts.total)"></span></span><b>{{n(d.workouts.notCompleted)}}</b></div>
            </div>
            <p class="foot">El porcentaje de completitud describe registros de entrenamiento, no adherencia causal al programa.</p>
          </section>
        </div>

        <h2 class="ti-section">Comportamiento del motor</h2>
        <div class="ti-columns">
          <section class="ti-panel"><h2>Adaptaciones por tipo y origen</h2>
            @if (d.adaptations.length) {
              <div class="ti-table-wrap"><table><thead><tr><th>Tipo</th><th>Origen</th><th>Veces</th></tr></thead><tbody>
                @for (a of d.adaptations; track a.kind + ':' + a.source) { <tr><td>{{adaptationLabel(a.kind)}}</td><td>{{sourceLabel(a.source)}}</td><td>{{n(a.count)}}</td></tr> }
              </tbody></table></div>
            } @else { <p class="empty">No hay adaptaciones en el periodo.</p> }
          </section>
          <section class="ti-panel"><h2>Decisiones longitudinales</h2>
            @if (d.decisions.length) {
              <div class="ti-table-wrap"><table><thead><tr><th>Acción</th><th>Política</th><th>Decisiones</th></tr></thead><tbody>
                @for (x of d.decisions; track x.action + x.policyVersion) { <tr><td>{{x.action}}</td><td>{{x.policyVersion}}</td><td>{{n(x.count)}}</td></tr> }
              </tbody></table></div>
            } @else { <p class="empty">No hay decisiones longitudinales registradas en el periodo.</p> }
          </section>
        </div>

        <h2 class="ti-section">Respuesta por grupo muscular</h2>
        <section class="ti-panel">
          @if (d.observations.length) {
            <div class="ti-table-wrap"><table><thead><tr><th>Músculo</th><th>Calidad</th><th>Periodos</th><th>Series prescritas</th><th>Series realizadas</th><th>Muestras rendimiento</th><th>Cambio medio</th><th>Señales fatiga</th></tr></thead>
              <tbody>@for (o of d.observations; track o.muscle + o.quality) {
                <tr><td>{{o.muscle}}</td><td><span class="quality">{{qualityLabel(o.quality)}}</span></td><td>{{n(o.count)}}</td><td>{{decimal(o.prescribedSets)}}</td><td>{{decimal(o.performedSets)}}</td><td>{{n(o.performanceSamples)}}</td><td>{{o.averagePerformanceChange === null ? '—' : decimal(o.averagePerformanceChange) + ' %'}}</td><td>{{n(o.fatigueSignals)}}</td></tr>
              }</tbody>
            </table></div>
            <p class="foot">No mezcles periodos válidos, parciales y excluidos al estimar respuestas. El cambio de rendimiento es un indicador observado, no una medida directa de hipertrofia.</p>
          } @else { <p class="empty">No hay observaciones longitudinales actualizadas en el periodo.</p> }
        </section>
      }
    </div>
  `
})
export class IntelligencePageComponent {
  private readonly http = inject(HttpClient);
  protected readonly presets = [{days:30,label:'30 días'},{days:90,label:'90 días'},{days:365,label:'12 meses'}];
  protected readonly preset = signal<number | null>(90);
  protected readonly from = signal(this.daysAgo(89));
  protected readonly to = signal(this.today());
  protected readonly data = signal<IntelligenceSummary | null>(null);
  protected readonly loading = signal(false);
  protected readonly exporting = signal(false);
  protected readonly error = signal('');

  protected usePreset(days: number): void {
    this.preset.set(days); this.from.set(this.daysAgo(days - 1)); this.to.set(this.today()); void this.load();
  }
  protected setFrom(value: string): void { this.from.set(value); this.preset.set(null); }
  protected setTo(value: string): void { this.to.set(value); this.preset.set(null); }
  private params(): HttpParams {
    const from = new Date(this.from() + 'T00:00:00.000Z').toISOString();
    const end = new Date(this.to() + 'T00:00:00.000Z'); end.setUTCDate(end.getUTCDate() + 1);
    return new HttpParams().set('from', from).set('to', end.toISOString());
  }
  protected async load(): Promise<void> {
    if (!this.from() || !this.to() || this.from() > this.to()) { this.error.set('El periodo no es válido.'); return; }
    this.loading.set(true); this.error.set('');
    try { this.data.set(await firstValueFrom(this.http.get<IntelligenceSummary>('/api/v1/admin/intelligence/summary', {params:this.params()}))); }
    catch (e) { this.error.set((e as {error?:{message?:string}})?.error?.message ?? 'No se han podido cargar los datos de Intelligence.'); }
    finally { this.loading.set(false); }
  }
  protected async export(): Promise<void> {
    if (!this.from() || !this.to() || this.from() > this.to()) { this.error.set('El periodo no es válido.'); return; }
    this.exporting.set(true); this.error.set('');
    try {
      const blob = await firstValueFrom(this.http.get('/api/v1/admin/intelligence/export', {params:this.params(), responseType:'blob'}));
      const url = URL.createObjectURL(blob); const a = document.createElement('a');
      a.href = url; a.download = 'trazo-intelligence-' + this.from().replaceAll('-','') + '-' + this.to().replaceAll('-','') + '.zip';
      document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(url);
    } catch { this.error.set('No se ha podido generar la exportación.'); }
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
  private today(): string { return new Date().toISOString().slice(0,10); }
  private daysAgo(days: number): string { const d=new Date(); d.setDate(d.getDate()-days); return d.toISOString().slice(0,10); }
  constructor() { void this.load(); }
}
