import { useMemo, useState } from 'react';
import type { DailyPoint } from '@/types/api';
import { formatNumber } from '@/utils/format';

const W = 640;
const H = 180;
const PAD = { top: 18, right: 8, bottom: 22, left: 32 };
const GAP = 2; // surface gap between adjacent bars

const dayLabel = new Intl.DateTimeFormat('vi-VN', { day: '2-digit', month: '2-digit' });
const fullLabel = new Intl.DateTimeFormat('vi-VN', { weekday: 'short', day: '2-digit', month: '2-digit', year: 'numeric' });

/** Rounded top corners only: the data end is rounded, the baseline end stays square. */
function barPath(x: number, y: number, w: number, h: number, r: number) {
  const rr = Math.min(r, w / 2, h);
  return `M${x},${y + h}V${y + rr}Q${x},${y} ${x + rr},${y}H${x + w - rr}Q${x + w},${y} ${x + w},${y + rr}V${y + h}Z`;
}

/**
 * Single-series daily bar chart (30 days). One series → no legend; the card title names it.
 * Hover/focus shows a tooltip per bar; the peak value is the only direct label; a table view exists.
 */
export function DailyBars({ title, data, unit }: { title: string; data: DailyPoint[]; unit: string }) {
  const [hover, setHover] = useState<number | null>(null);
  const [table, setTable] = useState(false);

  const { max, ticks, peakIndex } = useMemo(() => {
    const raw = Math.max(1, ...data.map((d) => d.count));
    const step = Math.max(1, Math.ceil(raw / 4));
    const top = step * 4;
    let peak = 0;
    data.forEach((d, i) => (d.count > (data[peak]?.count ?? 0) ? (peak = i) : undefined));
    return { max: top, ticks: [0, step, step * 2, step * 3, top], peakIndex: data.some((d) => d.count > 0) ? peak : -1 };
  }, [data]);

  const plotW = W - PAD.left - PAD.right;
  const plotH = H - PAD.top - PAD.bottom;
  const slot = plotW / Math.max(1, data.length);
  const barW = Math.max(2, slot - GAP);
  const y = (v: number) => PAD.top + plotH - (v / max) * plotH;
  const total = data.reduce((s, d) => s + d.count, 0);
  const hovered = hover !== null ? data[hover] : undefined;

  return (
    <figure className="card p-4">
      <figcaption className="mb-2 flex items-start justify-between gap-3">
        <div>
          <p className="font-semibold">{title}</p>
          <p className="text-xs text-muted">30 ngày gần nhất · tổng {formatNumber(total)} {unit}</p>
        </div>
        <button className="text-xs text-accent hover:underline" onClick={() => setTable((t) => !t)}>
          {table ? 'Xem biểu đồ' : 'Xem dạng bảng'}
        </button>
      </figcaption>

      {table ? (
        <div className="max-h-56 overflow-y-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-xs text-muted"><tr><th className="py-1">Ngày</th><th className="py-1 text-right">Số {unit}</th></tr></thead>
            <tbody>
              {data.map((d) => (
                <tr key={d.day} className="border-t border-line"><td className="py-1">{fullLabel.format(new Date(d.day))}</td><td className="py-1 text-right tabular-nums">{d.count}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="relative">
          <svg viewBox={`0 0 ${W} ${H}`} className="h-auto w-full" role="img" aria-label={`${title}: tổng ${total} ${unit} trong 30 ngày`} onMouseLeave={() => setHover(null)}>
            {/* recessive grid + y labels (muted ink, never series colour) */}
            {ticks.map((t) => (
              <g key={t}>
                <line x1={PAD.left} x2={W - PAD.right} y1={y(t)} y2={y(t)} stroke="var(--line)" strokeWidth={1} strokeDasharray={t === 0 ? undefined : '2 4'} />
                <text x={PAD.left - 6} y={y(t)} textAnchor="end" dominantBaseline="middle" fontSize={10} fill="var(--muted)">{t}</text>
              </g>
            ))}
            {data.map((d, i) => {
              const x = PAD.left + i * slot + GAP / 2;
              const h = (d.count / max) * plotH;
              return (
                <g key={d.day}>
                  {d.count > 0 && (
                    <path d={barPath(x, y(d.count), barW, h, 4)} fill="var(--chart-1)" opacity={hover === null || hover === i ? 1 : 0.45} />
                  )}
                  {/* hit target larger than the mark: the full column */}
                  <rect x={PAD.left + i * slot} y={PAD.top} width={slot} height={plotH} fill="transparent" tabIndex={0}
                    aria-label={`${fullLabel.format(new Date(d.day))}: ${d.count} ${unit}`}
                    onMouseEnter={() => setHover(i)} onFocus={() => setHover(i)} onBlur={() => setHover(null)} />
                </g>
              );
            })}
            {peakIndex >= 0 && data[peakIndex] && (
              <text x={PAD.left + peakIndex * slot + slot / 2} y={y(data[peakIndex].count) - 5} textAnchor="middle" fontSize={10} fontWeight={600} fill="var(--ink)">
                {data[peakIndex].count}
              </text>
            )}
            {data.map((d, i) => (data.length - 1 - i) % 7 === 0 && (
              <text key={d.day} x={PAD.left + i * slot + slot / 2} y={H - 6} textAnchor="middle" fontSize={10} fill="var(--muted)">
                {dayLabel.format(new Date(d.day))}
              </text>
            ))}
          </svg>
          {hovered && hover !== null && (
            <div className="pointer-events-none absolute top-0 -translate-x-1/2 rounded-lg border border-line bg-surface px-2.5 py-1.5 text-xs shadow-lg"
              style={{ left: `${((PAD.left + hover * slot + slot / 2) / W) * 100}%` }}>
              <p className="text-muted">{fullLabel.format(new Date(hovered.day))}</p>
              <p className="font-semibold tabular-nums">{hovered.count} {unit}</p>
            </div>
          )}
        </div>
      )}
    </figure>
  );
}
