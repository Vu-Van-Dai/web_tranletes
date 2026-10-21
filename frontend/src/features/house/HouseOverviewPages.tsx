import { useState } from 'react';
import { Link } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { AlertTriangle, BookCopy, CheckCircle2, HardDrive, Loader, Users } from 'lucide-react';
import { paths } from '@/app/paths';
import { Button, Checkbox, DataTable, EmptyState, ErrorState, Field, Input, PageHeader, Skeleton, StatTile, Textarea, useToast } from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { HouseLogo } from '@/features/catalog/CatalogPages';
import { houseApi } from '@/services/api';
import { useDocumentTitle } from '@/hooks/useTheme';
import { permissionLabel, roleLabel } from '@/text/vi';
import { applyServerErrors } from '@/utils/forms';
import { chapterLabel, formatBytes, formatNumber, timeAgo } from '@/utils/format';
import { isActiveJob, JobProgress, JobStatusBadge } from './shared';

export function HouseDashboardPage() {
  useDocumentTitle('Tổng quan nhóm dịch');
  const { currentHouse } = useAuth();
  const dashboard = useQuery({
    queryKey: ['house', currentHouse?.houseId, 'dashboard'],
    queryFn: houseApi.dashboard,
    refetchInterval: (q) => (q.state.data?.recentJobs.some(isActiveJob) ? 3000 : false),
  });
  const d = dashboard.data;

  return (
    <>
      <PageHeader title={`Xin chào, ${currentHouse?.houseName ?? ''}`} description="Tình hình truyện và chương của nhóm." />
      {dashboard.isError && <ErrorState error={dashboard.error} onRetry={() => dashboard.refetch()} />}
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {d ? (
          <>
            <StatTile label="Truyện" value={formatNumber(d.comicCount)} icon={<BookCopy className="h-5 w-5" />} hint={`${formatNumber(d.chapterCount)} chương`} />
            <StatTile label="Đã đăng" value={formatNumber(d.publishedChapterCount)} icon={<CheckCircle2 className="h-5 w-5" />} hint={`${d.readyCount} chương chờ đăng`} />
            <StatTile label="Đang xử lý" value={d.processingCount} icon={<Loader className="h-5 w-5" />} hint={d.failedCount > 0 ? `${d.failedCount} chương lỗi` : 'Không có lỗi'} />
            <StatTile label="Dung lượng" value={formatBytes(d.storageBytes)} icon={<HardDrive className="h-5 w-5" />} hint={`${d.memberCount} thành viên`} />
          </>
        ) : (
          Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-24 rounded-2xl" />)
        )}
      </div>
      <h2 className="mb-3 mt-8 font-semibold">Hoạt động xử lý gần đây</h2>
      {d?.recentJobs.length === 0 ? (
        <EmptyState title="Chưa có lần tải lên nào" description="Vào mục Truyện để tạo truyện, thêm chương và tải tệp ZIP/CBZ lên." action={<Link className="link text-sm" to={paths.studio.comics}>Tới danh sách truyện</Link>} />
      ) : (
        <JobsTable jobs={d?.recentJobs ?? []} />
      )}
    </>
  );
}

function JobsTable({ jobs }: { jobs: import('@/types/api').ProcessingJobItem[] }) {
  return (
    <DataTable
      rows={jobs}
      rowKey={(j) => j.id}
      columns={[
        { key: 'chapter', header: 'Chương', cell: (j) => <Link className="hover:text-accent" to={paths.studio.chapter(j.chapterId)}>{j.comicTitle} – {chapterLabel(j.chapterNumber)}</Link> },
        { key: 'status', header: 'Trạng thái', cell: (j) => <JobStatusBadge status={j.status} /> },
        {
          key: 'progress', header: 'Tiến độ', className: 'w-64',
          cell: (j) => (j.status === 'failed' ? <span className="text-xs text-danger">{j.errorMessage}</span> : <JobProgress job={j} />),
        },
        { key: 'time', header: 'Thời gian', cell: (j) => <span className="text-xs text-muted">{timeAgo(j.createdAt)}</span>, hideOnMobile: true },
      ]}
    />
  );
}

export function HouseProcessingPage() {
  useDocumentTitle('Trạng thái xử lý');
  const { currentHouse } = useAuth();
  const jobs = useQuery({
    queryKey: ['house', currentHouse?.houseId, 'jobs'],
    queryFn: houseApi.jobs,
    refetchInterval: (q) => (q.state.data?.some(isActiveJob) ? 2000 : 15000),
  });
  return (
    <>
      <PageHeader title="Trạng thái xử lý" description="Các lần tải chương lên gần đây. Trang tự cập nhật khi có chương đang xử lý." />
      {jobs.isError ? <ErrorState error={jobs.error} onRetry={() => jobs.refetch()} />
        : jobs.data?.length === 0 ? <EmptyState title="Chưa có lần xử lý nào" />
          : jobs.isLoading ? <Skeleton className="h-48 rounded-2xl" /> : <JobsTable jobs={jobs.data ?? []} />}
    </>
  );
}

const optionalUrl = z.string().trim().url('Liên kết không hợp lệ.').or(z.literal(''));
const profileSchema = z.object({
  name: z.string().trim().min(2, 'Tên nhóm cần ít nhất 2 ký tự.').max(100),
  slug: z.string().trim().regex(/^[a-z0-9]+(?:-[a-z0-9]+)*$/, 'Chỉ gồm chữ thường không dấu, số và dấu gạch ngang.'),
  tagline: z.string().max(160, 'Tối đa 160 ký tự.'),
  description: z.string().max(2000, 'Tối đa 2000 ký tự.'),
  contactEmail: z.string().trim().email('Email không hợp lệ.').or(z.literal('')),
  foundedYear: z.string().regex(/^(\d{4})?$/, 'Nhập năm, ví dụ 2021.'),
  recruiting: z.boolean(),
  recruitmentInfo: z.string().max(1000, 'Tối đa 1000 ký tự.'),
  facebook: optionalUrl,
  tiktok: optionalUrl,
  youtube: optionalUrl,
  discord: optionalUrl,
  website: optionalUrl,
  donate: optionalUrl,
});
type ProfileForm = z.infer<typeof profileSchema>;

const linkFields = [
  { key: 'facebook', label: 'Facebook' },
  { key: 'tiktok', label: 'TikTok' },
  { key: 'youtube', label: 'YouTube' },
  { key: 'discord', label: 'Discord' },
  { key: 'website', label: 'Trang web' },
  { key: 'donate', label: 'Ủng hộ nhóm (Ko-fi, Momo…)' },
] as const;

export function HouseProfilePage() {
  useDocumentTitle('Hồ sơ nhóm');
  const { currentHouse, can, reloadMe } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const profile = useQuery({ queryKey: ['house', currentHouse?.houseId, 'profile'], queryFn: houseApi.profile });
  const editable = can('manageProfile');
  const p = profile.data;

  const form = useForm<ProfileForm>({
    resolver: zodResolver(profileSchema),
    values: p
      ? {
          name: p.name,
          slug: p.slug,
          tagline: p.tagline ?? '',
          description: p.description ?? '',
          contactEmail: p.contactEmail ?? '',
          foundedYear: p.foundedYear ? String(p.foundedYear) : '',
          recruiting: p.recruiting,
          recruitmentInfo: p.recruitmentInfo ?? '',
          facebook: p.links.facebook ?? '',
          tiktok: p.links.tiktok ?? '',
          youtube: p.links.youtube ?? '',
          discord: p.links.discord ?? '',
          website: p.links.website ?? '',
          donate: p.links.donate ?? '',
        }
      : undefined,
  });

  const recruiting = useWatch({ control: form.control, name: 'recruiting' });

  const save = form.handleSubmit(async (v) => {
    try {
      await houseApi.updateProfile({
        name: v.name,
        slug: v.slug,
        description: v.description,
        tagline: v.tagline || undefined,
        contactEmail: v.contactEmail || undefined,
        foundedYear: v.foundedYear ? Number(v.foundedYear) : null,
        recruiting: v.recruiting,
        recruitmentInfo: v.recruitmentInfo || undefined,
        links: Object.fromEntries(linkFields.map((l) => [l.key, v[l.key] || undefined])),
      });
      toast.success('Đã lưu hồ sơ nhóm.');
      queryClient.invalidateQueries({ queryKey: ['house'] });
      await reloadMe();
    } catch (e) {
      if (!applyServerErrors(e, form.setError, ['name', 'slug', 'tagline', 'description', 'contactEmail', 'foundedYear', 'recruitmentInfo'])) toast.error(e);
    }
  });

  const onImageSaved = (message: string) => async () => {
    toast.success(message);
    await queryClient.invalidateQueries({ queryKey: ['house'] });
    await reloadMe();
  };
  const logo = useMutation({ mutationFn: (file: File) => houseApi.uploadLogo(file), onSuccess: onImageSaved('Đã cập nhật logo.'), onError: (e) => toast.error(e) });
  const cover = useMutation({ mutationFn: (file: File) => houseApi.uploadHouseCover(file), onSuccess: onImageSaved('Đã cập nhật ảnh bìa nhóm.'), onError: (e) => toast.error(e) });
  const removeCover = useMutation({ mutationFn: () => houseApi.removeHouseCover(), onSuccess: onImageSaved('Đã gỡ ảnh bìa.'), onError: (e) => toast.error(e) });

  if (profile.isError) return <ErrorState error={profile.error} onRetry={() => profile.refetch()} />;
  const errors = form.formState.errors;

  return (
    <>
      <PageHeader title="Hồ sơ nhóm dịch" description="Thông tin hiển thị công khai trên trang nhóm dịch. Ảnh được tự động nén sang WebP." />

      {/* Cover banner (Facebook-cover proportions) */}
      <section className="card mb-6 overflow-hidden" aria-label="Ảnh bìa nhóm">
        <div className="relative aspect-[1640/624] max-h-72 w-full bg-surface-2">
          {p?.coverUrl ? (
            <img src={p.coverUrl} alt="Ảnh bìa nhóm" className="h-full w-full object-cover" />
          ) : (
            <div className="grid h-full place-items-center text-sm text-muted">Chưa có ảnh bìa — nên dùng ảnh ngang 1640×624px.</div>
          )}
        </div>
        {editable && (
          <div className="flex flex-wrap items-center gap-3 p-3 text-sm">
            <label className="cursor-pointer text-accent hover:underline">
              {cover.isPending ? 'Đang xử lý ảnh…' : p?.coverUrl ? 'Đổi ảnh bìa' : 'Tải ảnh bìa lên'}
              <input type="file" accept="image/jpeg,image/png,image/webp" className="sr-only" data-testid="house-cover-input"
                onChange={(e) => { if (e.target.files?.[0]) cover.mutate(e.target.files[0]); e.target.value = ''; }} />
            </label>
            {p?.coverUrl && <button type="button" className="text-muted hover:text-danger" onClick={() => removeCover.mutate()}>Gỡ ảnh bìa</button>}
            <span className="text-xs text-muted">JPEG/PNG/WebP, tối đa 8 MB.</span>
          </div>
        )}
      </section>

      <div className="grid gap-6 lg:grid-cols-[16rem_1fr]">
        <div className="card flex h-fit flex-col items-center gap-3 p-5 text-center">
          {p ? <HouseLogo url={p.logoUrl} name={p.name} size="lg" /> : <Skeleton className="h-20 w-20 rounded-2xl" />}
          {editable && (
            <label className="cursor-pointer text-sm text-accent hover:underline">
              {logo.isPending ? 'Đang tải lên…' : 'Đổi logo (JPEG/PNG/WebP)'}
              <input type="file" accept="image/jpeg,image/png,image/webp" className="sr-only"
                onChange={(e) => { if (e.target.files?.[0]) logo.mutate(e.target.files[0]); e.target.value = ''; }} />
            </label>
          )}
          {p && <Link className="text-xs text-muted hover:text-ink" to={paths.house(p.slug)}>Xem trang công khai</Link>}
        </div>
        <form onSubmit={save} className="card space-y-5 p-5" noValidate>
          <fieldset disabled={!editable} className="space-y-4">
            <legend className="mb-2 font-semibold">Thông tin chung</legend>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Tên nhóm" error={errors.name?.message} required>{(id) => <Input id={id} {...form.register('name')} />}</Field>
              <Field label="Đường dẫn" error={errors.slug?.message} hint="Dùng trong URL trang nhóm.">{(id) => <Input id={id} {...form.register('slug')} />}</Field>
            </div>
            <Field label="Khẩu hiệu" error={errors.tagline?.message} hint="Một câu ngắn hiển thị dưới tên nhóm.">{(id) => <Input id={id} placeholder="Ví dụ: Dịch bằng cả trái tim" {...form.register('tagline')} />}</Field>
            <Field label="Giới thiệu" error={errors.description?.message}>{(id) => <Textarea id={id} rows={5} {...form.register('description')} />}</Field>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="Email liên hệ" error={errors.contactEmail?.message}>{(id) => <Input id={id} type="email" placeholder="nhom@gmail.com" {...form.register('contactEmail')} />}</Field>
              <Field label="Năm thành lập" error={errors.foundedYear?.message}>{(id) => <Input id={id} inputMode="numeric" placeholder="2021" {...form.register('foundedYear')} />}</Field>
            </div>
          </fieldset>

          <fieldset disabled={!editable} className="space-y-4">
            <legend className="mb-2 font-semibold">Mạng xã hội & ủng hộ</legend>
            <div className="grid gap-4 sm:grid-cols-2">
              {linkFields.map((l) => (
                <Field key={l.key} label={l.label} error={errors[l.key]?.message}>{(id) => <Input id={id} placeholder="https://" {...form.register(l.key)} />}</Field>
              ))}
            </div>
          </fieldset>

          <fieldset disabled={!editable} className="space-y-4">
            <legend className="mb-2 font-semibold">Tuyển thành viên</legend>
            <Checkbox label="Nhóm đang tuyển thành viên" description="Hiển thị nhãn “Đang tuyển” trên trang nhóm." {...form.register('recruiting')} />
            {recruiting && (
              <Field label="Thông tin tuyển" error={errors.recruitmentInfo?.message} hint="Vị trí cần tuyển, yêu cầu, cách liên hệ…">
                {(id) => <Textarea id={id} rows={3} {...form.register('recruitmentInfo')} />}
              </Field>
            )}
          </fieldset>

          {editable ? (
            <div className="flex justify-end"><Button type="submit" loading={form.formState.isSubmitting}>Lưu thay đổi</Button></div>
          ) : (
            <p className="text-sm text-muted">Chỉ chủ nhóm mới có thể sửa hồ sơ.</p>
          )}
        </form>
      </div>
    </>
  );
}

export function HouseSettingsPage() {
  useDocumentTitle('Cài đặt');
  const { currentHouse, houses, selectHouse } = useAuth();
  const [selected, setSelected] = useState(currentHouse?.houseId ?? '');
  return (
    <>
      <PageHeader title="Cài đặt" description="Quyền của bạn và hướng dẫn tải chương." />
      <div className="grid gap-6 lg:grid-cols-2">
        <section className="card p-5">
          <h2 className="mb-3 flex items-center gap-2 font-semibold"><Users className="h-4 w-4 text-accent" /> Quyền của bạn</h2>
          <p className="mb-3 text-sm text-muted">Vai trò: <strong className="text-ink">{currentHouse ? roleLabel[currentHouse.role] : '—'}</strong></p>
          <ul className="space-y-1.5 text-sm">
            {currentHouse?.permissions.map((p) => <li key={p} className="flex items-center gap-2"><CheckCircle2 className="h-4 w-4 text-success" /> {permissionLabel[p]}</li>)}
          </ul>
          {houses.length > 1 && (
            <div className="mt-5 flex gap-2">
              <select className="h-10 flex-1 rounded-xl border border-line bg-surface px-3 text-sm" value={selected} onChange={(e) => setSelected(e.target.value)} aria-label="Nhóm làm việc">
                {houses.map((h) => <option key={h.houseId} value={h.houseId}>{h.houseName}</option>)}
              </select>
              <Button variant="outline" onClick={() => selectHouse(selected)}>Chuyển nhóm</Button>
            </div>
          )}
        </section>
        <section className="card p-5 text-sm leading-relaxed">
          <h2 className="mb-3 flex items-center gap-2 font-semibold"><AlertTriangle className="h-4 w-4 text-warn" /> Hướng dẫn tải chương</h2>
          <ul className="list-disc space-y-1.5 pl-5 text-muted">
            <li>Nén các trang thành một tệp <strong className="text-ink">.zip</strong> hoặc <strong className="text-ink">.cbz</strong>.</li>
            <li>Chỉ nhận ảnh JPEG, PNG, WebP (không nhận ảnh động). Ảnh được chuyển sang WebP tự động.</li>
            <li>Thứ tự trang theo tên tệp tự nhiên: <code>1.jpg, 2.jpg, …, 10.jpg</code>.</li>
            <li>Mỗi chiều ảnh tối đa 16.383px — hãy cắt nhỏ các ảnh dài (webtoon).</li>
            <li>Tải lại chương đã đăng sẽ tạo phiên bản mới; bản cũ vẫn hiển thị cho đến khi bạn bấm “Đăng”.</li>
          </ul>
        </section>
      </div>
    </>
  );
}
