// Validation primitives and per-field rules for encoder inputs.

const INT_RE = /^-?\d+$/;
const FLOAT_RE = /^-?\d+(\.\d+)?$/;

export function isInt(s: string): boolean {
  return INT_RE.test(s);
}

export function isFloat(s: string): boolean {
  return FLOAT_RE.test(s);
}

export function inRange(v: number, min: number, max = Infinity): boolean {
  return v >= min && v <= max;
}

// Empty string means "unset" and maps to null everywhere.
export function emptyToNull(s: string): string | null {
  return s === '' ? null : s;
}

export interface FieldError {
  field: string;
  message: string;
}

function checkIntField(
  errors: FieldError[],
  field: string,
  raw: string,
  min: number,
  max = Infinity,
): number | null {
  const s = emptyToNull(raw.trim());
  if (s === null) return null;
  if (!isInt(s)) {
    errors.push({ field, message: 'must be an integer' });
    return null;
  }
  const v = Number.parseInt(s, 10);
  if (!inRange(v, min, max)) {
    errors.push({ field, message: `must be between ${min} and ${max}` });
    return null;
  }
  return v;
}

function checkFloatField(
  errors: FieldError[],
  field: string,
  raw: string,
  min: number,
  max = Infinity,
): number | null {
  const s = emptyToNull(raw.trim());
  if (s === null) return null;
  if (!isFloat(s)) {
    errors.push({ field, message: 'must be a number' });
    return null;
  }
  const v = Number.parseFloat(s);
  if (!inRange(v, min, max)) {
    errors.push({ field, message: `must be between ${min} and ${max}` });
    return null;
  }
  return v;
}

export function validateCropW(
  errors: FieldError[],
  raw: string,
): number | null {
  return checkIntField(errors, 'cropw', raw, 1);
}

export function validateCropH(
  errors: FieldError[],
  raw: string,
): number | null {
  return checkIntField(errors, 'croph', raw, 1);
}

export function validateCropX(
  errors: FieldError[],
  raw: string,
): number | null {
  return checkIntField(errors, 'cropx', raw, 0);
}

export function validateCropY(
  errors: FieldError[],
  raw: string,
): number | null {
  return checkIntField(errors, 'cropy', raw, 0);
}

export function validateScaleW(
  errors: FieldError[],
  raw: string,
): number | null {
  return checkIntField(errors, 'scalew', raw, 1);
}

export function validateScaleH(
  errors: FieldError[],
  raw: string,
): number | null {
  return checkIntField(errors, 'scaleh', raw, 1);
}

export function validateFadeIn(
  errors: FieldError[],
  raw: string,
): number | null {
  return checkFloatField(errors, 'fadeIn', raw, 0.001);
}

export function validateFadeOut(
  errors: FieldError[],
  raw: string,
): number | null {
  return checkFloatField(errors, 'fadeOut', raw, 0.001);
}

export function validateAmplify(
  errors: FieldError[],
  raw: string,
): number | null {
  return checkIntField(errors, 'amplify', raw, 1, 64);
}
