import { t } from '../i18n';
import { useCompanyAccess } from './roleApi';
import { RolesByUser } from './RolesTab';

/** Fisa firmei › Utilizatori (21.11.2026): cine are roluri care acopera firma, pe ce nod, si drepturile lor, doar pentru citire. */
export function CompanyUsersTab({ companyId, currentUserId }: { readonly companyId: number; readonly currentUserId: number }) {
  const access = useCompanyAccess(companyId);
  if (access.data === undefined) {
    return <p className="muted" role="status">{access.isError ? access.error.message : t('app.loading')}</p>;
  }
  return (
    <div className="tab-body">
      <RolesByUser matrix={access.data} companyId={companyId} currentUserId={currentUserId} readOnly />
    </div>
  );
}
