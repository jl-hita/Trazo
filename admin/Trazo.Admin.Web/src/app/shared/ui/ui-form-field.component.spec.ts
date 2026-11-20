import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { UiFormFieldComponent } from './ui-form-field.component';

@Component({
  standalone: true,
  imports: [UiFormFieldComponent],
  template: '<trazo-form-field controlId="email" label="Correo" hint="Ayuda" error="Correo no válido"><input id="email"></trazo-form-field>'
})
class FormFieldHostComponent {}

describe('UiFormFieldComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('associates the label with its control and prioritizes validation errors over hints', async () => {
    await TestBed.configureTestingModule({ imports: [FormFieldHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(FormFieldHostComponent);
    fixture.detectChanges();

    const label = fixture.nativeElement.querySelector('label') as HTMLLabelElement;
    const error = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(label.htmlFor).toBe('email');
    expect(error.textContent).toBe('Correo no válido');
    expect(fixture.nativeElement.textContent).not.toContain('Ayuda');
  });
});
