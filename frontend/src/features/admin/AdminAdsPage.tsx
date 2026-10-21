import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { Badge, Button, Checkbox, ConfirmDialog, EmptyState, ErrorState, Field, Input, Modal, PageHeader, Select, Skeleton, Textarea, useToast } from '@/components/ui';
import { useDocumentTitle } from '@/hooks/useTheme';
import { adminApi } from '@/services/api';
import { ApiError } from '@/services/http';
import type { AdFormat, AdPage, AdSlot, AdUnit, AdsConfig } from '@/types/api';

const formatLabel: Record<AdFormat, string> = {
  banner: 'Banner trong trang',
  popup: 'Pop-up (bảng nổi)',
  newtab: 'Mở tab mới khi bấm',
  script: 'Mã của mạng quảng cáo',
};

const formatHint: Record<AdFormat, string> = {
  banner: 'Hiển thị tại các vị trí đã chọn. Dùng ảnh + liên kết (quảng cáo trực tiếp) hoặc mã HTML banner của mạng quảng cáo.',
  popup: 'Bảng nổi che trang. Có thể cho đóng ngay, hoặc bắt buộc xem đủ số giây mới hiện nút đóng.',
  newtab: 'Khi người đọc bấm, liên kết quảng cáo mở trong tab mới; trang họ bấm vẫn mở bình thường ở tab hiện tại. Chọn mở ở lần bấm đầu tiên, hoặc mỗi khi bấm vào truyện / chương / nút đọc, theo dõi.',
  script: 'Dán nguyên mã của mạng quảng cáo (popunder, social bar, auto ads…). Mã chạy trực tiếp trên trang công khai — chỉ dán mã từ nguồn tin cậy.',
};

const slotLabel: Record<AdSlot, string> = {
  site_top: 'Mọi trang – dưới thanh tiêu đề',
  site_bottom: 'Mọi trang – cuối trang',
  home_top: 'Trang chủ – đầu trang',
  home_middle: 'Trang chủ – giữa trang',
  browse_top: 'Danh sách / tìm kiếm / thể loại',
  comic_top: 'Trang truyện – đầu trang',
  comic_chapters: 'Trang truyện – trên danh sách chương',
  house_top: 'Trang nhóm dịch',
  reader_top: 'Trang đọc – trên trang đầu',
  reader_bottom: 'Trang đọc – cuối chương',
};

const pageLabel: Record<AdPage, string> = {
  home: 'Trang chủ',
  browse: 'Danh sách / tìm kiếm',
  comic: 'Trang truyện',
  reader: 'Trang đọc chương',
  house: 'Trang nhóm dịch',
  library: 'Tủ truyện',
  other: 'Trang khác (đăng nhập…)',
};

const emptyUnit = (): AdUnit => ({
  id: `ad-${Math.random().toString(36).slice(2, 10)}`,
  name: '',
  enabled: true,
  format: 'banner',
  imageUrl: null,
  targetUrl: null,
  html: null,
  slots: ['home_top'],
  pages: [],
  closeMode: 'button',
  countdownSeconds: 5,
  delaySeconds: 0,
  frequencyMinutes: 30,
});

function describe(unit: AdUnit): string {
  if (unit.format === 'banner') return (unit.slots ?? []).map((s) => slotLabel[s]).join(' · ');
  const where = unit.pages && unit.pages.length > 0 ? unit.pages.map((p) => pageLabel[p]).join(', ') : 'Mọi trang công khai';
  const trigger = unit.format === 'newtab' ? (unit.trigger === 'content' ? ' · khi bấm truyện/chương' : ' · lần bấm đầu tiên') : '';
  const freq = unit.format === 'script' ? '' : unit.frequencyMinutes ? ` · tối đa 1 lần / ${unit.frequencyMinutes} phút` : ' · mỗi lần mở trang';
  const close = unit.format === 'popup' ? (unit.closeMode === 'countdown' ? ` · bắt buộc xem ${unit.countdownSeconds}s` : ' · đóng ngay được') : '';
  return where + close + trigger + freq;
}

export function AdminAdsPage() {
  useDocumentTitle('Quảng cáo');
  const toast = useToast();
  const queryClient = useQueryClient();
  const ads = useQuery({ queryKey: ['admin', 'ads'], queryFn: adminApi.ads });
  const [draft, setDraft] = useState<AdsConfig | null>(null);
  const [editing, setEditing] = useState<AdUnit | null>(null);
  const [deleting, setDeleting] = useState<AdUnit | null>(null);
  const config = draft ?? ads.data ?? null;
  const dirty = draft !== null;

  const save = useMutation({
    mutationFn: (body: AdsConfig) => adminApi.updateAds(body),
    onSuccess: (saved) => {
      toast.success('Đã lưu quảng cáo. Người đọc sẽ thấy thay đổi trong khoảng 1 phút.');
      setDraft(null);
      queryClient.setQueryData(['admin', 'ads'], saved);
      queryClient.invalidateQueries({ queryKey: ['site'] });
    },
    onError: (e) => {
      const first = e instanceof ApiError && e.errors ? Object.values(e.errors)[0]?.[0] : null;
      toast.error(first ? new Error(first) : e);
    },
  });

  const update = (next: AdsConfig) => setDraft(next);
  const upsert = (unit: AdUnit) => {
    if (!config) return;
    const exists = config.units.some((u) => u.id === unit.id);
    update({ ...config, units: exists ? config.units.map((u) => (u.id === unit.id ? unit : u)) : [...config.units, unit] });
    setEditing(null);
  };

  if (ads.isError) return <ErrorState error={ads.error} onRetry={() => ads.refetch()} />;

  return (
    <>
      <PageHeader
        title="Quảng cáo"
        description="Quảng cáo hiển thị trên trang chủ, trang truyện, trang đọc và mọi trang công khai — với mọi người đọc, kể cả tài khoản của nhóm dịch. Khu vực quản lý nhóm dịch và trang quản trị luôn không có quảng cáo."
        actions={
          <>
            <Button variant="outline" icon={<Plus className="h-4 w-4" />} disabled={!config} onClick={() => setEditing(emptyUnit())} data-testid="add-ad">Thêm quảng cáo</Button>
            <Button disabled={!dirty} loading={save.isPending} onClick={() => config && save.mutate(config)} data-testid="save-ads">Lưu thay đổi</Button>
          </>
        }
      />
      {!config ? <Skeleton className="h-48 rounded-2xl" /> : (
        <div className="space-y-4">
          <div className="card p-4">
            <Checkbox label="Bật quảng cáo trên toàn trang" description="Tắt để ẩn mọi quảng cáo ngay lập tức mà không xoá cấu hình."
              checked={config.enabled} onChange={(e) => update({ ...config, enabled: e.target.checked })} data-testid="ads-enabled" />
          </div>
          {config.units.length === 0 ? (
            <EmptyState title="Chưa có quảng cáo nào" description="Thêm banner, pop-up, tab mới hoặc mã của mạng quảng cáo." />
          ) : (
            <ul className="space-y-3" data-testid="ad-units">
              {config.units.map((u) => (
                <li key={u.id} className="card flex flex-wrap items-center gap-3 p-4">
                  {u.imageUrl ? <img src={u.imageUrl} alt="" className="h-12 w-20 shrink-0 rounded-lg object-cover" /> : <span className="grid h-12 w-20 shrink-0 place-items-center rounded-lg bg-surface-2 text-xs text-muted">{u.html ? 'HTML' : '—'}</span>}
                  <div className="min-w-0 flex-1">
                    <p className="flex flex-wrap items-center gap-2 font-medium">{u.name}<Badge tone="accent">{formatLabel[u.format]}</Badge>{!u.enabled && <Badge>Đang tắt</Badge>}</p>
                    <p className="truncate text-xs text-muted">{describe(u)}</p>
                  </div>
                  <Checkbox label="Bật" checked={u.enabled} onChange={(e) => upsert({ ...u, enabled: e.target.checked })} />
                  <Button variant="ghost" size="icon" aria-label={`Sửa ${u.name}`} onClick={() => setEditing(u)}><Pencil className="h-4 w-4" /></Button>
                  <Button variant="ghost" size="icon" className="text-danger" aria-label={`Xoá ${u.name}`} onClick={() => setDeleting(u)}><Trash2 className="h-4 w-4" /></Button>
                </li>
              ))}
            </ul>
          )}
          {dirty && <p className="text-sm text-warn">Có thay đổi chưa lưu.</p>}
          <section className="card space-y-2 p-5 text-sm leading-relaxed text-muted">
            <h2 className="font-semibold text-ink">Gợi ý</h2>
            <p>• <strong className="text-ink">Mạng quảng cáo popunder / social bar</strong> (Adsterra, Monetag, PropellerAds…): tạo một quảng cáo loại “Mã của mạng quảng cáo” và dán mã họ cung cấp.</p>
            <p>• <strong className="text-ink">Banner của mạng quảng cáo</strong>: loại “Banner trong trang”, dán mã HTML — mã chạy trong khung cách ly nên không đọc được phiên đăng nhập của người dùng.</p>
            <p>• <strong className="text-ink">Google AdSense</strong>: không cho phép pop-up/popunder và không chạy trong khung cách ly; dùng loại “Mã của mạng quảng cáo” cho đoạn mã Auto ads và đọc kỹ chính sách nội dung của Google.</p>
            <p>• Ảnh tải lên được nén sang WebP. Quá nhiều pop-up có thể làm người đọc rời đi — nên đặt tần suất (ví dụ 1 lần / 30 phút).</p>
          </section>
        </div>
      )}
      {editing && <AdEditor unit={editing} onCancel={() => setEditing(null)} onSave={upsert} />}
      <ConfirmDialog open={deleting !== null} danger title="Xoá quảng cáo" confirmLabel="Xoá"
        message={<>Xoá <strong>{deleting?.name}</strong>? Nhớ bấm “Lưu thay đổi” sau đó.</>}
        onClose={() => setDeleting(null)}
        onConfirm={() => {
          if (config && deleting) update({ ...config, units: config.units.filter((u) => u.id !== deleting.id) });
          setDeleting(null);
        }} />
    </>
  );
}

function toggle<T>(list: T[] | null, value: T, on: boolean): T[] {
  const set = new Set(list ?? []);
  if (on) set.add(value);
  else set.delete(value);
  return [...set];
}

function AdEditor({ unit, onCancel, onSave }: { unit: AdUnit; onCancel: () => void; onSave: (u: AdUnit) => void }) {
  const toast = useToast();
  const [u, setU] = useState<AdUnit>(unit);
  const [creative, setCreative] = useState<'image' | 'html'>(unit.html && !unit.imageUrl ? 'html' : 'image');
  const set = (patch: Partial<AdUnit>) => setU((x) => ({ ...x, ...patch }));
  const upload = useMutation({
    mutationFn: (file: File) => adminApi.uploadAdImage(file),
    onSuccess: (r) => set({ imageUrl: r.url }),
    onError: (e) => toast.error(e),
  });
  const hasCreative = u.format === 'banner' || u.format === 'popup';
  const usesPages = u.format !== 'banner';

  const finish = () => {
    const clean: AdUnit = {
      ...u,
      name: u.name.trim(),
      imageUrl: hasCreative && creative === 'image' ? u.imageUrl : null,
      html: (hasCreative && creative === 'html') || u.format === 'script' ? u.html : null,
      targetUrl: (hasCreative && creative === 'image') || u.format === 'newtab' ? u.targetUrl : null,
    };
    onSave(clean);
  };

  return (
    <Modal open size="lg" onClose={onCancel} title={unit.name ? `Sửa: ${unit.name}` : 'Thêm quảng cáo'}
      footer={<><Button variant="outline" onClick={onCancel}>Huỷ</Button><Button disabled={!u.name.trim() || upload.isPending} onClick={finish} data-testid="ad-editor-done">Xong</Button></>}>
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Tên (chỉ quản trị thấy)" required>{(id) => <Input id={id} value={u.name} maxLength={100} onChange={(e) => set({ name: e.target.value })} data-testid="ad-name" />}</Field>
          <Field label="Loại quảng cáo">
            {(id) => (
              <Select id={id} value={u.format} onChange={(e) => set({ format: e.target.value as AdFormat })} data-testid="ad-format">
                {Object.entries(formatLabel).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
              </Select>
            )}
          </Field>
        </div>
        <p className="rounded-xl bg-surface-2 px-3 py-2 text-sm text-muted">{formatHint[u.format]}</p>

        {hasCreative && (
          <div className="space-y-3">
            <div className="flex gap-4 text-sm">
              <label className="flex items-center gap-2"><input type="radio" checked={creative === 'image'} onChange={() => setCreative('image')} /> Ảnh + liên kết</label>
              <label className="flex items-center gap-2"><input type="radio" checked={creative === 'html'} onChange={() => setCreative('html')} /> Mã HTML</label>
            </div>
            {creative === 'image' ? (
              <div className="grid gap-4 sm:grid-cols-[10rem_1fr]">
                <div className="space-y-2">
                  <div className="grid aspect-video place-items-center overflow-hidden rounded-xl border border-line bg-surface-2">
                    {u.imageUrl ? <img src={u.imageUrl} alt="" className="h-full w-full object-contain" /> : <span className="text-xs text-muted">Chưa có ảnh</span>}
                  </div>
                  <label className="block cursor-pointer text-center text-sm text-accent hover:underline">
                    {upload.isPending ? 'Đang nén sang WebP…' : 'Tải ảnh lên'}
                    <input type="file" accept="image/jpeg,image/png,image/webp" className="sr-only" data-testid="ad-image-input"
                      onChange={(e) => { if (e.target.files?.[0]) upload.mutate(e.target.files[0]); e.target.value = ''; }} />
                  </label>
                </div>
                <div className="space-y-3">
                  <Field label="Hoặc dán địa chỉ ảnh">{(id) => <Input id={id} placeholder="https://" value={u.imageUrl ?? ''} onChange={(e) => set({ imageUrl: e.target.value || null })} />}</Field>
                  <Field label="Liên kết khi bấm vào" required>{(id) => <Input id={id} placeholder="https://" value={u.targetUrl ?? ''} onChange={(e) => set({ targetUrl: e.target.value || null })} data-testid="ad-target" />}</Field>
                </div>
              </div>
            ) : (
              <Field label="Mã HTML của quảng cáo" hint="Chạy trong khung cách ly (iframe sandbox).">
                {(id) => <Textarea id={id} rows={6} className="font-mono text-xs" value={u.html ?? ''} onChange={(e) => set({ html: e.target.value || null })} />}
              </Field>
            )}
          </div>
        )}

        {u.format === 'newtab' && (
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Liên kết mở trong tab mới" required>{(id) => <Input id={id} placeholder="https://" value={u.targetUrl ?? ''} onChange={(e) => set({ targetUrl: e.target.value || null })} />}</Field>
            <Field label="Mở khi" hint="Trang người đọc bấm vẫn mở bình thường; quảng cáo mở ở tab riêng.">
              {(id) => (
                <Select id={id} value={u.trigger ?? 'any'} onChange={(e) => set({ trigger: e.target.value as 'any' | 'content' })} data-testid="ad-trigger">
                  <option value="any">Lần bấm đầu tiên bất kỳ</option>
                  <option value="content">Bấm vào truyện, chương, nút đọc / theo dõi</option>
                </Select>
              )}
            </Field>
          </div>
        )}
        {u.format === 'script' && (
          <Field label="Mã của mạng quảng cáo" required hint="Dán nguyên văn, kể cả thẻ <script>.">
            {(id) => <Textarea id={id} rows={6} className="font-mono text-xs" value={u.html ?? ''} onChange={(e) => set({ html: e.target.value || null })} data-testid="ad-script" />}
          </Field>
        )}

        {u.format === 'banner' ? (
          <fieldset>
            <legend className="mb-2 text-sm font-medium">Vị trí hiển thị</legend>
            <div className="grid gap-1 sm:grid-cols-2">
              {(Object.keys(slotLabel) as AdSlot[]).map((s) => (
                <Checkbox key={s} label={slotLabel[s]} checked={u.slots?.includes(s) ?? false} onChange={(e) => set({ slots: toggle(u.slots, s, e.target.checked) })} />
              ))}
            </div>
          </fieldset>
        ) : usesPages && (
          <fieldset>
            <legend className="mb-2 text-sm font-medium">Hiện trên trang (để trống = mọi trang công khai)</legend>
            <div className="grid gap-1 sm:grid-cols-2">
              {(Object.keys(pageLabel) as AdPage[]).map((p) => (
                <Checkbox key={p} label={pageLabel[p]} checked={u.pages?.includes(p) ?? false} onChange={(e) => set({ pages: toggle(u.pages, p, e.target.checked) })} />
              ))}
            </div>
          </fieldset>
        )}

        {u.format === 'popup' && (
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Cách đóng">
              {(id) => (
                <Select id={id} value={u.closeMode ?? 'button'} onChange={(e) => set({ closeMode: e.target.value as 'button' | 'countdown' })} data-testid="ad-close-mode">
                  <option value="button">Có nút đóng ngay</option>
                  <option value="countdown">Bắt buộc xem rồi mới đóng</option>
                </Select>
              )}
            </Field>
            {u.closeMode === 'countdown' && (
              <Field label="Số giây bắt buộc xem (3–60)">{(id) => <Input id={id} type="number" min={3} max={60} value={u.countdownSeconds ?? 5} onChange={(e) => set({ countdownSeconds: Number(e.target.value) })} />}</Field>
            )}
          </div>
        )}
        {u.format !== 'banner' && u.format !== 'script' && (
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Hiện sau (giây)">{(id) => <Input id={id} type="number" min={0} max={300} value={u.delaySeconds ?? 0} onChange={(e) => set({ delaySeconds: Number(e.target.value) })} />}</Field>
            <Field label="Tần suất tối đa (phút)" hint="0 = mỗi lần mở trang. Đếm riêng từng trình duyệt.">
              {(id) => <Input id={id} type="number" min={0} max={10080} value={u.frequencyMinutes ?? 0} onChange={(e) => set({ frequencyMinutes: Number(e.target.value) })} />}
            </Field>
          </div>
        )}
        <Checkbox label="Bật quảng cáo này" checked={u.enabled} onChange={(e) => set({ enabled: e.target.checked })} />
      </div>
    </Modal>
  );
}
