# Fonts embedded in test fixtures

`truetype-cmap-fallback.pdf`, `truetype-format0-subset.pdf` and the
`Helvetica.ttf` substitution fixture contain subsets of DejaVu Sans. The
DejaVu changes are public domain and the original
Bitstream Vera outlines are distributed under the Bitstream Vera font
license. Copyright © 2003 Bitstream, Inc. The license permits reproduction,
modification and distribution of the font software when its copyright and
permission notices are retained. The fixtures use renamed subsets.

`opentype-cff-cmap-fallback.pdf` embeds a subset of Nimbus Sans and
`type1-rendering.pdf` embeds the corresponding Type 1 program from the
URW Base 35 fonts. Copyright © 2015 URW Software and © 2013–2014 URW++
Design & Development. The font is distributed under AGPL-3.0 with the font
exception, which explicitly permits inclusion of the font program in a PDF
document containing text displayed by that font.

The fixtures contain only the glyphs required for `ABC`. They are test data,
not runtime font dependencies. Their reproducible source is
`generate_font_fixtures.py`.
