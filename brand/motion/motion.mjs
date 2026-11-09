// Editable deterministic vector timeline. Times are seconds; geometry uses 64 units.
// renderFrame() drives both the HTML preview and the encoded 60fps master.
export const MOTION={duration:5.2,spreadStart:2,spreadDuration:1.4,typeStart:2.28,typeDuration:1.24,letterStep:.048,letterSettle:.95,letterOpacity:.72};
export function mountFilm(svg, g, {dark=false}={}) {
  const ink=dark?'#FFFFFF':'#111114', paper=dark?'#111114':'#FFFFFF', accent=dark?'#9B87FF':g.accent;
  const letters=g.letters.map((x,i)=>`<g id="letter-${i}"><path d="${x.path}"/></g>`).join('');
  svg.setAttribute('viewBox','0 0 1920 1080');
  svg.innerHTML=`<defs>
    <path id="centre" d="${g.centre}"/>
    <mask id="inkReveal" maskUnits="userSpaceOnUse" x="0" y="0" width="64" height="64"><path id="inkMask" d="${g.centre}" fill="none" stroke="white" stroke-width="12" stroke-linecap="butt"/></mask>
    <mask id="frameReveal" maskUnits="userSpaceOnUse" x="0" y="0" width="64" height="64"><path id="frameTop" d="M10 33V22C10 16.48 14.48 12 20 12H34" fill="none" stroke="white" stroke-width="8"/><path id="frameBottom" d="M10 31V43C10 48.52 14.48 53 20 53H43C48.52 53 53 48.52 53 43V31" fill="none" stroke="white" stroke-width="8"/></mask>
    <clipPath id="typeReveal"><rect x="77" y="0" height="64" width="${g.width-77+8}"/></clipPath>
  </defs><rect width="1920" height="1080" fill="${paper}"/>
  <g id="composition"><g id="symbol">
   <path d="${g.frame}" fill="${ink}" mask="url(#frameReveal)"/>
   <path id="drawnInk" d="${g.line}" fill="${accent}" mask="url(#inkReveal)"/>
   <path id="pen" d="${g.pen}" fill="${ink}"/>
  </g><g id="word" fill="${ink}" clip-path="url(#typeReveal)"><g id="letters" transform="translate(77 0)">${letters}</g></g></g>`;
  const byId=x=>svg.querySelector('#'+x);
  const centre=byId('centre'), total=centre.getTotalLength(), inkMask=byId('inkMask'), pen=byId('pen');
  const top=byId('frameTop'), bottom=byId('frameBottom');
  const lt=top.getTotalLength(),lb=bottom.getTotalLength();
  inkMask.setAttribute('stroke-dasharray',`${total} ${total+20}`);
  top.setAttribute('stroke-dasharray',`${lt} ${lt+20}`);
  bottom.setAttribute('stroke-dasharray',`${lb} ${lb+20}`);
  // Cubic Bézier timing is solved for x, preserving the actual authored easing.
  function bezier(x,x1,y1,x2,y2){
    x=Math.max(0,Math.min(1,x));let lo=0,hi=1,t=x;
    const sample=(a,b,t)=>3*(1-t)*(1-t)*t*a+3*(1-t)*t*t*b+t*t*t;
    for(let i=0;i<18;i++){if(sample(x1,x2,t)<x)lo=t;else hi=t;t=(lo+hi)/2;}
    return sample(y1,y2,t);
  }
  const phase=(t,start,duration,curve=[.25,0,.2,1])=>bezier((t-start)/duration,...curve);
  const mix=(a,b,t)=>a+(b-a)*t;
  function renderFrame(t) {
    t=Math.max(0,Math.min(MOTION.duration,t));
    const entry=phase(t,0,.22,[.16,.4,.27,1]);
    const writing=phase(t,.22,.76,[.36,0,.34,1]);
    const p=centre.getPointAtLength(total*writing);
    const wrist=37-5*Math.sin(writing*Math.PI);
    const py=p.y-(1-entry)*7;
    const px=p.x-(1-entry)*3;
    pen.setAttribute('transform',`translate(${px} ${py}) rotate(${mix(30,wrist,entry)})`);
    pen.setAttribute('opacity',Math.min(1,t/.10));
    inkMask.setAttribute('stroke-dashoffset',total*(1-writing));
    byId('drawnInk').setAttribute('opacity',t<.22?0:1);
    const enclose=phase(t,.86,.63,[.4,0,.16,1]);
    top.setAttribute('stroke-dashoffset',lt*(1-enclose));
    bottom.setAttribute('stroke-dashoffset',lb*(1-phase(t,.90,.63,[.4,0,.16,1])));
    top.setAttribute('opacity',Math.max(0,Math.min(1,(t-.86)/.06)));bottom.setAttribute('opacity',Math.max(0,Math.min(1,(t-.90)/.06)));
    // Let the complete symbol rest before opening into the horizontal lockup.
    const spread=phase(t,MOTION.spreadStart,MOTION.spreadDuration,[.42,0,.3,1]);
    const scale=mix(6.4,3.8,spread), x=mix(960-32*6.4,(1920-g.width*3.8)/2,spread), y=540-32*scale;
    byId('composition').setAttribute('transform',`translate(${x} ${y}) scale(${scale})`);
    // Broadly overlapping arrivals preserve the letter-by-letter character.
    // Near-baseline movement and eased opacity make them one continuous phrase.
    const type=phase(t,MOTION.typeStart,MOTION.typeDuration,[.38,0,.3,1]);
    byId('letters').setAttribute('transform',`translate(${77+(1-type)*1.8} 0)`);
    for(let i=0;i<g.letters.length;i++){
      const begin=MOTION.typeStart+i*MOTION.letterStep;
      const settle=phase(t,begin,MOTION.letterSettle,[.3,0,.24,1]);
      byId('letter-'+i).setAttribute('transform',`translate(0 ${(1-settle)*.65})`);
      byId('letter-'+i).setAttribute('opacity',phase(t,begin,MOTION.letterOpacity,[.36,0,.24,1]));
    }
    return {time:t,writing,pen:{x:px,y:py},end:{x:p.x,y:p.y},scale,enclose,type};
  }
  renderFrame(0);
  return {renderFrame,total,duration:MOTION.duration};
}
