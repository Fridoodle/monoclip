# Änderungen

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
