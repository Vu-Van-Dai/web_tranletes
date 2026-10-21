import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Activity, BookCopy, Building2, Cpu, FileClock, Gauge, HardDrive, Megaphone, Plus, Settings, ShieldCheck, UserPlus, Users, Wallet } from 'lucide-react';
import { paths } from '@/app/paths';
import {
  Badge, Button, Checkbox, ConfirmDialog, DataTable, EmptyState, ErrorState, Field, Input, Modal, PageHeader, Pagination, Select, Skeleton, StatTile, Tabs, Textarea, useToast,
} from '@/components/ui';
import { RequireAdmin } from '@/features/auth/guards';
import { useAuth } from '@/features/auth/AuthProvider';
import { HouseLogo } from '@/features/catalog/CatalogPages';
import { MemberEditor, MembersTable, type MemberFormValue } from '@/features/house/HouseMembersPage';
import { WorkspaceLayout } from '@/layouts/WorkspaceLayout';
import { adminApi } from '@/services/api';
import { useDebounced, useDocumentTitle } from '@/hooks/useTheme';
import { comicStatusLabel } from '@/text/vi';
import type { Member } from '@/types/api';
import { applyServerErrors } from '@/utils/forms';
import { formatBytes, formatDate, formatDateTime, formatNumber, slugify, timeAgo } from '@/utils/format';
import { DailyBars } from './DailyBars';

export function AdminLayout() {
  return (
    <RequireAdmin>
      <WorkspaceLayout
        title="Quản trị hệ thống"
        items={[
          { to: paths.admin.root, label: 'Tổng quan', icon: <Gauge className="h-4 w-4" />, end: true },
          { to: paths.admin.houses, label: 'Nhóm dịch', icon: <Building2 className="h-4 w-4" /> },
          { to: paths.admin.users, label: 'Người dùng', icon: <Users className="h-4 w-4" /> },
          { to: paths.admin.stats, label: 'Thống kê', icon: <Activity className="h-4 w-4" /> },
          { to: paths.admin.revenue, label: 'Doanh thu', icon: <Wallet className="h-4 w-4" /> },
          { to: paths.admin.ads, label: 'Quảng cáo', icon: <Megaphone className="h-4 w-4" /> },
          { to: paths.admin.audit, label: 'Nhật ký', icon: <FileClock className="h-4 w-4" /> },
          { to: paths.admin.settings, label: 'Cài đặt hệ thống', icon: <Settings className="h-4 w-4" /> },
        ]}
      />
    </RequireAdmin>
  );
}

// ---------------- Dashboard ----------------
export function AdminDashboardPage() {
  useDocumentTitle('Quản trị hệ thống');
  const stats = useQuery({ queryKey: ['admin', 'stats'], queryFn: adminApi.stats });
  const system = useQuery({ queryKey: ['admin', 'system'], queryFn: adminApi.system, refetchInterval: 10_000 });
  const s = stats.data;
  const sys = system.data;
  return (
    <>
      <PageHeader title="Tổng quan hệ thống" description="Nhóm dịch, nội dung và tình trạng máy chủ." />
      {stats.isError && <ErrorState error={stats.error} onRetry={() => stats.refetch()} />}
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {s ? (
          <>
            <StatTile label="Nhóm dịch" value={s.activeHouses} hint={`${s.suspendedHouses} đang tạm ngưng`} icon={<Building2 className="h-5 w-5" />} />
            <StatTile label="Người dùng" value={formatNumber(s.users)} hint={`${s.disabledUsers} bị khoá`} icon={<Users className="h-5 w-5" />} />
            <StatTile label="Truyện" value={formatNumber(s.comics)} hint={`${formatNumber(s.publishedChapters)} chương đã đăng`} icon={<BookCopy className="h-5 w-5" />} />
            <StatTile label="Dung lượng đã đăng" value={formatBytes(s.publishedBytes)} hint={`Tổng gói: ${formatBytes(s.totalPackageBytes)}`} icon={<HardDrive className="h-5 w-5" />} />
          </>
        ) : Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-24 rounded-2xl" />)}
      </div>
      <div className="mt-6 grid gap-4 lg:grid-cols-2">
        {s && <DailyBars title="Lượt tải chương lên" data={s.uploadsPerDay} unit="lượt" />}
        <section className="card p-4" data-testid="system-status">
          <h2 className="mb-3 flex items-center gap-2 font-semibold"><Cpu className="h-4 w-4 text-accent" /> Máy chủ</h2>
          {sys ? (
            <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
              <dt className="text-muted">Hàng đợi xử lý</dt><dd>{sys.processing.queueLength}/{sys.processing.queueCapacity}</dd>
              <dt className="text-muted">Luồng xử lý ảnh</dt><dd>{sys.processing.activeConversions}/{sys.processing.imageConcurrency} đang chạy</dd>
              <dt className="text-muted">Job thành công / lỗi <span className="text-xs">(từ lần khởi động)</span></dt><dd>{sys.processing.jobsSucceeded} / {sys.processing.jobsFailed}</dd>
              <dt className="text-muted">Bộ nhớ (working set)</dt><dd>{formatBytes(sys.runtime.workingSetBytes)}</dd>
              <dt className="text-muted">CPU · luồng</dt><dd>{sys.runtime.processorCount} lõi · {sys.runtime.threadCount} luồng</dd>
              <dt className="text-muted">Kho lưu trữ</dt><dd>{sys.storage.store.toUpperCase()} · trống {formatBytes(sys.storage.freeBytes)}</dd>
              <dt className="text-muted">Chạy được</dt><dd>{Math.round(sys.runtime.uptimeSeconds / 60)} phút</dd>
            </dl>
          ) : <Skeleton className="h-40" />}
        </section>
      </div>
    </>
  );
}

// ---------------- Houses ----------------
const createSchema = z
  .object({
    name: z.string().trim().min(2, 'Tên nhóm cần ít nhất 2 ký tự.').max(100),
    slug: z.string().trim().regex(/^([a-z0-9]+(?:-[a-z0-9]+)*)?$/, 'Chỉ gồm chữ thường không dấu, số và dấu gạch ngang.'),
    description: z.string().max(2000),
    ownerMode: z.enum(['new', 'existing']),
    existingLogin: z.string(),
    username: z.string(),
    email: z.string(),
    displayName: z.string(),
  })
  .superRefine((v, ctx) => {
    if (v.ownerMode === 'existing' && !v.existingLogin.trim()) ctx.addIssue({ code: 'custom', path: ['existingLogin'], message: 'Nhập email hoặc tên đăng nhập của chủ nhóm.' });
    if (v.ownerMode === 'new') {
      if (!/^[a-z0-9_.]{3,32}$/.test(v.username.trim())) ctx.addIssue({ code: 'custom', path: ['username'], message: 'Tên đăng nhập 3–32 ký tự: chữ thường không dấu, số, . hoặc _' });
      if (!/^\S+@\S+\.\S+$/.test(v.email.trim())) ctx.addIssue({ code: 'custom', path: ['email'], message: 'Email không hợp lệ.' });
      if (v.displayName.trim().length < 2) ctx.addIssue({ code: 'custom', path: ['displayName'], message: 'Tên hiển thị cần ít nhất 2 ký tự.' });
    }
  });

function CreateHouseDialog({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [created, setCreated] = useState<{ id: string; password: string | null; owner: string } | null>(null);
  const form = useForm<z.infer<typeof createSchema>>({
    resolver: zodResolver(createSchema),
    defaultValues: { name: '', slug: '', description: '', ownerMode: 'new', existingLogin: '', username: '', email: '', displayName: '' },
  });
  const mode = useWatch({ control: form.control, name: 'ownerMode' });
  const e = form.formState.errors;

  const submit = form.handleSubmit(async (v) => {
    try {
      const result = await adminApi.createHouse({
        name: v.name,
        slug: v.slug || undefined,
        description: v.description || undefined,
        owner: v.ownerMode === 'existing'
          ? { existingLogin: v.existingLogin.trim() }
          : { newAccount: { username: v.username.trim(), email: v.email.trim(), displayName: v.displayName.trim() } },
      });
      queryClient.invalidateQueries({ queryKey: ['admin', 'houses'] });
      toast.success('Đã tạo nhóm dịch.');
      setCreated({ id: result.house.id, password: result.generatedPassword, owner: v.ownerMode === 'new' ? v.username : v.existingLogin });
    } catch (err) {
      if (!applyServerErrors(err, form.setError, ['name', 'slug', 'description', 'existingLogin', 'username', 'email', 'displayName'])) toast.error(err);
    }
  });

  if (created) {
    return (
      <Modal open onClose={onClose} title="Đã tạo nhóm dịch" size="sm"
        footer={<Button onClick={() => navigate(paths.admin.house(created.id))} data-testid="go-to-house">Xem nhóm dịch</Button>}>
        {created.password ? (
          <div className="space-y-3 text-sm">
            <p>Tài khoản chủ nhóm <strong>{created.owner}</strong> đã được tạo với mật khẩu tạm:</p>
            <code className="block select-all rounded-xl bg-surface-2 p-3 text-base font-semibold" data-testid="temp-password">{created.password}</code>
            <p className="text-muted">Mật khẩu chỉ hiển thị một lần. Chủ nhóm sẽ phải đổi mật khẩu ở lần đăng nhập đầu tiên.</p>
          </div>
        ) : (
          <p className="text-sm">Tài khoản <strong>{created.owner}</strong> đã được đặt làm chủ nhóm.</p>
        )}
      </Modal>
    );
  }

  return (
    <Modal open onClose={onClose} title="Tạo nhóm dịch" size="lg"
      footer={<><Button variant="outline" onClick={onClose}>Huỷ</Button><Button loading={form.formState.isSubmitting} onClick={submit} data-testid="create-house-submit">Tạo nhóm</Button></>}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Tên nhóm dịch" error={e.name?.message} required>
            {(id) => <Input id={id} {...form.register('name', { onChange: (ev) => !form.getFieldState('slug').isDirty && form.setValue('slug', slugify(ev.target.value)) })} data-testid="house-name" />}
          </Field>
          <Field label="Đường dẫn" error={e.slug?.message}>{(id) => <Input id={id} {...form.register('slug')} />}</Field>
        </div>
        <Field label="Giới thiệu" error={e.description?.message}>{(id) => <Textarea id={id} rows={3} {...form.register('description')} />}</Field>
        <fieldset className="space-y-3 rounded-xl border border-line p-4">
          <legend className="px-1 text-sm font-medium">Chủ nhóm</legend>
          <Tabs value={mode} onChange={(v) => form.setValue('ownerMode', v)} items={[{ value: 'new', label: 'Tạo tài khoản mới' }, { value: 'existing', label: 'Tài khoản có sẵn' }]} />
          {mode === 'existing' ? (
            <Field label="Email hoặc tên đăng nhập" error={e.existingLogin?.message}>{(id) => <Input id={id} {...form.register('existingLogin')} />}</Field>
          ) : (
            <div className="grid gap-4 sm:grid-cols-3">
              <Field label="Tên đăng nhập" error={e.username?.message}>{(id) => <Input id={id} {...form.register('username')} data-testid="owner-username" />}</Field>
              <Field label="Email" error={e.email?.message}>{(id) => <Input id={id} type="email" {...form.register('email')} data-testid="owner-email" />}</Field>
              <Field label="Tên hiển thị" error={e.displayName?.message}>{(id) => <Input id={id} {...form.register('displayName')} data-testid="owner-display" />}</Field>
            </div>
          )}
          {mode === 'new' && <p className="text-xs text-muted">Hệ thống tạo mật khẩu tạm; chủ nhóm phải đổi ở lần đăng nhập đầu.</p>}
        </fieldset>
      </form>
    </Modal>
  );
}

export function AdminHousesPage() {
  useDocumentTitle('Nhóm dịch');
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const [q, setQ] = useState('');
  const debounced = useDebounced(q);
  const status = params.get('status') ?? '';
  const page = Number(params.get('page') ?? '1') || 1;
  const [creating, setCreating] = useState(false);
  const houses = useQuery({ queryKey: ['admin', 'houses', debounced, status, page], queryFn: () => adminApi.houses({ q: debounced || undefined, status: status || undefined, page }), placeholderData: keepPreviousData });

  return (
    <>
      <PageHeader title="Nhóm dịch" description={houses.data && `${houses.data.total} nhóm`}
        actions={<Button icon={<Plus className="h-4 w-4" />} onClick={() => setCreating(true)} data-testid="create-house">Tạo nhóm dịch</Button>} />
      <div className="mb-4 flex flex-wrap gap-2">
        <Input className="max-w-xs" placeholder="Tìm theo tên…" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Tìm nhóm dịch" />
        <Select className="w-44" value={status} onChange={(e) => setParams(e.target.value ? { status: e.target.value } : {})} aria-label="Trạng thái">
          <option value="">Mọi trạng thái</option>
          <option value="active">Đang hoạt động</option>
          <option value="suspended">Tạm ngưng</option>
        </Select>
      </div>
      {houses.isError ? <ErrorState error={houses.error} onRetry={() => houses.refetch()} />
        : houses.isLoading ? <Skeleton className="h-64 rounded-2xl" />
          : houses.data?.items.length === 0 ? <EmptyState title="Chưa có nhóm dịch nào" action={<Button onClick={() => setCreating(true)}>Tạo nhóm đầu tiên</Button>} />
            : (
              <DataTable rows={houses.data?.items ?? []} rowKey={(h) => h.id} onRowClick={(h) => navigate(paths.admin.house(h.id))} testId="admin-houses"
                columns={[
                  { key: 'name', header: 'Nhóm dịch', cell: (h) => <div className="flex items-center gap-3"><HouseLogo url={h.logoUrl} name={h.name} /><div><p className="font-medium">{h.name}</p><p className="text-xs text-muted">/{h.slug}</p></div></div> },
                  { key: 'status', header: 'Trạng thái', cell: (h) => <Badge tone={h.status === 'active' ? 'success' : 'danger'}>{h.status === 'active' ? 'Hoạt động' : 'Tạm ngưng'}</Badge> },
                  { key: 'members', header: 'Thành viên', cell: (h) => h.memberCount },
                  { key: 'comics', header: 'Truyện / chương đăng', cell: (h) => `${h.comicCount} / ${h.publishedChapterCount}` },
                  { key: 'created', header: 'Ngày tạo', cell: (h) => <span className="text-xs text-muted">{formatDate(h.createdAt)}</span>, hideOnMobile: true },
                ]} />
            )}
      {houses.data && <Pagination page={houses.data.page} totalPages={houses.data.totalPages} onChange={(p) => setParams({ page: String(p) })} />}
      {creating && <CreateHouseDialog onClose={() => setCreating(false)} />}
    </>
  );
}

export function AdminHouseDetailPage() {
  const { id = '' } = useParams();
  const toast = useToast();
  const queryClient = useQueryClient();
  const house = useQuery({ queryKey: ['admin', 'house', id], queryFn: () => adminApi.house(id) });
  const members = useQuery({ queryKey: ['admin', 'house', id, 'members'], queryFn: () => adminApi.members(id) });
  const [comicPage, setComicPage] = useState(1);
  const comics = useQuery({ queryKey: ['admin', 'house', id, 'comics', comicPage], queryFn: () => adminApi.houseComics(id, { page: comicPage }), placeholderData: keepPreviousData });
  const [suspending, setSuspending] = useState(false);
  const [reason, setReason] = useState('');
  const [editing, setEditing] = useState<Member | null>(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<Member | null>(null);
  useDocumentTitle(house.data?.name);

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['admin', 'house', id] });
  const status = useMutation({
    mutationFn: (suspend: boolean) => (suspend ? adminApi.suspend(id, reason) : adminApi.activate(id)),
    onSuccess: (_, suspend) => { toast.success(suspend ? 'Đã tạm ngưng nhóm dịch.' : 'Đã kích hoạt lại nhóm dịch.'); setSuspending(false); refresh(); },
    onError: (e) => toast.error(e),
  });
  const saveMember = useMutation({
    mutationFn: (v: MemberFormValue) => (editing ? adminApi.updateMember(id, editing.id, { role: v.role, permissions: v.permissions }) : adminApi.addMember(id, v)),
    onSuccess: () => { toast.success('Đã lưu thành viên.'); setEditing(null); setAdding(false); refresh(); },
    onError: (e) => toast.error(e),
  });
  const removeMember = useMutation({
    mutationFn: (m: Member) => adminApi.removeMember(id, m.id),
    onSuccess: () => { toast.success('Đã xoá thành viên.'); setRemoving(null); refresh(); },
    onError: (e) => toast.error(e),
  });

  if (house.isError) return <ErrorState error={house.error} onRetry={() => house.refetch()} />;
  const h = house.data;
  return (
    <>
      <Link to={paths.admin.houses} className="mb-3 inline-block text-sm text-muted hover:text-ink">← Danh sách nhóm dịch</Link>
      <PageHeader
        title={h?.name ?? '…'}
        description={h && <span className="flex flex-wrap items-center gap-2"><Badge tone={h.status === 'active' ? 'success' : 'danger'} data-testid="house-status">{h.status === 'active' ? 'Đang hoạt động' : 'Tạm ngưng'}</Badge>{h.suspendedReason && <span>Lý do: {h.suspendedReason}</span>}</span>}
        actions={h && (h.status === 'active'
          ? <Button variant="danger" onClick={() => setSuspending(true)} data-testid="suspend-house">Tạm ngưng</Button>
          : <Button onClick={() => status.mutate(false)} loading={status.isPending} data-testid="activate-house">Kích hoạt lại</Button>)}
      />
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {h ? (
          <>
            <StatTile label="Thành viên" value={h.memberCount} />
            <StatTile label="Truyện" value={h.comicCount} />
            <StatTile label="Chương (đã đăng)" value={`${h.chapterCount} (${h.publishedChapterCount})`} />
            <StatTile label="Dung lượng" value={formatBytes(h.storageBytes)} />
          </>
        ) : Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-24 rounded-2xl" />)}
      </div>

      <div className="mb-3 mt-8 flex items-center justify-between">
        <h2 className="font-semibold">Thành viên</h2>
        <Button size="sm" variant="outline" icon={<UserPlus className="h-4 w-4" />} onClick={() => setAdding(true)}>Thêm thành viên</Button>
      </div>
      {members.data ? <MembersTable members={members.data} canManage onEdit={setEditing} onRemove={setRemoving} /> : <Skeleton className="h-32 rounded-2xl" />}

      <h2 className="mb-3 mt-8 font-semibold">Truyện của nhóm <span className="text-sm font-normal text-muted">(chỉ thông tin tổng quan)</span></h2>
      {comics.data?.items.length === 0 ? <EmptyState title="Nhóm chưa có truyện" /> : (
        <DataTable rows={comics.data?.items ?? []} rowKey={(c) => c.id} testId="admin-house-comics"
          columns={[
            { key: 't', header: 'Tên dịch', cell: (c) => <span className="font-medium">{c.translatedTitle}</span> },
            { key: 'o', header: 'Tên gốc', cell: (c) => <span className="text-muted">{c.originalTitle}</span> },
            { key: 's', header: 'Trạng thái', cell: (c) => <Badge>{comicStatusLabel[c.status]}</Badge>, hideOnMobile: true },
            { key: 'c', header: 'Chương (đã đăng)', cell: (c) => `${c.chapterCount} (${c.publishedChapterCount})` },
            { key: 'b', header: 'Dung lượng', cell: (c) => formatBytes(c.storageBytes), hideOnMobile: true },
            { key: 'u', header: 'Cập nhật', cell: (c) => <span className="text-xs text-muted">{timeAgo(c.updatedAt)}</span>, hideOnMobile: true },
          ]} />
      )}
      {comics.data && <Pagination page={comics.data.page} totalPages={comics.data.totalPages} onChange={setComicPage} />}

      <Modal open={suspending} onClose={() => setSuspending(false)} title="Tạm ngưng nhóm dịch" size="sm"
        footer={<><Button variant="outline" onClick={() => setSuspending(false)}>Huỷ</Button><Button variant="danger" loading={status.isPending} onClick={() => status.mutate(true)} data-testid="confirm-suspend">Tạm ngưng</Button></>}>
        <p className="mb-3 text-sm text-muted">Truyện của nhóm sẽ bị ẩn khỏi trang đọc và thành viên không thể thao tác cho đến khi được kích hoạt lại.</p>
        <Field label="Lý do (không bắt buộc)">{(fid) => <Textarea id={fid} rows={3} value={reason} onChange={(e) => setReason(e.target.value)} />}</Field>
      </Modal>
      {(adding || editing) && <MemberEditor open member={editing} saving={saveMember.isPending} onClose={() => { setAdding(false); setEditing(null); }} onSubmit={(v) => saveMember.mutate(v)} />}
      <ConfirmDialog open={!!removing} danger title="Xoá thành viên" confirmLabel="Xoá" loading={removeMember.isPending}
        message={<>Xoá <strong>{removing?.displayName}</strong> khỏi nhóm?</>} onClose={() => setRemoving(null)} onConfirm={() => removing && removeMember.mutate(removing)} />
    </>
  );
}

// ---------------- Users ----------------
export function AdminUsersPage() {
  useDocumentTitle('Người dùng');
  const toast = useToast();
  const queryClient = useQueryClient();
  const { user: me } = useAuth();
  const [q, setQ] = useState('');
  const debounced = useDebounced(q);
  const [page, setPage] = useState(1);
  const [temp, setTemp] = useState<{ username: string; password: string } | null>(null);
  const users = useQuery({ queryKey: ['admin', 'users', debounced, page], queryFn: () => adminApi.users({ q: debounced || undefined, page }), placeholderData: keepPreviousData });
  const act = useMutation({
    mutationFn: async ({ id, kind }: { id: string; username: string; kind: 'disable' | 'enable' | 'reset' }) =>
      kind === 'disable' ? adminApi.disableUser(id) : kind === 'enable' ? adminApi.enableUser(id) : adminApi.resetPassword(id),
    onSuccess: (result, v) => {
      if (v.kind === 'reset' && result) setTemp({ username: v.username, password: (result as { temporaryPassword: string }).temporaryPassword });
      else toast.success(v.kind === 'disable' ? 'Đã khoá tài khoản.' : 'Đã mở khoá tài khoản.');
      queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });
    },
    onError: (e) => toast.error(e),
  });

  return (
    <>
      <PageHeader title="Người dùng" description={users.data && `${users.data.total} tài khoản`} />
      <Input className="mb-4 max-w-xs" placeholder="Tìm tên, email…" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} aria-label="Tìm người dùng" />
      {users.isError ? <ErrorState error={users.error} onRetry={() => users.refetch()} /> : users.isLoading ? <Skeleton className="h-64 rounded-2xl" /> : (
        <DataTable rows={users.data?.items ?? []} rowKey={(u) => u.id}
          columns={[
            { key: 'u', header: 'Tài khoản', cell: (u) => <div><p className="font-medium">{u.displayName} {u.systemRole === 'systemAdmin' && <ShieldCheck className="inline h-4 w-4 text-accent" aria-label="Quản trị" />}</p><p className="text-xs text-muted">@{u.username} · {u.email}</p></div> },
            { key: 's', header: 'Trạng thái', cell: (u) => <div className="flex gap-1"><Badge tone={u.status === 'active' ? 'success' : 'danger'}>{u.status === 'active' ? 'Hoạt động' : 'Đã khoá'}</Badge>{u.mustChangePassword && <Badge tone="warn">Mật khẩu tạm</Badge>}</div> },
            { key: 'h', header: 'Nhóm dịch', cell: (u) => u.houseCount, hideOnMobile: true },
            { key: 'l', header: 'Đăng nhập gần nhất', cell: (u) => <span className="text-xs text-muted">{timeAgo(u.lastLoginAt)}</span>, hideOnMobile: true },
            {
              key: 'a', header: '', className: 'text-right',
              cell: (u) => u.id !== me?.id && (
                <div className="flex justify-end gap-1">
                  <Button size="sm" variant="ghost" onClick={() => act.mutate({ id: u.id, username: u.username, kind: 'reset' })}>Đặt lại mật khẩu</Button>
                  {u.status === 'active'
                    ? <Button size="sm" variant="ghost" className="text-danger" onClick={() => act.mutate({ id: u.id, username: u.username, kind: 'disable' })}>Khoá</Button>
                    : <Button size="sm" variant="ghost" onClick={() => act.mutate({ id: u.id, username: u.username, kind: 'enable' })}>Mở khoá</Button>}
                </div>
              ),
            },
          ]} />
      )}
      {users.data && <Pagination page={users.data.page} totalPages={users.data.totalPages} onChange={setPage} />}
      <Modal open={!!temp} onClose={() => setTemp(null)} title="Mật khẩu tạm" size="sm" footer={<Button onClick={() => setTemp(null)}>Đã lưu</Button>}>
        <p className="text-sm">Mật khẩu tạm của <strong>{temp?.username}</strong>:</p>
        <code className="mt-2 block select-all rounded-xl bg-surface-2 p-3 text-base font-semibold">{temp?.password}</code>
        <p className="mt-2 text-xs text-muted">Chỉ hiển thị một lần. Người dùng phải đổi mật khẩu khi đăng nhập.</p>
      </Modal>
    </>
  );
}

// ---------------- Statistics ----------------
export function AdminStatsPage() {
  useDocumentTitle('Thống kê');
  const stats = useQuery({ queryKey: ['admin', 'stats'], queryFn: adminApi.stats });
  const s = stats.data;
  if (stats.isError) return <ErrorState error={stats.error} onRetry={() => stats.refetch()} />;
  return (
    <>
      <PageHeader title="Thống kê" description="Số liệu toàn hệ thống (không bao gồm nội dung trang truyện)." />
      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        {s ? (
          <>
            <StatTile label="Chương đã đăng" value={formatNumber(s.publishedChapters)} hint={`${formatNumber(s.publishedPages)} trang`} />
            <StatTile label="Đang xử lý" value={s.processingChapters} hint={`${s.failedChapters} chương lỗi`} />
            <StatTile label="Job 24 giờ qua" value={s.jobsLast24h} hint={`${s.failedJobsLast24h} thất bại`} />
            <StatTile label="Tổng chương" value={formatNumber(s.chapters)} />
          </>
        ) : Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-24 rounded-2xl" />)}
      </div>
      {s && (
        <div className="mt-6 grid gap-4 lg:grid-cols-2">
          <DailyBars title="Lượt tải chương lên" data={s.uploadsPerDay} unit="lượt" />
          <DailyBars title="Chương được đăng" data={s.publishedPerDay} unit="chương" />
        </div>
      )}
    </>
  );
}

// ---------------- Audit ----------------
const auditLabels: Record<string, string> = {
  'house.created': 'Tạo nhóm dịch', 'house.updated': 'Sửa nhóm dịch', 'house.suspended': 'Tạm ngưng nhóm', 'house.activated': 'Kích hoạt nhóm',
  'house.profile_updated': 'Sửa hồ sơ nhóm', 'member.added': 'Thêm thành viên', 'member.updated': 'Đổi quyền thành viên', 'member.removed': 'Xoá thành viên',
  'user.created': 'Tạo tài khoản', 'user.disabled': 'Khoá tài khoản', 'user.enabled': 'Mở khoá tài khoản', 'user.password_reset': 'Đặt lại mật khẩu',
  'comic.created': 'Tạo truyện', 'comic.deleted': 'Xoá truyện', 'chapter.deleted': 'Xoá chương', 'chapter.uploaded': 'Tải chương lên',
  'chapter.published': 'Đăng chương', 'chapter.unpublished': 'Gỡ chương', 'chapter.retried': 'Xử lý lại chương', 'settings.updated': 'Đổi cài đặt',
  'security.cross_tenant_denied': 'Chặn truy cập chéo nhóm', 'security.house_selector_mismatch': 'Chọn nhóm không hợp lệ',
};

export function AdminAuditPage() {
  useDocumentTitle('Nhật ký');
  const [action, setAction] = useState('');
  const [page, setPage] = useState(1);
  const audit = useQuery({ queryKey: ['admin', 'audit', action, page], queryFn: () => adminApi.audit({ action: action || undefined, page }), placeholderData: keepPreviousData });
  return (
    <>
      <PageHeader title="Nhật ký hoạt động" description="Các thao tác quản trị và sự kiện bảo mật quan trọng." />
      <Select className="mb-4 w-64" value={action} onChange={(e) => { setAction(e.target.value); setPage(1); }} aria-label="Lọc theo loại">
        <option value="">Tất cả</option>
        <option value="house.">Nhóm dịch</option>
        <option value="member.">Thành viên</option>
        <option value="user.">Tài khoản</option>
        <option value="chapter.">Chương</option>
        <option value="comic.">Truyện</option>
        <option value="security.">Bảo mật</option>
      </Select>
      {audit.isError ? <ErrorState error={audit.error} onRetry={() => audit.refetch()} /> : audit.data?.items.length === 0 ? <EmptyState title="Chưa có sự kiện" /> : (
        <DataTable rows={audit.data?.items ?? []} rowKey={(a) => a.id} testId="audit-table"
          columns={[
            { key: 'at', header: 'Thời gian', cell: (a) => <span className="text-xs">{formatDateTime(a.at)}</span> },
            { key: 'act', header: 'Hành động', cell: (a) => <Badge tone={a.action.startsWith('security.') ? 'danger' : 'neutral'}>{auditLabels[a.action] ?? a.action}</Badge> },
            { key: 'who', header: 'Người thực hiện', cell: (a) => a.actorUsername ?? '—' },
            { key: 'house', header: 'Nhóm', cell: (a) => a.houseName ?? '—', hideOnMobile: true },
            { key: 'ip', header: 'IP', cell: (a) => <code className="text-xs text-muted">{a.ip ?? '—'}</code>, hideOnMobile: true },
          ]} />
      )}
      {audit.data && <Pagination page={audit.data.page} totalPages={audit.data.totalPages} onChange={setPage} />}
    </>
  );
}

// ---------------- Settings ----------------
export function AdminSettingsPage() {
  useDocumentTitle('Cài đặt hệ thống');
  const toast = useToast();
  const queryClient = useQueryClient();
  const settings = useQuery({ queryKey: ['admin', 'settings'], queryFn: adminApi.settings });
  const system = useQuery({ queryKey: ['admin', 'system'], queryFn: adminApi.system });
  const [draft, setDraft] = useState<{ registrationEnabled: boolean; announcement: string; rate: string } | null>(null);
  const current = draft ?? (settings.data
    ? { registrationEnabled: settings.data.registrationEnabled, announcement: settings.data.announcement ?? '', rate: String(settings.data.revenuePerThousandViews) }
    : null);
  const save = useMutation({
    mutationFn: () => adminApi.updateSettings({
      registrationEnabled: current!.registrationEnabled,
      announcement: current!.announcement,
      revenuePerThousandViews: Math.max(0, Number(current!.rate) || 0),
    }),
    onSuccess: () => { toast.success('Đã lưu cài đặt.'); setDraft(null); queryClient.invalidateQueries({ queryKey: ['admin', 'settings'] }); queryClient.invalidateQueries({ queryKey: ['site'] }); },
    onError: (e) => toast.error(e),
  });

  return (
    <>
      <PageHeader title="Cài đặt hệ thống" />
      <div className="grid gap-6 lg:grid-cols-2">
        <section className="card space-y-4 p-5">
          <h2 className="font-semibold">Chung</h2>
          {current ? (
            <>
              <Checkbox label="Cho phép đăng ký tài khoản mới" checked={current.registrationEnabled} onChange={(e) => setDraft({ ...current, registrationEnabled: e.target.checked })} />
              <Field label="Thông báo trên trang (để trống để tắt)" hint="Hiển thị ở đầu mọi trang đọc truyện.">
                {(id) => <Textarea id={id} rows={3} maxLength={500} value={current.announcement} onChange={(e) => setDraft({ ...current, announcement: e.target.value })} />}
              </Field>
              <Field label="Đơn giá trả nhóm dịch (VNĐ / 1.000 lượt xem hợp lệ)"
                hint="Áp dụng cho các lượt xem được tính từ lúc lưu; doanh thu đã ghi nhận không bị tính lại. Đặt 0 để tạm ngừng tính tiền.">
                {(id) => <Input id={id} type="number" min={0} step={100} inputMode="decimal" value={current.rate} onChange={(e) => setDraft({ ...current, rate: e.target.value })} data-testid="revenue-rate" />}
              </Field>
              <div className="flex justify-end"><Button loading={save.isPending} onClick={() => save.mutate()}>Lưu cài đặt</Button></div>
            </>
          ) : <Skeleton className="h-40" />}
        </section>
        <section className="card p-5">
          <h2 className="mb-3 font-semibold">Giới hạn hệ thống (từ biến môi trường)</h2>
          {system.data ? (
            <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
              <dt className="text-muted">Dung lượng tải lên tối đa</dt><dd>{formatBytes(system.data.limits.maxUploadSize)}</dd>
              <dt className="text-muted">Dung lượng giải nén tối đa</dt><dd>{formatBytes(system.data.limits.maxExtractedSize)}</dd>
              <dt className="text-muted">Số trang tối đa / chương</dt><dd>{system.data.limits.maxPagesPerChapter}</dd>
              <dt className="text-muted">Kích thước ảnh tối đa</dt><dd>{system.data.limits.maxImageDimension}px · {formatNumber((system.data.limits.maxImagePixels ?? 0) / 1_000_000)} MP</dd>
              <dt className="text-muted">Chiều rộng trang tối đa</dt><dd>{system.data.limits.pageMaxWidth}px</dd>
              <dt className="text-muted">Chất lượng WebP</dt><dd>{system.data.limits.webpQuality}</dd>
              <dt className="text-muted">Luồng xử lý ảnh</dt><dd>{system.data.processing.imageConcurrency}</dd>
              <dt className="text-muted">Kho lưu trữ</dt><dd>{system.data.storage.store.toUpperCase()}</dd>
            </dl>
          ) : <Skeleton className="h-40" />}
        </section>
      </div>
    </>
  );
}
