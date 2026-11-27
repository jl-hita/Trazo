import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { IntelligenceApi } from './intelligence.api';
import { IntelligenceSummary } from './intelligence.models';
import { IntelligencePageComponent } from './intelligence.page';

describe('IntelligencePageComponent', () => {
  it('names the exercise swaps of the user instead of showing their technical kind', async () => {
    const summary: IntelligenceSummary = {
      from: '2026-11-01T00:00:00Z', to: '2026-12-01T00:00:00Z', generatedAt: '2026-11-27T12:00:00Z',
      plans: { total: 1, generated: 1, manual: 0, engineVersions: [] },
      prescriptions: { sessions: 1, withOriginalSnapshot: 1, withoutOriginalSnapshot: 0 },
      workouts: { total: 0, completed: 0, partial: 0, notCompleted: 0 },
      adaptations: [{ kind: 'user_swap_exercise', source: 'user', count: 2 }],
      observations: [], decisions: []
    };
    TestBed.configureTestingModule({ providers: [{ provide: IntelligenceApi, useValue: { summary: async () => summary } }] });
    const fixture = TestBed.createComponent(IntelligencePageComponent);

    await (fixture.componentInstance as unknown as { load(): Promise<void> }).load();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Sustitución de ejercicio');
    expect(text).not.toContain('user_swap_exercise');
    TestBed.resetTestingModule();
  });
});
