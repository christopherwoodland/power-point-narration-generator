export const VOICES: { value: string; label: string; group: string }[] = [
  // ── English (MAI) ─────────────────────────────────────────────────────────
  // MAI-Voice-1 model voices — accessed via the standard Azure Speech SSML endpoint.
  { value: 'en-US-Grant:MAI-Voice-1',  label: 'Grant (en-US, ♂)',  group: 'English (MAI)' },
  { value: 'en-US-Iris:MAI-Voice-1',   label: 'Iris (en-US, ♀)',   group: 'English (MAI)' },
  { value: 'en-US-Jasper:MAI-Voice-1', label: 'Jasper (en-US, ♂)', group: 'English (MAI)' },
  { value: 'en-US-Joy:MAI-Voice-1',    label: 'Joy (en-US, ♀)',    group: 'English (MAI)' },
  { value: 'en-US-June:MAI-Voice-1',   label: 'June (en-US, ♀)',   group: 'English (MAI)' },
  { value: 'en-US-Reed:MAI-Voice-1',   label: 'Reed (en-US, ♂)',   group: 'English (MAI)' },

  // ── English (Dragon HD) ───────────────────────────────────────────────────
  // DragonHDLatestNeural voices — high-definition LLM-based voices.
  { value: 'en-US-Ava:DragonHDLatestNeural',    label: 'Ava (en-US, ♀)',    group: 'English (Dragon HD)' },
  { value: 'en-US-Andrew:DragonHDLatestNeural', label: 'Andrew (en-US, ♂)', group: 'English (Dragon HD)' },
  { value: 'en-US-Adam:DragonHDLatestNeural',   label: 'Adam (en-US, ♂)',   group: 'English (Dragon HD)' },
  { value: 'en-US-Emma:DragonHDLatestNeural',   label: 'Emma (en-US, ♀)',   group: 'English (Dragon HD)' },
  { value: 'en-US-Brian:DragonHDLatestNeural',  label: 'Brian (en-US, ♂)',  group: 'English (Dragon HD)' },
  { value: 'en-US-Davis:DragonHDLatestNeural',  label: 'Davis (en-US, ♂)',  group: 'English (Dragon HD)' },
  { value: 'en-US-Jenny:DragonHDLatestNeural',  label: 'Jenny (en-US, ♀)',  group: 'English (Dragon HD)' },
  { value: 'en-US-Aria:DragonHDLatestNeural',   label: 'Aria (en-US, ♀)',   group: 'English (Dragon HD)' },
  { value: 'en-US-Steffan:DragonHDLatestNeural',label: 'Steffan (en-US, ♂)',group: 'English (Dragon HD)' },
  { value: 'en-US-Jane:DragonHDLatestNeural',   label: 'Jane (en-US, ♀)',   group: 'English (Dragon HD)' },
  { value: 'en-US-Nova:DragonHDLatestNeural',   label: 'Nova (en-US, ♀)',   group: 'English (Dragon HD)' },
  { value: 'en-US-Phoebe:DragonHDLatestNeural', label: 'Phoebe (en-US, ♀)', group: 'English (Dragon HD)' },
  { value: 'en-GB-Ada:DragonHDLatestNeural',    label: 'Ada (en-GB, ♀)',    group: 'English (Dragon HD)' },
  { value: 'en-GB-Ollie:DragonHDLatestNeural',  label: 'Ollie (en-GB, ♂)',  group: 'English (Dragon HD)' },

  // ── English ───────────────────────────────────────────────────────────────
  { value: 'en-US-JennyNeural',     label: 'Jenny (en-US, ♀)',   group: 'English' },
  { value: 'en-US-GuyNeural',       label: 'Guy (en-US, ♂)',     group: 'English' },
  { value: 'en-US-AriaNeural',      label: 'Aria (en-US, ♀)',    group: 'English' },
  { value: 'en-US-DavisNeural',     label: 'Davis (en-US, ♂)',   group: 'English' },
  { value: 'en-US-NancyNeural',     label: 'Nancy (en-US, ♀)',   group: 'English' },
  { value: 'en-US-TonyNeural',      label: 'Tony (en-US, ♂)',    group: 'English' },
  { value: 'en-US-SteffanNeural',   label: 'Steffan (en-US, ♂)', group: 'English' },
  { value: 'en-GB-SoniaNeural',     label: 'Sonia (en-GB, ♀)',   group: 'English' },
  { value: 'en-GB-RyanNeural',      label: 'Ryan (en-GB, ♂)',    group: 'English' },
  { value: 'en-GB-LibbyNeural',     label: 'Libby (en-GB, ♀)',   group: 'English' },
  { value: 'en-AU-NatashaNeural',   label: 'Natasha (en-AU, ♀)', group: 'English' },
  { value: 'en-AU-WilliamNeural',   label: 'William (en-AU, ♂)', group: 'English' },
  { value: 'en-CA-ClaraNeural',     label: 'Clara (en-CA, ♀)',   group: 'English' },
  { value: 'en-CA-LiamNeural',      label: 'Liam (en-CA, ♂)',    group: 'English' },
  { value: 'en-IN-NeerjaNeural',    label: 'Neerja (en-IN, ♀)',  group: 'English' },
  { value: 'en-IN-PrabhatNeural',   label: 'Prabhat (en-IN, ♂)', group: 'English' },

  // ── French ────────────────────────────────────────────────────────────────
  { value: 'fr-FR-Vivienne:DragonHDLatestNeural', label: 'Vivienne (fr-FR, ♀, HD)', group: 'French' },
  { value: 'fr-FR-Remy:DragonHDLatestNeural',     label: 'Remy (fr-FR, ♂, HD)',     group: 'French' },
  { value: 'fr-FR-DeniseNeural',                  label: 'Denise (fr-FR, ♀)',       group: 'French' },
  { value: 'fr-FR-HenriNeural',                   label: 'Henri (fr-FR, ♂)',        group: 'French' },
  { value: 'fr-CA-Sylvie:DragonHDLatestNeural',   label: 'Sylvie (fr-CA, ♀, HD)',   group: 'French' },
  { value: 'fr-CA-Thierry:DragonHDLatestNeural',  label: 'Thierry (fr-CA, ♂, HD)',  group: 'French' },
  { value: 'fr-CA-SylvieNeural',                  label: 'Sylvie (fr-CA, ♀)',       group: 'French' },
  { value: 'fr-CA-JeanNeural',                    label: 'Jean (fr-CA, ♂)',         group: 'French' },

  // ── Spanish ───────────────────────────────────────────────────────────────
  { value: 'es-ES-Ximena:DragonHDLatestNeural', label: 'Ximena (es-ES, ♀, HD)', group: 'Spanish' },
  { value: 'es-ES-Tristan:DragonHDLatestNeural',label: 'Tristan (es-ES, ♂, HD)', group: 'Spanish' },
  { value: 'es-ES-ElviraNeural',                label: 'Elvira (es-ES, ♀)',      group: 'Spanish' },
  { value: 'es-ES-AlvaroNeural',                label: 'Alvaro (es-ES, ♂)',      group: 'Spanish' },
  { value: 'es-MX-Ximena:DragonHDLatestNeural', label: 'Ximena (es-MX, ♀, HD)', group: 'Spanish' },
  { value: 'es-MX-Tristan:DragonHDLatestNeural',label: 'Tristan (es-MX, ♂, HD)', group: 'Spanish' },
  { value: 'es-MX-DaliaNeural',                 label: 'Dalia (es-MX, ♀)',       group: 'Spanish' },
  { value: 'es-MX-JorgeNeural',                 label: 'Jorge (es-MX, ♂)',       group: 'Spanish' },

  // ── German ────────────────────────────────────────────────────────────────
  { value: 'de-DE-Seraphina:DragonHDLatestNeural', label: 'Seraphina (de-DE, ♀, HD)', group: 'German' },
  { value: 'de-DE-Florian:DragonHDLatestNeural',   label: 'Florian (de-DE, ♂, HD)',   group: 'German' },
  { value: 'de-DE-KatjaNeural',                    label: 'Katja (de-DE, ♀)',         group: 'German' },
  { value: 'de-DE-ConradNeural',                   label: 'Conrad (de-DE, ♂)',        group: 'German' },

  // ── Italian ───────────────────────────────────────────────────────────────
  { value: 'it-IT-Isabella:DragonHDLatestNeural', label: 'Isabella (it-IT, ♀, HD)', group: 'Italian' },
  { value: 'it-IT-Alessio:DragonHDLatestNeural',  label: 'Alessio (it-IT, ♂, HD)',  group: 'Italian' },
  { value: 'it-IT-ElsaNeural',                    label: 'Elsa (it-IT, ♀)',         group: 'Italian' },
  { value: 'it-IT-DiegoNeural',                   label: 'Diego (it-IT, ♂)',        group: 'Italian' },

  // ── Portuguese ────────────────────────────────────────────────────────────
  { value: 'pt-BR-Thalita:DragonHDLatestNeural', label: 'Thalita (pt-BR, ♀, HD)', group: 'Portuguese' },
  { value: 'pt-BR-Macerio:DragonHDLatestNeural', label: 'Macerio (pt-BR, ♂, HD)', group: 'Portuguese' },
  { value: 'pt-BR-FranciscaNeural',              label: 'Francisca (pt-BR, ♀)',   group: 'Portuguese' },
  { value: 'pt-BR-AntonioNeural',                label: 'Antonio (pt-BR, ♂)',     group: 'Portuguese' },
  { value: 'pt-PT-RaquelNeural',                 label: 'Raquel (pt-PT, ♀)',      group: 'Portuguese' },
  { value: 'pt-PT-DuarteNeural',                 label: 'Duarte (pt-PT, ♂)',      group: 'Portuguese' },

  // ── Japanese ──────────────────────────────────────────────────────────────
  { value: 'ja-JP-Nanami:DragonHDLatestNeural', label: 'Nanami (ja-JP, ♀, HD)', group: 'Japanese' },
  { value: 'ja-JP-Masaru:DragonHDLatestNeural', label: 'Masaru (ja-JP, ♂, HD)', group: 'Japanese' },
  { value: 'ja-JP-NanamiNeural',               label: 'Nanami (ja-JP, ♀)',     group: 'Japanese' },
  { value: 'ja-JP-KeitaNeural',                label: 'Keita (ja-JP, ♂)',      group: 'Japanese' },

  // ── Chinese ───────────────────────────────────────────────────────────────
  { value: 'zh-CN-Xiaochen:DragonHDLatestNeural', label: 'Xiaochen (zh-CN, ♀, HD)', group: 'Chinese' },
  { value: 'zh-CN-Yunfan:DragonHDLatestNeural',   label: 'Yunfan (zh-CN, ♂, HD)',   group: 'Chinese' },
  { value: 'zh-CN-XiaoxiaoNeural',                label: 'Xiaoxiao (zh-CN, ♀)',     group: 'Chinese' },
  { value: 'zh-CN-YunxiNeural',                   label: 'Yunxi (zh-CN, ♂)',        group: 'Chinese' },
  { value: 'zh-TW-HsiaoChenNeural',               label: 'HsiaoChen (zh-TW, ♀)',   group: 'Chinese' },
  { value: 'zh-TW-YunJheNeural',                  label: 'YunJhe (zh-TW, ♂)',      group: 'Chinese' },
  { value: 'zh-HK-HiuMaanNeural',                 label: 'HiuMaan (zh-HK, ♀)',     group: 'Chinese' },
  { value: 'zh-HK-WanLungNeural',                 label: 'WanLung (zh-HK, ♂)',     group: 'Chinese' },

  // ── Korean ────────────────────────────────────────────────────────────────
  { value: 'ko-KR-SunHi:DragonHDLatestNeural',  label: 'SunHi (ko-KR, ♀, HD)', group: 'Korean' },
  { value: 'ko-KR-Hyunsu:DragonHDLatestNeural', label: 'Hyunsu (ko-KR, ♂, HD)', group: 'Korean' },
  { value: 'ko-KR-SunHiNeural',                 label: 'SunHi (ko-KR, ♀)',     group: 'Korean' },
  { value: 'ko-KR-InJoonNeural',                label: 'InJoon (ko-KR, ♂)',    group: 'Korean' },

  // ── Dutch ─────────────────────────────────────────────────────────────────
  { value: 'nl-NL-FennaNeural',   label: 'Fenna (nl-NL, ♀)',  group: 'Dutch' },
  { value: 'nl-NL-MaartenNeural', label: 'Maarten (nl-NL, ♂)', group: 'Dutch' },
  { value: 'nl-NL-ColetteNeural', label: 'Colette (nl-NL, ♀)', group: 'Dutch' },

  // ── Arabic ────────────────────────────────────────────────────────────────
  { value: 'ar-SA-ZariyahNeural', label: 'Zariyah (ar-SA, ♀)', group: 'Arabic' },
  { value: 'ar-SA-HamedNeural',   label: 'Hamed (ar-SA, ♂)',   group: 'Arabic' },
  { value: 'ar-AE-FatimaNeural',  label: 'Fatima (ar-AE, ♀)',  group: 'Arabic' },
  { value: 'ar-AE-HamdanNeural',  label: 'Hamdan (ar-AE, ♂)',  group: 'Arabic' },
  { value: 'ar-EG-SalmaNeural',   label: 'Salma (ar-EG, ♀)',   group: 'Arabic' },
  { value: 'ar-EG-ShakirNeural',  label: 'Shakir (ar-EG, ♂)',  group: 'Arabic' },

  // ── Hindi ─────────────────────────────────────────────────────────────────
  { value: 'hi-IN-SwaraNeural',  label: 'Swara (hi-IN, ♀)',  group: 'Hindi' },
  { value: 'hi-IN-MadhurNeural', label: 'Madhur (hi-IN, ♂)', group: 'Hindi' },

  // ── Swedish ───────────────────────────────────────────────────────────────
  { value: 'sv-SE-SofieNeural',   label: 'Sofie (sv-SE, ♀)',   group: 'Swedish' },
  { value: 'sv-SE-MattiasNeural', label: 'Mattias (sv-SE, ♂)', group: 'Swedish' },

  // ── Turkish ───────────────────────────────────────────────────────────────
  { value: 'tr-TR-EmelNeural',  label: 'Emel (tr-TR, ♀)',  group: 'Turkish' },
  { value: 'tr-TR-AhmetNeural', label: 'Ahmet (tr-TR, ♂)', group: 'Turkish' },

  // ── Polish ────────────────────────────────────────────────────────────────
  { value: 'pl-PL-ZofiaNeural',     label: 'Zofia (pl-PL, ♀)',     group: 'Polish' },
  { value: 'pl-PL-MarekNeural',     label: 'Marek (pl-PL, ♂)',     group: 'Polish' },
  { value: 'pl-PL-AgnieszkaNeural', label: 'Agnieszka (pl-PL, ♀)', group: 'Polish' },

  // ── Russian ───────────────────────────────────────────────────────────────
  { value: 'ru-RU-SvetlanaNeural', label: 'Svetlana (ru-RU, ♀)', group: 'Russian' },
  { value: 'ru-RU-DmitryNeural',   label: 'Dmitry (ru-RU, ♂)',   group: 'Russian' },
];
