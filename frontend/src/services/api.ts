import { apiUrl, putFile, request, uploadFile, type ExtraPart } from './http';
import type { UploadLimits } from '@/features/house/zipInspect';
import type {
  AdminHouseComic,
  AdminRevenueReport,
  AdsConfig,
  HouseLinks,
  HouseViewReport,
  ViewTicket,
  AdminHouseDetail,
  AdminHouseItem,
  AdminStats,
  AdminUser,
  AuditItem,
  ChapterSummary,
  ComicCard,
  ComicDetail,
  ComicLibraryState,
  VersionPreview,
  CreateHouseResult,
  HistoryItem,
  HouseChapterDetail,
  HouseChapterItem,
  HouseComicDetail,
  HouseComicItem,
  HouseDashboard,
  HouseProfile,
  JobSummary,
  Me,
  Member,
  Paged,
  ProcessingJobItem,
  PublicChapter,
  PublicHouse,
  ReaderData,
  Session,
  SiteInfo,
  SystemInfo,
  SystemSettings,
  TagWithCount,
} from '@/types/api';

const qs = (params: Record<string, string | number | undefined | null>) => {
  const search = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== null && v !== '') search.set(k, String(v));
  }
  const s = search.toString();
  return s ? `?${s}` : '';
};

// ---------------- Auth ----------------
export const authApi = {
  login: (login: string, password: string) => request<Session>('/api/auth/login', { method: 'POST', body: { login, password } }),
  register: (body: { username: string; email: string; displayName: string; password: string }) =>
    request<Session>('/api/auth/register', { method: 'POST', body }),
  logout: () => request<void>('/api/auth/logout', { method: 'POST' }),
  me: () => request<Me>('/api/auth/me'),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<Session>('/api/auth/change-password', { method: 'POST', body: { currentPassword, newPassword } }),
};

// ---------------- Public ----------------
export interface ComicQuery {
  q?: string;
  tag?: string;
  house?: string;
  status?: string;
  sort?: string;
  page?: number;
  pageSize?: number;
}

export const publicApi = {
  site: () => request<SiteInfo>('/api/site'),
  comics: (query: ComicQuery, signal?: AbortSignal) => request<Paged<ComicCard>>(`/api/comics${qs({ ...query })}`, { signal }),
  comic: (id: string) => request<ComicDetail>(`/api/comics/${id}`),
  chapters: (comicId: string) => request<PublicChapter[]>(`/api/comics/${comicId}/chapters`),
  chapter: (id: string) => request<ChapterSummary>(`/api/chapters/${id}`),
  reader: (id: string, signal?: AbortSignal) => request<ReaderData>(`/api/chapters/${id}/reader`, { signal }),
  tags: () => request<TagWithCount[]>('/api/tags'),
  houses: () => request<PublicHouse[]>('/api/houses'),
  house: (slug: string) => request<PublicHouse>(`/api/houses/${encodeURIComponent(slug)}`),
  rankings: (period: 'day' | 'week' | 'month') => request<ComicCard[]>(`/api/rankings${qs({ period })}`),
  viewTicket: (chapterId: string, humanToken?: string) =>
    request<ViewTicket>(`/api/chapters/${chapterId}/view-ticket`, { method: 'POST', body: { humanToken } }),
  view: (chapterId: string, body: { ticket: string; pagesSeen: number; activeSeconds: number }) =>
    request<void>(`/api/chapters/${chapterId}/view`, { method: 'POST', body }),
};

export const libraryApi = {
  follows: () => request<ComicCard[]>('/api/me/follows'),
  follow: (comicId: string) => request<void>(`/api/me/follows/${comicId}`, { method: 'PUT' }),
  unfollow: (comicId: string) => request<void>(`/api/me/follows/${comicId}`, { method: 'DELETE' }),
  history: () => request<HistoryItem[]>('/api/me/history'),
  saveProgress: (chapterId: string, page: number) => request<void>('/api/me/progress', { method: 'PUT', body: { chapterId, page } }),
  state: (comicId: string) => request<ComicLibraryState>(`/api/me/comics/${comicId}/state`),
};

// ---------------- House workspace ----------------
const h = { house: true } as const;

export interface SaveComicBody {
  translatedTitle: string;
  originalTitle: string;
  slug?: string;
  description?: string;
  author?: string;
  artist?: string;
  status: string;
  isMature: boolean;
  tags: string[];
}

export interface UpdateHouseProfileBody {
  name: string;
  slug: string;
  description: string;
  links: Partial<Record<keyof HouseLinks, string | undefined>>;
  tagline?: string;
  contactEmail?: string;
  foundedYear?: number | null;
  recruiting: boolean;
  recruitmentInfo?: string;
}

export const houseApi = {
  profile: () => request<HouseProfile>('/api/translation-house/profile', h),
  updateProfile: (body: UpdateHouseProfileBody) => request<HouseProfile>('/api/translation-house/profile', { ...h, method: 'PUT', body }),
  uploadLogo: (file: File) => putFile<HouseProfile>('/api/translation-house/profile/logo', file),
  uploadHouseCover: (file: File) => putFile<HouseProfile>('/api/translation-house/profile/cover', file),
  removeHouseCover: () => request<HouseProfile>('/api/translation-house/profile/cover', { ...h, method: 'DELETE' }),
  viewStats: (query: { from?: string; to?: string }) => request<HouseViewReport>(`/api/translation-house/stats/views${qs(query)}`, h),
  dashboard: () => request<HouseDashboard>('/api/translation-house/dashboard', h),

  members: () => request<Member[]>('/api/translation-house/members', h),
  addMember: (body: { login: string; role: string; permissions: string[] }) => request<Member>('/api/translation-house/members', { ...h, method: 'POST', body }),
  updateMember: (id: string, body: { role: string; permissions: string[] }) =>
    request<Member>(`/api/translation-house/members/${id}`, { ...h, method: 'PUT', body }),
  removeMember: (id: string) => request<void>(`/api/translation-house/members/${id}`, { ...h, method: 'DELETE' }),

  comics: (query: { q?: string; status?: string; page?: number }) => request<Paged<HouseComicItem>>(`/api/translation-house/comics${qs(query)}`, h),
  createComic: (body: SaveComicBody) => request<HouseComicDetail>('/api/translation-house/comics', { ...h, method: 'POST', body }),
  comic: (id: string) => request<HouseComicDetail>(`/api/translation-house/comics/${id}`, h),
  updateComic: (id: string, body: SaveComicBody) => request<HouseComicDetail>(`/api/translation-house/comics/${id}`, { ...h, method: 'PUT', body }),
  deleteComic: (id: string) => request<void>(`/api/translation-house/comics/${id}`, { ...h, method: 'DELETE' }),
  uploadCover: (id: string, file: File) => putFile<HouseComicDetail>(`/api/translation-house/comics/${id}/cover`, file),

  chapters: (comicId: string) => request<HouseChapterItem[]>(`/api/translation-house/comics/${comicId}/chapters`, h),
  createChapter: (comicId: string, body: { number: number; title?: string }) =>
    request<HouseChapterDetail>(`/api/translation-house/comics/${comicId}/chapters`, { ...h, method: 'POST', body }),
  chapter: (id: string) => request<HouseChapterDetail>(`/api/translation-house/chapters/${id}`, h),
  updateChapter: (id: string, body: { number: number; title?: string }) =>
    request<HouseChapterDetail>(`/api/translation-house/chapters/${id}`, { ...h, method: 'PUT', body }),
  deleteChapter: (id: string) => request<void>(`/api/translation-house/chapters/${id}`, { ...h, method: 'DELETE' }),
  /** Promotional images (promoStart / promoEnd) travel before the archive in the same request. */
  upload: (id: string, file: File, onProgress: (f: number) => void, signal?: AbortSignal, promos: ExtraPart[] = []) =>
    uploadFile<JobSummary>(`/api/translation-house/chapters/${id}/upload`, file, onProgress, signal, promos),
  processing: (id: string) => request<JobSummary | null>(`/api/translation-house/chapters/${id}/processing`, h),
  publish: (id: string) => request<HouseChapterDetail>(`/api/translation-house/chapters/${id}/publish`, { ...h, method: 'POST' }),
  unpublish: (id: string) => request<HouseChapterDetail>(`/api/translation-house/chapters/${id}/unpublish`, { ...h, method: 'POST' }),
  retry: (id: string) => request<JobSummary>(`/api/translation-house/chapters/${id}/retry`, { ...h, method: 'POST' }),
  jobs: () => request<ProcessingJobItem[]>('/api/translation-house/processing', h),
  uploadLimits: () => request<UploadLimits>('/api/translation-house/upload-limits', h),
  preview: (id: string, version: number) => request<VersionPreview>(`/api/translation-house/chapters/${id}/versions/${version}/preview`, h),
  /** Private, house-only package of a ready/published version (fetch with authHeaders(true)). */
  previewPackageUrl: (id: string, version: number) => apiUrl(`/api/translation-house/chapters/${id}/versions/${version}/package`),
};

// ---------------- Admin ----------------
export const adminApi = {
  houses: (query: { q?: string; status?: string; page?: number }) => request<Paged<AdminHouseItem>>(`/api/admin/translation-houses${qs(query)}`),
  createHouse: (body: unknown) => request<CreateHouseResult>('/api/admin/translation-houses', { method: 'POST', body }),
  house: (id: string) => request<AdminHouseDetail>(`/api/admin/translation-houses/${id}`),
  updateHouse: (id: string, body: unknown) => request<AdminHouseDetail>(`/api/admin/translation-houses/${id}`, { method: 'PUT', body }),
  suspend: (id: string, reason: string) => request<AdminHouseDetail>(`/api/admin/translation-houses/${id}/suspend`, { method: 'POST', body: { reason } }),
  activate: (id: string) => request<AdminHouseDetail>(`/api/admin/translation-houses/${id}/activate`, { method: 'POST' }),
  members: (id: string) => request<Member[]>(`/api/admin/translation-houses/${id}/members`),
  addMember: (id: string, body: { login: string; role: string; permissions?: string[] }) =>
    request<Member>(`/api/admin/translation-houses/${id}/members`, { method: 'POST', body }),
  updateMember: (id: string, memberId: string, body: { role: string; permissions?: string[] }) =>
    request<Member>(`/api/admin/translation-houses/${id}/members/${memberId}`, { method: 'PUT', body }),
  removeMember: (id: string, memberId: string) => request<void>(`/api/admin/translation-houses/${id}/members/${memberId}`, { method: 'DELETE' }),
  houseComics: (id: string, query: { q?: string; page?: number }) => request<Paged<AdminHouseComic>>(`/api/admin/translation-houses/${id}/comics${qs(query)}`),
  users: (query: { q?: string; status?: string; page?: number }) => request<Paged<AdminUser>>(`/api/admin/users${qs(query)}`),
  disableUser: (id: string) => request<void>(`/api/admin/users/${id}/disable`, { method: 'POST' }),
  enableUser: (id: string) => request<void>(`/api/admin/users/${id}/enable`, { method: 'POST' }),
  resetPassword: (id: string) => request<{ temporaryPassword: string }>(`/api/admin/users/${id}/reset-password`, { method: 'POST' }),
  stats: () => request<AdminStats>('/api/admin/stats'),
  audit: (query: { action?: string; page?: number }) => request<Paged<AuditItem>>(`/api/admin/audit-logs${qs(query)}`),
  settings: () => request<SystemSettings>('/api/admin/settings'),
  updateSettings: (body: Partial<SystemSettings>) => request<SystemSettings>('/api/admin/settings', { method: 'PUT', body }),
  system: () => request<SystemInfo>('/api/admin/system'),
  revenue: (query: { from?: string; to?: string }) => request<AdminRevenueReport>(`/api/admin/revenue${qs(query)}`),
  houseRevenue: (id: string, query: { from?: string; to?: string }) => request<HouseViewReport>(`/api/admin/revenue/houses/${id}${qs(query)}`),
  ads: () => request<AdsConfig>('/api/admin/ads'),
  updateAds: (body: AdsConfig) => request<AdsConfig>('/api/admin/ads', { method: 'PUT', body }),
  uploadAdImage: (file: File) => putFile<{ url: string }>('/api/admin/ads/images', file, 'POST'),
};
