// Original, quiet contact-and-motion texture. No external samples.
export function createSound(duration) {
  const rate=48000,count=Math.round(rate*duration),samples=new Float64Array(count*2);
  let seed=9011,fast=0,slow=0,peak=0;
  const envelope=(t,start,length,power=1.5)=>{const p=(t-start)/length;return p>0&&p<1?Math.sin(Math.PI*p)**power:0};
  for(let i=0;i<count;i++){
    const t=i/rate;seed=(Math.imul(seed,1664525)+1013904223)>>>0;
    const noise=seed/2147483648-1;fast+=.20*(noise-fast);slow+=.027*(noise-slow);
    const contact=(fast-slow)*envelope(t,.22,.76)*.15;
    const surround=(fast-slow)*envelope(t,.86,.67)*.025;
    const age=t-1.53;
    const settle=age>0?Math.sin(2*Math.PI*165*age)*Math.exp(-age/0.055)*(1-Math.exp(-age/.005))*.018:0;
    const value=contact+surround+settle;
    samples[i*2]=value*(.91-.08*Math.min(1,t/1.16));
    samples[i*2+1]=value*(.83+.08*Math.min(1,t/1.16));
    peak=Math.max(peak,Math.abs(samples[i*2]),Math.abs(samples[i*2+1]));
  }
  const wav=Buffer.alloc(44+count*4);wav.write('RIFF');wav.writeUInt32LE(wav.length-8,4);wav.write('WAVEfmt ',8);wav.writeUInt32LE(16,16);wav.writeUInt16LE(1,20);wav.writeUInt16LE(2,22);wav.writeUInt32LE(rate,24);wav.writeUInt32LE(rate*4,28);wav.writeUInt16LE(4,32);wav.writeUInt16LE(16,34);wav.write('data',36);wav.writeUInt32LE(count*4,40);
  for(let i=0;i<samples.length;i++)wav.writeInt16LE(Math.round(samples[i]/peak*.16*32767),44+i*2);
  return wav;
}
