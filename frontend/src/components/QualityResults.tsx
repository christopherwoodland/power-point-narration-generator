import type { QualityCheckResult } from '../types';

interface Props {
  results: QualityCheckResult[];
}

function ConfidenceBadge({ value }: { value: number }) {
  const pct = Math.round(value * 100);
  const cls =
    pct >= 85 ? 'badge--green' :
    pct >= 60 ? 'badge--yellow' : 'badge--red';
  return <span className={`badge ${cls}`}>{pct}%</span>;
}

export default function QualityResults({ results }: Props) {
  const avg = results.length > 0
    ? results.reduce((s, r) => s + r.confidence, 0) / results.length
    : 0;

  return (
    <div className="qc-results">
      <div className="qc-summary">
        <span>Overall confidence:</span>
        <ConfidenceBadge value={avg} />
        <span className="qc-summary-count">{results.length} slides checked</span>
      </div>
      <table className="qc-table" aria-label="Quality check results">
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Slide</th>
            <th scope="col">Confidence</th>
            <th scope="col">Issues</th>
          </tr>
        </thead>
        <tbody>
          {results.map(r => (
            <tr key={r.slide_num}>
              <td className="td-num">{r.slide_num}</td>
              <td className="td-title">{r.title}</td>
              <td><ConfidenceBadge value={r.confidence} /></td>
              <td className="td-issues">
                {r.issues.length === 0
                  ? <span className="issue-none"><span aria-hidden="true">✓ </span>No issues</span>
                  : <ul className="issue-list">
                      {r.issues.map((issue, i) => (
                        <li key={i}>{issue}</li>
                      ))}
                    </ul>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
