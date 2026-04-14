export default function Header() {
  return (
    <header className="header">
      <div className="header-inner">
        <div className="header-logo">
          <svg width="32" height="32" viewBox="0 0 32 32" fill="none" aria-hidden="true">
            <rect width="32" height="32" rx="8" fill="url(#logo-grad)" />
            <path d="M9 9h8a4 4 0 0 1 0 8H9V9z" fill="#fff" opacity=".9" />
            <path d="M9 17h6a3 3 0 0 1 0 6H9v-6z" fill="#fff" opacity=".6" />
            <defs>
              <linearGradient id="logo-grad" x1="0" y1="0" x2="32" y2="32" gradientUnits="userSpaceOnUse">
                <stop stopColor="#7c3aed" />
                <stop offset="1" stopColor="#2563eb" />
              </linearGradient>
            </defs>
          </svg>
          <h1 className="header-title">PowerPoint Narration Generator</h1>
        </div>
        <div className="header-badge">
          <span>Powered by Azure AI</span>
        </div>
      </div>
    </header>
  );
}
