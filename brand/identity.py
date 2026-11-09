"""Editable canonical vector identity and outlined lettering.

Run with fontTools installed. Outlined lettering derives from the bundled
unmodified SIL OFL Manrope; SVG exports require no installed fonts.
"""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
import json

ROOT=Path(__file__).resolve().parent
INK='#111114'
WHITE='#FFFFFF'
FONT=ROOT/'type/Manrope-wght.ttf'

CONTACT_FRAME='M34 9H20C12.82 9 7 14.82 7 22V43C7 50.18 12.82 56 20 56H43C50.18 56 56 50.18 56 43V31H50V43C50 46.87 46.87 50 43 50H20C16.13 50 13 46.87 13 43V22C13 18.13 16.13 15 20 15H34Z'
CONTACT_LINE='M18 43C22 35 28 29 39.72 34.72C29.5 32.8 24 40 18 43Z'
CONTACT_CENTRE='M18 43C23 36.5 29 31.5 39.5 35'
CONTACT_PEN='M0 0L-3.5 -7.4V-27C-3.5 -29 -2.2 -30.2 0 -30.2S3.5 -29 3.5 -27V-7.4Z'

def word_paths(weight=600):
    f=instantiateVariableFont(TTFont(FONT),{'wght':weight})
    gs=f.getGlyphSet(); cmap=f.getBestCmap(); scale=49/f['head'].unitsPerEm
    # Pair-specific display spacing, in ems, rather than uniform compression.
    adjustments={'In':-.010,'nk':-.018,'kl':-.010,'la':-.006,'an':-.015,'ne':-.012}
    parts=[]; cursor=0
    for i,ch in enumerate('Inklane'):
        name=cmap[ord(ch)]
        pen=SVGPathPen(gs)
        if ch=='l':
            # Open foot, matching the frame's softened inner corner.
            pen.moveTo((cursor+3.92,12.985));pen.lineTo((cursor+9.22,12.985));pen.lineTo((cursor+9.22,41.3));pen.curveTo((cursor+9.22,43.4),(cursor+10.1,44.3),(cursor+12.3,44.3));pen.lineTo((cursor+14.3,44.3));pen.lineTo((cursor+14.3,49));pen.lineTo((cursor+11.3,49));pen.curveTo((cursor+6.4,49),(cursor+3.92,46.5),(cursor+3.92,41.6));pen.closePath()
        else:
            gs[name].draw(TransformPen(pen,(scale,0,0,-scale,cursor,49)))
        parts.append({'letter':ch,'path':pen.getCommands(),'x':cursor})
        cursor+=17.2 if ch=='l' else gs[name].width*scale
        if i<6: cursor+=adjustments['Inklane'[i:i+2]]*49
    return parts,cursor

def svg(body,w=64,h=64,label='Inklane'):
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}" role="img" aria-label="{label}">{body}</svg>'

def shape(name,fore=INK,accent='#5633FF',small=False):
    if name=='contact':
        frame='M33 8H20C12.27 8 6 14.27 6 22V43C6 50.73 12.27 57 20 57H43C50.73 57 57 50.73 57 43V32H50V43C50 46.87 46.87 50 43 50H20C16.13 50 13 46.87 13 43V22C13 18.13 16.13 15 20 15H33Z' if small else CONTACT_FRAME
        line='M19 42C23 34 29 29.5 39.72 34.72C29.8 32.7 25 40 19 42Z' if small else CONTACT_LINE
        return f'<path d="{frame}" fill="{fore}"/><path d="{line}" fill="{accent}"/><path d="{CONTACT_PEN}" fill="{fore}" transform="translate(39.5 35) rotate(37)"/>'
    raise ValueError('Unknown artwork')

def android_icons():
    """Use the same filled paths in Android's native vector format."""
    res=ROOT.parent/'platforms/android/src/main/res'
    def vector(body):
        return '<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="108dp" android:height="108dp" android:viewportWidth="64" android:viewportHeight="64">\n'+body+'\n</vector>\n'
    def artwork(accent):
        return (f'<path android:pathData="{CONTACT_FRAME}" android:fillColor="{WHITE}"/>\n'
                f'<path android:pathData="{CONTACT_LINE}" android:fillColor="{accent}"/>\n'
                '<group android:translateX="39.5" android:translateY="35" android:rotation="37">\n'
                f'<path android:pathData="{CONTACT_PEN}" android:fillColor="{WHITE}"/>\n</group>')
    for filename,accent in [('inklane_foreground.xml','#9B87FF'),('inklane_monochrome.xml',WHITE)]:
        # Android's guaranteed circle is 66/108 of the canvas, smaller than
        # the web maskable safe area. Keep the nib inside it under every mask.
        body='<group android:translateX="13.44" android:translateY="13.44" android:scaleX=".58" android:scaleY=".58">\n'+artwork(accent)+'\n</group>'
        (res/'drawable'/filename).write_text(vector(body),encoding='utf8')
    tile=f'<path android:pathData="M14 0H50A14 14 0 0 1 64 14V50A14 14 0 0 1 50 64H14A14 14 0 0 1 0 50V14A14 14 0 0 1 14 0Z" android:fillColor="{INK}"/>'
    body=tile+'\n<group android:translateX="4" android:translateY="4" android:scaleX=".875" android:scaleY=".875">\n'+artwork('#9B87FF')+'\n</group>'
    (res/'drawable/inklane.xml').write_text(vector(body),encoding='utf8')
    (res/'values/inklane_brand.xml').write_text(f'<resources>\n    <color name="inklane_icon_background">{INK}</color>\n</resources>\n',encoding='utf8')
    for version in [26,33]:
        folder=res/f'mipmap-anydpi-v{version}';folder.mkdir(exist_ok=True)
        mono='\n    <monochrome android:drawable="@drawable/inklane_monochrome"/>' if version>=33 else ''
        (folder/'inklane_app.xml').write_text('<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">\n    <background android:drawable="@color/inklane_icon_background"/>\n    <foreground android:drawable="@drawable/inklane_foreground"/>'+mono+'\n</adaptive-icon>\n',encoding='utf8')

def build():
    assets=ROOT.parent/'src/Inklane/Assets'
    assets.mkdir(parents=True,exist_ok=True)
    parts,width=word_paths(620)
    words=''.join(f'<path d="{x["path"]}"/>' for x in parts)
    lock_width=round(77+width,3)
    for mode,fore,accent in [('light',INK,'#5633FF'),('dark',WHITE,'#9B87FF'),('mono','currentColor','currentColor')]:
        (assets/f'inklane-symbol-{mode}.svg').write_text(svg(shape('contact',fore,accent)),encoding='utf8')
        (assets/f'inklane-lockup-{mode}.svg').write_text(svg(shape('contact',fore,accent)+f'<g fill="{fore}" transform="translate(77 0)">{words}</g>',lock_width),encoding='utf8')
        (assets/f'inklane-wordmark-{mode}.svg').write_text(svg(f'<g fill="{fore}" transform="translate(-3 0)">{words}</g>',round(width,3)),encoding='utf8')
    (assets/'inklane.svg').write_bytes((assets/'inklane-symbol-light.svg').read_bytes())
    (assets/'inklane-symbol-small.svg').write_text(svg(shape('contact',INK,'#5633FF',True)),encoding='utf8')
    for suffix,small in [('',False),('-small',True)]:
        body=f'<rect width="64" height="64" rx="14" fill="{INK}"/><g transform="translate(4 4) scale(.875)">{shape("contact",WHITE,"#9B87FF",small)}</g>'
        (assets/f'inklane-app{suffix}.svg').write_text(svg(body),encoding='utf8')
    (assets/'inklane-app-square.svg').write_text(svg(f'<rect width="64" height="64" fill="{INK}"/><g transform="translate(4 4) scale(.875)">{shape("contact",WHITE,"#9B87FF")}</g>'),encoding='utf8')
    (assets/'inklane-adaptive-foreground.svg').write_text(svg(f'<g transform="translate(11 11) scale(.65625)">{shape("contact",WHITE,"#9B87FF")}</g>'),encoding='utf8')
    (assets/'inklane-adaptive-background.svg').write_text(svg(f'<rect width="64" height="64" fill="{INK}"/>'),encoding='utf8')
    data={'contact':{'accent':'#5633FF','width':lock_width,'letters':parts,'frame':CONTACT_FRAME,'line':CONTACT_LINE,'pen':CONTACT_PEN,'centre':CONTACT_CENTRE}}
    (ROOT/'motion/geometry.json').write_text(json.dumps(data,indent=2),encoding='utf8')
    template=(ROOT/'motion/player.html').read_text(encoding='utf8')
    module=(ROOT/'motion/motion.mjs').read_text(encoding='utf8').replace('export function mountFilm','function mountFilm')
    template=template.replace("import {mountFilm} from './motion.mjs';",module).replace("const data=await(await fetch('./geometry.json')).json(),svg=document.querySelector('svg');",'const data='+json.dumps(data)+",svg=document.querySelector('svg');")
    (ROOT/'motion/inklane-sting.html').write_text(template,encoding='utf8')
    android_icons()
    print(json.dumps({'lockupWidth':lock_width}))

if __name__=='__main__':build()
