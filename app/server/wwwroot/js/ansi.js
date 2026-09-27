// Streams text with ANSI SGR colors (Terraform, Task and TFLint all emit them) into a <pre>.
// Stateful across writes: a color set in one chunk carries into the next, and an escape
// sequence split across two chunks is held back until it's complete.

const CSI = /\x1b\[([0-9;?]*)([A-Za-z])/g;
const OSC = /\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)/g;
const INCOMPLETE_TAIL = /(?:\x1b(?:\[[0-9;?]*)?|\r)$/;
const BASIC_FG = { 30: 0, 31: 1, 32: 2, 33: 3, 34: 4, 35: 5, 36: 6, 37: 7, 90: 8, 91: 9, 92: 10, 93: 11, 94: 12, 95: 13, 96: 14, 97: 15 };

export class AnsiRenderer {
  constructor(target) {
    this.target = target;
    this.pending = '';
    this.resetStyle();
  }

  resetStyle() {
    this.fg = null;
    this.bold = false;
    this.dim = false;
    this.italic = false;
    this.underline = false;
  }

  write(text, stream = 'stdout') {
    let input = this.pending + text;
    this.pending = '';
    const tail = INCOMPLETE_TAIL.exec(input);
    if (tail) {
      this.pending = input.slice(tail.index);
      input = input.slice(0, tail.index);
    }
    input = input.replace(OSC, '').replace(/\r\n/g, '\n').replace(/\r/g, '\n');

    const fragment = document.createDocumentFragment();
    let last = 0;
    CSI.lastIndex = 0;
    for (let match; (match = CSI.exec(input));) {
      if (match.index > last) fragment.append(this.span(input.slice(last, match.index), stream));
      if (match[2] === 'm') this.applySgr(match[1]);
      last = CSI.lastIndex;
    }
    if (last < input.length) fragment.append(this.span(input.slice(last), stream));
    this.target.append(fragment);
  }

  note(text) {
    const el = document.createElement('span');
    el.className = 'trim-note';
    el.textContent = text;
    this.target.append(el);
  }

  span(text, stream) {
    const el = document.createElement('span');
    const classes = [`s-${stream}`];
    if (this.fg != null) classes.push(`a-fg${this.fg}`);
    if (this.bold) classes.push('a-b');
    if (this.dim) classes.push('a-d');
    if (this.italic) classes.push('a-i');
    if (this.underline) classes.push('a-u');
    el.className = classes.join(' ');
    el.textContent = text;
    return el;
  }

  applySgr(params) {
    const codes = params === '' ? [0] : params.split(';').map(Number);
    for (let i = 0; i < codes.length; i++) {
      const code = codes[i];
      if (code === 0) this.resetStyle();
      else if (code === 1) this.bold = true;
      else if (code === 2) this.dim = true;
      else if (code === 3) this.italic = true;
      else if (code === 4) this.underline = true;
      else if (code === 22) { this.bold = false; this.dim = false; }
      else if (code === 23) this.italic = false;
      else if (code === 24) this.underline = false;
      else if (code === 39) this.fg = null;
      else if (code in BASIC_FG) this.fg = BASIC_FG[code];
      else if (code === 38 || code === 48) {
        // 256-color (38;5;n) maps onto the 16-color palette when possible; truecolor is ignored.
        if (codes[i + 1] === 5) {
          if (code === 38) this.fg = codes[i + 2] < 16 ? codes[i + 2] : null;
          i += 2;
        } else if (codes[i + 1] === 2) {
          i += 4;
        }
      }
    }
  }
}
