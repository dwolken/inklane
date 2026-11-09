// Runs against the real hosted editor DOM. It changes public controls rather
// than reaching into the application model or substituting native bridges.
(async () => {
  const $ = selector => document.querySelector(selector);
  const check = (value, label) => { if (!value) throw Error(label); };
  const count = selector => document.querySelectorAll(selector).length;
  const wait = async predicate => { for (let i=0;i<100;i++) { if (predicate()) return; await new Promise(resolve=>setTimeout(resolve,50)); } throw Error('Hosted edit did not settle'); };
  const change = (selector, value) => { $(selector).value = value; $(selector).dispatchEvent(new Event('change', {bubbles:true})); };
  const index = label => {
    $('#index').click();
    const button = [...document.querySelectorAll('#results button')].find(b => b.textContent.includes(label));
    check(button, 'Index entry ' + label); button.click();
  };
  check(count('.placed .katex') === 1 && count('.placed .hljs-keyword') === 1, 'Local math fonts and code renderer');
  check(count('[data-shape]') === 3, 'Connected diagram rendered');
  index('# Host review'); $('#properties').click();
  change('#object-width', 210); change('#color-hex', '#F97316'); $('#text-bold').click(); $('#close-properties').click();
  const text = $('.placed');
  check(getComputedStyle(text).color === 'rgb(249, 115, 22)', 'Existing text recolored');
  check(Number(getComputedStyle(text).fontWeight) >= 600, 'Existing text formatted');
  check(Math.abs(text.getBoundingClientRect().width / $('#sheet').getBoundingClientRect().width - .35) < .005, 'Text box width changed independently of font size');
  index('Source');
  check(count('[data-shape].object-selected') === 3, 'Group selection retained');
  const path = () => $('[data-id="host-arrow"][data-shape] path').getAttribute('d');
  const before = path();
  $('#viewport').dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowDown',shiftKey:true,bubbles:true}));
  check(path() !== before, 'Connected group moves'); $('#undo').click();
  check(path() === before, 'Undo restores connection geometry');
  index('Source');
  $('#duplicate').click(); await wait(()=>count('[data-shape]') === 6);
  $('#undo').click(); check(count('[data-shape]') === 3, 'Duplicate undo');
  index('Complete explanation'); $('#note-read').click();
  check(count('#note-preview .katex') === 1, 'Complete remark renders display math');
  $('#close-card').click();
  return true;
})()
