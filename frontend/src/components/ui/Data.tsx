import { useRef, useState, type DragEvent, type ReactNode } from 'react';
import { ChevronLeft, ChevronRight, UploadCloud } from 'lucide-react';
import { Button } from './Button';
import { cn, formatBytes } from '@/utils/format';

export interface Column<T> {
  key: string;
  header: string;
  cell: (row: T) => ReactNode;
  className?: string;
  /** Hidden on small screens' card view (shown only in the table). */
  hideOnMobile?: boolean;
}

/** Table on wide screens, stacked cards on mobile. */
export function DataTable<T>({ rows, columns, rowKey, onRowClick, testId }: { rows: T[]; columns: Column<T>[]; rowKey: (r: T) => string; onRowClick?: (r: T) => void; testId?: string }) {
  return (
    <div className="card overflow-hidden" data-testid={testId}>
      <table className="hidden w-full text-sm md:table">
        <thead className="bg-surface-2 text-left text-xs uppercase tracking-wide text-muted">
          <tr>
            {columns.map((c) => (
              <th key={c.key} scope="col" className={cn('px-4 py-3 font-medium', c.className)}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {rows.map((row) => (
            <tr key={rowKey(row)} className={cn(onRowClick && 'cursor-pointer hover:bg-surface-2')} onClick={onRowClick ? () => onRowClick(row) : undefined}>
              {columns.map((c) => (
                <td key={c.key} className={cn('px-4 py-3 align-middle', c.className)}>
                  {c.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      <ul className="divide-y divide-line md:hidden">
        {rows.map((row) => (
          <li key={rowKey(row)} className={cn('space-y-1.5 px-4 py-3', onRowClick && 'cursor-pointer active:bg-surface-2')} onClick={onRowClick ? () => onRowClick(row) : undefined}>
            {columns
              .filter((c) => !c.hideOnMobile)
              .map((c, i) => (
                <div key={c.key} className={cn('flex items-center justify-between gap-3 text-sm', i === 0 && 'font-medium')}>
                  {i > 0 && <span className="text-xs text-muted">{c.header}</span>}
                  <div className={cn(i === 0 ? 'w-full' : 'text-right')}>{c.cell(row)}</div>
                </div>
              ))}
          </li>
        ))}
      </ul>
    </div>
  );
}

export function Pagination({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (page: number) => void }) {
  if (totalPages <= 1) return null;
  const pages = Array.from(new Set([1, page - 1, page, page + 1, totalPages].filter((p) => p >= 1 && p <= totalPages))).sort((a, b) => a - b);
  return (
    <nav className="mt-6 flex items-center justify-center gap-1" aria-label="Phân trang">
      <Button variant="ghost" size="icon" disabled={page <= 1} onClick={() => onChange(page - 1)} aria-label="Trang trước">
        <ChevronLeft className="h-5 w-5" />
      </Button>
      {pages.map((p, i) => (
        <span key={p} className="flex items-center">
          {i > 0 && p - (pages[i - 1] ?? p) > 1 && <span className="px-1 text-muted">…</span>}
          <Button variant={p === page ? 'primary' : 'ghost'} size="sm" aria-current={p === page ? 'page' : undefined} onClick={() => onChange(p)}>
            {p}
          </Button>
        </span>
      ))}
      <Button variant="ghost" size="icon" disabled={page >= totalPages} onClick={() => onChange(page + 1)} aria-label="Trang sau">
        <ChevronRight className="h-5 w-5" />
      </Button>
    </nav>
  );
}

export function Tabs<T extends string>({ value, onChange, items }: { value: T; onChange: (v: T) => void; items: { value: T; label: string }[] }) {
  return (
    <div role="tablist" className="inline-flex rounded-xl bg-surface-2 p-1">
      {items.map((item) => (
        <button
          key={item.value}
          role="tab"
          aria-selected={value === item.value}
          className={cn('rounded-lg px-3 py-1.5 text-sm font-medium transition-colors', value === item.value ? 'bg-surface text-ink shadow-sm' : 'text-muted hover:text-ink')}
          onClick={() => onChange(item.value)}
        >
          {item.label}
        </button>
      ))}
    </div>
  );
}

interface DropzoneProps {
  accept: string;
  maxBytes: number;
  disabled?: boolean;
  onFile: (file: File) => void;
  title: string;
  hint: string;
}

export function Dropzone({ accept, maxBytes, disabled, onFile, title, hint }: DropzoneProps) {
  const input = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const take = (file: File | undefined) => {
    if (!file) return;
    if (file.size > maxBytes) {
      setError(`Tệp quá lớn (${formatBytes(file.size)}). Tối đa ${formatBytes(maxBytes)}.`);
      return;
    }
    setError(null);
    onFile(file);
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    if (!disabled) take(e.dataTransfer.files[0]);
  };

  return (
    <div>
      <button
        type="button"
        disabled={disabled}
        onClick={() => input.current?.click()}
        onDragOver={(e) => {
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
        className={cn(
          'flex w-full flex-col items-center gap-2 rounded-2xl border-2 border-dashed px-6 py-10 text-center transition-colors',
          dragging ? 'border-accent bg-accent-soft' : 'border-line hover:border-accent/60 hover:bg-surface-2',
          disabled && 'cursor-not-allowed opacity-60',
        )}
      >
        <UploadCloud className="h-8 w-8 text-accent" aria-hidden />
        <span className="font-medium">{title}</span>
        <span className="text-sm text-muted">{hint}</span>
      </button>
      <input ref={input} type="file" accept={accept} className="sr-only" data-testid="file-input" onChange={(e) => {
        take(e.target.files?.[0]);
        e.target.value = '';
      }} />
      {error && <p className="mt-2 text-sm text-danger" role="alert">{error}</p>}
    </div>
  );
}
