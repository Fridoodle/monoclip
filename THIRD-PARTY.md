# Third-party components

## OBS Studio libraries

The portable ZIP contains an unmodified subset of the official **OBS Studio 32.2.2** Windows x64 release: libobs, the capture, audio, encoder and mux modules, and the FFmpeg and other libraries shipped with them. The OBS frontend, Qt, browser source and scripting are not included.

- Release: https://github.com/obsproject/obs-studio/releases/tag/32.2.2
- Archive: https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64.zip
- SHA256: `4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1`
- Source: https://github.com/obsproject/obs-studio/tree/ba2f32bdf791005443988a4955e963663e16b1ed
- Dependencies: https://github.com/obsproject/obs-deps/tree/8683107a02300923abe4f293920f4b5edc8cb624

OBS and its plugins are GPL-2.0-or-later. The bundled FFmpeg build (`n8.1.2`, https://github.com/FFmpeg/FFmpeg/tree/38b88335f99e76ed89ff3c93f877fdefce736c13) is GPL-3.0-or-later. Other libraries keep their own licenses. Each release includes the official OBS source archive as an asset. The package lists the origin in `info/OBS-ORIGIN.json` and the OBS license in `info/licenses/OBS-LICENSE-gplv2.txt`.

## .NET

The self-contained .NET 10 runtime and Windows Forms are provided by Microsoft under their own terms (`info/licenses/DOTNET-LICENSE.txt`, `info/licenses/DOTNET-THIRD-PARTY-NOTICES.txt`). Source: https://github.com/dotnet/runtime, https://github.com/dotnet/winforms.

## cloudflared

Not included in the ZIP. For sharing, MonoClip downloads the official **cloudflared 2026.9.3** (`cloudflared-windows-amd64.exe`) from https://github.com/cloudflare/cloudflared/releases/tag/2026.9.3 and runs it only if its SHA256 is `f096265ec2fcbe9bb6e2d64268db167ced3fcbb83d894bdb9e2fcdb26f2ea7e2`. cloudflared is Apache-2.0 licensed. Quick tunnels (trycloudflare.com) are subject to Cloudflare's terms.

## MonoClip

MonoClip's own code is GPL-2.0-or-later, see [LICENSE](LICENSE).

If you redistribute a package that contains OBS or other native libraries, you are responsible for meeting their license and source requirements.
