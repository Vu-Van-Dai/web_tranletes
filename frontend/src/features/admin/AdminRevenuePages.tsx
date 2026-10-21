import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ArrowLeft } from 'lucide-react';
import { paths } from '@/app/paths';
import { DataTable, ErrorState, PageHeader, Skeleton } from '@/components/ui';
import { ComicStatsTable, DailyViews, DateRangePicker, downloadCsv, ExportReportButton, formatVnd, presetRange, TotalsTiles } from '@/features/stats/ViewReport';
import { useDocumentTitle } from '@/hooks/useTheme';
import { adminApi } from '@/services/api';
import { cn, formatNumber } from '@/utils/format';

const rejectionLabel: Record<string, string> = {
  no_ticket: 'Không có vé xem (script/ứng dụng cũ)',
  invalid_ticket: 'Vé giả hoặc sai chương',
  too_fast: 'Gửi trước thời gian đọc tối thiểu',
  expired: 'Vé hết hạn',
  visitor_mismatch: 'Vé dùng trên máy/mạng khác',
  bot: 'Bot / trình duyệt tự động',
  replay: 'Dùng lại vé đã tính',
  incomplete: 'Đọc chưa đủ trang/thời gian',
  own_house: 'Thành viên nhóm tự đọc',
  rate_limited: 'Vượt giới hạn lượt/giờ',
  human_check_failed: 'Không qua kiểm tra Turnstile',
};

export function AdminRevenuePage() {
  useDocumentTitle('Doanh thu & lượt xem');
  const [range, setRange] = useState(() => presetRange('month'));
  const report = useQuery({ queryKey: ['admin', 'revenue', range], queryFn: () => adminApi.revenue(range), placeholderData: keepPreviousData });
  const r = report.data;

  return (
    <>
      <PageHeader
        title="Doanh thu & lượt xem"
        description="Số tiền phải trả từng nhóm dịch theo lượt xem hợp lệ. Đơn giá đặt tại Cài đặt hệ thống và áp dụng cho các lượt xem tính từ lúc đổi."
        actions={r && (
          <button
            type="button"
            className="text-sm text-accent hover:underline disabled:opacity-50"
            disabled={r.houses.length === 0}
            onClick={() => downloadCsv(`doanh-thu_${r.from}_${r.to}.csv`, ['Nhóm dịch', 'Lượt xem hợp lệ', 'Bị loại', 'Tỉ lệ bị loại', 'Doanh thu (VND)'],
              r.houses.map((h) => [h.name, h.views, h.suspiciousViews, `${(h.suspiciousRatio * 100).toFixed(1)}%`, h.revenue]))}
          >
            Xuất CSV
          </button>
        )}
      />
      <div className="mb-6"><DateRangePicker value={range} onChange={setRange} /></div>
      {report.isError ? <ErrorState error={report.error} onRetry={() => report.refetch()} /> : !r ? <Skeleton className="h-64 rounded-2xl" /> : (
        <div className="space-y-6">
          <TotalsTiles totals={r.totals} rate={r.revenuePerThousandViews} revenueVisible />
          {r.daily.length > 0 && <DailyViews daily={r.daily} />}
          <DataTable
            rows={r.houses}
            rowKey={(h) => h.houseId}
            testId="revenue-houses"
            columns={[
              { key: 'name', header: 'Nhóm dịch', cell: (h) => <Link className="font-medium hover:text-accent" to={`${paths.admin.revenue}/${h.houseId}`}>{h.name}</Link> },
              { key: 'views', header: 'Lượt xem hợp lệ', cell: (h) => <span className="tabular-nums">{formatNumber(h.views)}</span> },
              {
                key: 'sus', header: 'Bị loại',
                cell: (h) => (
                  <span className={cn('tabular-nums', h.suspiciousRatio > 0.3 ? 'font-semibold text-danger' : 'text-muted')} title="Tỉ lệ cao có thể là dấu hiệu cày view">
                    {formatNumber(h.suspiciousViews)} ({(h.suspiciousRatio * 100).toFixed(1)}%)
                  </span>
                ),
              },
              { key: 'rev', header: 'Doanh thu', cell: (h) => <span className="font-semibold tabular-nums">{formatVnd(h.revenue)}</span> },
            ]}
          />
          <section className="card p-5">
            <h2 className="mb-3 font-semibold">Lý do loại lượt xem</h2>
            {r.rejections.length === 0 ? <p className="text-sm text-muted">Không có lượt nào bị loại.</p> : (
              <ul className="grid gap-2 text-sm sm:grid-cols-2">
                {r.rejections.map((x) => (
                  <li key={x.reason} className="flex justify-between gap-3 rounded-xl bg-surface-2 px-3 py-2">
                    <span>{rejectionLabel[x.reason] ?? x.reason}</span>
                    <span className="font-semibold tabular-nums">{formatNumber(x.count)}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      )}
    </>
  );
}

export function AdminHouseRevenuePage() {
  const { id = '' } = useParams();
  const [range, setRange] = useState(() => presetRange('month'));
  const house = useQuery({ queryKey: ['admin', 'house', id], queryFn: () => adminApi.house(id) });
  const report = useQuery({ queryKey: ['admin', 'revenue', id, range], queryFn: () => adminApi.houseRevenue(id, range), placeholderData: keepPreviousData });
  useDocumentTitle(house.data ? `Doanh thu – ${house.data.name}` : 'Doanh thu nhóm');
  const r = report.data;
  return (
    <>
      <Link to={paths.admin.revenue} className="mb-3 inline-flex items-center gap-1 text-sm text-muted hover:text-ink"><ArrowLeft className="h-4 w-4" /> Doanh thu</Link>
      <PageHeader title={house.data?.name ?? '…'} description="Chi tiết theo truyện và chương." actions={r && <ExportReportButton report={r} name={`doanh-thu_${house.data?.slug ?? id}`} />} />
      <div className="mb-6"><DateRangePicker value={range} onChange={setRange} /></div>
      {report.isError ? <ErrorState error={report.error} onRetry={() => report.refetch()} /> : !r ? <Skeleton className="h-64 rounded-2xl" /> : (
        <div className="space-y-6">
          <TotalsTiles totals={r.totals} rate={r.revenuePerThousandViews} revenueVisible />
          {r.daily.length > 0 && <DailyViews daily={r.daily} />}
          <ComicStatsTable report={r} />
        </div>
      )}
    </>
  );
}
