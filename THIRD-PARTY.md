# Komponenten, Herkunft und Veröffentlichungsumfang

## GitHub-Download

Das portable ZIP enthält **MonoClip, die selbstenthaltene .NET-Runtime und eine Teilmenge unveränderter Binärdateien aus dem offiziellen OBS-Studio-32.2.2-Release** (libobs, Capture-/Audio-/Encoder-/Mux-Module sowie die damit ausgelieferten FFmpeg-, x264- und weiteren Bibliotheken). Qt, Frontend, Browser-Plugin und Python-/Lua-Integration sind nicht enthalten. Das OBS-Archiv wird beim Bauen gegen den gepinnten SHA256-Wert geprüft; Herkunft und Prüfsumme stehen im Paket in `OBS-ORIGIN.json`, die OBS-Lizenz in `licenses/OBS-LICENSE-gplv2.txt`.

Jedes Release führt neben dem ZIP das offizielle, unveränderte OBS-Quellarchiv (`OBS-Studio-32.2.2-Sources.tar.gz`) als Asset. Die Quellen der übrigen nativen Abhängigkeiten sind über die unten genannten gepinnten Upstream-Rezepte referenziert. Es wird kein vollständiger lizenzrechtlicher Audit aller nativen Abhängigkeiten behauptet.

## MonoClip

MonoClips eigener Quellcode: **GPL-2.0-or-later**, siehe [LICENSE](LICENSE). Das bezeichnet nicht eine GPLv2-only-Lizenz für jede mögliche Kombination mit externen Bibliotheken.

## .NET / Windows Forms

.NET 10 und Windows Forms werden von Microsoft und den .NET-Mitwirkenden unter ihren ursprünglichen Lizenzbedingungen bereitgestellt. Lizenz- und Drittanbietertexte der verwendeten Runtime liegen in `licenses/DOTNET-LICENSE.txt` und `licenses/DOTNET-THIRD-PARTY-NOTICES.txt`. Copyright- und Autorenhinweise werden unverändert beibehalten.

Upstream: https://github.com/dotnet/runtime und https://github.com/dotnet/winforms. AMD-/NVIDIA-Treiber sind nicht im Paket.

## cloudflared (nur beim Teilen, nicht im ZIP)

Für „Letzten Clip teilen“ lädt MonoClip beim ersten Teilen das unveränderte offizielle Programm **cloudflared 2026.9.3** (`cloudflared-windows-amd64.exe`) von https://github.com/cloudflare/cloudflared/releases/tag/2026.9.3 und führt es nur aus, wenn der SHA256 `f096265ec2fcbe9bb6e2d64268db167ced3fcbb83d894bdb9e2fcdb26f2ea7e2` stimmt. cloudflared steht unter der **Apache License 2.0** (Cloudflare, Inc.); Quellcode im genannten Repository. Es ist nicht Teil des MonoClip-Pakets. Die Nutzung der Quick Tunnels (trycloudflare.com) unterliegt den Bedingungen von Cloudflare.

## Extern bezogene native Runtime

- Version: **OBS Studio 32.2.2**, Windows x64.
- Release: https://github.com/obsproject/obs-studio/releases/tag/32.2.2
- Archiv: https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64.zip
- Archiv-SHA256: `4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1`.
- OBS-Quellsnapshot: https://github.com/obsproject/obs-studio/tree/ba2f32bdf791005443988a4955e963663e16b1ed
- Offizielles OBS-Quellarchiv: https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Sources.tar.gz
- Dependency-Rezepte: https://github.com/obsproject/obs-deps/tree/8683107a02300923abe4f293920f4b5edc8cb624

OBS/libobs und Plugins sind grundsätzlich GPL-2.0-or-later. Der konkrete mit OBS veröffentlichte **FFmpeg-Build meldet GPL-3.0-or-later** und Version `n8.1.2`; seine Lizenz darf nicht pauschal als LGPL oder GPLv2-only beschrieben werden. FFmpeg-Quellpin: https://github.com/FFmpeg/FFmpeg/tree/38b88335f99e76ed89ff3c93f877fdefce736c13. Weitere Abhängigkeiten behalten ihre eigenen ursprünglichen Bedingungen.

Das Paket enthält nur die benötigten Capture-/Audio-/Mux-/Encoder-Module und Daten. Frontend, Qt, Browser-Plugin und Python-/Lua-Integrationen sind nicht enthalten. Upstream-Binärdateien werden nicht verändert. Einstellungen oder Clips werden nicht an OBS oder GitHub übertragen.

## Build und Weitergabe

`build.ps1` bzw. `tools/prepare-runtime.py` lädt die Runtime vom offiziellen Upstream und übernimmt nur die benötigten Dateien. Wer ein eigenes Paket mit OBS- oder anderen nativen Bibliotheken weiterverbreitet, muss deren Lizenz-, Hinweis- und Quellenpflichten selbst erfüllen. Die oben genannten Herkunftslinks sind kein vollständiges schriftliches Quellenangebot.
