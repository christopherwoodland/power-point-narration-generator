import { VOICES } from '../data/voices';
import { useAdmin } from '../context/AdminContext';

interface Props {
  value: string;
  onChange: (v: string) => void;
  disabled?: boolean;
  ttsMode?: string;
}

export default function VoiceSelector({ value, onChange, disabled, ttsMode }: Props) {
  const { settings } = useAdmin();
  const isMai = ttsMode === 'mai';
  let filtered = isMai ? VOICES.filter(v => v.group === 'English (MAI)') : VOICES.filter(v => v.group !== 'English (MAI)');

  // Apply admin voice restrictions
  if (settings.enabledVoices.length > 0) {
    filtered = filtered.filter(v => settings.enabledVoices.includes(v.value));
  }

  const groups = [...new Set(filtered.map(v => v.group))];

  return (
    <div className="field">
      <label className="field-label" htmlFor="voice-select">Language &amp; Voice</label>
      <select
        id="voice-select"
        className="field-select"
        value={value}
        disabled={disabled}
        onChange={e => onChange(e.target.value)}
      >
        {groups.map(group => (
          <optgroup key={group} label={group}>
            {filtered.filter(v => v.group === group).map(v => (
              <option key={v.value} value={v.value}>{v.label}</option>
            ))}
          </optgroup>
        ))}
      </select>
    </div>
  );
}
