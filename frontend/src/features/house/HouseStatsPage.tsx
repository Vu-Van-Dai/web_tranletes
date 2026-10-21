import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ErrorState, PageHeader, Skeleton } from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { ComicStatsTable, DailyViews, DateRangePicker, ExportReportButton, presetRange, TotalsTiles } from '@/features/stats/ViewReport';
import { useDocumentTitle } from '@/hooks/useTheme';
import { houseApi } from '@/services/api';

export function HouseStatsPage() {
  useDocumentTitle('Lượt xem & doanh thu');
  const { currentHouse } = useAuth();
  const [range, setRange] = useState(() => presetRange('month'));
  const report = useQuery({
    queryKey: ['house', currentHouse?.houseId, 'view-stats', range],
    queryFn: () => houseApi.viewStats(range),
    placeholderData: keepPreviousData,
  });
  const r = report.data;

  return (
    <>
      <PageHeader
        title="Lượt xem & doanh thu"
        description="Chỉ tính lượt đọc đã được xác minh (đọc đủ thời gian và số trang, không trùng trong ngày, không phải bot). Số liệu của truyện là tổng các chương. Cập nhật mỗi phút; ngày tính theo giờ Việt Nam."
        actions={r && <ExportReportButton report={r} name={`luot-xem_${currentHouse?.houseSlug ?? 'nhom'}`} />}
      />
      <div className="mb-6"><DateRangePicker value={range} onChange={setRange} /></div>
      {report.isError ? (
        <ErrorState error={report.error} onRetry={() => report.refetch()} />
      ) : !r ? (
        <Skeleton className="h-64 rounded-2xl" />
      ) : (
        <div className="space-y-6">
          <TotalsTiles totals={r.totals} rate={r.revenuePerThousandViews} revenueVisible={r.revenueVisible} />
          {r.daily.length > 0 && <DailyViews daily={r.daily} />}
          <ComicStatsTable report={r} />
          <p className="text-xs text-muted">
            Lượt đọc của chính thành viên nhóm không được tính. “Bị loại” là các lượt gửi về không vượt qua kiểm tra chống gian lận — nếu con số này tăng bất thường,
            hệ thống quản trị sẽ xem xét trước khi thanh toán.
          </p>
        </div>
      )}
    </>
  );
}
