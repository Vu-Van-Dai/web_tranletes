import type { ChapterStatus, ComicStatus, JobStage, JobStatus, Permission } from '@/types/api';

/** Shared Vietnamese labels (single source of truth for terminology). */
export const comicStatusLabel: Record<ComicStatus, string> = {
  ongoing: 'Đang tiến hành',
  completed: 'Đã hoàn thành',
  hiatus: 'Tạm ngưng',
  dropped: 'Đã dừng',
};

export const chapterStatusLabel: Record<ChapterStatus, string> = {
  draft: 'Bản nháp',
  processing: 'Đang xử lý',
  ready: 'Sẵn sàng đăng',
  published: 'Đã đăng',
  unpublished: 'Đã gỡ',
  failed: 'Xử lý lỗi',
};

export const jobStatusLabel: Record<JobStatus, string> = {
  queued: 'Đang chờ',
  running: 'Đang xử lý',
  succeeded: 'Hoàn tất',
  failed: 'Thất bại',
  canceled: 'Đã huỷ',
};

export const jobStageLabel: Record<JobStage, string> = {
  queued: 'Trong hàng đợi',
  validating: 'Kiểm tra tệp nén',
  converting: 'Chuyển đổi ảnh sang WebP',
  packaging: 'Đóng gói chương',
  finalizing: 'Hoàn tất',
  completed: 'Xong',
};

export const permissionLabel: Record<Permission, string> = {
  viewMembers: 'Xem thành viên',
  manageComics: 'Quản lý truyện',
  manageChapters: 'Quản lý chương',
  uploadChapters: 'Tải chương lên',
  publishChapters: 'Đăng / gỡ chương',
  manageMembers: 'Quản lý thành viên',
  manageProfile: 'Sửa hồ sơ nhóm',
};

export const delegablePermissions: Permission[] = ['viewMembers', 'manageComics', 'manageChapters', 'uploadChapters', 'publishChapters'];

export const roleLabel = { owner: 'Chủ nhóm', staff: 'Thành viên' } as const;

export const sortOptions = [
  { value: 'updated', label: 'Mới cập nhật' },
  { value: 'new', label: 'Truyện mới' },
  { value: 'az', label: 'Tên A → Z' },
  { value: 'chapters', label: 'Nhiều chương' },
] as const;
