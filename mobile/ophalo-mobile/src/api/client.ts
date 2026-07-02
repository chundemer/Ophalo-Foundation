import { getSessionToken } from '../auth/secureStore';

const API_URL = (process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5092').replace(/\/$/, '');

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
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
    throw new ApiError(response.status, body || response.statusText);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  get<T>(path: string, bearerToken?: string): Promise<T> {
    return request<T>(path, { method: 'GET', bearerToken });
  },
  post<T>(path: string, body?: unknown, authenticated = false): Promise<T> {
    return request<T>(path, {
      method: 'POST',
      body: body !== undefined ? JSON.stringify(body) : undefined,
      authenticated,
    });
  },
  delete<T>(path: string): Promise<T> {
    return request<T>(path, { method: 'DELETE' });
  },
};
