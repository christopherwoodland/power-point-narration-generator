import { createContext, useContext, useEffect, useState } from 'react';

export interface AdminSettings {
  appName: string;
  logoUrl: string;
  primaryColor: string;
  primaryColorDark: string;
  primaryColorLight: string;
  accentColor: string;
  enabledVoices: string[];   // voice value ids; empty array = all voices enabled
}

const DEFAULT_SETTINGS: AdminSettings = {
  appName: 'GAO Text to Speech',
  logoUrl: '',
  primaryColor: '#004d2f',
  primaryColorDark: '#003320',
  primaryColorLight: '#e6f4ee',
  accentColor: '#007a4d',
  enabledVoices: [],
};

const STORAGE_KEY = 'admin-settings';

function loadSettings(): AdminSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) return { ...DEFAULT_SETTINGS, ...JSON.parse(raw) };
  } catch { /* ignore */ }
  return DEFAULT_SETTINGS;
}

interface AdminContextValue {
  settings: AdminSettings;
  updateSettings: (patch: Partial<AdminSettings>) => void;
  resetSettings: () => void;
}

const AdminContext = createContext<AdminContextValue>({
  settings: DEFAULT_SETTINGS,
  updateSettings: () => {},
  resetSettings: () => {},
});

export function AdminProvider({ children }: { children: React.ReactNode }) {
  const [settings, setSettings] = useState<AdminSettings>(loadSettings);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
    // Apply CSS custom properties
    const root = document.documentElement;
    root.style.setProperty('--color-primary', settings.primaryColor);
    root.style.setProperty('--color-primary-dark', settings.primaryColorDark);
    root.style.setProperty('--color-primary-light', settings.primaryColorLight);
    root.style.setProperty('--color-accent', settings.accentColor);
  }, [settings]);

  const updateSettings = (patch: Partial<AdminSettings>) =>
    setSettings(s => ({ ...s, ...patch }));

  const resetSettings = () => setSettings(DEFAULT_SETTINGS);

  return (
    <AdminContext.Provider value={{ settings, updateSettings, resetSettings }}>
      {children}
    </AdminContext.Provider>
  );
}

export function useAdmin() {
  return useContext(AdminContext);
}
