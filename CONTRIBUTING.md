# Beiträge

Bitte zuerst ein Issue für größere Änderungen eröffnen. Capture- und Export-Änderungen benötigen Tests, einschließlich Fehler-/Shutdown-Pfaden. Neue P/Invoke-Aufrufe gegen die tatsächlich gebündelte DLL prüfen; ein normaler Happy-Path-Test findet keine selten ausgelösten fehlenden Exports.

Vor einem Pull Request:

1. Core-/UI-Tests ausführen.
2. Windows-Release-Build ohne neue Compilerwarnungen erstellen.
3. Bei Capture-/Export-Änderungen reale native Tests durchführen und Video/Audio prüfen.
4. Keine persönlichen Clips, Einstellungen, Logs, Tokens, Build-Ausgaben oder Drittanbieter-Binärdateien committen.

Bugreports sollten Windows-Version, GPU/Treiber, Spiel/Fenstermodus, Einstellungen und minimale Reproduktionsschritte enthalten. Logs vor dem Teilen anonymisieren. Für Sicherheitsprobleme keine sensiblen Daten öffentlich posten.
