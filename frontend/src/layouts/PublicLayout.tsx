import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { BookOpen, Compass, Home, Library, LogIn, LogOut, Menu, Moon, Search, Shield, Sun, Users, Wrench, X } from 'lucide-react';
import { paths } from '@/app/paths';
import { env } from '@/config/env';
import { Button, ButtonLink } from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { useTheme } from '@/hooks/useTheme';
import { publicApi } from '@/services/api';
import { cn } from '@/utils/format';

export function Logo() {
  return (
    <Link to={paths.home} className="flex shrink-0 items-center gap-2 font-semibold tracking-tight" aria-label={`${env.appName} – trang chủ`}>
      <span className="grid h-8 w-8 place-items-center rounded-lg bg-accent text-accent-ink">
        <BookOpen className="h-4.5 w-4.5" aria-hidden />
      </span>
      <span className="hidden text-lg sm:inline">{env.appName}</span>
    </Link>
  );
}

function SearchBox({ className, autoFocus }: { className?: string; autoFocus?: boolean }) {
  const navigate = useNavigate();
  const location = useLocation();
  const [q, setQ] = useState(() => new URLSearchParams(location.search).get('q') ?? '');
  const submit = (e: FormEvent) => {
    e.preventDefault();
    navigate(`${paths.search}?q=${encodeURIComponent(q.trim())}`);
  };
  return (
    <form role="search" onSubmit={submit} className={cn('relative', className)}>
      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden />
      <input
        type="search"
        value={q}
        autoFocus={autoFocus}
        onChange={(e) => setQ(e.target.value)}
        placeholder="Tìm truyện, tác giả…"
        aria-label="Tìm kiếm truyện"
        className="h-10 w-full rounded-xl border border-line bg-surface-2 pl-9 pr-3 text-sm placeholder:text-muted focus:border-accent focus:bg-surface focus:outline-none"
      />
    </form>
  );
}

function GenreMenu() {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const tags = useQuery({ queryKey: ['tags'], queryFn: publicApi.tags, staleTime: 5 * 60_000, enabled: open });
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  return (
    <div className="relative" ref={ref}>
      <Button variant="ghost" size="sm" aria-expanded={open} onClick={() => setOpen((o) => !o)} icon={<Compass className="h-4 w-4" />}>
        Thể loại
      </Button>
      {open && (
        <div className="card absolute left-0 top-11 z-40 w-[min(32rem,90vw)] p-3">
          {tags.isLoading && <p className="p-2 text-sm text-muted">Đang tải…</p>}
          {tags.data?.length === 0 && <p className="p-2 text-sm text-muted">Chưa có thể loại nào.</p>}
          <div className="grid grid-cols-2 gap-1 sm:grid-cols-3">
            {tags.data?.map((t) => (
              <Link key={t.id} to={paths.genre(t.slug)} onClick={() => setOpen(false)} className="flex justify-between rounded-lg px-3 py-2 text-sm hover:bg-surface-2">
                <span className="truncate">{t.name}</span>
                <span className="text-xs text-muted">{t.comicCount}</span>
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function AccountMenu() {
  const { user, logout, houses } = useAuth();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  if (!user) {
    return (
      <ButtonLink to={paths.login} size="sm" icon={<LogIn className="h-4 w-4" />} data-testid="login-link">
        Đăng nhập
      </ButtonLink>
    );
  }

  const item = 'flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left text-sm hover:bg-surface-2';
  return (
    <div className="relative" ref={ref}>
      <button className="flex items-center gap-2 rounded-xl px-2 py-1.5 hover:bg-surface-2" aria-expanded={open} onClick={() => setOpen((o) => !o)} data-testid="account-menu">
        <span className="grid h-8 w-8 place-items-center rounded-full bg-accent-soft text-sm font-semibold text-accent">{user.displayName.slice(0, 1).toUpperCase()}</span>
        <span className="hidden max-w-32 truncate text-sm font-medium md:inline">{user.displayName}</span>
      </button>
      {open && (
        <div className="card absolute right-0 top-12 z-40 w-60 p-2" onClick={() => setOpen(false)}>
          <p className="truncate px-3 py-2 text-xs text-muted">@{user.username}</p>
          <Link className={item} to={paths.library}><Library className="h-4 w-4" /> Tủ truyện</Link>
          {houses.length > 0 && <Link className={item} to={paths.studio.root} data-testid="studio-link"><Wrench className="h-4 w-4" /> Quản lý nhóm dịch</Link>}
          {user.systemRole === 'systemAdmin' && <Link className={item} to={paths.admin.root} data-testid="admin-link"><Shield className="h-4 w-4" /> Quản trị hệ thống</Link>}
          <Link className={item} to={paths.changePassword}>Đổi mật khẩu</Link>
          <button className={cn(item, 'text-danger')} data-testid="logout" onClick={async () => { await logout(); navigate(paths.home); }}>
            <LogOut className="h-4 w-4" /> Đăng xuất
          </button>
        </div>
      )}
    </div>
  );
}

export function ThemeToggle() {
  const { theme, toggle } = useTheme();
  return (
    <Button variant="ghost" size="icon" onClick={toggle} aria-label={theme === 'dark' ? 'Chuyển sang nền sáng' : 'Chuyển sang nền tối'}>
      {theme === 'dark' ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
    </Button>
  );
}

export function PublicLayout() {
  const [mobileSearch, setMobileSearch] = useState(false);
  const location = useLocation();
  const site = useQuery({ queryKey: ['site'], queryFn: publicApi.site, staleTime: 60_000 });
  useEffect(() => setMobileSearch(false), [location.pathname]);

  const tab = 'flex flex-1 flex-col items-center gap-0.5 py-2 text-[11px] text-muted aria-[current=page]:text-accent';
  return (
    <div className="flex min-h-dvh flex-col">
      {site.data?.announcement && (
        <div className="bg-accent px-4 py-2 text-center text-sm text-accent-ink" role="status">{site.data.announcement}</div>
      )}
      <header className="sticky top-0 z-30 border-b border-line bg-surface/90 backdrop-blur">
        <div className="container-page flex h-16 items-center gap-3">
          <Logo />
          <nav className="hidden items-center gap-1 md:flex" aria-label="Điều hướng chính">
            <ButtonLink to={paths.browse} variant="ghost" size="sm">Truyện</ButtonLink>
            <GenreMenu />
            <ButtonLink to={paths.houses} variant="ghost" size="sm">Nhóm dịch</ButtonLink>
          </nav>
          <SearchBox className="ml-auto hidden w-full max-w-sm sm:block" />
          <div className="ml-auto flex items-center gap-1 sm:ml-2">
            <Button variant="ghost" size="icon" className="sm:hidden" aria-label="Tìm kiếm" onClick={() => setMobileSearch((v) => !v)}>
              {mobileSearch ? <X className="h-5 w-5" /> : <Search className="h-5 w-5" />}
            </Button>
            <ThemeToggle />
            <AccountMenu />
          </div>
        </div>
        {mobileSearch && (
          <div className="container-page pb-3 sm:hidden">
            <SearchBox autoFocus />
          </div>
        )}
      </header>

      <main className="flex-1 pb-20 md:pb-0">
        <Outlet />
      </main>

      <footer className="mt-12 hidden border-t border-line py-8 text-sm text-muted md:block">
        <div className="container-page flex flex-wrap items-center justify-between gap-4">
          <p>© {new Date().getFullYear()} {env.appName}. Bản dịch thuộc về các nhóm dịch.</p>
          <nav className="flex gap-4" aria-label="Liên kết cuối trang">
            <Link className="hover:text-ink" to={paths.browse}>Danh sách truyện</Link>
            <Link className="hover:text-ink" to={paths.houses}>Nhóm dịch</Link>
          </nav>
        </div>
      </footer>

      {/* Mobile bottom navigation */}
      <nav className="fixed inset-x-0 bottom-0 z-30 flex border-t border-line bg-surface/95 backdrop-blur md:hidden" aria-label="Điều hướng nhanh" data-testid="mobile-nav">
        <NavLink to={paths.home} end className={tab}><Home className="h-5 w-5" />Trang chủ</NavLink>
        <NavLink to={paths.browse} className={tab}><BookOpen className="h-5 w-5" />Truyện</NavLink>
        <NavLink to={paths.houses} className={tab}><Users className="h-5 w-5" />Nhóm dịch</NavLink>
        <NavLink to={paths.library} className={tab}><Library className="h-5 w-5" />Tủ truyện</NavLink>
        <button className={cn(tab, 'md:hidden')} onClick={() => document.querySelector<HTMLButtonElement>('[data-testid="account-menu"],[data-testid="login-link"]')?.click()}>
          <Menu className="h-5 w-5" />Tài khoản
        </button>
      </nav>
    </div>
  );
}
