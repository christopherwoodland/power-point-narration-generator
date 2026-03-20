import zipfile, io, re, sys
sys.path.insert(0, 'backend')
from pptx_builder import embed_audio_into_pptx

pptx_bytes = open('test.pptx', 'rb').read()

with zipfile.ZipFile('test_real_audio5.pptx') as z:
    mp3_bytes = z.read('ppt/media/audio_slide1.mp3')
print(f'Reusing cached MP3, {len(mp3_bytes)} bytes')

result = embed_audio_into_pptx(pptx_bytes, [mp3_bytes, None, None, None, None])
open('test_api_output.pptx', 'wb').write(result)
print(f'Saved {len(result)} bytes')

with zipfile.ZipFile(io.BytesIO(result)) as z:
    rels = z.read('ppt/slides/_rels/slide1.xml.rels').decode()
    xml  = z.read('ppt/slides/slide1.xml').decode()
    ct   = z.read('[Content_Types].xml').decode()
    print('audio rel:', 'relationships/audio' in rels)
    print('media rel:', '2007/relationships/media' in rels)
    print('a:audioFile:', 'a:audioFile' in xml)
    print('mainSeq:', 'mainSeq' in xml)
    print('p14:media:', 'p14:media' in xml)
    print('audio/mpeg in CT:', 'audio/mpeg' in ct)
    print('timing present:', '<p:timing' in xml)
    m = re.search(r'<p:cNvPr id="(\d+)" name="Audio', xml)
    pic_sid = m.group(1) if m else 'NOT FOUND'
    timing_spids = re.findall(r'spid="(\d+)"', xml)
    print(f'pic shape id: {pic_sid}')
    print(f'timing spids: {timing_spids}')
    if pic_sid != 'NOT FOUND' and timing_spids:
        print('spid MATCH:', all(s == pic_sid for s in timing_spids))

print('Done')
