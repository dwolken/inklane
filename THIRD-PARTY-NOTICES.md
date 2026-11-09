# Third-party notices

The Inklane repository is public. These dependency notices do not grant an open-source license to Inklane itself.

## PDFsharp-WPF 6.2.4

Source: https://github.com/empira/PDFsharp/tree/v6.2.4
Package: https://www.nuget.org/packages/PDFsharp-WPF/6.2.4

Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany

http://docs.pdfsharp.net

MIT License

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.

## Microsoft.Extensions.Logging.Abstractions 8.0.3 and Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2

Copyright (c) .NET Foundation and Contributors.
Copyright (c) Microsoft Corporation. All rights reserved.

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## .NET runtime

Self-contained distributions include the .NET runtime. Its license and third-party
notices are included in the distribution's `licenses/` directory.

## Windows Runtime projections

The Windows build uses Microsoft's .NET SDK targeting pack to call the PDF renderer supplied by Windows. The application includes `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll`. See the [targeting pack](https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref/10.0.19041.57), [Windows SDK terms](https://aka.ms/WinSDKLicenseURL), and [C#/WinRT source and license](https://github.com/microsoft/CsWinRT/blob/master/LICENSE).

## Fonts

The browser workspace uses Mozilla [PDF.js](https://github.com/mozilla/pdf.js) under Apache-2.0.
Its local distribution includes the PDF.js license and the supplied notices for character maps,
standard fonts, ICC profiles and image decoders under `editor/pdf-lib/`. No PDF scripting
engine or third-party hosted scripts are loaded.

The application uses installed Windows Segoe UI fonts. PDFsharp embeds the used
glyphs in generated PDFs. Windows font files are not bundled with Inklane.

## Windows setup

The installer and uninstaller use NSIS 3.13 and its zlib compressor, copyright
1999–2026 NSIS contributors. These components use the zlib/libpng license. The
installed `licenses/nsis-COPYING.txt` file contains the upstream notices. See the
[NSIS license](https://nsis.sourceforge.io/License).


## Markdig 1.6.0 — BSD-2-Clause

Source: https://raw.githubusercontent.com/xoofx/markdig/1.6.0/license.txt

Copyright (c) 2016-2026, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification
, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## WpfMath bundled fonts

## Computer Modern

This software contains TTF files of Computer Modern font by Donald E. Knuth:
- `cmex10.ttf`
- `cmmi10.ttf`
- `cmr10.ttf`
- `cmsy10.ttf`

Original font sources may be [retrieved from CTAN][ctan.cm].

The files are distributed under the terms of the Knuth License, reproduced below.

> This software is copyrighted. Unlimited copying and redistribution of this package and/or its individual files are permitted as long as there are no modifications. Modifications, and redistribution of modifications, are also permitted, but only if the resulting package and/or files are renamed.

[ctan.cm]: https://ctan.org/tex-archive/fonts/cm

## `jlm_msam10.ttf`

This software contains the font file `jlm_msam10.ttf` ([taken from JLaTeXMath project][jlatexmath.fonts]) that is licensed under the Open Font License, reproduced below.

```
Copyright (c) 1997, 2009, American Mathematical Society (http://www.ams.org).
All Rights Reserved.

"eufb10" is a Reserved Font Name for this Font Software.
"eufm10" is a Reserved Font Name for this Font Software.
"msam10" is a Reserved Font Name for this Font Software.
"msbm10" is a Reserved Font Name for this Font Software.

This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
http://scripts.sil.org/OFL

-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
   in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
   redistributed and/or sold with any software, provided that each copy
   contains the above copyright notice and this license. These can be
   included either as stand-alone text files, human-readable headers or
   in the appropriate machine-readable metadata fields within text or
   binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
   Name(s) unless explicit written permission is granted by the corresponding
   Copyright Holder. This restriction only applies to the primary font name as
   presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
   Software shall not be used to promote, endorse or advertise any
   Modified Version, except to acknowledge the contribution(s) of the
   Copyright Holder(s) and the Author(s) or with their explicit written
   permission.

5) The Font Software, modified or unmodified, in part or in whole,
   must be distributed entirely under this license, and must not be
   distributed under any other license. The requirement for fonts to
   remain under this license does not apply to any document created
   using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
```

[jlatexmath.fonts]: https://github.com/opencollab/jlatexmath/tree/af77a8e80d41ff67dfe2f42f14b41f6860dfeeec/jlatexmath/src/main/resources/org/scilab/forge/jlatexmath/fonts/maths


## WpfMath and XamlMath.Shared 2.1.0

Source: https://raw.githubusercontent.com/ForNeVeR/xaml-math/d6f576345998777dcf7ad112da1add9bd7ad9674/LICENSE.md

The MIT License
===============

Copyright (c) 2010 Alex Regueiro

Copyright (c) 2023 WPF-Math Contributors

Copyright (c) 2023 XAML-Math Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
## Local rich-text rendering

The shared workspace bundles Marked 18.1.3 (MIT), DOMPurify 3.4.17
(Apache-2.0), KaTeX 0.19.1 (MIT), and Highlight.js 11.12.0 (BSD-3-Clause).
Their license notices are included under `editor/rich-lib/` in every workspace
build. KaTeX's bundled mathematical fonts are included with its MIT notice.
Rendering uses these local copies; no text is sent to a hosted rendering service.
