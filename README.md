ZX Spectrum ASMP (c) 2010 Ref / Ret
===================================

Asmp is a realtime image generator for Original Zx Spectrum Computers for 48k to +3 models.

See https://arda.kisafilm.org/blog/?p=726&lang=en blogpost for more insights.


Overview
--------

MLT files cannot be displayed on a standard ZX Spectrum due to contention
patterns. On a ZX Spectrum, the 8×1 multicolor limitation cannot exceed a
width of 18–20 characters. This has been demonstrated in various demo
productions so far.

However, MLT files rarely require 20 color changes on every line. Even in
such cases, it may still be possible to transfer a full-screen MLT image to
the Spectrum in the best possible way by making certain trade-offs. This is
where Asmp comes into play.

What Asmp Is (and Isn’t)
------------------------

Asmp is essentially a proof-of-concept project, not a converter. Asmp can 
produce executable graphics file without 18 column limitation so you can 
enjoy a mlt image in full screen on a regular speccy!

The Asmp creator program:

- Takes an MLT file
- Calculates how many color changes are required on each raster line
- Optimizes these changes when possible
- Provides tools to help the user intentionally reduce colors and edit pixels

In this sense, Asmp is not an automation tool — it functions more like a
pixel art editor.

Compression Methods
-------------------

Asmp uses ZX0 compression. The compression list offers none or zx0 (default when available).

Enable Compress Code before generating to pack the raster code, attributes and bitmap together. This requires both zx0.exe and pasmo.exe in the application folder. The generated ASM embeds the compressed bytes and can be assembled or exported to TAP normally.

The package loads at 32768 and expands at 33280. A separate loader stack preserves BASIC return state. Compressed input is relocated above the expanded program and below the IM2 area at 65021. Generation reports an error if both blocks cannot fit safely in that space. The built-in raster preview continues to use the original assembly.

Output Generation
-----------------

Asmp generates an assembly (.asm) file and compiles it with Pasmo to produce
a TAP file.

As a result, an Asmp output is not a standard image but actually executable
machine code. In this form, it falls under the category of realtime
graphics.

Recommended Tools
------------

To use Asmp outputs, you need the following files based on your output preferences.
(asmp will work and produce asm files without those)
- zx0.exe
- pasmo.exe

Copyright
---------

Asmp (c) 2010 Arda Erdikmen

Compression verification
------------------------

Build the Debug configuration, then run `tests/CompressionSmoke.ps1`. It uses
`zx0.exe`, `dzx0.exe` and `pasmo.exe` from `aSMP/bin/Debug`. The test covers all
three target models, BASIC return on/off, plain/bitmap/full compression,
standalone ASM/TAP assembly, byte-for-byte decompression, oversized payloads
and missing tools. A small instruction interpreter checks the generated
loader, bitmap copy and BASIC register/SP restoration; it does not simulate
Spectrum raster timing. Pass `-Fixture` to use another MLT file.
