# Third party notices

Kasumi Cert Helper itself is distributed under the GNU General Public License
version 3 (see `LICENSE`). The following third party components are used
unmodified as binary dependencies:

| Component | Version | License |
| --- | --- | --- |
| [BouncyCastle.Cryptography](https://www.bouncycastle.org/csharp/) | 2.7.0-kasumi.1 | MIT (Bouncy Castle Licence) |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT |
| [Microsoft Windows App SDK](https://github.com/microsoft/WindowsAppSDK) | 1.8.260921001 | Microsoft Software License Terms (MIT based) |
| [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools) | 10.0.26100.4654 | Build time only, Microsoft Software License Terms |

MIT licensed components are distributed under the following terms:

```
Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

BouncyCastle copyright: Copyright (c) 2000-2026 The Legion of the Bouncy Castle
Inc.

The BouncyCastle.Cryptography package is a patched fork. It adds support for
the GnuPG draft-v5 (crypto-refresh) OpenPGP format and is based on upstream
pull request #525 of bc-csharp. The fork remains under the Bouncy Castle
Licence (MIT); its source is available at
<https://github.com/feiudfyg/bc-csharp> (branch `kasumi-v5v6`) and the package
is vendored under `third_party/nuget` so the build does not depend on fetching
it.

No GnuPG binaries are bundled with this application or produced next to the
executable.
