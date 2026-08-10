# DejaVu Sans playground fallbacks

The four TrueType files in this directory are Unicode subsets of DejaVu Sans
2.37, downloaded from the official `dejavu-fonts/dejavu-fonts` GitHub release:

https://github.com/dejavu-fonts/dejavu-fonts/releases/tag/version_2_37

Source archive: `dejavu-fonts-ttf-2.37.zip`

SHA-256: `7576310B219E04159D35FF61DD4A4EC4CDBA4F35C00E002A136F00E96A908B0A`

The subsets retain glyphs from these ranges:

- U+0020-U+024F
- U+0300-U+036F
- U+2000-U+206F
- U+20A0-U+20CF

They were generated with fontTools 4.59.0 while retaining layout features,
names, legacy and symbol cmaps, glyph names, and recommended glyphs. See
`LICENSE-DejaVu.txt` for the complete upstream license and copyright notices.
