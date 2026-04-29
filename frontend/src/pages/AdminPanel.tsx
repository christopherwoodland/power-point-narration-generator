import { useState } from 'react';
import { useAdmin } from '../context/AdminContext';
import { VOICES } from '../data/voices';

interface Props {
  onClose: () => void;
}

export default function AdminPanel({ onClose }: Props) {
  const { settings, updateSettings, resetSettings } = useAdmin();
  const [form, setForm] = useState(settings);
  const [voiceFilter, setVoiceFilter] = useState('');

  const handleSave = () => {
    updateSettings(form);
    onClose();
  };

  const handleLogoChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      setForm(f => ({ ...f, logoUrl: reader.result as string }));
    };
    reader.readAsDataURL(file);
  };

  return (
    <div className="admin-overlay" onClick={onClose}>
      <div className="admin-panel" onClick={e => e.stopPropagation()}>
        <div className="admin-header">
          <h2 className="admin-title">Admin Customization</h2>
          <button className="btn btn--ghost admin-close" onClick={onClose} aria-label="Close">
            ✕
          </button>
        </div>

        <div className="admin-body">
          <div className="admin-field">
            <label className="field-label" htmlFor="admin-app-name">Application Name</label>
            <input
              id="admin-app-name"
              type="text"
              className="admin-input"
              value={form.appName}
              onChange={e => setForm(f => ({ ...f, appName: e.target.value }))}
            />
          </div>

          <div className="admin-field">
            <label className="field-label" htmlFor="admin-logo">Custom Logo</label>
            <input
              id="admin-logo"
              type="file"
              accept="image/*"
              className="admin-input"
              onChange={handleLogoChange}
            />
            {form.logoUrl && (
              <div className="admin-logo-preview">
                <img src={form.logoUrl} alt="Logo preview" />
                <button
                  className="btn btn--ghost"
                  onClick={() => setForm(f => ({ ...f, logoUrl: '' }))}
                >
                  Remove
                </button>
              </div>
            )}
          </div>

          <div className="admin-color-grid">
            <div className="admin-field">
              <label className="field-label" htmlFor="admin-primary">Primary Color</label>
              <div className="admin-color-row">
                <input
                  id="admin-primary"
                  type="color"
                  value={form.primaryColor}
                  onChange={e => setForm(f => ({ ...f, primaryColor: e.target.value }))}
                />
                <span className="admin-color-hex">{form.primaryColor}</span>
              </div>
            </div>

            <div className="admin-field">
              <label className="field-label" htmlFor="admin-primary-dark">Primary Dark</label>
              <div className="admin-color-row">
                <input
                  id="admin-primary-dark"
                  type="color"
                  value={form.primaryColorDark}
                  onChange={e => setForm(f => ({ ...f, primaryColorDark: e.target.value }))}
                />
                <span className="admin-color-hex">{form.primaryColorDark}</span>
              </div>
            </div>

            <div className="admin-field">
              <label className="field-label" htmlFor="admin-primary-light">Primary Light</label>
              <div className="admin-color-row">
                <input
                  id="admin-primary-light"
                  type="color"
                  value={form.primaryColorLight}
                  onChange={e => setForm(f => ({ ...f, primaryColorLight: e.target.value }))}
                />
                <span className="admin-color-hex">{form.primaryColorLight}</span>
              </div>
            </div>

            <div className="admin-field">
              <label className="field-label" htmlFor="admin-accent">Accent Color</label>
              <div className="admin-color-row">
                <input
                  id="admin-accent"
                  type="color"
                  value={form.accentColor}
                  onChange={e => setForm(f => ({ ...f, accentColor: e.target.value }))}
                />
                <span className="admin-color-hex">{form.accentColor}</span>
              </div>
            </div>
          </div>

          <div className="admin-section">
            <h3 className="admin-section-title">Available TTS Voices</h3>
            <p className="admin-section-hint">
              Select which voices appear in the user dropdown. If none are selected, all voices are available.
            </p>
            <div className="admin-voice-toolbar">
              <input
                type="text"
                className="admin-input"
                placeholder="Filter voices…"
                value={voiceFilter}
                onChange={e => setVoiceFilter(e.target.value)}
              />
              <button
                className="btn btn--ghost btn--sm"
                onClick={() => setForm(f => ({ ...f, enabledVoices: VOICES.map(v => v.value) }))}
              >
                Select All
              </button>
              <button
                className="btn btn--ghost btn--sm"
                onClick={() => setForm(f => ({ ...f, enabledVoices: [] }))}
              >
                Clear All
              </button>
            </div>
            <div className="admin-voice-list">
              {(() => {
                const groups = [...new Set(VOICES.map(v => v.group))];
                const lowerFilter = voiceFilter.toLowerCase();
                return groups.map(group => {
                  const groupVoices = VOICES.filter(v =>
                    v.group === group &&
                    (!voiceFilter || v.label.toLowerCase().includes(lowerFilter) || v.group.toLowerCase().includes(lowerFilter))
                  );
                  if (groupVoices.length === 0) return null;
                  const allSelected = groupVoices.every(v => form.enabledVoices.includes(v.value));
                  const someSelected = groupVoices.some(v => form.enabledVoices.includes(v.value));
                  return (
                    <div key={group} className="admin-voice-group">
                      <label className="admin-voice-group-header">
                        <input
                          type="checkbox"
                          checked={allSelected}
                          ref={el => { if (el) el.indeterminate = someSelected && !allSelected; }}
                          onChange={() => {
                            const vals = groupVoices.map(v => v.value);
                            if (allSelected) {
                              setForm(f => ({ ...f, enabledVoices: f.enabledVoices.filter(v => !vals.includes(v)) }));
                            } else {
                              setForm(f => ({ ...f, enabledVoices: [...new Set([...f.enabledVoices, ...vals])] }));
                            }
                          }}
                        />
                        <strong>{group}</strong>
                        <span className="admin-voice-count">
                          {groupVoices.filter(v => form.enabledVoices.includes(v.value)).length}/{groupVoices.length}
                        </span>
                      </label>
                      <div className="admin-voice-items">
                        {groupVoices.map(v => (
                          <label key={v.value} className="admin-voice-item">
                            <input
                              type="checkbox"
                              checked={form.enabledVoices.includes(v.value)}
                              onChange={() => {
                                setForm(f => ({
                                  ...f,
                                  enabledVoices: f.enabledVoices.includes(v.value)
                                    ? f.enabledVoices.filter(x => x !== v.value)
                                    : [...f.enabledVoices, v.value],
                                }));
                              }}
                            />
                            <span>{v.label}</span>
                          </label>
                        ))}
                      </div>
                    </div>
                  );
                });
              })()}
            </div>
            {form.enabledVoices.length > 0 && (
              <p className="admin-voice-summary">
                {form.enabledVoices.length} voice{form.enabledVoices.length !== 1 ? 's' : ''} selected
              </p>
            )}
          </div>
        </div>

        <div className="admin-footer">
          <button className="btn btn--ghost" onClick={() => { resetSettings(); setForm(settings); }}>
            Reset to Defaults
          </button>
          <div className="btn-group">
            <button className="btn btn--ghost" onClick={onClose}>Cancel</button>
            <button className="btn btn--primary" onClick={handleSave}>Save</button>
          </div>
        </div>
      </div>
    </div>
  );
}
