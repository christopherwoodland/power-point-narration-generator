import { VOICES } from '../data/voices';

interface Props {
  value: string;
  onChange: (v: string) => void;
  disabled?: boolean;
}

export default function VoiceSelector({ value, onChange, disabled }: Props) {
  const groups = [...new Set(VOICES.map(v => v.group))];

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
            {VOICES.filter(v => v.group === group).map(v => (
              <option key={v.value} value={v.value}>{v.label}</option>
            ))}
          </optgroup>
        ))}
      </select>
    </div>
  );
}
