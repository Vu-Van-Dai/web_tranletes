// Mirrors the backend DTOs (camelCase JSON).

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
  totalPages: number;
}

export type ComicStatus = 'ongoing' | 'completed' | 'hiatus' | 'dropped';
export type ChapterStatus = 'draft' | 'processing' | 'ready' | 'published' | 'unpublished' | 'failed';
export type JobStatus = 'queued' | 'running' | 'succeeded' | 'failed' | 'canceled';
export type JobStage = 'queued' | 'validating' | 'converting' | 'packaging' | 'finalizing' | 'completed';
export type Permission =
  | 'viewMembers'
  | 'manageComics'
  | 'manageChapters'
  | 'uploadChapters'
  | 'publishChapters'
  | 'manageMembers'
  | 'manageProfile';

// ---------------- Auth ----------------
export interface Membership {
  houseId: string;
  houseName: string;
  houseSlug: string;
  logoUrl: string | null;
  houseStatus: 'active' | 'suspended';
  role: 'owner' | 'staff';
  permissions: Permission[];
}

export interface Me {
  id: string;
  username: string;
  email: string;
  displayName: string;
  systemRole: 'user' | 'systemAdmin';
  mustChangePassword: boolean;
  memberships: Membership[];
}

export interface Session {
  accessToken: string;
  expiresAt: string;
  user: Me;
}

// ---------------- Public catalog ----------------
export interface HouseRef {
  id: string;
  name: string;
  slug: string;
  logoUrl: string | null;
}

export interface TagDto {
  id: string;
  name: string;
  slug: string;
}

export interface ChapterRef {
  id: string;
  number: number;
  title: string | null;
  publishedAt: string | null;
}

export interface ComicCard {
  id: string;
  slug: string;
  translatedTitle: string;
  originalTitle: string;
  coverUrl: string | null;
  status: ComicStatus;
  isMature: boolean;
  house: HouseRef;
  chapterCount: number;
  lastChapterPublishedAt: string | null;
  latestChapters: ChapterRef[];
}

export interface ComicDetail {
  id: string;
  slug: string;
  translatedTitle: string;
  originalTitle: string;
  description: string | null;
  author: string | null;
  artist: string | null;
  coverUrl: string | null;
  status: ComicStatus;
  isMature: boolean;
  house: HouseRef;
  tags: TagDto[];
  chapterCount: number;
  firstChapter: ChapterRef | null;
  latestChapter: ChapterRef | null;
  lastChapterPublishedAt: string | null;
  createdAt: string;
}

export interface PublicChapter {
  id: string;
  number: number;
  title: string | null;
  publishedAt: string | null;
  pageCount: number;
}

export interface ChapterSummary {
  id: string;
  comicId: string;
  comicTitle: string;
  comicSlug: string;
  isMature: boolean;
  number: number;
  title: string | null;
  publishedAt: string | null;
  pageCount: number;
  prev: ChapterRef | null;
  next: ChapterRef | null;
  house: HouseRef;
}

export interface ReaderData {
  chapter: ChapterSummary;
  package: { url: string; version: number; size: number; sha256: string; pageCount: number; format: number };
  pages: { n: number; w: number; h: number }[];
}

export interface PublicHouse {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  logoUrl: string | null;
  comicCount: number;
  links: { facebook: string | null; discord: string | null; website: string | null };
  createdAt: string;
}

export interface TagWithCount extends TagDto {
  comicCount: number;
}

export interface SiteInfo {
  registrationEnabled: boolean;
  announcement: string | null;
}

export interface HistoryItem {
  comic: ComicCard;
  chapter: ChapterRef;
  page: number;
  updatedAt: string;
}

export interface ComicLibraryState {
  following: boolean;
  lastRead: ChapterRef | null;
  lastPage: number | null;
}

// ---------------- House workspace ----------------
export interface HouseProfile {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  logoUrl: string | null;
  status: string;
  links: { facebook: string | null; discord: string | null; website: string | null };
  myRole: 'owner' | 'staff';
  myPermissions: Permission[];
  createdAt: string;
}

export interface Member {
  id: string;
  userId: string;
  username: string;
  displayName: string;
  email: string | null;
  role: 'owner' | 'staff';
  permissions: Permission[];
  status: string;
  joinedAt: string;
}

export interface HouseComicItem {
  id: string;
  slug: string;
  translatedTitle: string;
  originalTitle: string;
  coverUrl: string | null;
  status: ComicStatus;
  isMature: boolean;
  chapterCount: number;
  publishedChapterCount: number;
  updatedAt: string;
}

export interface HouseComicDetail extends Omit<HouseComicItem, 'chapterCount' | 'publishedChapterCount'> {
  description: string | null;
  author: string | null;
  artist: string | null;
  tags: TagDto[];
  chapterCount: number;
  publishedChapterCount: number;
  createdAt: string;
}

export interface JobSummary {
  id: string;
  chapterId: string;
  status: JobStatus;
  stage: JobStage;
  progressDone: number;
  progressTotal: number;
  attempts: number;
  errorCode: string | null;
  errorMessage: string | null;
  originalFileName: string | null;
  uploadSize: number;
  createdAt: string;
  startedAt: string | null;
  finishedAt: string | null;
}

export interface HouseChapterItem {
  id: string;
  number: number;
  title: string | null;
  status: ChapterStatus;
  pageCount: number;
  publishedAt: string | null;
  updatedAt: string;
  publishedVersion: number | null;
  readyVersion: number | null;
  latestJob: JobSummary | null;
}

export interface PackageVersion {
  id: string;
  version: number;
  status: string;
  fileSize: number;
  pageCount: number;
  checksum: string | null;
  createdAt: string;
  publishedAt: string | null;
}

export interface HouseChapterDetail {
  id: string;
  comicId: string;
  comicTitle: string;
  number: number;
  title: string | null;
  status: ChapterStatus;
  pageCount: number;
  publishedAt: string | null;
  createdAt: string;
  updatedAt: string;
  publishedVersion: number | null;
  readyVersion: number | null;
  versions: PackageVersion[];
  latestJob: JobSummary | null;
  actions: { canUpload: boolean; canPublish: boolean; canUnpublish: boolean; canRetry: boolean; canDelete: boolean };
}

export interface ProcessingJobItem {
  id: string;
  chapterId: string;
  comicId: string;
  comicTitle: string;
  chapterNumber: number;
  status: JobStatus;
  stage: JobStage;
  progressDone: number;
  progressTotal: number;
  errorMessage: string | null;
  originalFileName: string | null;
  createdAt: string;
  finishedAt: string | null;
}

export interface HouseDashboard {
  comicCount: number;
  chapterCount: number;
  publishedChapterCount: number;
  processingCount: number;
  failedCount: number;
  readyCount: number;
  storageBytes: number;
  memberCount: number;
  recentJobs: ProcessingJobItem[];
}

// ---------------- Admin ----------------
export interface AdminHouseItem {
  id: string;
  name: string;
  slug: string;
  logoUrl: string | null;
  status: 'active' | 'suspended';
  memberCount: number;
  comicCount: number;
  publishedChapterCount: number;
  createdAt: string;
}

export interface AdminHouseDetail {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  logoUrl: string | null;
  status: 'active' | 'suspended';
  suspendedReason: string | null;
  links: { facebook: string | null; discord: string | null; website: string | null };
  memberCount: number;
  comicCount: number;
  chapterCount: number;
  publishedChapterCount: number;
  storageBytes: number;
  owners: { userId: string; username: string; displayName: string; email: string }[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateHouseResult {
  house: AdminHouseDetail;
  ownerUserId: string;
  generatedPassword: string | null;
}

export interface AdminHouseComic {
  id: string;
  translatedTitle: string;
  originalTitle: string;
  status: ComicStatus;
  isMature: boolean;
  chapterCount: number;
  publishedChapterCount: number;
  storageBytes: number;
  lastChapterPublishedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface AdminUser {
  id: string;
  username: string;
  email: string;
  displayName: string;
  systemRole: 'user' | 'systemAdmin';
  status: 'active' | 'disabled';
  mustChangePassword: boolean;
  houseCount: number;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface DailyPoint {
  day: string;
  count: number;
}

export interface AdminStats {
  activeHouses: number;
  suspendedHouses: number;
  users: number;
  disabledUsers: number;
  comics: number;
  chapters: number;
  publishedChapters: number;
  processingChapters: number;
  failedChapters: number;
  publishedBytes: number;
  totalPackageBytes: number;
  publishedPages: number;
  jobsLast24h: number;
  failedJobsLast24h: number;
  uploadsPerDay: DailyPoint[];
  publishedPerDay: DailyPoint[];
}

export interface AuditItem {
  id: string;
  at: string;
  actorUserId: string | null;
  actorUsername: string | null;
  action: string;
  targetType: string | null;
  targetId: string | null;
  houseId: string | null;
  houseName: string | null;
  ip: string | null;
  data: string | null;
  success: boolean;
}

export interface SystemSettings {
  registrationEnabled: boolean;
  announcement: string | null;
}

export interface SystemInfo {
  limits: Record<string, number>;
  processing: {
    imageConcurrency: number;
    queueLength: number;
    queueCapacity: number;
    workerBacklog: number;
    activeConversions: number;
    maxObservedConversions: number;
    jobsSucceeded: number;
    jobsFailed: number;
    pagesConverted: number;
    lastJobMilliseconds: number;
    currentJobId: string | null;
  };
  runtime: {
    processorCount: number;
    workingSetBytes: number;
    gcHeapBytes: number;
    totalCpuMilliseconds: number;
    threadPoolBusyWorkers: number;
    threadCount: number;
    uptimeSeconds: number;
  };
  storage: { store: string; freeBytes: number | null; publicImageUrl: string };
}

export interface VersionPreview {
  chapterId: string;
  comicId: string;
  comicTitle: string;
  number: number;
  title: string | null;
  version: number;
  status: string;
  fileSize: number;
  pageCount: number;
  sha256: string;
  isPublished: boolean;
  canPublish: boolean;
  pages: { n: number; w: number; h: number; size: number; sourceName: string | null }[];
}
