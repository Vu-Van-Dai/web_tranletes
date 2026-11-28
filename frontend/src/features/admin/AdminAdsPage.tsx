import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { Badge, Button, Checkbox, ConfirmDialog, EmptyState, ErrorState, Field, Input, Modal, PageHeader, Select, Skeleton, Textarea, useToast } from '@/components/ui';
import { useDocumentTitle } from '@/hooks/useTheme';
import { adminApi } from '@/services/api';
import { ApiError } from '@/services/http';
import type { AdFormat, AdPage, AdSlot, AdUnit, AdsConfig, AdProvider, AdProviderReport } from '@/types/api';

import { providerLabels, providerOf } from '@/features/ads/providers';
import { dimensions, readDiagnostics, suggestedAdsterraLayout, adKind, suggestedPopunderTest, diagnosticLabel, adsterraScriptPosition, eligibleOnClickLink, isKnownAdsterraTag, suggestedHilltopLayout, suggestedMultiProviderLayout } from '@/features/ads/placement';
import { directTagKind } from '@/features/ads/directCreative';

const formatLabel: Record<AdFormat, string> = {
  banner: 'Banner trong trang',
  popup: 'Pop-up (bảng nổi)',
  newtab: 'OnClick New Tab',
  script: 'Mã của mạng quảng cáo (Popunder / In-Page / MultiTag)',
};

const formatHint: Record<AdFormat, string> = {
  banner: 'Hiển thị tại các vị trí đã chọn. Dùng ảnh + liên kết (quảng cáo trực tiếp) hoặc mã HTML banner của mạng quảng cáo.',
  popup: 'Bảng nổi che trang. Có thể cho đóng ngay, hoặc bắt buộc xem đủ số giây mới hiện nút đóng.',
  newtab: 'Dùng Popunder Direct URL chính thức của HilltopAds hoặc liên kết tài trợ trực tiếp. Mặc định bật với thông báo và tùy chọn tắt; chỉ mở khi khách bấm điều hướng nội dung công khai; không dùng Smartlink Adsterra.' ,
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
  home_extra: 'Trang chủ – banner bổ sung',
  side_left: 'Desktop rộng – bên trái (sticky)',
  side_right: 'Desktop rộng – bên phải (sticky)',
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
  provider: 'direct',
  scriptApproved: false,
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
  const freq = unit.frequencyMinutes ? ` · tối đa 1 lần / ${unit.frequencyMinutes} phút` : ' · mỗi lần mở trang';
  const close = unit.format === 'popup' ? (unit.closeMode === 'countdown' ? ` · bắt buộc xem ${unit.countdownSeconds}s` : ' · đóng ngay được') : '';
  return where + close + trigger + freq;
}

export function AdminAdsPage() {
  useDocumentTitle('Quảng cáo');
  const toast = useToast();
  const queryClient = useQueryClient();
  const ads = useQuery({ queryKey: ['admin', 'ads'], queryFn: adminApi.ads });
  const [diagnostics,setDiagnostics]=useState(readDiagnostics);
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
            <Checkbox label="Bật quảng cáo trên toàn trang" description="Tắt quảng cáo khi cấu hình mới được đồng bộ (tối đa khoảng 60 giây với trang đang mở), không xoá cấu hình."
              checked={config.enabled} onChange={(e) => update({ ...config, enabled: e.target.checked })} data-testid="ads-enabled" />
          </div>
          {config.schemaVersion === 2 ? <ProviderSettings config={config} update={update} /> : <p className="card p-4 text-sm text-muted">Quản lý theo nhà cung cấp sẽ khả dụng sau khi backend được cập nhật.</p>}
          <section className="card space-y-2 p-4">
            <h2 className="font-semibold">Bố cục và chẩn đoán</h2>
            <p className="text-sm text-muted">Sắp xếp Adsterra theo kích thước tag, giữ trạng thái bật và phê duyệt. Kiểm tra lại rồi lưu. Chuẩn bị thử Popunder sẽ tắt Smartlink và Social Bar/In-Page được nhận diện trong bản nháp, không tự bật hoặc phê duyệt Popunder. Bố trí HilltopAds + Adsterra đặt banner Hilltop ở đầu, Native ở giữa, tắt Smartlink và các script popup cạnh tranh, giữ trạng thái bật/phê duyệt của banner và Direct URL. Phân bổ banner đa nhà cung cấp mở rộng vị trí ứng viên cho các banner tương thích, không tự bật hoặc phê duyệt mã. Runtime dành mỗi mã cho một vị trí, ưu tiên mã độc lập và thử tối đa một mã dự phòng. Các script mạng và OnClick được chọn luân phiên theo lần thử gần nhất, không chạy đồng thời. OnClick dùng giới hạn chung 2 phút. Smartlink không được dùng làm banner. Để có banner cuối trang, chọn vị trí Mọi trang – cuối trang cho ít nhất một mã banner độc lập. Chẩn đoán chỉ của trình duyệt hiện tại, không phải lượt hiển thị tính tiền.</p>
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" onClick={()=>update({...config,units:config.units.map(suggestedAdsterraLayout)})}>Sắp xếp Adsterra theo kích thước</Button>
              <Button variant="outline" onClick={()=>update({...config,units:config.units.map(suggestedPopunderTest)})}>Chuẩn bị thử Popunder</Button>
              <Button variant="outline" onClick={()=>update({...config,units:config.units.map(suggestedHilltopLayout)})}>Bố trí HilltopAds + Adsterra (thử riêng)</Button>
              <Button variant="outline" onClick={()=>update({...config,units:config.units.map(suggestedMultiProviderLayout)})}>Phân bổ banner đa nhà cung cấp</Button>
              <Button variant="outline" onClick={()=>setDiagnostics(readDiagnostics())}>Đọc lại chẩn đoán cục bộ</Button>
            </div>
          </section>
          {config.units.length === 0 ? (
            <EmptyState title="Chưa có quảng cáo nào" description="Thêm banner, pop-up, tab mới hoặc mã của mạng quảng cáo." />
          ) : (
            <ul className="space-y-3" data-testid="ad-units">
              {config.units.map((u) => (
                <li key={u.id} className="card flex flex-wrap items-center gap-3 p-4">
                  {u.imageUrl ? <img src={u.imageUrl} alt="" className="h-12 w-20 shrink-0 rounded-lg object-cover" /> : <span className="grid h-12 w-20 shrink-0 place-items-center rounded-lg bg-surface-2 text-xs text-muted">{u.provider==='adcash' ? 'ADCASH' : u.html ? 'HTML' : '—'}</span>}
                  <div className="min-w-0 flex-1">
                    <p className="flex flex-wrap items-center gap-2 font-medium">{u.name}<Badge tone="accent">{formatLabel[u.format]}</Badge><Badge>{providerLabels[providerOf(u)]}</Badge>{u.format === 'banner' && u.scriptApproved && <Badge>{u.provider==='adcash'?'Adcash Display trực tiếp':'Script trực tiếp'}</Badge>}{adKind(u)!=='other'&&<Badge>{adKind(u)==='popunder'?'Popunder':adKind(u)==='socialbar'?'Social Bar':'Smartlink'}</Badge>}{u.format==='banner'&&u.html&&/<script\b/i.test(u.html)&&<Badge>{directTagKind(u.html,u.provider)==='async'?'Async':directTagKind(u.html,u.provider)==='sync'?'Sync (tối đa 1 trực tiếp)':'Tag chưa tương thích'}</Badge>}{!u.enabled && <Badge>Đang tắt</Badge>}</p>
                    <p className="text-xs text-muted">{describe(u)} · {u.device??'all'} · {dimensions(u)?dimensions(u)!.width+'×'+dimensions(u)!.height:'tự thích ứng'} · trọng số {u.weight??1}{u.placementId?' · ID '+u.placementId:''}</p>
                    {diagnostics[u.id] && <p className="text-xs text-muted">Chẩn đoán cục bộ: {diagnosticLabel[diagnostics[u.id]!.state]??diagnostics[u.id]!.state} · {new Date(diagnostics[u.id]!.at).toLocaleString('vi-VN')}</p>}
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
            <p>• <strong className="text-ink">Banner của mạng quảng cáo</strong>: loại “Banner trong trang”, dán mã HTML — mặc định chạy trong khung cách ly. Chỉ chọn chạy trực tiếp nếu đã kiểm tra nhà cung cấp; dùng JSAsync nếu mã đồng bộ không tương thích.</p>
            <p>• <strong className="text-ink">Google AdSense</strong>: dùng cấu hình triển khai PUBLIC_ADSENSE_AUTO_ADS=true nếu đã được duyệt; không dán Auto ads vào loại mã mạng quảng cáo. Không chạy chung với quảng cáo pop-up/popunder.</p>
            <p>• Ảnh tải lên được nén sang WebP. Quá nhiều pop-up có thể làm người đọc rời đi — nên đặt tần suất (ví dụ 1 lần / 30 phút).</p>
          </section>
        </div>
      )}
      {editing && <AdEditor unit={editing} providerSettings={config?.schemaVersion === 2} onCancel={() => setEditing(null)} onSave={upsert} />}
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

function AdEditor({ unit, providerSettings, onCancel, onSave }: { unit: AdUnit; providerSettings: boolean; onCancel: () => void; onSave: (u: AdUnit) => void }) {
  const toast = useToast();
  const [u, setU] = useState<AdUnit>(unit);
  const [creative, setCreative] = useState<'image' | 'html'>(unit.html && !unit.imageUrl ? 'html' : 'image');
  const set = (patch: Partial<AdUnit>) => setU((x) => ({ ...x, ...patch }));
  const upload = useMutation({
    mutationFn: (file: File) => adminApi.uploadAdImage(file),
    onSuccess: (r) => set({ imageUrl: r.url }),
    onError: (e) => toast.error(e),
  });
  const adcashBanner = providerSettings && u.provider === 'adcash' && u.format === 'banner';
  const hasCreative = (u.format === 'banner' || u.format === 'popup') && !adcashBanner;
  const usesPages = u.format !== 'banner';

  const finish = () => {
    const clean: AdUnit = {
      ...u,
      name: u.name.trim(),
      scriptApproved: adcashBanner ? u.scriptApproved === true : (u.format === 'script' || (u.format === 'banner' && creative === 'html')) && u.scriptApproved === true,
      imageUrl: !adcashBanner && hasCreative && creative === 'image' ? u.imageUrl : null,
      html: !adcashBanner && ((hasCreative && creative === 'html') || u.format === 'script') ? u.html : null,
      targetUrl: !adcashBanner && ((hasCreative && creative === 'image') || u.format === 'newtab') ? u.targetUrl : null,
    };
    onSave(clean);
  };

  return (
    <Modal open size="lg" onClose={onCancel} title={unit.name ? `Sửa: ${unit.name}` : 'Thêm quảng cáo'}
      footer={<><Button variant="outline" onClick={onCancel}>Huỷ</Button><Button disabled={!u.name.trim() || upload.isPending || (adcashBanner && !/^\\d{1,20}$/.test(u.placementId??''))} onClick={finish} data-testid="ad-editor-done">Xong</Button></>}>
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Tên (chỉ quản trị thấy)" required>{(id) => <Input id={id} value={u.name} maxLength={100} onChange={(e) => set({ name: e.target.value })} data-testid="ad-name" />}</Field>
          <Field label="Loại quảng cáo">
            {(id) => (
              <Select id={id} value={u.format} onChange={(e) => set({ format: e.target.value as AdFormat, scriptApproved: false })} data-testid="ad-format">
                {Object.entries(formatLabel).filter(([v])=>u.provider!=='adcash'||v==='banner').map(([v, l]) => <option key={v} value={v}>{l}</option>)}
              </Select>
            )}
          </Field>
        </div>
        {providerSettings && <Field label="Nhà cung cấp">{(id) => <Select id={id} value={providerOf(u)} onChange={e => {
          const provider=e.target.value as AdProvider;
          set({provider,format:provider==='adcash'?'banner':u.format,scriptApproved:false,
            placementId:provider==='adcash'&&/^\\d{1,20}$/.test(u.placementId??'')?u.placementId:null,
            width:provider==='adcash'?300:u.width,height:provider==='adcash'?250:u.height,
            slots:provider==='adcash'?['reader_bottom']:u.slots});
        }}>{Object.entries(providerLabels).filter(([p]) => p !== 'adsense').map(([p,label]) => <option key={p} value={p}>{label}</option>)}</Select>}</Field>}
        <p className="rounded-xl bg-surface-2 px-3 py-2 text-sm text-muted">{adcashBanner ? 'Adcash Display: hệ thống tự nạp thư viện chính thức một lần và gọi Zone ID vào container riêng. Chỉ hỗ trợ Display Banner; không dán HTML/JS thủ công.' : formatHint[u.format]}</p>
        {providerSettings && u.format === 'script' && <Checkbox label="Đã kiểm tra mã, nguồn cung cấp và danh mục quảng cáo hợp pháp" description="Mã chạy với quyền của trang web; chỉ bật sau khi đã xác minh nguồn. Thay đổi mã sẽ hủy phê duyệt." checked={u.scriptApproved === true} onChange={e => set({scriptApproved:e.target.checked})} />}
        {adcashBanner && <section className="space-y-3 rounded-xl border border-line p-3">
          <Field label="Adcash Display Zone ID" required hint="Nhập ID số từ Adcash → Zones → My Zones, ví dụ 12501966. Không nhập mã Autotag.">
            {id => <Input id={id} type="text" maxLength={20} placeholder="12501966" value={u.placementId??''} onChange={e=>set({placementId:e.target.value.trim()||null})} data-testid="adcash-zone-id"/>}
          </Field>
          <Checkbox label="Đã xác minh website và Display Zone trong tài khoản Adcash" description="Chỉ phê duyệt sau khi website được Adcash chấp nhận và bạn đã kiểm tra đúng zone Display. Mã thư viện chính thức sẽ chạy trực tiếp trên trang, chỉ với khách chưa đăng nhập; cần bật công tắc script toàn hệ thống." checked={u.scriptApproved===true} onChange={e=>set({scriptApproved:e.target.checked})} data-testid="adcash-approval"/>
        </section>}

        {hasCreative && (
          <div className="space-y-3">
            <div className="flex gap-4 text-sm">
              <label className="flex items-center gap-2"><input type="radio" checked={creative === 'image'} onChange={() => { setCreative('image'); set({scriptApproved:false}); }} /> Ảnh + liên kết</label>
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
              <Field label="Mã HTML của quảng cáo" hint={u.format === 'banner' && u.scriptApproved ? 'Chạy trực tiếp trong tài liệu công khai, chỉ với khách chưa đăng nhập.' : 'Chạy trong khung cách ly (iframe sandbox).'}>
                {(id) => <Textarea id={id} rows={6} className="font-mono text-xs" value={u.html ?? ''} onChange={(e) => set({ html: e.target.value || null, scriptApproved: false })} />}
              </Field>
            )}
          </div>
        )}

        {u.format==='banner'&&creative==='html'&&u.html&&/<script\b/i.test(u.html)&&(
          <p className="rounded-xl bg-surface-2 p-3 text-sm text-muted">
            {directTagKind(u.html,u.provider)==='async'?'Tag bất đồng bộ: có thể cùng tồn tại với các tag độc lập khác.':directTagKind(u.html,u.provider)==='sync'?'Tag đồng bộ: chỉ một banner trực tiếp mỗi tài liệu. Yêu cầu mã JSAsync chính thức nếu cần nhiều banner.':'Tag chưa tương thích với trình nạp trực tiếp.'}
            {!u.scriptApproved?' Đang chọn iframe cách ly; một số nhà cung cấp yêu cầu cookie nên có thể không hiển thị.':''}
          </p>
        )}
        {u.format==='newtab'&&u.provider==='adsterra'&&<p className="rounded-xl bg-surface-2 p-3 text-sm text-muted">Adsterra khuyến cáo không gắn Smartlink vào nút điều hướng. Dùng Popunder chính thức cho hành vi mở tab và Smartlink trên nút quảng cáo được ghi nhãn.</p>}
        {providerSettings && u.provider !== 'adcash' && u.format === 'banner' && creative === 'html' && (
          <Checkbox label="Chạy trực tiếp mã banner (không dùng iframe của Rồng Nhỏ)"
            description="Chỉ dùng mã chính thức đã kiểm tra và danh mục quảng cáo hợp pháp. Script có quyền của trang web; chỉ chạy với khách chưa đăng nhập, cần bật quyền script toàn hệ thống. Bỏ chọn để quay về iframe cách ly. Ưu tiên mã JSAsync; mỗi trang chỉ thử một banner đồng bộ."
            checked={u.scriptApproved === true} onChange={e => set({scriptApproved:e.target.checked})} data-testid="ad-direct-banner" />
        )}

        {u.format==='newtab'&&!eligibleOnClickLink(u)&&<p className="rounded-xl bg-surface-2 p-3 text-sm text-muted">Cần URL HTTPS Popunder Direct URL của HilltopAds hoặc URL nhà tài trợ trực tiếp. Các URL mạng khác không chạy theo cơ chế này.</p>}
        {u.format==='banner'&&u.provider==='hilltopads'&&<p className="rounded-xl bg-surface-2 p-3 text-sm text-muted">MultiTag mặc định có thể mở Popup ngoài banner. Chỉ bật banner này sau khi quản lý HilltopAds xác nhận chế độ chỉ Banner, danh mục phù hợp và tần suất. Dán nguyên mã Zone, không sửa bootstrap.</p>}
        {u.format==='script'&&u.provider==='hilltopads'&&<p className="rounded-xl bg-surface-2 p-3 text-sm text-muted">Dán mã Zone chính thức. MultiTag có thể kèm Popup; yêu cầu quản lý HilltopAds chế độ chỉ Banner nếu cần. Tần suất thực tế do nhà cung cấp điều khiển. Không bật đồng thời với OnClick Direct URL.</p>}
        {u.format==='banner'&&isKnownAdsterraTag(u)&&!u.scriptApproved&&<p className="rounded-xl bg-surface-2 p-3 text-sm text-muted">Tag Adsterra này từng lỗi trong iframe cách ly; hệ thống sẽ bỏ qua thay vì tạo khung trắng. Kiểm tra danh mục rồi phê duyệt chạy trực tiếp nếu phù hợp; tag Sync vẫn giới hạn một mã mỗi tài liệu.</p>}
        {u.format === 'newtab' && (
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Liên kết mở trong tab mới" required>{(id) => <Input id={id} placeholder="https://" value={u.targetUrl ?? ''} onChange={(e) => set({ targetUrl: e.target.value || null })} />}</Field>
            <p className="text-sm text-muted">Kích hoạt khi khách bấm truyện, chương, logo, danh sách, thể loại hoặc nhóm dịch công khai. Không bắt click nhập liệu, tài khoản hay quản trị.</p>
          </div>
        )}
        {u.format==='script'&&u.provider==='adsterra'&&adsterraScriptPosition(u)&&<p className="rounded-xl bg-surface-2 p-3 text-sm text-muted">{adsterraScriptPosition(u)==='head'?'Popunder: nạp script cuối head.':'Social Bar: nạp script cuối body.'} Mã được nạp động sau khi kiểm tra quyền và điều kiện hiển thị.</p>}
        {u.format === 'script' && (
          <Field label="Mã của mạng quảng cáo" required hint="Dán nguyên văn, kể cả thẻ <script>.">
            {(id) => <Textarea id={id} rows={6} className="font-mono text-xs" value={u.html ?? ''} onChange={(e) => set({ html: e.target.value || null, scriptApproved: false })} data-testid="ad-script" />}
          </Field>
        )}

        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Placement ID (tùy chọn)">{id=><Input id={id} maxLength={80} value={u.placementId??''} onChange={e=>set({placementId:e.target.value||null})}/>}</Field>
          <Field label="Trọng số luân phiên (1–100)">{id=><Input id={id} type="number" min={1} max={100} value={u.weight??1} onChange={e=>set({weight:Number(e.target.value)})}/>}</Field>
          {u.format==='banner' && <>
            <Field label="Thiết bị">{id=><Select id={id} value={u.device??'all'} onChange={e=>set({device:e.target.value as 'all'|'desktop'|'mobile'})}><option value="all">Mọi thiết bị</option><option value="desktop">Máy tính</option><option value="mobile">Điện thoại</option></Select>}</Field>
            <Field label="Rộng (px, bỏ trống để đọc tag)">{id=><Input id={id} type="number" min={1} max={2000} value={u.width??''} onChange={e=>set({width:e.target.value?Number(e.target.value):null})}/>}</Field>
            <Field label="Cao (px, bỏ trống để đọc tag)">{id=><Input id={id} type="number" min={1} max={1200} value={u.height??''} onChange={e=>set({height:e.target.value?Number(e.target.value):null})}/>}</Field>
          </>}
        </div>
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
        {u.format !== 'banner' && (
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Hiện sau (giây)">{(id) => <Input id={id} type="number" min={0} max={300} value={u.delaySeconds ?? 0} onChange={(e) => set({ delaySeconds: Number(e.target.value) })} />}</Field>
            <Field label="Tần suất tối đa (phút)" hint="0 = mỗi lần mở trang. Với mã mạng, thời gian chờ dùng chung giữa các mã, tối thiểu 1 phút; không kiểm soát popup do nhà cung cấp tạo.">
              {(id) => <Input id={id} type="number" min={0} max={10080} value={u.frequencyMinutes ?? 0} onChange={(e) => set({ frequencyMinutes: Number(e.target.value) })} />}
            </Field>
          </div>
        )}
        <Checkbox label="Bật quảng cáo này" checked={u.enabled} onChange={(e) => set({ enabled: e.target.checked })} />
      </div>
    </Modal>
  );
}

function ProviderSettings({ config, update }: { config: AdsConfig; update: (next: AdsConfig) => void }) {
  const reports = config.reports ?? [];
  const [entry, setEntry] = useState<AdProviderReport>({provider:'direct',month:new Date().toISOString().slice(0,7),currency:'USD',impressions:0,revenue:0});
  const valid = /^\d{4}-(0[1-9]|1[0-2])$/.test(entry.month) && /^[A-Z]{3}$/.test(entry.currency) && Number.isSafeInteger(entry.impressions) && entry.impressions >= 0 && Number.isFinite(entry.revenue) && entry.revenue >= 0;
  return <>
    <section className="card space-y-3 p-4">
      <h2 className="font-semibold">Nhà cung cấp quảng cáo</h2>
      <p className="text-sm text-muted">Bật/tắt từng nhà cung cấp. Không có mạng quảng cáo nào được đăng ký tự động. Chỉ sử dụng nội dung hợp pháp, phù hợp mọi lứa tuổi; kiểm soát danh mục trong dashboard của nhà cung cấp.</p>
      <div className="grid gap-3 sm:grid-cols-2">{Object.entries(providerLabels).map(([p,label]) => <Checkbox key={p} label={label} checked={config.providerEnabled?.[p as AdProvider] !== false} onChange={e => update({...config,providerEnabled:{...config.providerEnabled,[p]:e.target.checked}})} />)}</div>
      <Checkbox label="Cho phép script bên thứ ba chạy trực tiếp" description="Mặc định tắt. Chỉ script và banner trực tiếp đã phê duyệt riêng mới được chạy, chỉ với khách chưa đăng nhập." checked={config.allowDirectScripts === true} onChange={e => update({...config,allowDirectScripts:e.target.checked})} />
    </section>
    <section className="card space-y-3 p-4">
      <h2 className="font-semibold">Đối soát doanh thu theo nhà cung cấp</h2>
      <p className="text-sm text-muted">Nhập số liệu thực tế theo tháng từ dashboard nhà cung cấp. Không tự tính tiền từ lượt đọc truyện, không tự thay đổi doanh thu chia nhóm dịch và không cộng gộp các đơn vị tiền tệ.</p>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Nhà cung cấp">{id => <Select id={id} value={entry.provider} onChange={e => setEntry({...entry,provider:e.target.value as AdProvider})}>{Object.entries(providerLabels).map(([p,l]) => <option key={p} value={p}>{l}</option>)}</Select>}</Field>
        <Field label="Tháng">{id => <Input id={id} type="month" value={entry.month} onChange={e => setEntry({...entry,month:e.target.value})}/>}</Field>
        <Field label="Tiền tệ (ISO)">{id => <Input id={id} maxLength={3} value={entry.currency} onChange={e => setEntry({...entry,currency:e.target.value.toUpperCase()})}/>}</Field>
        <Field label="Lượt hiển thị">{id => <Input id={id} type="number" min={0} step={1} value={entry.impressions} onChange={e => setEntry({...entry,impressions:Number(e.target.value)})}/>}</Field>
        <Field label="Doanh thu báo cáo">{id => <Input id={id} type="number" min={0} step="any" value={entry.revenue} onChange={e => setEntry({...entry,revenue:Number(e.target.value)})}/>}</Field>
      </div>
      <Button variant="outline" disabled={!valid} onClick={() => update({...config,reports:[...reports.filter(r => !(r.provider===entry.provider && r.month===entry.month && r.currency===entry.currency)),entry]})}>Thêm / cập nhật số liệu</Button>
      {reports.length > 0 && <div className="overflow-x-auto"><table className="w-full text-sm"><thead><tr><th className="text-left">Mạng / tháng</th><th className="text-right">Hiển thị</th><th className="text-right">Doanh thu</th><th></th></tr></thead><tbody>{reports.map((r,i) => <tr key={r.provider+r.month+r.currency} className="border-t border-line"><td className="py-2">{providerLabels[r.provider]} · {r.month}</td><td className="text-right">{r.impressions.toLocaleString('vi-VN')}</td><td className="text-right">{r.revenue.toLocaleString('vi-VN')} {r.currency}</td><td className="text-right"><Button variant="ghost" size="sm" onClick={() => setEntry(r)}>Sửa</Button><Button variant="ghost" size="sm" onClick={() => update({...config,reports:reports.filter((_,j)=>i!==j)})}>Xóa</Button></td></tr>)}</tbody></table></div>}
    </section>
  </>;
}
