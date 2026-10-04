import type { ReactNode } from 'react';
import { AlertTriangle, Inbox, Loader2, RotateCcw } from 'lucide-react';
import { ApiError } from '@/services/http';
import { cn } from '@/utils/format';
import { Button } from './Button';

type Tone = 'neutral' | 'accent' | 'success' | 'warn' | 'danger';

const tones: Record<Tone, string> = {
  neutral: 'bg-surface-2 text-muted',
  accent: 'bg-accent-soft text-accent',
  success: 'bg-success-soft text-success',
  warn: 'bg-warn-soft text-warn',
  danger: 'bg-danger-soft text-danger',
};

export function Badge({ tone = 'neutral', children, className, ...rest }: { tone?: Tone; children: ReactNode; className?: string } & React.HTMLAttributes<HTMLSpanElement>) {
  return <span className={cn('inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-medium', tones[tone], className)} {...rest}>{children}</span>;
}

export function Spinner({ label = 'Đang tải…', className }: { label?: string; className?: string }) {
  return (
    <span role="status" className={cn('inline-flex items-center gap-2 text-sm text-muted', className)}>
      <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
      {label}
    </span>
  );
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={cn('skeleton', className)} aria-hidden />;
}

export function EmptyState({ title, description, action, icon }: { title: string; description?: string; action?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 rounded-2xl border border-dashed border-line px-6 py-14 text-center">
      <div className="rounded-2xl bg-surface-2 p-3 text-muted">{icon ?? <Inbox className="h-6 w-6" aria-hidden />}</div>
      <div>
        <p className="font-semibold">{title}</p>
        {description && <p className="mx-auto mt-1 max-w-md text-sm text-muted">{description}</p>}
      </div>
      {action}
    </div>
  );
}

export function ErrorState({ error, onRetry, title = 'Không tải được dữ liệu' }: { error: unknown; onRetry?: () => void; title?: string }) {
  const message = error instanceof ApiError ? error.message : 'Đã có lỗi xảy ra. Vui lòng thử lại.';
  const traceId = error instanceof ApiError ? error.traceId : null;
  return (
    <div role="alert" className="flex flex-col items-center gap-3 rounded-2xl border border-danger/30 bg-danger-soft/40 px-6 py-10 text-center">
      <AlertTriangle className="h-7 w-7 text-danger" aria-hidden />
      <div>
        <p className="font-semibold">{title}</p>
        <p className="mt-1 text-sm text-muted">{message}</p>
        {traceId && <p className="mt-2 font-mono text-[11px] text-muted">Mã tra cứu: {traceId}</p>}
      </div>
      {onRetry && (
        <Button variant="outline" size="sm" icon={<RotateCcw className="h-4 w-4" />} onClick={onRetry}>
          Thử lại
        </Button>
      )}
    </div>
  );
}

export function ProgressBar({ value, label, tone = 'accent' }: { value: number; label?: string; tone?: 'accent' | 'success' | 'danger' }) {
  const pct = Math.max(0, Math.min(100, Math.round(value * 100)));
  const color = tone === 'success' ? 'bg-success' : tone === 'danger' ? 'bg-danger' : 'bg-accent';
  return (
    <div className="w-full">
      {label && (
        <div className="mb-1 flex justify-between text-xs text-muted">
          <span>{label}</span>
          <span>{pct}%</span>
        </div>
      )}
      <div className="h-2 overflow-hidden rounded-full bg-surface-3" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100} aria-label={label}>
        <div className={cn('h-full rounded-full transition-[width] duration-300', color)} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

export function StatTile({ label, value, hint, icon }: { label: string; value: ReactNode; hint?: ReactNode; icon?: ReactNode }) {
  return (
    <div className="card flex items-start gap-3 p-4">
      {icon && <div className="rounded-xl bg-accent-soft p-2 text-accent">{icon}</div>}
      <div className="min-w-0">
        <p className="text-xs font-medium uppercase tracking-wide text-muted">{label}</p>
        <p className="mt-1 text-2xl font-semibold tabular-nums">{value}</p>
        {hint && <p className="mt-0.5 text-xs text-muted">{hint}</p>}
      </div>
    </div>
  );
}

export function PageHeader({ title, description, actions }: { title: string; description?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
      <div className="min-w-0">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        {description && <div className="mt-1 text-sm text-muted">{description}</div>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  );
}
