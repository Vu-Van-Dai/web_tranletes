import { lazy, Suspense, type ComponentType } from 'react';
import { createBrowserRouter, Navigate, Outlet, useRouteError } from 'react-router';
import { useAuth } from '@/features/auth/AuthProvider';
import { paths } from './paths';
import { ErrorState } from '@/components/ui';
import { FullPageSpinner, RequireAuth } from '@/features/auth/guards';
import { LoginPage, RegisterPage, ChangePasswordPage } from '@/features/auth/AuthPages';
import { BrowsePage, HomePage, HousePage, HousesPage, NotFoundPage } from '@/features/catalog/CatalogPages';
import { ComicDetailPage } from '@/features/catalog/ComicDetailPage';
import { PublicLayout } from '@/layouts/PublicLayout';

/** Lazy route groups: admin, house workspace and reader code is not shipped to every visitor. */
function lazyNamed<T>(load: () => Promise<T>, name: keyof T) {
  const Component = lazy(async () => ({ default: (await load())[name] as unknown as ComponentType }));
  return (
    <Suspense fallback={<FullPageSpinner />}>
      <Component />
    </Suspense>
  );
}

const reader = () => import('@/features/reader/ReaderPage');
const library = () => import('@/features/library/LibraryPage');
const house = () => import('@/features/house/HouseLayout');
const houseOverview = () => import('@/features/house/HouseOverviewPages');
const houseMembers = () => import('@/features/house/HouseMembersPage');
const houseComics = () => import('@/features/house/HouseComicPages');
const houseChapter = () => import('@/features/house/HouseChapterPage');
const housePreview = () => import('@/features/house/HouseVersionPreviewPage');
const admin = () => import('@/features/admin/AdminPages');
const adminRevenue = () => import('@/features/admin/AdminRevenuePages');
const adminAds = () => import('@/features/admin/AdminAdsPage');
const houseStats = () => import('@/features/house/HouseStatsPage');

function RouteError() {
  const error = useRouteError();
  return (
    <div className="container-page py-16">
      <ErrorState error={error} title="Trang gặp sự cố" onRetry={() => window.location.reload()} />
    </div>
  );
}

/**
 * /workspace: where an earlier deployment redirected the workspace/admin URLs (a cached permanent redirect in some
 * browsers). Send the visitor to the area they most likely wanted.
 */
function WorkspaceRedirect() {
  const { user, houses } = useAuth();
  const target = houses.length === 0 && user?.systemRole === 'systemAdmin' ? paths.admin.root : paths.studio.root;
  return <Navigate to={target} replace />;
}

export const router = createBrowserRouter([
  {
    errorElement: <RouteError />,
    children: [
      { path: '/doc/:chapterId', element: lazyNamed(reader, 'ReaderPage') },
      { path: '/workspace', element: <RequireAuth><WorkspaceRedirect /></RequireAuth> },
      {
        element: <PublicLayout />,
        children: [
          { index: true, element: <HomePage /> },
          { path: 'truyen', element: <BrowsePage mode="browse" /> },
          { path: 'tim-kiem', element: <BrowsePage mode="search" /> },
          { path: 'the-loai/:slug', element: <BrowsePage mode="genre" /> },
          { path: 'truyen/:id/:slug?', element: <ComicDetailPage /> },
          { path: 'nhom-dich', element: <HousesPage /> },
          { path: 'nhom-dich/:slug', element: <HousePage /> },
          { path: 'dang-nhap', element: <LoginPage /> },
          { path: 'dang-ky', element: <RegisterPage /> },
          { path: 'doi-mat-khau', element: <RequireAuth><ChangePasswordPage /></RequireAuth> },
          { path: 'tu-truyen', element: <RequireAuth>{lazyNamed(library, 'LibraryPage')}</RequireAuth> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
      {
        path: '/quan-ly-nhom',
        element: <RequireAuth>{lazyNamed(house, 'HouseLayout')}</RequireAuth>,
        children: [
          { index: true, element: lazyNamed(houseOverview, 'HouseDashboardPage') },
          { path: 'ho-so', element: lazyNamed(houseOverview, 'HouseProfilePage') },
          { path: 'xu-ly', element: lazyNamed(houseOverview, 'HouseProcessingPage') },
          { path: 'thong-ke', element: lazyNamed(houseStats, 'HouseStatsPage') },
          { path: 'cai-dat', element: lazyNamed(houseOverview, 'HouseSettingsPage') },
          { path: 'thanh-vien', element: lazyNamed(houseMembers, 'HouseMembersPage') },
          { path: 'truyen', element: lazyNamed(houseComics, 'HouseComicsPage') },
          { path: 'truyen/:id', element: lazyNamed(houseComics, 'HouseComicPage') },
          { path: 'chuong/:id', element: lazyNamed(houseChapter, 'HouseChapterPage') },
          { path: 'chuong/:id/xem-truoc/:version', element: lazyNamed(housePreview, 'HouseVersionPreviewPage') },
        ],
      },
      {
        path: '/quan-tri',
        element: <RequireAuth>{lazyNamed(admin, 'AdminLayout')}</RequireAuth>,
        children: [
          { index: true, element: lazyNamed(admin, 'AdminDashboardPage') },
          { path: 'nhom-dich', element: lazyNamed(admin, 'AdminHousesPage') },
          { path: 'nhom-dich/:id', element: lazyNamed(admin, 'AdminHouseDetailPage') },
          { path: 'nguoi-dung', element: lazyNamed(admin, 'AdminUsersPage') },
          { path: 'thong-ke', element: lazyNamed(admin, 'AdminStatsPage') },
          { path: 'nhat-ky', element: lazyNamed(admin, 'AdminAuditPage') },
          { path: 'cai-dat', element: lazyNamed(admin, 'AdminSettingsPage') },
          { path: 'doanh-thu', element: lazyNamed(adminRevenue, 'AdminRevenuePage') },
          { path: 'doanh-thu/:id', element: lazyNamed(adminRevenue, 'AdminHouseRevenuePage') },
          { path: 'quang-cao', element: lazyNamed(adminAds, 'AdminAdsPage') },
        ],
      },
      { path: '*', element: <Outlet /> },
    ],
  },
]);
