# MonoClip

**Native Windows-Clips ohne dauerhaft geöffnetes Aufnahmefenster.** MonoClip hält die letzten Sekunden als komprimierten GPU-Replay-Puffer im RAM und speichert sie per Hotkey. Die App läuft im Tray; die Einstellungen sind schlicht und schwarz-weiß.

> **Beta für Windows 11 x64.** Hardware-Encoding über AMD AMF oder NVIDIA NVENC. AMD ist auf echter Hardware geprüft; NVIDIA ist implementiert, aber noch nicht hardwaregetestet. Intel-only, CPU-Encoding und HDR werden derzeit nicht unterstützt.

## Funktionen

- Automatische Vollbild-/Borderless-Spielaufnahme und Desktop-Fallback.
- Der Monitor folgt dem aktiven Fenster – keine manuelle Bildschirmauswahl.
- GPU-Aufnahme über DXGI; bei Startproblemen Windows Graphics Capture.
- 720p / 1080p / 1440p / 2160p und 30 / 60 / 120 FPS, soweit die GPU die Kombination unterstützt.
- Automatische Bitrate, begrenzter RAM-Puffer und 5–300 Sekunden Cliplänge.
- Konfigurierbarer globaler Hotkey, standardmäßig **Ctrl+Shift+F9**. Windows-Hotkey plus ereignisgesteuerter Raw-Input-Zustellung, ohne Dauer-Polling.
- Separate Desktop-/Mikrofonspuren und zusätzlicher Wiedergabe-Mix.
- Abschaltbarer kurzer Beep nach erfolgreichem Speichern, keine Erfolgs-Popups.
- Optionaler Windows-Autostart, immer minimiert; automatisches Aktivieren des Puffers separat einstellbar.
- Spielordner als Clip-Index. **Keine JSON-Begleitdateien neben neuen Clips.**
- Kein Konto, Cloud-Upload, Telemetrie, Electron oder WebView.

## Download und Start

Aktuelle Pakete: **[GitHub Releases](https://github.com/Fridoodle/monoclip/releases)**.

1. MonoClip vor einem Update beenden.
2. Das komplette Paket in einen neuen Ordner entpacken. Keine einzelnen EXE-/DLL-Dateien verschiedener Versionen mischen.
3. Falls dem Paket **Runtime einrichten.cmd** beiliegt, diese einmal ausführen. Sie lädt die unveränderten OBS-Bibliotheken direkt vom offiziellen Release und prüft deren SHA256. Danach ist für die Aufnahme keine Internetverbindung nötig.
4. `MonoClip.exe` starten. Bei einer frischen Konfiguration startet die App nur im Tray und mit gestopptem Puffer.
5. Rechtsklick auf das Schwarz-Weiß-Symbol → **Puffer starten** oder **Einstellungen**.
6. Für einen vollständigen Clip die eingestellte Cliplänge plus wenige Sekunden warten, dann den Hotkey drücken.

Fenster schließen/minimieren versteckt die Einstellungen. Wirklich beenden: **Tray → Beenden**. `MonoClip.exe --settings` zeigt beim ersten Start direkt die Einstellungen. Eine zweite Instanz wird verhindert.

### Einstellungen

| Option | Bedeutung |
|---|---|
| Nur im Tray starten | Kein Einstellungsfenster beim Start |
| Mit Windows starten | Autostart nur für den aktuellen Benutzer |
| Replay-Puffer beim App-Start aktivieren | Aufnahme automatisch starten; unabhängig vom Windows-Autostart |
| Kurzer Beep bei gespeichertem Clip | Abschaltbarer Erfolgston, standardmäßig aktiviert |
| Hotkey | Feld auswählen und die gewünschte Kombination drücken |

**Übernehmen** speichert Änderungen. Einstellungen liegen in `%LOCALAPPDATA%\MonoClip\settings.json`. Updates behalten sie und deine Clips. Bei einem anderen Programmordner den Windows-Autostart einmal aus- und wieder einschalten.

## Dateien und Audio

```text
Videos/MonoClip/
├── Desktop/
│   └── Zeitstempel_zufallskennung.mkv
└── Spiel-EXE-Name/
    └── Zeitstempel_zufallskennung.mkv
```

Die Zuordnung benutzt das tatsächlich gehookte Spiel, nicht beliebige Fenstertitel. Neue Clips bestehen ausschließlich aus **MKV-Dateien**. JSON-Begleitdateien älterer Versionen werden nicht mehr benötigt und können entfernt werden. Die Bibliothek wird nur beim Öffnen oder auf Wunsch aktualisiert; `.pending` erscheint nicht als Spiel.

Audio verwendet die Windows-Standardausgabe und das Windows-Standardmikrofon:

1. **Playback mix:** gemischte Wiedergabe.
2. **Desktop:** separate Desktopspur, sofern aktiviert.
3. **Microphone:** separate Mikrofonspur, sofern aktiviert. Bei deaktiviertem Desktop ist sie die zweite vorhandene Spur.

Windows muss Desktop-Apps den Mikrofonzugriff erlauben. Sind beide Quellen deaktiviert, bleibt nur eine technisch notwendige stumme AAC-Spur; es wird keine Audioquelle geöffnet. Der Beep läuft über die Standardausgabe und kann in einem späteren Desktop-Audio-Clip enthalten sein.

MKV lässt sich etwa mit VLC abspielen. Manche Upload-Dienste/Editoren verlangen MP4; dafür ist ein verlustfreies Remux erforderlich. Ein Editor oder Remux-Dialog ist nicht eingebaut.

## Grenzen und Fehlerbehebung

- **Vollbild / Anti-Cheat:** Verwendet den normalen OBS-Hook. Anti-Cheat kann ihn blockieren; Schutzfunktionen werden nicht umgangen. Ein eigenes D3D11-Borderless-Testspiel wurde geprüft; exklusives Vollbild ließ sich auf dem Testtreiber nicht zuverlässig simulieren. Mit deinem Spiel prüfen.
- **Anlaufphase:** Direkt nach dem Start ist der Puffer noch nicht gefüllt; es kann nur bereits aufgenommene Zeit gespeichert werden.
- **Dauer:** Verlustfreie H.264-Speicherung beginnt an Keyframes. Clips können ungefähr eine Sekunde länger sein.
- **GPU-Grenzen:** 4K120 ist nicht zugesichert. Kein heimlicher CPU-Fallback. 1440p/4K wurden noch nicht vollständig praktisch getestet.
- **Schwarzes Bild:** Geschützte Inhalte, Sicherheitsdialoge und manche Spiel-/Treiberkombinationen sind nicht aufnehmbar. Treiber aktualisieren und Borderless testen; keine Schutzfunktionen deaktivieren.
- **`.pending`:** Bei Unterbrechungen/Exportfehlern bleiben vorhandene Aufnahmen hier erhalten. Sie werden nicht automatisch gelöscht. Ein nie gespeicherter RAM-Puffer geht beim Beenden verloren.
- **Hotkey belegt:** Eine andere Kombination wählen. Seit 0.1.3 ergänzt Raw Input die normale Windows-Zustellung für Spiele, die Hotkey-Meldungen verschlucken. Die App bleibt ohne Adminrechte; Anti-Cheat-Einschränkungen werden nicht umgangen. Beim Ändern der Kombination im sichtbaren Eingabefeld wird kein Clip ausgelöst.
- **Fehlende DLL / EntryPoint:** Vollständiges aktuelles Paket verwenden, Runtime einrichten und keine fremden OBS-DLLs darüberkopieren.
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

Ergebnis: `dist/MonoClip/MonoClip.exe`. Das Paket ist selbstenthalten; zum Starten wird kein separat installiertes .NET-SDK benötigt.

### Tests

```powershell
dotnet run --project tests/MonoClip.Tests
dotnet run --project tests/MonoClip.UiTests
dotnet build tests/MonoClip.NativeTests -c Release
.\tests\MonoClip.NativeTests\bin\Release\net10.0-windows\MonoClip.NativeTests.exe --exports
.\tests\MonoClip.NativeTests\bin\Release\net10.0-windows\MonoClip.NativeTests.exe --capture
```

Native Tests als erzeugte **EXE**, nicht über `dotnet run`, ausführen: OBS erwartet den Mux-Helfer neben dem Prozessprogramm. Weitere Modi: `--properties`, `--monitor-alias`, `--restart`, `--timeout`, `--stale`, `--stop-save`, `--dispose-save`, `--export-drain`, `--rapid-save`, `--export-error`, `--target`, `--benchmark`. `--game` öffnet kurz ein eigenes Vollbild-Testfenster; nur bewusst ausführen.

## Mitwirken

[Issues](https://github.com/Fridoodle/monoclip/issues): Windows-Version, GPU/Treiber, Spiel/Fenstermodus, MonoClip-Version, Einstellungen, genaue Meldung und minimale Reproduktionsschritte angeben. Keine privaten Clips, Tokens oder unbereinigten Logs hochladen.

Pull Requests sind willkommen. Capture-/Export-Änderungen benötigen Regressionstests. Siehe [CONTRIBUTING.md](CONTRIBUTING.md) und [CHANGELOG.md](CHANGELOG.md).

## Lizenz

MonoClip: **GPL-2.0-or-later**, siehe [LICENSE](LICENSE). OBS/libobs und weitere native Komponenten behalten ihre eigenen Lizenzen. Unveränderte OBS-Bibliotheken werden aus dem gepinnten offiziellen Release bezogen. Quellen und Hinweise: [THIRD-PARTY.md](THIRD-PARTY.md).
