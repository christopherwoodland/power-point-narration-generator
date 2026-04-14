import type { SlideInfo } from '../types';

interface Props {
  slides: SlideInfo[];
  pptxSlideCount: number;
  aiMode: boolean;
  mapping: Record<number, number>;
  onChange: (mapping: Record<number, number>) => void;
}

function escapeHtml(str: string) {
  return str
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

export default function SlideMappingTable({ slides, pptxSlideCount, aiMode, mapping, onChange }: Props) {
  const handleChange = (wordIdx: number, pptxIdx: number) => {
    onChange({ ...mapping, [wordIdx]: pptxIdx });
  };

  if (aiMode) {
    return (
      <table className="mapping-table" aria-label="Slides for AI generation">
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Title</th>
            <th scope="col">Narration Preview</th>
          </tr>
        </thead>
        <tbody>
          {slides.map((slide, idx) => (
            <tr key={idx}>
              <td className="td-num">{idx + 1}</td>
              <td className="td-title">{escapeHtml(slide.title)}</td>
              <td className="td-preview">
                {slide.text.length > 220
                  ? slide.text.substring(0, 220) + '…'
                  : slide.text}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    );
  }

  return (
    <table className="mapping-table" aria-label="Slide mapping">
      <thead>
        <tr>
          <th scope="col">#</th>
          <th scope="col">Script Section</th>
          <th scope="col">Preview</th>
          <th scope="col">→ PPTX Slide</th>
        </tr>
      </thead>
      <tbody>
        {slides.map((slide, wordIdx) => {
          const currentVal = mapping[wordIdx] ?? (wordIdx < pptxSlideCount ? wordIdx : -1);
          return (
            <tr key={wordIdx}>
              <td className="td-num">{wordIdx + 1}</td>
              <td className="td-title">{escapeHtml(slide.title)}</td>
              <td className="td-preview">
                {slide.text.length > 180
                  ? slide.text.substring(0, 180) + '…'
                  : slide.text}
              </td>
              <td className="td-mapping">
                <select
                  className="mapping-select"
                  value={currentVal}
                  aria-label={`Map script slide ${wordIdx + 1}`}
                  onChange={e => handleChange(wordIdx, parseInt(e.target.value))}
                >
                  <option value={-1}>— Skip —</option>
                  {Array.from({ length: pptxSlideCount }, (_, i) => (
                    <option key={i} value={i}>PPTX Slide {i + 1}</option>
                  ))}
                </select>
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
