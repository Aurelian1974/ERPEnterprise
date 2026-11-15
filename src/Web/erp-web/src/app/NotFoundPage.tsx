import { Link } from 'react-router';
import { t } from '../i18n';

export function NotFoundPage() {
  return (
    <div className="page">
      <h1>{t('notFound.title')}</h1>
      <p className="sub2">{t('notFound.text')}</p>
      <p>
        <Link to="/">{t('notFound.back')}</Link>
      </p>
    </div>
  );
}
