import { Fragment, useState } from 'react';
import { ChevronDown, ChevronRight, Download, Eye, ShieldAlert, Wallet } from 'lucide-react';
import { Button, Input, StatTile, Tabs } from '@/components/ui';
import { DailyBars } from '@/features/admin/DailyBars';
import type { ComicStats, DailyStats, HouseViewReport, ViewTotals } from '@/types/api';
import { chapterLabel, cn, formatNumber } from '@/utils/format';

const vnd = new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 });
export const formatVnd = (n: number | null | undefined) => (n === null || n === undefined ? '—' : vnd.format(n));

// ---------------- Date range (reporting days are Vietnam time, as on the server) ----------------
export interface DateRange {
  from: string;
  to: string;
}

const iso = (d: Date) => d.toISOString().slice(0, 10);

/** "Today" in Vietnam (UTC+7), matching the server's reporting day. */
function vnToday(): Date {
  return new Date(Date.now() + 7 * 3600_000);
}

export function presetRange(preset: 'month' | 'last-month' | '30d'): DateRange {
  const today = vnToday();
  const y = today.getUTCFullYear();
  const m = today.getUTCMonth();
  if (preset === 'month') return { from: iso(new Date(Date.UTC(y, m, 1))), to: iso(today) };
  if (preset === 'last-month') return { from: iso(new Date(Date.UTC(y, m - 1, 1))), to: iso(new Date(Date.UTC(y, m, 0))) };
  return { from: iso(new Date(today.getTime() - 29 * 86_400_000)), to: iso(today) };
}

export function DateRangePicker({ value, onChange }: { value: DateRange; onChange: (r: DateRange) => void }) {
  const [preset, setPreset] = useState<'month' | 'last-month' | '30d' | 'custom'>('month');
  return (
    <div className="flex flex-wrap items-end gap-3">
      <Tabs
        value={preset}
        onChange={(p) => {
          setPreset(p);
          if (p !== 'custom') onChange(presetRange(p));
        }}
        items={[
          { value: 'month', label: 'Tháng này' },
          { value: 'last-month', label: 'Tháng trước' },
          { value: '30d', label: '30 ngày' },
          { value: 'custom', label: 'Tuỳ chọn' },
        ]}
      />
      {preset === 'custom' && (
        <div className="flex items-center gap-2 text-sm">
          <Input type="date" aria-label="Từ ngày" value={value.from} max={value.to} onChange={(e) => e.target.value && onChange({ ...value, from: e.target.value })} className="w-40" />
          <span className="text-muted">→</span>
          <Input type="date" aria-label="Đến ngày" value={value.to} min={value.from} onChange={(e) => e.target.value && onChange({ ...value, to: e.target.value })} className="w-40" />
        </div>
      )}
    </div>
  );
}

// ---------------- Totals + daily chart ----------------
export function TotalsTiles({ totals, rate, revenueVisible }: { totals: ViewTotals; rate: number | null; revenueVisible: boolean }) {
  const all = totals.views + totals.suspiciousViews;
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
      <StatTile label="Lượt xem hợp lệ" value={formatNumber(totals.views)} icon={<Eye className="h-5 w-5" />} hint="Đã qua xác minh, được tính tiền" />
      <StatTile
        label="Lượt xem bị loại"
        value={formatNumber(totals.suspiciousViews)}
        icon={<ShieldAlert className="h-5 w-5" />}
        hint={all > 0 ? `${((totals.suspiciousViews / all) * 100).toFixed(1)}% số lượt gửi về — nghi gian lận` : 'Nghi gian lận (bot, quá nhanh, giả vé…)'}
      />
      {revenueVisible ? (
        <StatTile label="Doanh thu" value={formatVnd(totals.revenue)} icon={<Wallet className="h-5 w-5" />} hint={rate !== null ? `Đơn giá hiện tại ${formatVnd(rate)} / 1.000 lượt` : undefined} />
      ) : (
        <StatTile label="Doanh thu" value="—" icon={<Wallet className="h-5 w-5" />} hint="Chỉ chủ nhóm xem được doanh thu" />
      )}
    </div>
  );
}

export function DailyViews({ daily }: { daily: DailyStats[] }) {
  return <DailyBars title="Lượt xem hợp lệ theo ngày" unit="lượt" data={daily.map((d) => ({ day: d.day, count: d.views }))} />;
}

// ---------------- Per-series table with per-chapter rows ----------------
export function ComicStatsTable({ report }: { report: HouseViewReport }) {
  const [open, setOpen] = useState<Set<string>>(new Set());
  const toggle = (id: string) => setOpen((s) => {
    const next = new Set(s);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    return next;
  });
  const money = report.revenueVisible;

  if (report.comics.length === 0) return <p className="card p-6 text-center text-sm text-muted">Chưa có lượt xem hợp lệ nào trong khoảng thời gian này.</p>;

  return (
    <div className="card overflow-x-auto">
      <table className="w-full min-w-[36rem] text-sm" data-testid="view-report">
        <thead className="bg-surface-2 text-left text-xs uppercase tracking-wide text-muted">
          <tr>
            <th scope="col" className="px-4 py-3 font-medium">Truyện / chương</th>
            <th scope="col" className="px-4 py-3 text-right font-medium">Lượt xem</th>
            <th scope="col" className="px-4 py-3 text-right font-medium">Bị loại</th>
            {money && <th scope="col" className="px-4 py-3 text-right font-medium">Doanh thu</th>}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {report.comics.map((c) => (
            <Fragment key={c.comicId}>
              <tr className="cursor-pointer bg-surface hover:bg-surface-2" onClick={() => toggle(c.comicId)}>
                <td className="px-4 py-3 font-medium">
                  <span className="inline-flex items-center gap-1">
                    {open.has(c.comicId) ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                    {c.title}
                    {c.deleted && <span className="text-xs text-muted">(đã xoá)</span>}
                  </span>
                  <span className="block pl-5 text-xs font-normal text-muted">{c.chapters.length} chương · tổng của các chương</span>
                </td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">{formatNumber(c.views)}</td>
                <td className="px-4 py-3 text-right tabular-nums text-muted">{formatNumber(c.suspiciousViews)}</td>
                {money && <td className="px-4 py-3 text-right font-semibold tabular-nums">{formatVnd(c.revenue)}</td>}
              </tr>
              {open.has(c.comicId) && c.chapters.map((ch) => (
                <tr key={ch.chapterId} className="bg-surface-2/40">
                  <td className="py-2 pl-10 pr-4">{ch.deleted || ch.number === null ? <span className="text-muted">Chương đã xoá</span> : chapterLabel(ch.number, ch.title)}</td>
                  <td className="px-4 py-2 text-right tabular-nums">{formatNumber(ch.views)}</td>
                  <td className={cn('px-4 py-2 text-right tabular-nums', ch.suspiciousViews > ch.views ? 'text-danger' : 'text-muted')}>{formatNumber(ch.suspiciousViews)}</td>
                  {money && <td className="px-4 py-2 text-right tabular-nums">{formatVnd(ch.revenue)}</td>}
                </tr>
              ))}
            </Fragment>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ---------------- CSV export (Excel-friendly: BOM + semicolons are not needed with quoted fields) ----------------
const csvCell = (v: string | number | null | undefined) => `"${String(v ?? '').replace(/"/g, '""')}"`;

export function downloadCsv(fileName: string, header: string[], rows: (string | number | null | undefined)[][]) {
  const text = '﻿' + [header, ...rows].map((r) => r.map(csvCell).join(',')).join('\r\n');
  const url = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' }));
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function chapterCsvRows(comics: ComicStats[], money: boolean) {
  return comics.flatMap((c) =>
    c.chapters.map((ch) => [c.title, ch.number ?? 'đã xoá', ch.title ?? '', ch.views, ch.suspiciousViews, ...(money ? [ch.revenue ?? 0] : [])]),
  );
}

export function ExportReportButton({ report, name }: { report: HouseViewReport; name: string }) {
  const money = report.revenueVisible;
  return (
    <Button
      variant="outline"
      size="sm"
      icon={<Download className="h-4 w-4" />}
      disabled={report.comics.length === 0}
      onClick={() =>
        downloadCsv(`${name}_${report.from}_${report.to}.csv`, ['Truyện', 'Chương', 'Tên chương', 'Lượt xem hợp lệ', 'Bị loại', ...(money ? ['Doanh thu (VND)'] : [])], chapterCsvRows(report.comics, money))
      }
    >
      Xuất CSV
    </Button>
  );
}
