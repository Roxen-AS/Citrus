# Original geometric test glyphs, dedicated to the public domain under CC0-1.0.
# No outlines are copied from installed fonts. Requires fontTools 4.61.1.
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.ttLib import TTCollection
from pathlib import Path
from hashlib import sha256
root = Path(__file__).parent
chars = ' ABCXYZ0123456789中文'
for variant in (1,2):
    fb=FontBuilder(1000,isTTF=True)
    names=['.notdef']+[f'u{ord(c):04X}' for c in chars]
    fb.setupGlyphOrder(names); fb.setupCharacterMap({ord(c):n for c,n in zip(chars,names[1:])})
    glyphs={}
    for i,n in enumerate(names):
        pen=TTGlyphPen(None)
        if n != 'u0020':
            # Deliberately simple, distinct known-answer silhouettes; not a reading font.
            x=80; w=250+variant*80+(i%3)*40
            pen.moveTo((x,0)); pen.lineTo((x+w,0)); pen.lineTo((x+w,700)); pen.lineTo((x,700)); pen.closePath()
        glyphs[n]=pen.glyph()
    fb.setupGlyf(glyphs); fb.setupHorizontalMetrics({n:(650+variant*50,80) for n in names})
    fb.setupHorizontalHeader(ascent=800,descent=-200)
    fb.setupNameTable({'familyName':f'Citrus Fixture {variant}','styleName':'Regular','uniqueFontIdentifier':f'CitrusFixture{variant}', 'fullName':f'Citrus Fixture {variant} Regular','psName':f'CitrusFixture{variant}-Regular','version':'Version 1.0'})
    fb.setupOS2(sTypoAscender=800,sTypoDescender=-200,usWinAscent=800,usWinDescent=200)
    fb.setupPost(); fb.setupMaxp(); fb.font['head'].created=fb.font['head'].modified=2082844800
    fb.save(root/f'Fixture{variant}.ttf')
collection=TTCollection()
from fontTools.ttLib import TTFont
collection.fonts=[TTFont(root/'Fixture1.ttf', recalcTimestamp=False),TTFont(root/'Fixture2.ttf', recalcTimestamp=False)]
collection.save(root/'FixtureFaces.ttc')
for p in sorted(root.glob('*.tt*')): print(p.name, len(p.read_bytes()), sha256(p.read_bytes()).hexdigest())
