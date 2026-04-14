import React, { useRef } from 'react';

interface Props {
  label: string;
  hint: string;
  accept: string;
  file: File | null;
  disabled?: boolean;
  testId?: string;
  onFile: (f: File) => void;
}

export default function FileUploadCard({ label, hint, accept, file, disabled, testId, onFile }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0];
    if (f) onFile(f);
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if ((e.key === 'Enter' || e.key === ' ') && !disabled) inputRef.current?.click();
  };

  return (
    <div
      className={`upload-card ${file ? 'upload-card--has-file' : ''} ${disabled ? 'upload-card--disabled' : ''}`}
      role="button"
      tabIndex={disabled ? -1 : 0}
      aria-label={file ? `${label}: ${file.name} – click to replace` : `Upload ${label}: ${hint}`}
      data-testid={testId}
      onClick={() => !disabled && inputRef.current?.click()}
      onKeyDown={handleKeyDown}
    >
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        className="upload-input"
        aria-hidden="true"
        tabIndex={-1}
        disabled={disabled}
        onChange={handleChange}
      />
      <div className="upload-icon">
        {file ? (
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
            <path d="M9 12l2 2 4-4" stroke="var(--color-violet)" strokeWidth="2"
              strokeLinecap="round" strokeLinejoin="round" />
            <circle cx="12" cy="12" r="9" stroke="var(--color-violet)" strokeWidth="1.5" />
          </svg>
        ) : (
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" stroke="currentColor"
              strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
            <polyline points="17 8 12 3 7 8" stroke="currentColor" strokeWidth="1.5"
              strokeLinecap="round" strokeLinejoin="round" />
            <line x1="12" y1="3" x2="12" y2="15" stroke="currentColor" strokeWidth="1.5"
              strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        )}
      </div>
      <div className="upload-text">
        <span className="upload-label">{label}</span>
        <span className="upload-hint">{file ? file.name : hint}</span>
      </div>
    </div>
  );
}
