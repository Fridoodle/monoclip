# Änderungen

## 0.1.7 Beta

- Tray → **Letzten Clip teilen · 15 Min.**: verlustfreie MP4-Kopie mit vorgezogenem Index, Bereitstellung nur auf 127.0.0.1 und ein Cloudflare-Quick-Tunnel. Link landet in der Zwischenablage; ohne Portfreigabe, Konto oder Adminrechte, auch hinter DS-Lite/CGNAT.
- Nach 15 Minuten, per „Teilen beenden“ oder beim Beenden werden Tunnel, Server und Kopie entfernt; ein Job-Objekt beendet cloudflared auch bei einem Absturz. Maximal 6 parallele Verbindungen.
- cloudflared 2026.9.3 wird erst beim ersten Teilen von GitHub geladen und gegen einen festen SHA256 geprüft.
- Der Link wird vor dem Kopieren über IPv4- und IPv6-Edge geprüft, ohne den neuen Hostnamen vorzeitig im Windows-DNS-Cache als „nicht vorhanden“ zu speichern.
- Teilen auch im Einstellungsfenster neben „Puffer stoppen“/„Clip speichern“, mit „Link kopieren“ und Restzeit; Dauer in den erweiterten Einstellungen 5–30 Minuten (Standard 15). Benachrichtigungen nennen Wartezeit, Ablaufzeit und Ende.
- Eigenes App-Icon für EXE, Taskleiste und Fenster. Tray-Symbol ohne Eckklammern: schwarze Kachel mit Punkt (aufnehmend) bzw. Ring (gestoppt).
- Auflösung und Bildrate sind jetzt erweiterte Einstellungen (Standard 1080p60).
- Aufgeräumter Programmordner: nur noch `MonoClip.exe` (Starter, ca. 0,15 s) sowie `app\` und `info\` statt rund 300 losen Dateien. Autostart und `--settings` funktionieren unverändert.
- Deutlich kleiner: Download 71 → 54 MB, entpackt 298 → 150 Dateien. Nie referenzierte .NET-Assemblies und Debugger-/Crashdump-Dateien entfallen (ohne Code aus benutzten Assemblies zu schneiden); unbenutzte OBS-Teile (WebRTC, Lua, OpenGL, QSV-Test, Streaming-Plugin `obs-outputs`) werden nicht mehr ausgeliefert. Release-Starter per NativeAOT (ca. 1,1 MB).
- Effizienter im Betrieb: Einstellungen per JSON-Source-Generator, Fenster-/Monitor-Infos werden pro Fenster zwischengespeichert statt viermal pro Sekunde neu abgefragt, OBS-Log mit dauerhaft geöffnetem Writer, kein Hintergrund-GC-Thread.

## 0.1.6 Beta

- Aufnahme folgt dem Fenster unter der Maus: gehooktes Spiel, sonst nur das jeweilige App-Fenster (WGC), über Desktop/Taskleiste der Monitor unter der Maus. Ein Spiel im Hintergrund landet nicht mehr im Clip, wenn man z. B. in Discord ist.
- Spiel-Hook und Fensteraufnahme bleiben beim Umschalten aktiv; ein neues Fenster wird verdeckt vorgewärmt, damit kein schwarzes Bild entsteht. Menüs, Tooltips und Startmenü lösen keinen Wechsel aus.
- Einfache und erweiterte Einstellungen; einfach: Auflösung, FPS, Cliplänge, Hotkey, Autostart, Ordner.
- Qualitätsregler Performance / Ausgewogen / Qualität mit neu berechneter Bitrate (Ausgewogen 1080p60 = 15 statt 12 Mbit/s) und geschätzter Dateigröße pro Clip.
- Replay-Puffer startet bei neuen Konfigurationen standardmäßig mit der App. Bestehende Einstellungen bleiben unverändert.
- Tray → Clip-Ordner öffnen markiert den zuletzt gespeicherten Clip im Explorer.

## 0.1.5 Beta

- **Portabler Ein-Datei-Download:** Ein ZIP enthält App, .NET und die Aufnahme-Runtime. Entpacken, `MonoClip.exe` starten – kein `Runtime einrichten.cmd`, kein zweiter Download, keine Installation und keine Adminrechte.
- Runtime schlanker: nur benötigte OBS-Module, Sprachdateien nur Deutsch/Englisch (73 statt 388 Runtime-Dateien), keine .NET-Sprachordner außer Deutsch/Englisch.
- Klare Meldung, falls Dateien der Aufnahme-Runtime fehlen (z. B. unvollständig entpackt oder von einem Virenscanner entfernt).
- Einstellungsfenster funktioniert in jeder Größe: Texte umbrechen statt abgeschnitten zu werden, in schmalen Fenstern stehen Beschriftung und Feld untereinander, Mindestgröße 380 × 320, das Fenster öffnet nie größer als der Bildschirm und skaliert mit der Windows-Anzeigeskalierung.
- Schwarze Titelleiste statt weißer (Windows 11 exakt schwarz, Windows 10 dunkel) und dunkle Scrollleiste.
- CI baut das portable ZIP, prüft alle libobs-Exports gegen die gebündelte DLL und startet die App aus einem frisch entpackten Ordner mit Leerzeichen und Klammern im Pfad. Tags `v*` veröffentlichen das Release automatisch.

## 0.1.4 Beta

- CMD-Launcher übergibt keinen fehlerhaft gequoteten Ordnerpfad mehr; PowerShell verwendet den eigenen Skriptordner.
- Regressionstest reproduziert die echte CMD-/PowerShell-Argumentübergabe, auch bei Leerzeichen, Umlauten, Klammern und Ampersand im Pfad.
- Windows-CI prüft zusätzlich eine vollständige Runtime-Einrichtung über die CMD-Datei mit offiziellem Download und SHA256-Prüfung.
- Aufnahme und Hotkey-Fix aus 0.1.3 unverändert.

## 0.1.3 Beta

- Ereignisgesteuerter Raw-Input-Zustellweg ergänzt globale Windows-Hotkeys, ohne Dauer-Polling oder Adminrechte.
- Gemeinsame Wiederholungs-/Doppel-Auslösungssperre für beide Eingabewege; linke/rechte Modifier getrennt berücksichtigt.
- Während der Hotkey-Eingabe im sichtbaren Einstellungsfenster wird kein Clip ausgelöst.
- Zusätzliche Regressionstests für Zustellreihenfolge, verzögerte Nachrichten, Konflikte, Fokus und Ressourcenfreigabe.
- Physischer Nutzertest: drei Tastendrücke ergaben drei vollständig dekodierbare Clips, davon zwei unter LoL und einer beim Fensterwechsel unter Desktop. Dies ist keine universelle Anti-Cheat-Kompatibilitätszusage.

## 0.1.2 Beta

- Keine JSON-Begleitdateien mehr neben Clips; Zuordnung weiterhin über Spielordner.
- Interne temporäre Ordner werden nicht als Spiel angezeigt.
- Export-/Shutdown-Tests auf reine Videodateien umgestellt.
- Öffentliche Dokumentation, Build-Anleitung und Repository-Struktur ergänzt.

## 0.1.1 Beta

- Falschen OBS-Funktionsnamen im Monitor-Fallback korrigiert.
- Stabiler Windows-Monitoralias bei fehlender Geräte-GUID.
- Erfolgs-Popup entfernt und optionalen kurzen Beep ergänzt.

## 0.1.0 Beta

- Native Windows-Tray-App mit GPU-Replay-Puffer, automatischer Aufnahme, getrennten Audiospuren und Spielordnern.
- Konfigurierbare Auflösung, FPS, Cliplänge und globaler Hotkey.
- Minimierter Autostart und monochrome Einstellungen.
