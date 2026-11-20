import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { UiBadgeComponent } from './ui-badge.component';

describe('UiBadgeComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders its tone, custom class and projected content', async () => {
    await TestBed.configureTestingModule({ imports: [UiBadgeComponent] }).compileComponents();
    const fixture = TestBed.createComponent(UiBadgeComponent);
    fixture.componentInstance.tone = 'success';
    fixture.componentInstance.badgeClass = 'compact';
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('span') as HTMLSpanElement;
    expect(badge.dataset['tone']).toBe('success');
    expect(badge.classList.contains('trazo-badge')).toBe(true);
    expect(badge.classList.contains('compact')).toBe(true);
  });
});
