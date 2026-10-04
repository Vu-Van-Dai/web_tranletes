/** Public runtime configuration (only PUBLIC_* variables are exposed by Vite). */
export const env = {
  appName: (import.meta.env.PUBLIC_APP_NAME as string | undefined) || 'Trạm Truyện',
  /** Empty in development: requests go through the Vite proxy on the same origin. */
  apiUrl: ((import.meta.env.PUBLIC_API_URL as string | undefined) ?? '').replace(/\/+$/, ''),
};
