import { useEffect, useState, type ReactNode } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router';
import { ArrowLeft, Menu, X } from 'lucide-react';
import { paths } from '@/app/paths';
import { Button } from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { cn } from '@/utils/format';
import { Logo, ThemeToggle } from './PublicLayout';

export interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
  end?: boolean;
  visible?: boolean;
}

/** Shared shell for the SystemAdmin console and the translation-house workspace. */
export function WorkspaceLayout({ title, items, header }: { title: string; items: NavItem[]; header?: ReactNode }) {
  const [open, setOpen] = useState(false);
  const location = useLocation();
  const { user } = useAuth();
  useEffect(() => setOpen(false), [location.pathname]);

  const nav = (
    <nav className="flex flex-col gap-1" aria-label={title}>
      {items
        .filter((i) => i.visible !== false)
        .map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={({ isActive }) =>
              cn('flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition-colors', isActive ? 'bg-accent-soft text-accent' : 'text-muted hover:bg-surface-2 hover:text-ink')
            }
          >
            {item.icon}
            {item.label}
          </NavLink>
        ))}
    </nav>
  );

  return (
    <div className="flex min-h-dvh">
      {/* Desktop sidebar */}
      <aside className="sticky top-0 hidden h-dvh w-64 shrink-0 flex-col gap-6 border-r border-line bg-surface p-4 lg:flex">
        <Logo />
        <div>
          <p className="mb-2 px-3 text-xs font-semibold uppercase tracking-wide text-muted">{title}</p>
          {header}
        </div>
        {nav}
        <Link to={paths.home} className="mt-auto flex items-center gap-2 px-3 text-sm text-muted hover:text-ink">
          <ArrowLeft className="h-4 w-4" /> Về trang đọc truyện
        </Link>
      </aside>

      {/* Mobile drawer */}
      {open && (
        <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label={title}>
          <div className="absolute inset-0 bg-black/40" onClick={() => setOpen(false)} />
          <div className="absolute inset-y-0 left-0 flex w-72 flex-col gap-6 overflow-y-auto bg-surface p-4 shadow-xl">
            <div className="flex items-center justify-between">
              <Logo />
              <Button variant="ghost" size="icon" onClick={() => setOpen(false)} aria-label="Đóng menu">
                <X className="h-5 w-5" />
              </Button>
            </div>
            {header}
            {nav}
            <Link to={paths.home} className="flex items-center gap-2 px-3 text-sm text-muted">
              <ArrowLeft className="h-4 w-4" /> Về trang đọc truyện
            </Link>
          </div>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-14 items-center gap-2 border-b border-line bg-surface/90 px-4 backdrop-blur">
          <Button variant="ghost" size="icon" className="lg:hidden" onClick={() => setOpen(true)} aria-label="Mở menu" data-testid="workspace-menu">
            <Menu className="h-5 w-5" />
          </Button>
          <span className="font-semibold lg:hidden">{title}</span>
          <div className="ml-auto flex items-center gap-2">
            <ThemeToggle />
            <span className="hidden text-sm text-muted sm:inline">{user?.displayName}</span>
          </div>
        </header>
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6 sm:px-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
