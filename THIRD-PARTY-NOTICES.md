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

## Fonts

The application uses installed Windows Segoe UI fonts. PDFsharp embeds the used
glyphs in generated PDFs. Windows font files are not bundled with Inklane.

## Windows setup

The installer and uninstaller use NSIS 3.13 and its zlib compressor, copyright
1999–2026 NSIS contributors. These components use the zlib/libpng license. The
installed `licenses/nsis-COPYING.txt` file contains the upstream notices. See the
[NSIS license](https://nsis.sourceforge.io/License).
