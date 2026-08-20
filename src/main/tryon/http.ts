export async function jsonResponse<T>(response: Response, context: string): Promise<T> {
  const raw = await response.text();
  let value: unknown;
  try {
    value = raw ? JSON.parse(raw) : {};
  } catch {
    throw new Error(`${context}: invalid JSON response (${response.status})`);
  }
  if (!response.ok) {
    const message = errorMessage(value) ?? response.statusText;
    throw new Error(`${context}: ${message} (${response.status})`);
  }
  return value as T;
}

export function errorMessage(value: unknown): string | null {
  if (!value || typeof value !== 'object') return null;
  const record = value as Record<string, unknown>;
  if (typeof record.message === 'string') return record.message;
  if (typeof record.error === 'string') return record.error;
  if (record.error && typeof record.error === 'object') {
    const nested = record.error as Record<string, unknown>;
    if (typeof nested.message === 'string') return nested.message;
    if (typeof nested.name === 'string') return nested.name;
  }
  return null;
}

export async function imageResponseToDataUrl(response: Response, context: string): Promise<string> {
  if (!response.ok) throw new Error(`${context}: ${response.statusText} (${response.status})`);
  const contentType = response.headers.get('content-type')?.split(';')[0] || 'image/png';
  const bytes = Buffer.from(await response.arrayBuffer());
  return `data:${contentType};base64,${bytes.toString('base64')}`;
}

export function abortError(message = 'Generation cancelled'): Error {
  return new DOMException(message, 'AbortError');
}

export function ensureNotAborted(signal: AbortSignal): void {
  if (signal.aborted) throw abortError();
}
