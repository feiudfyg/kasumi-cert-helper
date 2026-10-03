# Kasumi Cert Helper

A certificate and OpenPGP toolbox for Windows: manage the Windows certificate stores, run your own
X.509 workbench, and handle OpenPGP keys in process — no `gpg.exe`, no command line.

## Features

| Page | What it does |
| --- | --- |
| **Certificate stores** | Browse, import, export and delete certificates in the current user and local machine stores, with per-store counts and parsed details |
| **X.509 certificates** | Own `.kdb` database: keys, self-signed certificates, CAs, CSRs and signing, with SAN / EKU / CRL / SKI / … extensions |
| **OpenPGP** | Generate, import and export keys, encrypt, decrypt, sign and verify — BouncyCastle in process, no GnuPG installation |
| **Settings** | Interface language, elevation, data locations, licence and source code |

- Interface in **English and 简体中文**, follows the Windows display language.
- X.509 private keys are encrypted with the database password (PKCS#8 PBES2, AES-256-CBC).
- OpenPGP keys are ASCII armored files, so GnuPG can read and import them directly.
- Resizable columns and panes, remembered per user; parsed output instead of raw dumps.

Not implemented, by design: smart cards, `gpg-agent`/pinentry integration and keyserver (HKP) access.

## Requirements

Windows 10 version 1809 (build 17763) or newer, x64. The release archive is self-contained — the
.NET runtime and the Windows App SDK are included, so nothing has to be installed.

## Download

Take `KasumiCertHelper-vX.Y.Z-win-x64.zip` from the
[releases page](https://github.com/feiudfyg/kasumi-cert-helper/releases), unpack it anywhere and run
`KasumiCertHelper.exe`. Managing machine-wide stores needs administrator rights; the settings page
can restart the application elevated.

## Build

The required .NET SDK is pinned in `global.json`.

```powershell
git clone https://github.com/feiudfyg/kasumi-cert-helper.git
cd kasumi-cert-helper
dotnet build KasumiCertHelper.slnx -c Release

# unit tests
dotnet test tests\KasumiCertHelper.Core.Tests\KasumiCertHelper.Core.Tests.csproj -c Release

# end-to-end UI tests (start the real window)
dotnet test tests\KasumiCertHelper.UiTests\KasumiCertHelper.UiTests.csproj -c Release

# self-contained build
dotnet publish src\KasumiCertHelper\KasumiCertHelper.csproj -c Release -r win-x64 `
  --self-contained true -o artifacts\win-x64-standalone
```

## Data locations

| What | Where |
| --- | --- |
| Settings, log | `%APPDATA%\KasumiCertHelper` |
| X.509 databases (default) | `%APPDATA%\KasumiCertHelper\databases` |
| OpenPGP keys | `%APPDATA%\KasumiCertHelper\pgp\keys` |

Set the `KASUMI_APPDATA` environment variable to keep all of it in one folder.

## Project layout

```
src/KasumiCertHelper.Core   models, services, localization — no UI dependency
src/KasumiCertHelper        WinUI 3 application
tests/KasumiCertHelper.Core.Tests   xUnit unit tests
tests/KasumiCertHelper.UiTests      FlaUI tests driving the real window
```

## Licence

GNU General Public License v3.0, see [LICENSE](LICENSE). Bundled libraries and their notices are
listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

# Kasumi 证书助手

Windows 上的证书与 OpenPGP 工具箱：管理 Windows 证书存储、自有的 X.509 数据库，并在进程内处理
OpenPGP 密钥 —— 不需要 `gpg.exe`，也不需要命令行。

- **证书存储**：查看、导入、导出、删除当前用户与本机证书存储中的证书，每个存储显示数量，详情经过解析。
- **X.509 证书**：自有 `.kdb` 数据库，支持密钥、自签名证书、CA、CSR 与签发，可设置 SAN / EKU / CRL / SKI 等扩展。
- **OpenPGP**：生成、导入导出密钥，加密、解密、签名、验签，全部在进程内完成。
- **设置**：界面语言、提权、数据位置、许可证与源代码。

界面提供**英文与简体中文**，默认跟随 Windows 显示语言。X.509 私钥使用数据库口令加密（PKCS#8
PBES2、AES-256-CBC）；OpenPGP 密钥为 ASCII armor 文件，GnuPG 可直接导入。

未实现（有意取舍）：智能卡、`gpg-agent`/pinentry 集成、密钥服务器（HKP）。

下载：在 [Releases](https://github.com/feiudfyg/kasumi-cert-helper/releases) 获取
`KasumiCertHelper-vX.Y.Z-win-x64.zip`，解压后运行 `KasumiCertHelper.exe` 即可（自带 .NET 运行时与
Windows App SDK）。管理本机证书存储需要管理员权限，可在设置页以管理员身份重启。

构建方式见上，需要 `global.json` 指定的 .NET SDK；构建产物可设 `KASUMI_APPDATA` 环境变量集中存放数据。
