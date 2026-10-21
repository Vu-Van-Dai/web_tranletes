import { BarChart3, BookCopy, Cog, Gauge, Layers, UserCog, Users } from 'lucide-react';
import { paths } from '@/app/paths';
import { Select } from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { RequireHouse } from '@/features/auth/guards';
import { WorkspaceLayout } from '@/layouts/WorkspaceLayout';
import { roleLabel } from '@/text/vi';

function HouseSwitcher() {
  const { houses, currentHouse, selectHouse } = useAuth();
  if (!currentHouse) return null;
  return (
    <div className="space-y-1">
      {houses.length > 1 ? (
        <Select aria-label="Chọn nhóm dịch" value={currentHouse.houseId} onChange={(e) => selectHouse(e.target.value)} data-testid="house-switcher">
          {houses.map((h) => (
            <option key={h.houseId} value={h.houseId}>
              {h.houseName}
              {h.houseStatus !== 'active' ? ' (tạm ngưng)' : ''}
            </option>
          ))}
        </Select>
      ) : (
        <p className="truncate px-3 font-semibold" data-testid="current-house">{currentHouse.houseName}</p>
      )}
      <p className="px-3 text-xs text-muted">{roleLabel[currentHouse.role]}</p>
    </div>
  );
}

export function HouseLayout() {
  const { can } = useAuth();
  return (
    <RequireHouse>
      <WorkspaceLayout
        title="Quản lý nhóm dịch"
        header={<HouseSwitcher />}
        items={[
          { to: paths.studio.root, label: 'Tổng quan', icon: <Gauge className="h-4 w-4" />, end: true },
          { to: paths.studio.comics, label: 'Truyện', icon: <BookCopy className="h-4 w-4" /> },
          { to: paths.studio.processing, label: 'Trạng thái xử lý', icon: <Layers className="h-4 w-4" /> },
          { to: paths.studio.stats, label: 'Lượt xem & doanh thu', icon: <BarChart3 className="h-4 w-4" /> },
          { to: paths.studio.members, label: 'Thành viên', icon: <Users className="h-4 w-4" />, visible: can('viewMembers') },
          { to: paths.studio.profile, label: 'Hồ sơ nhóm', icon: <UserCog className="h-4 w-4" /> },
          { to: paths.studio.settings, label: 'Cài đặt', icon: <Cog className="h-4 w-4" /> },
        ]}
      />
    </RequireHouse>
  );
}
