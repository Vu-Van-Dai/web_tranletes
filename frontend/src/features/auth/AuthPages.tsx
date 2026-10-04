import { useState, type ReactNode } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Link, Navigate, useNavigate, useSearchParams } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { KeyRound, LogIn, UserPlus } from 'lucide-react';
import { paths } from '@/app/paths';
import { Button, Field, Input } from '@/components/ui';
import { authApi, publicApi } from '@/services/api';
import { ApiError } from '@/services/http';
import { useDocumentTitle } from '@/hooks/useTheme';
import { applyServerErrors } from '@/utils/forms';
import { useAuth } from './AuthProvider';

function AuthCard({ title, subtitle, icon, children }: { title: string; subtitle: string; icon: ReactNode; children: ReactNode }) {
  return (
    <div className="container-page flex min-h-[calc(100dvh-8rem)] items-center justify-center py-10">
      <div className="card w-full max-w-md p-6 sm:p-8">
        <div className="mb-6 flex items-center gap-3">
          <div className="rounded-xl bg-accent-soft p-2.5 text-accent">{icon}</div>
          <div>
            <h1 className="text-xl font-semibold">{title}</h1>
            <p className="text-sm text-muted">{subtitle}</p>
          </div>
        </div>
        {children}
      </div>
    </div>
  );
}

function FormError({ error }: { error: string | null }) {
  if (!error) return null;
  return (
    <p role="alert" className="rounded-xl bg-danger-soft px-3 py-2 text-sm text-danger" data-testid="form-error">
      {error}
    </p>
  );
}

/** Only allow same-site relative redirects after login. */
function safeNext(next: string | null) {
  return next && next.startsWith('/') && !next.startsWith('//') ? next : paths.home;
}

const loginSchema = z.object({
  login: z.string().trim().min(1, 'Vui lòng nhập email hoặc tên đăng nhập.'),
  password: z.string().min(1, 'Vui lòng nhập mật khẩu.'),
});

export function LoginPage() {
  useDocumentTitle('Đăng nhập');
  const { status, user, applySession } = useAuth();
  const [params] = useSearchParams();
  const [error, setError] = useState<string | null>(null);
  const form = useForm<z.infer<typeof loginSchema>>({ resolver: zodResolver(loginSchema), defaultValues: { login: '', password: '' } });

  // Single redirect point (also covers "already signed in"): temporary passwords must be changed first.
  if (status === 'authenticated') {
    return <Navigate to={user?.mustChangePassword ? paths.changePassword : safeNext(params.get('next'))} replace />;
  }

  const onSubmit = form.handleSubmit(async (values) => {
    setError(null);
    try {
      applySession(await authApi.login(values.login, values.password));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Đăng nhập không thành công.');
    }
  });

  return (
    <AuthCard title="Đăng nhập" subtitle="Chào mừng bạn quay lại" icon={<LogIn className="h-5 w-5" />}>
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <FormError error={error} />
        <Field label="Email hoặc tên đăng nhập" error={form.formState.errors.login?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} autoComplete="username" autoFocus aria-invalid={!!form.formState.errors.login} {...form.register('login')} />}
        </Field>
        <Field label="Mật khẩu" error={form.formState.errors.password?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="current-password" aria-invalid={!!form.formState.errors.password} {...form.register('password')} />}
        </Field>
        <Button type="submit" className="w-full" loading={form.formState.isSubmitting}>
          Đăng nhập
        </Button>
      </form>
      <p className="mt-6 text-center text-sm text-muted">
        Chưa có tài khoản?{' '}
        <Link className="link" to={paths.register}>
          Đăng ký
        </Link>
      </p>
    </AuthCard>
  );
}

const registerSchema = z
  .object({
    username: z.string().trim().toLowerCase().regex(/^[a-z0-9_.]{3,32}$/, 'Tên đăng nhập gồm 3–32 ký tự: chữ thường không dấu, số, dấu chấm hoặc gạch dưới.'),
    displayName: z.string().trim().min(2, 'Tên hiển thị cần ít nhất 2 ký tự.').max(64, 'Tên hiển thị tối đa 64 ký tự.'),
    email: z.string().trim().email('Email không hợp lệ.'),
    password: z.string().min(10, 'Mật khẩu cần ít nhất 10 ký tự.').max(128, 'Mật khẩu tối đa 128 ký tự.'),
    confirm: z.string(),
  })
  .refine((v) => v.password === v.confirm, { path: ['confirm'], message: 'Mật khẩu nhập lại không khớp.' });

export function RegisterPage() {
  useDocumentTitle('Đăng ký');
  const { status, applySession } = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const site = useQuery({ queryKey: ['site'], queryFn: publicApi.site, staleTime: 60_000 });
  const form = useForm<z.infer<typeof registerSchema>>({
    resolver: zodResolver(registerSchema),
    defaultValues: { username: '', displayName: '', email: '', password: '', confirm: '' },
  });

  if (status === 'authenticated') return <Navigate to={paths.home} replace />;

  const onSubmit = form.handleSubmit(async ({ confirm: _confirm, ...values }) => {
    setError(null);
    try {
      applySession(await authApi.register(values));
      navigate(paths.home, { replace: true });
    } catch (e) {
      if (!applyServerErrors(e, form.setError, ['username', 'email', 'displayName', 'password'])) {
        setError(e instanceof ApiError ? e.message : 'Đăng ký không thành công.');
      }
    }
  });

  const errors = form.formState.errors;
  return (
    <AuthCard title="Tạo tài khoản" subtitle="Theo dõi truyện và lưu lịch sử đọc" icon={<UserPlus className="h-5 w-5" />}>
      {site.data && !site.data.registrationEnabled ? (
        <p className="rounded-xl bg-warn-soft px-3 py-2 text-sm text-warn">Hệ thống đang tạm khoá đăng ký tài khoản mới.</p>
      ) : (
        <form onSubmit={onSubmit} className="space-y-4" noValidate>
          <FormError error={error} />
          <Field label="Tên đăng nhập" error={errors.username?.message} hint="Chữ thường không dấu, số, dấu chấm hoặc gạch dưới.">
            {(id, d) => <Input id={id} aria-describedby={d} autoComplete="username" aria-invalid={!!errors.username} {...form.register('username')} />}
          </Field>
          <Field label="Tên hiển thị" error={errors.displayName?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} autoComplete="nickname" aria-invalid={!!errors.displayName} {...form.register('displayName')} />}
          </Field>
          <Field label="Email" error={errors.email?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} type="email" autoComplete="email" aria-invalid={!!errors.email} {...form.register('email')} />}
          </Field>
          <Field label="Mật khẩu" error={errors.password?.message} hint="Ít nhất 10 ký tự.">
            {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="new-password" aria-invalid={!!errors.password} {...form.register('password')} />}
          </Field>
          <Field label="Nhập lại mật khẩu" error={errors.confirm?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="new-password" aria-invalid={!!errors.confirm} {...form.register('confirm')} />}
          </Field>
          <Button type="submit" className="w-full" loading={form.formState.isSubmitting}>
            Đăng ký
          </Button>
        </form>
      )}
      <p className="mt-6 text-center text-sm text-muted">
        Đã có tài khoản?{' '}
        <Link className="link" to={paths.login}>
          Đăng nhập
        </Link>
      </p>
    </AuthCard>
  );
}

const changeSchema = z
  .object({
    currentPassword: z.string().min(1, 'Vui lòng nhập mật khẩu hiện tại.'),
    newPassword: z.string().min(10, 'Mật khẩu mới cần ít nhất 10 ký tự.').max(128, 'Mật khẩu tối đa 128 ký tự.'),
    confirm: z.string(),
  })
  .refine((v) => v.newPassword === v.confirm, { path: ['confirm'], message: 'Mật khẩu nhập lại không khớp.' })
  .refine((v) => v.newPassword !== v.currentPassword, { path: ['newPassword'], message: 'Mật khẩu mới phải khác mật khẩu hiện tại.' });

export function ChangePasswordPage() {
  useDocumentTitle('Đổi mật khẩu');
  const { user, applySession } = useAuth();
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const form = useForm<z.infer<typeof changeSchema>>({ resolver: zodResolver(changeSchema), defaultValues: { currentPassword: '', newPassword: '', confirm: '' } });
  const errors = form.formState.errors;

  const onSubmit = form.handleSubmit(async (values) => {
    setError(null);
    try {
      const session = await authApi.changePassword(values.currentPassword, values.newPassword);
      applySession(session);
      const u = session.user;
      navigate(u.systemRole === 'systemAdmin' ? paths.admin.root : u.memberships.length > 0 ? paths.studio.root : paths.home, { replace: true });
    } catch (e) {
      if (!applyServerErrors(e, form.setError, ['currentPassword', 'newPassword'])) {
        setError(e instanceof ApiError ? e.message : 'Không đổi được mật khẩu.');
      }
    }
  });

  return (
    <AuthCard
      title="Đổi mật khẩu"
      subtitle={user?.mustChangePassword ? 'Tài khoản của bạn đang dùng mật khẩu tạm — hãy đặt mật khẩu mới.' : 'Các phiên đăng nhập khác sẽ bị đăng xuất.'}
      icon={<KeyRound className="h-5 w-5" />}
    >
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <FormError error={error} />
        <Field label="Mật khẩu hiện tại" error={errors.currentPassword?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="current-password" aria-invalid={!!errors.currentPassword} {...form.register('currentPassword')} />}
        </Field>
        <Field label="Mật khẩu mới" error={errors.newPassword?.message} hint="Ít nhất 10 ký tự.">
          {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="new-password" aria-invalid={!!errors.newPassword} {...form.register('newPassword')} />}
        </Field>
        <Field label="Nhập lại mật khẩu mới" error={errors.confirm?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="new-password" aria-invalid={!!errors.confirm} {...form.register('confirm')} />}
        </Field>
        <Button type="submit" className="w-full" loading={form.formState.isSubmitting}>
          Lưu mật khẩu mới
        </Button>
      </form>
    </AuthCard>
  );
}
