ZX Spectrum ASMP (c) 2010 Ref / Ret
===================================

Asmp is a realtime image generator for Original Zx Spectrum Computers for 48k to +3 models.

ASMP README


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

The Asmp generator program:

- Takes an MLT file
- Calculates how many color changes are required on each raster line
- Optimizes these changes when possible
- Provides tools to help the user intentionally reduce colors and edit pixels

In this sense, Asmp is not an automation tool — it functions more like a
pixel art editor.

Compression Methods
-------------------

Asmp uses the MegaLZ ZX7 ZX0 compression algorithms for the bitmap sections:

Output Generation
-----------------

Asmp generates an assembly (.asm) file and compiles it with Pasmo to produce
a TAP file.

As a result, an Asmp output is not a standard image but actually executable
machine code. In this form, it falls under the category of realtime
graphics.

Requirements
------------

To use Asmp outputs, you need the following files:

- zx0.exe
- zx7.exe
- megalz.exe
- pasmo.exe

Copyright
---------

Asmp (c) 2010 Arda Erdikmen
