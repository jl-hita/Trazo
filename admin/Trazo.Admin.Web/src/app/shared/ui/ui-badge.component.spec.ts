import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { UiBadgeComponent } from './ui-badge.component';

@Component({
  standalone: true,
  imports: [UiBadgeComponent],
  template: '<trazo-badge tone="success" badgeClass="compact">Activo</trazo-badge>'
})
class BadgeHostComponent {}

describe('UiBadgeComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders its tone, custom class and projected content', async () => {
    await TestBed.configureTestingModule({ imports: [BadgeHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(BadgeHostComponent);
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('span') as HTMLSpanElement;
    expect(badge.dataset['tone']).toBe('success');
    expect(badge.classList.contains('trazo-badge')).toBe(true);
    expect(badge.classList.contains('compact')).toBe(true);
    expect(badge.textContent?.trim()).toBe('Activo');
  });
});
