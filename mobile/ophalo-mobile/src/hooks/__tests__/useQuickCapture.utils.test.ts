import { describe, expect, it } from 'vitest';
import { normalizePhoneDigits, validateAddressIfOpen } from '../phoneUtils';

describe('normalizePhoneDigits', () => {
  it('strips formatting from a 10-digit number', () => {
    expect(normalizePhoneDigits('(555) 123-4567')).toBe('5551234567');
  });

  it('strips leading 1 from an 11-digit +1 number', () => {
    expect(normalizePhoneDigits('15551234567')).toBe('5551234567');
  });

  it('strips leading 1 from a formatted +1 number', () => {
    expect(normalizePhoneDigits('+1 (555) 123-4567')).toBe('5551234567');
  });

  it('strips leading 1 from 1-555-123-4567 format', () => {
    expect(normalizePhoneDigits('1-555-123-4567')).toBe('5551234567');
  });

  it('does not strip leading 1 from a 10-digit number starting with 1', () => {
    expect(normalizePhoneDigits('1234567890')).toBe('1234567890');
  });

  it('does not strip leading 1 from a 12-digit number starting with 1', () => {
    expect(normalizePhoneDigits('123456789012')).toBe('123456789012');
  });

  it('returns empty string for empty input', () => {
    expect(normalizePhoneDigits('')).toBe('');
  });

  it('returns partial digits for partial input', () => {
    expect(normalizePhoneDigits('555')).toBe('555');
  });
});

describe('validateAddressIfOpen', () => {
  it('returns no errors when address is not open', () => {
    expect(validateAddressIfOpen(false, '', '', '')).toEqual({});
  });

  it('returns no errors when address is open and required fields are provided', () => {
    expect(validateAddressIfOpen(true, '123 Main St', 'Springfield', 'IL')).toEqual({});
  });

  it('requires line 1 when open', () => {
    const errors = validateAddressIfOpen(true, '', 'Springfield', 'IL');
    expect(errors.line1).toBeDefined();
    expect(errors.city).toBeUndefined();
    expect(errors.state).toBeUndefined();
  });

  it('requires city when open', () => {
    const errors = validateAddressIfOpen(true, '123 Main St', '', 'IL');
    expect(errors.city).toBeDefined();
    expect(errors.line1).toBeUndefined();
    expect(errors.state).toBeUndefined();
  });

  it('requires state when open', () => {
    const errors = validateAddressIfOpen(true, '123 Main St', 'Springfield', '');
    expect(errors.state).toBeDefined();
    expect(errors.line1).toBeUndefined();
    expect(errors.city).toBeUndefined();
  });

  it('returns all three errors when open with all required fields empty', () => {
    const errors = validateAddressIfOpen(true, '', '', '');
    expect(errors.line1).toBeDefined();
    expect(errors.city).toBeDefined();
    expect(errors.state).toBeDefined();
  });

  it('trims whitespace before validating', () => {
    const errors = validateAddressIfOpen(true, '   ', '  ', '\t');
    expect(errors.line1).toBeDefined();
    expect(errors.city).toBeDefined();
    expect(errors.state).toBeDefined();
  });
});
