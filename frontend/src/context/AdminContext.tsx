import { createContext, useContext, useEffect, useState } from 'react';
import { saveAdminSettings } from '../api/narrationApi';

export interface AdminSettings {
  appName: string;
  logoUrl: string;
  primaryColor: string;
  primaryColorDark: string;
  primaryColorLight: string;
  accentColor: string;
  enabledVoices: string[]; // voice value ids; empty array = all voices enabled
}

export const DEFAULT_SETTINGS: AdminSettings = {
  appName: 'GAO Text to Speech',
  logoUrl: '',
  primaryColor: '#004d2f',
  primaryColorDark: '#003320',
  primaryColorLight: '#e6f4ee',
  accentColor: '#007a4d',
  enabledVoices: [],
};

interface AdminContextValue {
  settings: AdminSettings;
  loadSettings: (s: AdminSettings) => void; // hydrate from server (no save)
  updateSettings: (patch: Partial<AdminSettings>) => Promise<void>; // save to server
  resetSettings: () => Promise<void>;
  ttsMode: string;
  setTtsMode: (mode: string) => void;
}

const AdminContext = createContext<AdminContextValue>({
  settings: DEFAULT_SETTINGS,
  loadSettings: () => {},
  updateSettings: async () => {},
  resetSettings: async () => {},
  ttsMode: 'standard',
  setTtsMode: () => {},
});

export function AdminProvider({ children }: { children: React.ReactNode }) {
  const [settings, setSettings] = useState<AdminSettings>(DEFAULT_SETTINGS);
  const [ttsMode, setTtsMode] = useState<string>('standard');

  // Apply CSS custom properties whenever settings change
  useEffect(() => {
    const root = document.documentElement;
    root.style.setProperty('--color-primary', settings.primaryColor);
    root.style.setProperty('--color-primary-dark', settings.primaryColorDark);
    root.style.setProperty('--color-primary-light', settings.primaryColorLight);
    root.style.setProperty('--color-primary-mid', settings.accentColor);
    root.style.setProperty('--color-accent', settings.accentColor);
  }, [settings]);

  const loadSettings = (s: AdminSettings) => setSettings(s);

  const updateSettings = async (patch: Partial<AdminSettings>) => {
    const next = { ...settings, ...patch };
    setSettings(next);
    await saveAdminSettings(next);
  };

  const resetSettings = async () => {
    setSettings(DEFAULT_SETTINGS);
    await saveAdminSettings(DEFAULT_SETTINGS);
  };

  return (
    <AdminContext.Provider
      value={{
        settings,
        loadSettings,
        updateSettings,
        resetSettings,
        ttsMode,
        setTtsMode,
      }}
    >
      {children}
    </AdminContext.Provider>
  );
}

export function useAdmin() {
  return useContext(AdminContext);
}
