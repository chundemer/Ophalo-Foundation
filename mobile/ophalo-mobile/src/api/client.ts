import { getSessionToken } from '../auth/secureStore';

const API_URL = (process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5092').replace(/\/$/, '');

export class ApiError extends Error {
  public readonly code?: string;

  constructor(
    public readonly status: number,
    message: string,
    code?: string,
  ) {
    super(message);
    this.name = 'ApiError';
    this.code = code;
  }
}

let on401Handler: (() => void) | null = null;

export function setOn401Handler(handler: (() => void) | null): void {
  on401Handler = handler;
}

async function request<T>(
  path: string,
  options: RequestInit & { authenticated?: boolean; bearerToken?: string } = {},
): Promise<T> {
  const { authenticated = true, bearerToken, headers: extraHeaders, ...fetchOptions } = options;

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(extraHeaders as Record<string, string>),
  };

  if (authenticated) {
    const token = bearerToken ?? await getSessionToken();
    if (token) headers['Authorization'] = `Bearer ${token}`;
  }

  const response = await fetch(`${API_URL}${path}`, { ...fetchOptions, headers });

  if (__DEV__) console.log('[api]', fetchOptions.method, path, response.status);

  if (!response.ok) {
    const body = await response.text().catch(() => '');
    let code: string | undefined;
    try {
      const parsed = JSON.parse(body) as { code?: string };
      if (typeof parsed.code === 'string') code = parsed.code;
    } catch { /* not JSON */ }
    if (authenticated && response.status === 401) on401Handler?.();
    throw new ApiError(response.status, body || response.statusText, code);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  get<T>(path: string, bearerToken?: string): Promise<T> {
    return request<T>(path, { method: 'GET', bearerToken });
  },
  post<T>(
    path: string,
    body?: unknown,
    authenticated = false,
    extraHeaders?: Record<string, string>,
    bearerToken?: string | null,
  ): Promise<T> {
    return request<T>(path, {
      method: 'POST',
      body: body !== undefined ? JSON.stringify(body) : undefined,
      authenticated,
      headers: extraHeaders,
      bearerToken: bearerToken ?? undefined,
    });
  },
  patch<T>(path: string, body?: unknown, extraHeaders?: Record<string, string>): Promise<T> {
    return request<T>(path, {
      method: 'PATCH',
      body: body !== undefined ? JSON.stringify(body) : undefined,
      authenticated: true,
      headers: extraHeaders,
    });
  },
  put<T>(path: string, body?: unknown, extraHeaders?: Record<string, string>): Promise<T> {
    return request<T>(path, {
      method: 'PUT',
      body: body !== undefined ? JSON.stringify(body) : undefined,
      authenticated: true,
      headers: extraHeaders,
    });
  },
  delete<T>(
    path: string,
    extraHeaders?: Record<string, string>,
    bearerToken?: string | null,
  ): Promise<T> {
    return request<T>(path, {
      method: 'DELETE',
      headers: extraHeaders,
      bearerToken: bearerToken ?? undefined,
    });
  },
};
