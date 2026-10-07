# Komponenten, Herkunft und Veröffentlichungsumfang

## GitHub-Download

Das öffentliche Setup-ZIP enthält **MonoClip und die selbstenthaltene .NET-Runtime**, aber **keine OBS-, FFmpeg-, x264-, Capture-Plugin- oder Qt-Binärdateien**. `Runtime einrichten.cmd` / `install-runtime.ps1` lädt die benötigten unveränderten Bibliotheken direkt vom offiziellen OBS-Release für den jeweiligen Benutzer und prüft das komplette Archiv vor dem Entpacken gegen den gepinnten SHA256-Wert.

Es wird kein vollständiger lizenzrechtlicher Audit oder vollständiges Corresponding-Source-Paket aller nativen OBS-Abhängigkeiten behauptet. Lokale Recherche-/Teilarchive sind nicht Bestandteil des öffentlichen Repositories. Ein späteres Paket mit mitgelieferten nativen Binärdateien benötigt einen gesonderten vollständigen Quellen-/Hinweissatz.

## MonoClip

MonoClips eigener Quellcode: **GPL-2.0-or-later**, siehe [LICENSE](LICENSE). Das bezeichnet nicht eine GPLv2-only-Lizenz für jede mögliche Kombination mit externen Bibliotheken.

## .NET / Windows Forms

.NET 10 und Windows Forms werden von Microsoft und den .NET-Mitwirkenden unter ihren ursprünglichen Lizenzbedingungen bereitgestellt. Lizenz- und Drittanbietertexte der verwendeten Runtime liegen in `licenses/DOTNET-LICENSE.txt` und `licenses/DOTNET-THIRD-PARTY-NOTICES.txt`. Copyright- und Autorenhinweise werden unverändert beibehalten.

Upstream: https://github.com/dotnet/runtime und https://github.com/dotnet/winforms. AMD-/NVIDIA-Treiber sind nicht im Paket.

## Extern bezogene native Runtime

- Version: **OBS Studio 32.2.2**, Windows x64.
- Release: https://github.com/obsproject/obs-studio/releases/tag/32.2.2
- Archiv: https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Windows-x64.zip
- Archiv-SHA256: `4d6e40e3ab155f56b30de517380566a206d74b63cdf5ad49aa596924768f97e1`.
- OBS-Quellsnapshot: https://github.com/obsproject/obs-studio/tree/ba2f32bdf791005443988a4955e963663e16b1ed
- Offizielles OBS-Quellarchiv: https://github.com/obsproject/obs-studio/releases/download/32.2.2/OBS-Studio-32.2.2-Sources.tar.gz
- Dependency-Rezepte: https://github.com/obsproject/obs-deps/tree/8683107a02300923abe4f293920f4b5edc8cb624

OBS/libobs und Plugins sind grundsätzlich GPL-2.0-or-later. Der konkrete mit OBS veröffentlichte **FFmpeg-Build meldet GPL-3.0-or-later** und Version `n8.1.2`; seine Lizenz darf nicht pauschal als LGPL oder GPLv2-only beschrieben werden. FFmpeg-Quellpin: https://github.com/FFmpeg/FFmpeg/tree/38b88335f99e76ed89ff3c93f877fdefce736c13. Weitere Abhängigkeiten behalten ihre eigenen ursprünglichen Bedingungen.

Das Setup verwendet nur die benötigten Capture-/Audio-/Mux-/Encoder-Module und Daten. Frontend, Qt, Browser-Plugin und Python-/Lua-Integrationen werden nicht installiert. Es verändert keine Upstream-Binärdateien. Einstellungen oder Clips werden nicht an OBS oder GitHub übertragen.

## Build und Weitergabe

`build.ps1` lädt die Runtime ebenfalls vom offiziellen Upstream für den lokalen Build. Ein lokal gebautes Komplettpaket ist nicht automatisch eine vollständig geprüfte native Binärdistribution. Wer OBS- oder andere native Bibliotheken selbst weiterverbreitet, muss deren Lizenz-, Hinweis- und vollständige Quellenpflichten gesondert erfüllen. Die oben genannten Herkunftslinks sind kein vollständiges schriftliches Quellenangebot.
