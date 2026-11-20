export const UI_LOCALE = 'es-ES';

function parseUiDate(value: string | Date): Date {
  if (value instanceof Date) return value;
  const dateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (dateOnly) return new Date(Number(dateOnly[1]), Number(dateOnly[2]) - 1, Number(dateOnly[3]));
  return new Date(value);
}

export function formatUiDate(value: string | Date | null | undefined): string {
  if (!value) return '—';
  const date = parseUiDate(value);
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(UI_LOCALE, {
    day: '2-digit', month: '2-digit', year: 'numeric'
  }).format(date);
}

export function formatUiDateTime(value: string | Date | null | undefined): string {
  if (!value) return '—';
  const date = parseUiDate(value);
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(UI_LOCALE, {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
  }).format(date);
}

export function formatUiNumber(value: number, maximumFractionDigits = 2): string {
  return new Intl.NumberFormat(UI_LOCALE, { maximumFractionDigits }).format(value);
}

export function formatUiCurrency(value: number, currency = 'EUR'): string {
  return new Intl.NumberFormat(UI_LOCALE, { style: 'currency', currency }).format(value);
}
