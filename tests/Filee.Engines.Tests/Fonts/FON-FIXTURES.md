# Synthetic FON test fixtures

`make-test-fon.py` builds NE RT_FONT containers with FNT v2/v3 resources from pixel art defined in the script. No
bytes or outlines are taken from any existing font, so the samples are original synthetic data under the project's
MIT licence. The tests read only the committed files and never run the generator.

Rebuild: `uv run tests/Filee.Engines.Tests/Fonts/make-test-fon.py`

Check: `sha256sum tests/Filee.Engines.Tests/Fonts/Fon-*.fon`

| File | SHA-256 |
|---|---|
| Fon-v2-fixed.fon | `3b1b05572ba5ce6e0c011e5fb7bcc9ecedf367abd1d58fdb31dca161ca75e9f4` |
| Fon-v2-proportional.fon | `802ec675899d50afd8b22907a505c8029cb837d555b59dac76492267e977d4b2` |
| Fon-v3-fixed.fon | `ebc793d05862802a57ee2af3d2abd7425bd97a2957e3f4fd449a50863a8804a8` |
| Fon-v3-proportional.fon | `0744fc346722abd93f735a6bc42540ccdb5248b703d196d1b5c4dd957b88525d` |
| Fon-multi.fon | `e1f5379b11a5d4bddcfbb5b7c03c08556d8bb32528e973e58fcecb3290697273` |
| Fon-edge.fon | `fa8dd5d6424850d6054ae89685cffae30ab2b5757d13be86b97c8d3b24dd8b09` |
| Fon-v3-flags-04.fon | `6c789389ed490fd4f88dd1e67329ba10464cd76e0c22c1acb95eebf17142f03e` |
| Fon-v3-flags-08.fon | `c7f99f904af5d83d9b47603fe13645c701bca7ef1c22f362f181c1d506d0d6a3` |
| Fon-v3-flags-10.fon | `6e5538d1cf78a776f4631861da563163bcd087f7205ebd8898171a6f00071917` |
| Fon-v3-flags-20.fon | `c218cd58e2bfb33653cf0726137f4c6eb6943c73866d815c72feb43e8f77d58d` |
| Fon-v3-flags-40.fon | `86caba38ada299971ca997b458e6c15162f3765149aec8070ce1d7bb79d73365` |
| Fon-v3-flags-80.fon | `89f79b840576b013100e39f87fc306c71d5b161be6bc9eec77e8f5eafa965d5b` |

The basic samples cover 0x20..0x7F at 13 px; only the v2 proportional sample also holds the control codes
0x00..0x1F. The space is blank, `!` is a single pixel at `(2,0)`, `"` is a diagonal, and every other glyph is a
framed rectilinear shape. Proportional widths are 0 px for the space, 9 px for even codes and 5 px for odd codes.
`Fon-multi.fon` holds 13, 13 and 14 px resources, and `Fon-edge.fon` holds 1 and 256 px resources. The flag samples
check that unsupported v3 dfFlags are rejected; each uses a glyph table entry size, widths and offsets that differ
from the allowed 6-byte table. The external `fixed.fon` and `tektite.fon` are not included.
