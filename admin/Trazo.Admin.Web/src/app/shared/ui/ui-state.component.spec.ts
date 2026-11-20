import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { UiStateComponent } from './ui-state.component';

@Component({
  standalone: true,
  imports: [UiStateComponent],
  template: '<trazo-state [kind]="kind" title="Sin resultados" description="Prueba"><button>Reintentar</button></trazo-state>'
})
class StateHostComponent {
  kind: 'loading' | 'empty' | 'error' | 'success' = 'empty';
}

describe('UiStateComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('exposes non-error states as polite status messages', async () => {
    await TestBed.configureTestingModule({ imports: [StateHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(StateHostComponent);
    fixture.detectChanges();

    const state = fixture.nativeElement.querySelector('section') as HTMLElement;
    expect(state.getAttribute('data-kind')).toBe('empty');
    expect(state.getAttribute('role')).toBe('status');
    expect(state.getAttribute('aria-live')).toBe('polite');
    expect(state.textContent).toContain('Sin resultados');
    expect(state.querySelector('button')?.textContent).toBe('Reintentar');
  });

  it('exposes error states as alerts', async () => {
    await TestBed.configureTestingModule({ imports: [StateHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(StateHostComponent);
    fixture.componentInstance.kind = 'error';
    fixture.detectChanges();

    const state = fixture.nativeElement.querySelector('section') as HTMLElement;
    expect(state.getAttribute('data-kind')).toBe('error');
    expect(state.getAttribute('role')).toBe('alert');
  });
});
