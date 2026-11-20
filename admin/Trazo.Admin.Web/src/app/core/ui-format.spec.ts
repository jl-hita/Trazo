import { describe, expect, it } from 'vitest';
import {
  formatUiCurrency,
  formatUiDate,
  formatUiDateTime,
  formatUiNumber,
  formatUiPercent
} from './ui-format';

describe('shared es-ES formatters', () => {
  it('formats date-only values without shifting their calendar day', () => {
    expect(formatUiDate('2026-11-05')).toBe('05/11/2026');
  });

  it('returns a stable placeholder for missing or invalid dates', () => {
    expect(formatUiDate(null)).toBe('—');
    expect(formatUiDate('not-a-date')).toBe('—');
  });

  it('formats date and time using the shared Spanish locale', () => {
    expect(formatUiDateTime('2026-11-05T14:07:00')).toContain('05/11/2026');
    expect(formatUiDateTime('2026-11-05T14:07:00')).toMatch(/\d{2}:\d{2}/);
  });

  it('formats numbers, euros and percentages with Spanish separators', () => {
    expect(formatUiNumber(12345.6)).toBe('12.345,6');
    expect(formatUiCurrency(12.5)).toContain('€');
    expect(formatUiPercent(0.125)).toContain('12,5');
    expect(formatUiPercent(null)).toBe('—');
  });
});
