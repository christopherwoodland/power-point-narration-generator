import { useState } from 'react';
import { useAdmin } from '../context/AdminContext';
import AdminPanel from '../pages/AdminPanel';

export default function Header() {
  const { settings } = useAdmin();
  const [showAdmin, setShowAdmin] = useState(false);

  return (
    <>
      <header className="header">
        <div className="header-inner">
          <div className="header-logo">
            {settings.logoUrl ? (
              <img src={settings.logoUrl} alt="Logo" className="header-logo-img" />
            ) : (
              <svg width="32" height="32" viewBox="0 0 32 32" fill="none" aria-hidden="true">
                <rect width="32" height="32" rx="8" fill="var(--color-primary, #004d2f)" />
                <path d="M9 9h8a4 4 0 0 1 0 8H9V9z" fill="#fff" opacity=".9" />
                <path d="M9 17h6a3 3 0 0 1 0 6H9v-6z" fill="#fff" opacity=".6" />
              </svg>
            )}
            <h1 className="header-title">{settings.appName}</h1>
          </div>
          <div className="header-right">
            <button
              className="admin-gear-btn"
              onClick={() => setShowAdmin(true)}
              aria-label="Admin settings"
              title="Admin settings"
            >
              <svg width="18" height="18" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                <path fillRule="evenodd" d="M11.49 3.17c-.38-1.56-2.6-1.56-2.98 0a1.532 1.532 0 01-2.286.948c-1.372-.836-2.942.734-2.106 2.106.54.886.061 2.042-.947 2.287-1.561.379-1.561 2.6 0 2.978a1.532 1.532 0 01.947 2.287c-.836 1.372.734 2.942 2.106 2.106a1.532 1.532 0 012.287.947c.379 1.561 2.6 1.561 2.978 0a1.533 1.533 0 012.287-.947c1.372.836 2.942-.734 2.106-2.106a1.533 1.533 0 01.947-2.287c1.561-.379 1.561-2.6 0-2.978a1.532 1.532 0 01-.947-2.287c.836-1.372-.734-2.942-2.106-2.106a1.532 1.532 0 01-2.287-.947zM10 13a3 3 0 100-6 3 3 0 000 6z" clipRule="evenodd"/>
              </svg>
            </button>
            <div className="header-badge">
              <span>Powered by Azure AI</span>
            </div>
          </div>
        </div>
      </header>
      {showAdmin && <AdminPanel onClose={() => setShowAdmin(false)} />}
    </>
  );
}
