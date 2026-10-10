# MonoClip

**Native Windows-Clips ohne dauerhaft geöffnetes Aufnahmefenster.** MonoClip hält die letzten Sekunden als komprimierten GPU-Replay-Puffer im RAM und speichert sie per Hotkey. Die App läuft im Tray; die Einstellungen sind schlicht und schwarz-weiß.

> **Beta für Windows 11 x64.** Hardware-Encoding über AMD AMF oder NVIDIA NVENC. AMD ist auf echter Hardware geprüft; NVIDIA ist implementiert, aber noch nicht hardwaregetestet. Intel-only, CPU-Encoding und HDR werden derzeit nicht unterstützt.

## Funktionen

- **Aufgenommen wird das Fenster unter der Maus:** das gehookte Spiel, sonst nur das jeweilige App-Fenster (z. B. Discord statt des Spiels im Hintergrund), über Desktop/Taskleiste der ganze Monitor.
- Automatische Vollbild-/Borderless-Spielaufnahme; der Spiel-Hook bleibt beim Wegtabben aktiv, das Zurückwechseln ist sofort.
- GPU-Aufnahme: Spiel-Hook, Fenster über Windows Graphics Capture, Monitor über DXGI mit WGC-Fallback.
- 720p / 1080p / 1440p / 2160p und 30 / 60 / 120 FPS, soweit die GPU die Kombination unterstützt.
- Automatisch ermittelte Bitrate mit Qualitätsregler (Performance / Ausgewogen / Qualität), geschätzte Dateigröße pro Clip, begrenzter RAM-Puffer und 5–300 Sekunden Cliplänge.
- Konfigurierbarer globaler Hotkey, standardmäßig **Ctrl+Shift+F9**. Windows-Hotkey plus ereignisgesteuerter Raw-Input-Zustellung, ohne Dauer-Polling.
- Separate Desktop-/Mikrofonspuren und zusätzlicher Wiedergabe-Mix.
- Abschaltbarer kurzer Beep nach erfolgreichem Speichern, keine Erfolgs-Popups.
- Optionaler Windows-Autostart, immer minimiert; der Puffer startet standardmäßig mit der App.
- **Tray → Clip-Ordner öffnen** springt im Explorer direkt zum zuletzt gespeicherten Clip.
- **Letzten Clip teilen** (Tray oder Einstellungsfenster): Link für Discord & Co., standardmäßig 15 Minuten (einstellbar 5–30), direkt von deinem PC gestreamt, ohne Portfreigabe, Konto oder Adminrechte. Danach automatisch offline.
- Tray-Symbol: schwarze Kachel mit gefülltem Punkt = Puffer nimmt auf, mit Ring = gestoppt.
- Spielordner als Clip-Index. **Keine JSON-Begleitdateien neben neuen Clips.**
- Kein Konto, Cloud-Upload, Telemetrie, Electron oder WebView.

## Download und Start

Aktuelle Version: **[GitHub Releases](https://github.com/Fridoodle/monoclip/releases)** → `MonoClip-…-win-x64-portable.zip`.

Portabel, **ein einziger Download**, keine Installation, **keine Adminrechte**: Die Aufnahme-Runtime (unveränderte OBS-Bibliotheken) und .NET sind bereits enthalten. Kein Setup-Skript, kein zweiter Download, keine Internetverbindung beim Start.

1. ZIP in einen beliebigen Ordner entpacken, z. B. `Dokumente\MonoClip` oder einen USB-Stick. Für Updates MonoClip vorher beenden und die neue Version in einen neuen Ordner entpacken.
   Im Ordner liegen nur `MonoClip.exe` und die Ordner `app` (Programm, .NET, OBS-Runtime) und `info` (Anleitung, Änderungen, Lizenzen). `MonoClip.exe` ist ein kleiner Starter für `app\MonoClip.exe`: OBS erwartet seine Hilfsprogramme neben der laufenden EXE, deshalb bleiben sie gemeinsam in `app`.
2. `MonoClip.exe` starten. Bei einer frischen Konfiguration startet die App nur im Tray, der Replay-Puffer läuft sofort.
3. Rechtsklick auf das Schwarz-Weiß-Symbol → **Einstellungen**, **Puffer stoppen** oder **Clip-Ordner öffnen**.
4. Für einen vollständigen Clip die eingestellte Cliplänge plus wenige Sekunden warten, dann den Hotkey drücken.

Deinstallieren: Ordner löschen (vorher ggf. „Mit Windows starten“ ausschalten). Einstellungen und Logs liegen unter `%LOCALAPPDATA%\MonoClip`.

Fenster schließen/minimieren versteckt die Einstellungen. Wirklich beenden: **Tray → Beenden**. `MonoClip.exe --settings` zeigt beim ersten Start direkt die Einstellungen. Eine zweite Instanz wird verhindert.

### Einstellungen

Standardmäßig zeigt das Fenster nur die wichtigsten Optionen. **Erweiterte Einstellungen** blendet den Rest ein; die Wahl wird sofort gespeichert.

| Option | Modus | Bedeutung |
|---|---|---|
| Cliplänge | Einfach | Darunter steht die geschätzte Dateigröße pro Clip |
| Auflösung, Bilder pro Sekunde | Erweitert | Standard 1920 × 1080 bei 60 FPS |
| Hotkey | Einfach | Feld auswählen und die gewünschte Kombination drücken |
| Mit Windows starten | Einfach | Autostart nur für den aktuellen Benutzer, immer minimiert |
| Speicherordner, Ordner öffnen | Einfach | Ordner öffnen markiert den neuesten Clip |
| Qualität · Bitrate | Erweitert | Performance (kleinere Dateien), Ausgewogen, Qualität (schärfer in schnellen Szenen) |
| Desktop-Audio, Mikrofon | Erweitert | Separate Spuren plus Wiedergabe-Mix |
| Kurzer Beep bei gespeichertem Clip | Erweitert | Abschaltbarer Erfolgston, standardmäßig aktiviert |
| Nur im Tray starten | Erweitert | Kein Einstellungsfenster beim Start |
| Replay-Puffer beim App-Start aktivieren | Erweitert | Standardmäßig an; unabhängig vom Windows-Autostart |
| Nach Spiel, Liste aktualisieren | Erweitert | Clip-Ordner eines Spiels öffnen |
| Link teilen · Minuten online | Erweitert | 5–30 Minuten, Standard 15 |

Die Bitrate wird aus Auflösung, FPS und Qualitätsstufe berechnet. Mehr Pixel und mehr Bilder kosten mehr Bits, aber nicht proportional, weil H.264 bei höherer Auflösung und Bildrate effizienter wird. Referenz: Ausgewogen 1080p60 = 15 Mbit/s; Performance ×0,6, Qualität ×1,6; begrenzt auf 2,5–100 Mbit/s. Die Größenschätzung enthält alle Audiospuren und etwas Container-Overhead; ruhige Inhalte werden meist etwas kleiner.

| Ausgewogen | 30 FPS | 60 FPS | 120 FPS |
|---|---|---|---|
| 720p | 4,5 | 7,5 | 12,5 Mbit/s |
| 1080p | 9 | 15 | 25 Mbit/s |
| 1440p | 14,5 | 24,5 | 41 Mbit/s |
| 2160p | 29 | 48,5 | 82 Mbit/s |

**Übernehmen** speichert Änderungen. Einstellungen liegen in `%LOCALAPPDATA%\MonoClip\settings.json`. Updates behalten sie und deine Clips. Bei einem anderen Programmordner den Windows-Autostart einmal aus- und wieder einschalten.

## Dateien und Audio

```text
Videos/MonoClip/
├── Desktop/
│   └── Zeitstempel_zufallskennung.mkv
└── Spiel-EXE-Name/
    └── Zeitstempel_zufallskennung.mkv
```

Die Zuordnung benutzt das tatsächlich gehookte Spiel, nicht beliebige Fenstertitel. Aufnahmen normaler App-Fenster (Discord, Browser …) landen unter `Desktop`. Neue Clips bestehen ausschließlich aus **MKV-Dateien**. JSON-Begleitdateien älterer Versionen werden nicht mehr benötigt und können entfernt werden. Die Bibliothek wird nur beim Öffnen oder auf Wunsch aktualisiert; `.pending` erscheint nicht als Spiel.

Audio verwendet die Windows-Standardausgabe und das Windows-Standardmikrofon:

1. **Playback mix:** gemischte Wiedergabe.
2. **Desktop:** separate Desktopspur, sofern aktiviert.
3. **Microphone:** separate Mikrofonspur, sofern aktiviert. Bei deaktiviertem Desktop ist sie die zweite vorhandene Spur.

Windows muss Desktop-Apps den Mikrofonzugriff erlauben. Sind beide Quellen deaktiviert, bleibt nur eine technisch notwendige stumme AAC-Spur; es wird keine Audioquelle geöffnet. Der Beep läuft über die Standardausgabe und kann in einem späteren Desktop-Audio-Clip enthalten sein.

MKV lässt sich etwa mit VLC abspielen. Manche Upload-Dienste/Editoren verlangen MP4; dafür ist ein verlustfreies Remux erforderlich. Ein Editor oder Remux-Dialog ist nicht eingebaut (beim Teilen remuxt MonoClip intern eine temporäre MP4-Kopie).

## Clip teilen (Standard 15 Minuten)

Rechtsklick auf das Tray-Symbol → **Letzten Clip teilen · 15 Min.**, oder im Einstellungsfenster oben **Letzten Clip teilen**. Dort gibt es während des Teilens auch **Link kopieren** und die Restzeit. Die Dauer lässt sich in den erweiterten Einstellungen zwischen 5 und 30 Minuten wählen. Benachrichtigungen zeigen beim Start, wie lange das Erstellen dauert, danach bis wann der Link online ist, und am Ende, dass er offline ist. MonoClip packt den Clip verlustfrei in eine MP4 um (spielt im Browser und in Discord), öffnet einen Cloudflare-Quick-Tunnel und kopiert den Link in die Zwischenablage. In Discord einfügen – Freunde schauen den Clip im Browser bzw. im Discord-Player.

- **Kein Port-Forwarding, kein Konto, keine Adminrechte.** Der Clip wird nur auf `127.0.0.1` bereitgestellt; cloudflared baut eine ausgehende Verbindung zu Cloudflare auf. Das funktioniert auch hinter DS-Lite/CGNAT, wo eine eigene öffentliche IPv4 gar nicht existiert. Deine IP-Adresse steht nicht im Link.
- **Nach Ablauf der Zeit** (oder **Teilen beenden**, oder beim Beenden von MonoClip) werden Tunnel, Server und die MP4-Kopie entfernt. Auch bei einem Absturz endet der Tunnel mit MonoClip. Höchstens 6 gleichzeitige Verbindungen, damit die eigene Upload-Leitung nicht zugestopft wird; jeder Zuschauer lädt den Clip aber von deinem Anschluss.
- **Beim ersten Teilen** wird einmalig das offizielle `cloudflared` (Cloudflare, Version 2026.9.3, ca. 55 MB) von GitHub geladen, per SHA256 geprüft und unter `%LOCALAPPDATA%\MonoClip\tools` abgelegt. Eine abweichende Datei wird nie ausgeführt.
- Der Link enthält einen zufälligen Pfad; nur genau dieser Clip ist erreichbar. Wer den Link hat, kann den Clip in dieser Zeit ansehen und herunterladen. Discord kann Vorschau/Video für eine Weile zwischenspeichern.
- Cloudflare nennt Quick Tunnels eine Test-Funktion ohne Verfügbarkeitsgarantie und begrenzt, wie viele Links ein Anschluss in kurzer Zeit erstellen darf. MonoClip prüft den Link vor dem Kopieren und meldet sich, falls Cloudflare ihn noch nicht ausliefert oder bremst.
- Discord zeigt einen eingebetteten Player laut Nutzerberichten nur für Videos bis etwa 100 MB; größere Clips (lange 4K-/Qualitäts-Clips) öffnen sich im Browser.

## Grenzen und Fehlerbehebung

- **Vollbild / Anti-Cheat:** Verwendet den normalen OBS-Hook. Anti-Cheat kann ihn blockieren; Schutzfunktionen werden nicht umgangen. Ein eigenes D3D11-Borderless-Testspiel wurde geprüft; exklusives Vollbild ließ sich auf dem Testtreiber nicht zuverlässig simulieren. Mit deinem Spiel prüfen.
- **Anlaufphase:** Direkt nach dem Start ist der Puffer noch nicht gefüllt; es kann nur bereits aufgenommene Zeit gespeichert werden.
- **Dauer:** Verlustfreie H.264-Speicherung beginnt an Keyframes. Clips können ungefähr eine Sekunde länger sein.
- **GPU-Grenzen:** 4K120 ist nicht zugesichert. Kein heimlicher CPU-Fallback. 1440p/4K wurden noch nicht vollständig praktisch getestet.
- **Schwarzes Bild:** Geschützte Inhalte, Sicherheitsdialoge und manche Spiel-/Treiberkombinationen sind nicht aufnehmbar. Treiber aktualisieren und Borderless testen; keine Schutzfunktionen deaktivieren.
- **`.pending`:** Bei Unterbrechungen/Exportfehlern bleiben vorhandene Aufnahmen hier erhalten. Sie werden nicht automatisch gelöscht. Ein nie gespeicherter RAM-Puffer geht beim Beenden verloren.
- **Hotkey belegt:** Eine andere Kombination wählen. Seit 0.1.3 ergänzt Raw Input die normale Windows-Zustellung für Spiele, die Hotkey-Meldungen verschlucken. Die App bleibt ohne Adminrechte; Anti-Cheat-Einschränkungen werden nicht umgangen. Beim Ändern der Kombination im sichtbaren Eingabefeld wird kein Clip ausgelöst.
- **Fehlende DLL / EntryPoint / „Aufnahme-Runtime unvollständig“:** Das komplette aktuelle ZIP in einen neuen Ordner entpacken, nicht nur einzelne Dateien kopieren, und keine fremden OBS-DLLs darüberkopieren. Manche Virenscanner verschieben einzelne DLLs in Quarantäne.
- **Mikrofon nicht hörbar:** Standardgerät, Berechtigung und Lautstärke prüfen; im Player die richtige Spur auswählen.

Logs: `%LOCALAPPDATA%\MonoClip`. Sie können Geräte-/Prozessangaben enthalten; vor dem Teilen persönliche Daten prüfen. Freier Speicher wird vor Aufnahmebeginn geprüft, aber nicht dauerhaft garantiert.

## Ressourcen und Teststand

Ein Desktop-Test bei **1080p60, 30 Sekunden Replay und beiden Audioquellen** ergab etwa **0,65 % Gesamt-CPU und 293 MiB Working Set** auf einem Ryzen AI 9 HX 370 / Radeon 890M mit 24 logischen CPUs. Das ist eine Stichprobe, keine Garantie für andere Geräte oder Spiel-FPS. GPU-/VRAM-Last bei vollständig ausgelasteten Spielen wurde noch nicht gemessen.

Tests decken Einstellungen, Hotkeys, Tray, native Exports, echte Clips, schnelle Mehrfach-Saves, Wiederanlauf und Shutdown während Speichern ab. Medien werden per ffprobe und tatsächlicher Dekodierung geprüft, nicht nur auf Dateiexistenz.

## Selbst bauen

Benötigt: **Windows 11 x64, .NET SDK 10, Python 3.11+** und beim ersten Vorbereiten der Runtime Internetzugang.

```powershell
git clone https://github.com/Fridoodle/monoclip.git
cd monoclip
.\build.ps1
```

Ergebnis: `dist/MonoClip/MonoClip.exe` (Starter, `src/MonoClip.Launcher`; mit Visual-Studio-Build-Tools als NativeAOT-EXE mit ca. 1,3 MB, sonst als Single-File-EXE mit ca. 10,7 MB) mit der App in `dist/MonoClip/app` und der fertige portable Download `dist/MonoClip-<Version>-win-x64-portable.zip`. `tools/prepare-runtime.py` lädt dafür einmalig das gepinnte offizielle OBS-Release (SHA256-geprüft) und übernimmt nur die benötigten Module. Das Paket ist selbstenthalten; zum Starten wird kein separat installiertes .NET benötigt.

Releases: Ein Tag `v<Version>` (passend zu `<Version>` in `MonoClip.Windows.csproj`) baut das ZIP in der Windows-CI, prüft einen echten Start aus einem frisch entpackten Ordner und veröffentlicht ZIP, `SHA256SUMS.txt` und das offizielle OBS-Quellarchiv als GitHub-Release.

### Tests

```powershell
dotnet run --project tests/MonoClip.Tests
dotnet run --project tests/MonoClip.UiTests
dotnet build tests/MonoClip.NativeTests -c Release
.\tests\MonoClip.NativeTests\bin\Release\net10.0-windows\MonoClip.NativeTests.exe --exports
.\tests\MonoClip.NativeTests\bin\Release\net10.0-windows\MonoClip.NativeTests.exe --capture
```

Native Tests als erzeugte **EXE**, nicht über `dotnet run`, ausführen: OBS erwartet den Mux-Helfer neben dem Prozessprogramm. Weitere Modi: `--properties`, `--monitor-alias`, `--restart`, `--timeout`, `--stale`, `--stop-save`, `--dispose-save`, `--export-drain`, `--rapid-save`, `--export-error`, `--target`, `--benchmark`, `--share-remux`. `--game` öffnet kurz ein eigenes Vollbild-Testfenster; nur bewusst ausführen. `--share-live` (nach `--game`) teilt den synthetischen Spiel-Testclip kurz über einen echten Cloudflare-Tunnel und prüft den öffentlichen Abruf; Cloudflare begrenzt die Anzahl neuer Tunnel pro Anschluss.

## Mitwirken

[Issues](https://github.com/Fridoodle/monoclip/issues): Windows-Version, GPU/Treiber, Spiel/Fenstermodus, MonoClip-Version, Einstellungen, genaue Meldung und minimale Reproduktionsschritte angeben. Keine privaten Clips, Tokens oder unbereinigten Logs hochladen.

Pull Requests sind willkommen. Capture-/Export-Änderungen benötigen Regressionstests. Siehe [CONTRIBUTING.md](CONTRIBUTING.md) und [CHANGELOG.md](CHANGELOG.md).

## Lizenz

MonoClip: **GPL-2.0-or-later**, siehe [LICENSE](LICENSE). OBS/libobs und weitere native Komponenten behalten ihre eigenen Lizenzen. Unveränderte OBS-Bibliotheken werden aus dem gepinnten offiziellen Release bezogen. Quellen und Hinweise: [THIRD-PARTY.md](THIRD-PARTY.md).
