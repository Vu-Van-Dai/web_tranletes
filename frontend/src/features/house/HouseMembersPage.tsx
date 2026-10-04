import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { UserPlus } from 'lucide-react';
import { Badge, Button, Checkbox, ConfirmDialog, DataTable, ErrorState, Field, Input, Modal, PageHeader, Select, Skeleton, useToast } from '@/components/ui';
import { useAuth } from '@/features/auth/AuthProvider';
import { houseApi } from '@/services/api';
import { useDocumentTitle } from '@/hooks/useTheme';
import { delegablePermissions, permissionLabel, roleLabel } from '@/text/vi';
import type { Member, Permission } from '@/types/api';
import { formatDate } from '@/utils/format';

export interface MemberFormValue {
  login: string;
  role: 'owner' | 'staff';
  permissions: Permission[];
}

/** Shared by the house workspace and the SystemAdmin house detail. */
export function MemberEditor({ open, member, onClose, onSubmit, saving }: {
  open: boolean;
  member: Member | null;
  onClose: () => void;
  onSubmit: (value: MemberFormValue) => void;
  saving: boolean;
}) {
  const [login, setLogin] = useState('');
  const [role, setRole] = useState<'owner' | 'staff'>(member?.role ?? 'staff');
  const [permissions, setPermissions] = useState<Permission[]>(member?.permissions ?? ['viewMembers', 'manageChapters', 'uploadChapters']);
  const toggle = (p: Permission) => setPermissions((all) => (all.includes(p) ? all.filter((x) => x !== p) : [...all, p]));

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={member ? `Phân quyền: ${member.displayName}` : 'Thêm thành viên'}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>Huỷ</Button>
          <Button loading={saving} onClick={() => onSubmit({ login, role, permissions })} data-testid="member-save">{member ? 'Lưu' : 'Thêm'}</Button>
        </>
      }
    >
      <div className="space-y-4">
        {!member && (
          <Field label="Email hoặc tên đăng nhập" hint="Người này cần đăng ký tài khoản trước.">
            {(id) => <Input id={id} value={login} onChange={(e) => setLogin(e.target.value)} autoFocus />}
          </Field>
        )}
        <Field label="Vai trò">
          {(id) => (
            <Select id={id} value={role} onChange={(e) => setRole(e.target.value as 'owner' | 'staff')}>
              <option value="staff">{roleLabel.staff}</option>
              <option value="owner">{roleLabel.owner}</option>
            </Select>
          )}
        </Field>
        {role === 'staff' ? (
          <div>
            <p className="mb-1 text-sm font-medium">Quyền được giao</p>
            {delegablePermissions.map((p) => (
              <Checkbox key={p} label={permissionLabel[p]} checked={permissions.includes(p)} onChange={() => toggle(p)} />
            ))}
          </div>
        ) : (
          <p className="rounded-xl bg-surface-2 p-3 text-sm text-muted">Chủ nhóm có toàn quyền, kể cả quản lý thành viên và hồ sơ nhóm.</p>
        )}
      </div>
    </Modal>
  );
}

export function MembersTable({ members, canManage, onEdit, onRemove }: { members: Member[]; canManage: boolean; onEdit: (m: Member) => void; onRemove: (m: Member) => void }) {
  return (
    <DataTable
      rows={members}
      rowKey={(m) => m.id}
      testId="members-table"
      columns={[
        { key: 'name', header: 'Thành viên', cell: (m) => <div><p className="font-medium">{m.displayName}</p><p className="text-xs text-muted">@{m.username}{m.email ? ` · ${m.email}` : ''}</p></div> },
        { key: 'role', header: 'Vai trò', cell: (m) => <Badge tone={m.role === 'owner' ? 'accent' : 'neutral'}>{roleLabel[m.role]}</Badge> },
        {
          key: 'perms', header: 'Quyền', hideOnMobile: true,
          cell: (m) => (m.role === 'owner' ? <span className="text-xs text-muted">Toàn quyền</span> : <span className="text-xs text-muted">{m.permissions.map((p) => permissionLabel[p]).join(', ') || '—'}</span>),
        },
        { key: 'joined', header: 'Tham gia', cell: (m) => <span className="text-xs text-muted">{formatDate(m.joinedAt)}</span>, hideOnMobile: true },
        {
          key: 'actions', header: '', className: 'text-right',
          cell: (m) => canManage && (
            <div className="flex justify-end gap-1">
              <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); onEdit(m); }}>Phân quyền</Button>
              <Button size="sm" variant="ghost" className="text-danger" onClick={(e) => { e.stopPropagation(); onRemove(m); }}>Xoá</Button>
            </div>
          ),
        },
      ]}
    />
  );
}

export function HouseMembersPage() {
  useDocumentTitle('Thành viên');
  const { currentHouse, can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const canManage = can('manageMembers');
  const members = useQuery({ queryKey: ['house', currentHouse?.houseId, 'members'], queryFn: houseApi.members });
  const [editing, setEditing] = useState<Member | null>(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<Member | null>(null);
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['house', currentHouse?.houseId, 'members'] });

  const save = useMutation({
    mutationFn: (v: MemberFormValue) => (editing ? houseApi.updateMember(editing.id, { role: v.role, permissions: v.permissions }) : houseApi.addMember(v)),
    onSuccess: () => {
      toast.success(editing ? 'Đã cập nhật quyền.' : 'Đã thêm thành viên.');
      setEditing(null);
      setAdding(false);
      refresh();
    },
    onError: (e) => toast.error(e),
  });

  const remove = useMutation({
    mutationFn: (m: Member) => houseApi.removeMember(m.id),
    onSuccess: () => {
      toast.success('Đã xoá thành viên khỏi nhóm.');
      setRemoving(null);
      refresh();
    },
    onError: (e) => toast.error(e),
  });

  return (
    <>
      <PageHeader title="Thành viên" description="Chủ nhóm có toàn quyền; thành viên chỉ làm được những việc được giao."
        actions={canManage && <Button icon={<UserPlus className="h-4 w-4" />} onClick={() => setAdding(true)} data-testid="add-member">Thêm thành viên</Button>} />
      {members.isError ? <ErrorState error={members.error} onRetry={() => members.refetch()} />
        : members.isLoading ? <Skeleton className="h-40 rounded-2xl" />
          : <MembersTable members={members.data ?? []} canManage={canManage} onEdit={setEditing} onRemove={setRemoving} />}
      {(adding || editing) && (
        <MemberEditor open member={editing} saving={save.isPending} onClose={() => { setAdding(false); setEditing(null); }} onSubmit={(v) => save.mutate(v)} />
      )}
      <ConfirmDialog open={!!removing} danger title="Xoá thành viên" confirmLabel="Xoá khỏi nhóm" loading={remove.isPending}
        message={<>Xoá <strong>{removing?.displayName}</strong> khỏi nhóm? Người này sẽ mất quyền truy cập ngay lập tức.</>}
        onClose={() => setRemoving(null)} onConfirm={() => removing && remove.mutate(removing)} />
    </>
  );
}
