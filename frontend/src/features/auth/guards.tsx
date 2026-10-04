import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router';
import { ShieldAlert } from 'lucide-react';
import { paths } from '@/app/paths';
import { ButtonLink, Spinner } from '@/components/ui';
import { useAuth } from './AuthProvider';

export function FullPageSpinner() {
  return (
    <div className="flex min-h-[60dvh] items-center justify-center">
      <Spinner />
    </div>
  );
}

export function Forbidden({ title = 'Bạn không có quyền truy cập trang này', description }: { title?: string; description?: string }) {
  return (
    <div className="container-page flex min-h-[60dvh] flex-col items-center justify-center gap-4 text-center" data-testid="forbidden">
      <ShieldAlert className="h-10 w-10 text-danger" aria-hidden />
      <h1 className="text-xl font-semibold">{title}</h1>
      {description && <p className="max-w-md text-sm text-muted">{description}</p>}
      <ButtonLink to={paths.home} variant="outline">
        Về trang chủ
      </ButtonLink>
    </div>
  );
}

/** Signed-in users only; enforces the forced password change for admin-created accounts. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status, user } = useAuth();
  const location = useLocation();
  if (status === 'loading') return <FullPageSpinner />;
  if (status === 'anonymous' || !user) {
    return <Navigate to={`${paths.login}?next=${encodeURIComponent(location.pathname + location.search)}`} replace />;
  }
  if (user.mustChangePassword && location.pathname !== paths.changePassword) {
    return <Navigate to={paths.changePassword} replace />;
  }
  return <>{children}</>;
}

export function RequireAdmin({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  if (user?.systemRole !== 'systemAdmin') {
    return <Forbidden description="Khu vực này chỉ dành cho quản trị hệ thống." />;
  }
  return <>{children}</>;
}

export function RequireHouse({ children }: { children: ReactNode }) {
  const { currentHouse } = useAuth();
  if (!currentHouse) {
    return <Forbidden title="Bạn chưa thuộc nhóm dịch nào" description="Hãy liên hệ chủ nhóm dịch hoặc quản trị hệ thống để được thêm vào nhóm." />;
  }
  if (currentHouse.houseStatus !== 'active') {
    return <Forbidden title="Nhóm dịch đang bị tạm ngưng" description="Mọi thao tác quản lý tạm thời bị khoá. Vui lòng liên hệ quản trị hệ thống." />;
  }
  return <>{children}</>;
}
