import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { UiButtonComponent } from './ui-button.component';

@Component({
  standalone: true,
  imports: [UiButtonComponent],
  template: '<trazo-button variant="primary" [loading]="loading" (pressed)="onClick()">Guardar</trazo-button>'
})
class ButtonHostComponent {
  loading = true;
  clicks = 0;
  onClick(): void { this.clicks += 1; }
}

describe('UiButtonComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('disables itself and exposes aria-busy while loading, then emits a click', async () => {
    await TestBed.configureTestingModule({ imports: [ButtonHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(ButtonHostComponent);
    fixture.detectChanges();

    let button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.classList.contains('trazo-button--primary')).toBe(true);
    button.click();
    expect(fixture.componentInstance.clicks).toBe(0);

    fixture.componentInstance.loading = false;
    fixture.detectChanges();
    button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-busy')).toBeNull();
    button.click();
    expect(fixture.componentInstance.clicks).toBe(1);
  });
});
