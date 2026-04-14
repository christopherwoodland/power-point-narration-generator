import { useEffect, useRef } from 'react';

interface Props {
  value: number;
  label?: string;
  size?: 'sm' | 'md';
  showPercent?: boolean;
}

export default function ProgressBar({ value, label, size = 'md', showPercent = true }: Props) {
  const pct = Math.min(100, Math.max(0, Math.round(value)));
  const fillRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (fillRef.current) fillRef.current.style.width = `${pct}%`;
  }, [pct]);

  return (
    <div className={`progress-wrap progress-wrap--${size}`}>
      {label && <div className="progress-label">{label}</div>}
      <div
        className="progress-track"
        role="progressbar"
        aria-label={label ?? 'Progress'}
        aria-valuenow={pct}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <div className="progress-fill" ref={fillRef} />
      </div>
      {showPercent && <div className="progress-pct">{pct}%</div>}
    </div>
  );
}
