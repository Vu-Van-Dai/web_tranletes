import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { ApiError } from '@/services/http';

/**
 * Copies server-side field errors (ProblemDetails "errors") onto the form.
 * Returns true when at least one field error was applied.
 */
export function applyServerErrors<T extends FieldValues>(error: unknown, setError: UseFormSetError<T>, fields: readonly string[]): boolean {
  if (!(error instanceof ApiError) || !error.errors) return false;
  let applied = false;
  for (const [key, messages] of Object.entries(error.errors)) {
    const field = fields.find((f) => f === key || key.endsWith(`.${f}`));
    if (field && messages[0]) {
      setError(field as Path<T>, { type: 'server', message: messages[0] });
      applied = true;
    }
  }
  return applied;
}
