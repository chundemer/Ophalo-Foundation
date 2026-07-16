// ADR-444: strip non-digits then strip a leading country code '1' from 11-digit values.
export function normalizePhoneDigits(raw: string): string {
  const digits = raw.replace(/\D/g, '');
  return digits.length === 11 && digits[0] === '1' ? digits.slice(1) : digits;
}

export interface AddressErrors {
  line1?: string;
  city?: string;
  state?: string;
}

// GAP-022: required fields when the address disclosure is open.
export function validateAddressIfOpen(
  open: boolean,
  line1: string,
  city: string,
  state: string,
): AddressErrors {
  if (!open) return {};
  const errors: AddressErrors = {};
  if (!line1.trim()) errors.line1 = 'Address line 1 is required.';
  if (!city.trim()) errors.city = 'City is required.';
  if (!state.trim()) errors.state = 'State is required.';
  return errors;
}
