# Phase 9 — "Browse mods" tab (implementer: Codex, branch ui/browse-mods)

The full product requirements are the user's prompt below (German, verbatim). Environment notes from
the orchestrator come first.

## Orchestrator notes (environment)
- Work only in this worktree (branch `ui/browse-mods`). Do not commit, push or change git config.
  Another Codex works in parallel on `tools/Jamm.ModMaker` (game mods) — do not touch `tools/`.
- Network access is enabled for you to inspect https://litterbox.fyi (index.html, assets/app.js) and
  Game Jolt. Use only public read access. Never write to Litterbox; no private keys.
- `dotnet test` may be blocked by the sandbox (VSTest socket permission). Build with
  `dotnet build -c Release -m:1 --disable-build-servers` if needed; if tests cannot run, say so — the
  orchestrator runs them on the VPS (Linux, .NET 10) and on the desktop.
- The user is available for questions: if a decision is genuinely theirs (not derivable from the prompt
  or code), stop and write the question at the top of your final message.
- Final report: `docs/reports/phase9-browse-mods.md` with the items listed in section 8 of the prompt.

## User prompt (verbatim)

Erweitere das bestehende Projekt JAMM-CGF um einen nativen Tab „Browse mods“, in dem Nutzer Cat-Goes-Fishing-Mods entdecken, herunterladen, zur Bibliothek hinzufügen und anschließend über den bestehenden Profilablauf installieren können.

Repository:
https://github.com/varialbe/JAMM-CGF

Referenzen:
- https://litterbox.fyi/index.html
- https://litterbox.fyi/mod.html?id=40
- https://gamejolt.com/games/cgfCOLexpansion/842548

Arbeite im bestehenden Projekt und passe dich seiner Architektur und Gestaltung an. Baue die Funktion vollständig ein; erstelle keine separate App.

1. Bestehende Architektur prüfen

Lies zuerst AGENTS.md, README.md und die relevanten Dokumente zu Datenlayout, GUI und Installation. Prüfe den aktuellen Checkout, bevor du dich auf folgende Einstiegspunkte verlässt:

- src/Jamm.Gui/MainWindow.axaml und MainWindow.axaml.cs
- src/Jamm.Gui/ViewModels/MainViewModel.cs
- src/Jamm.Core/Management/ModLibrary.cs
- src/Jamm.Core/Management/ManagerStore.cs
- src/Jamm.Core/Management/ManagerModels.cs
- src/Jamm.Core/Management/ProfileLibrary.cs
- src/Jamm.Core/Management/ProfileInstaller.cs
- vorhandene Core-, ViewModel- und Avalonia-Headless-Tests

Das Projekt verwendet .NET 10, Avalonia 11 und CommunityToolkit.Mvvm.
MainViewModel besitzt bereits AddAsync, RunAsync, Busy-/Cancel-Zustände
und InstallAsync. ModLibrary.Add übernimmt den bestehenden Import.
Verwende diese Abläufe beziehungsweise extrahiere bei Bedarf kleine,
gemeinsam nutzbare Methoden, ohne ihre Schutzmechanismen zu umgehen.

Zeige vor den Änderungen kurz, welche Integrationspunkte du verwenden
wirst. Fahre anschließend mit der Implementierung fort.

2. Quellen korrekt unterscheiden

Litterbox ist der Katalog, nicht automatisch der Dateihoster.

Bei der untersuchten Website:
- lädt index.html Mods über einen Supabase-Client;
- verwendet der Katalog unter anderem die Tabellen/Relationen mods,
  profiles und mod_categories/categories;
- enthält ein Mod unter anderem name, author, description, file_url,
  icon_url, banner_url, is_preview und Katalogstatistiken;
- führt file_url bei Mod 40 auf die Game-Jolt-Projektseite;
- ist die auf Litterbox angezeigte Klickzahl keine bestätigte Zahl
  abgeschlossener Downloads.

Prüfe die aktuelle Website und assets/app.js, bevor du diese Struktur
implementierst. Behandle die dort verwendete Schnittstelle als externe,
änderbare Abhängigkeit und nicht als zugesicherte öffentliche API.
Nutze ausschließlich öffentlich lesbare Daten und den dafür vorgesehenen
öffentlichen Zugriff. Keine privaten Schlüssel, Admin-Zugänge oder
Schreibzugriffe auf Litterbox.

Game Jolt trennt Projekt, Paket/Release und konkrete Download-Datei.
Beim untersuchten Colour-Expansion-Beispiel zeigt die Seite Version 5.0.0
und eine data.win von ungefähr 54 MB. Diese Werte sind Beispiele und
dürfen nicht als feste Download-Konfiguration eingebaut werden.

Ermittle den tatsächlich funktionierenden öffentlichen Weg, um
Paketinformationen und die konkrete Datei aufzulösen. Erfinde keine
API-Endpunkte und behandle eine Projektseite niemals als Mod-Datei.
Kurzlebige Download-URLs müssen bei Bedarf neu aufgelöst werden.

3. Benutzeroberfläche

Füge eine Hauptnavigation mit dem bisherigen Managerbereich und
„Browse mods“ hinzu. Erhalte Bibliothek, Load Order, Konfliktansicht,
Settings und den bisherigen Installationsablauf.

Der neue Tab soll bieten:
- Suchfeld für Modname, Autor und Beschreibung;
- Kategorienfilter;
- Released-/Preview-Filter;
- Sortierung nach verfügbaren Daten, etwa Name, Datum oder Bewertung;
- Modkarten oder eine übersichtliche Liste mit Icon, Name, Autor,
  kurzer Beschreibung, Kategorien und Verfügbarkeitsstatus;
- Detailansicht mit vollständiger Beschreibung, vorhandenen Bildern,
  Autorenhinweisen und Link zur Originalseite;
- Lade-, Leer-, Fehler- und Offline-Zustände mit „Retry“.

Orientiere dich an der Informationsstruktur von Litterbox, integriere
sie aber in das vorhandene Avalonia-Design. Verwende native Controls,
keine eingebettete Website als Hauptimplementierung. Behalte die
bestehende Sprache der App bei.

Zeige unbekannte Versionen oder Kompatibilität ausdrücklich als unbekannt.
Ein „Verified creator“-Badge oder ein Plattformname beweist keine
Kompatibilität mit der verwendeten Spielbasis.

4. Download-Auflösung

Trenne die Verantwortlichkeiten durch kleine, testbare Komponenten:
- Kataloganbieter;
- Anbieter zum Auflösen einer Quellseite in konkrete Dateien;
- Download-Service;
- Integration in den vorhandenen Bibliotheksimport.

Geeignete Namen wären beispielsweise IModCatalogProvider,
IModDownloadResolver und ModDownloadService. Passe die genaue Aufteilung
an das Projekt an; vermeide unnötige Frameworks und Abstraktionen.

Unterstütze zunächst Litterbox als Katalog und Game Jolt als konkreten
Downloadanbieter. Implementiere belegbar funktionierende direkte
HTTPS-Dateilinks, falls die Quellen solche liefern.

Falls mehrere Pakete oder Dateien angeboten werden, zeige die Auswahl
mit Name, Version, Dateityp und Größe, soweit verfügbar. Wähle nicht
blind die erste Datei.

Bei nicht unterstützten Anbietern oder nicht auflösbaren Downloads:
„Open source page“ und einen klaren Hinweis anbieten. Ein Browser-Fallback
ist erlaubt, darf aber nicht als erfolgreicher Direktdownload erscheinen.
Der Game-Jolt-Beispielpfad soll nach Möglichkeit vollständig innerhalb
des Managers funktionieren; eine tatsächliche externe Einschränkung
muss konkret dokumentiert werden.

Previews ohne veröffentlichten Download bleiben sichtbar, erhalten aber
keinen aktiven Download-/Installationsbutton.

5. Download und bestehende Installation verbinden

Der Ablauf soll sein:

Mod auswählen
→ Datei/Release auswählen, falls nötig
→ herunterladen
→ über den vorhandenen Core importieren
→ Bibliothek aktualisieren
→ optional dem gewählten Profil hinzufügen
→ über den bestehenden Check-/Build-/Verify-/Install-Ablauf installieren.

Biete „Download & add“ und, bei gültiger Konfiguration und ausgewähltem
Profil, „Download & install“ an.

„Download & install“ muss den Mod in das gewählte Profil übernehmen und
anschließend den bestehenden Prüf- und Installationsablauf verwenden.
Erforderliche Konfliktentscheidungen müssen im vorhandenen UI getroffen
werden können. Die bestehende Bestätigung mit konkreten Ziel- und
Backup-Pfaden bleibt erhalten.

Überschreibe data.win niemals direkt mit der heruntergeladenen Datei.
Beachte, dass JAMM mehrere Mods zu einem geprüften Archiv zusammenführt.

Unterscheide die Zustände:
- im Katalog verfügbar;
- wird heruntergeladen;
- wird importiert;
- in der Bibliothek;
- im Profil aktiviert;
- im aktuell installierten Profil enthalten;
- fehlgeschlagen oder abgebrochen.

Bezeichne einen bloßen Download oder Bibliotheksimport nicht als
abgeschlossene Spielinstallation.

6. Robustheit und Datenspeicherung

- Verwende asynchrone HTTP-Downloads mit Streaming, Fortschritt,
  Timeouts und CancellationToken.
- Zeige bei unbekannter Gesamtgröße einen unbestimmten Fortschritt.
- Lade in einen temporären Bereich unter dem Manager-Datenverzeichnis.
- Bereinige unvollständige Dateien nach Fehlern und Abbruch.
- Beachte --data-dir und CGFMM_DATA_DIR.
- Übernimm heruntergeladene Inhalte erst nach erfolgreicher Prüfung.
- Nutze die bestehenden Regeln für .win und ZIP-Dateien. ZIPs dürfen
  gemäß aktuellem Import genau eine data.win enthalten; andere Inhalte
  werden nicht ausgeführt.
- Prüfe tatsächlichen Inhalt und Archivstruktur. Ein Dateiname oder
  Content-Type allein genügt nicht; erkenne HTML-Fehlerseiten.
- Begrenze Downloadgröße und entpackte Datenmenge sinnvoll.
- Sichere Dateinamen und Pfade ab; verhindere Path Traversal.
- Prüfe Redirect-Ziele und lehne lokale/private Netzwerkziele ab.
- Führe keine mitgelieferten EXE-Dateien, Skripte oder Installer aus.
- Behalte bestehende Hash-, Basisversions- und Bytecodeprüfungen bei.
- Eine selbst berechnete SHA-256 dient der Identifikation; ohne
  vertrauenswürdigen Vergleichswert bestätigt sie keine Herkunft.
- Serialisiere Bibliotheks-, Profil- und Installationsänderungen mit
  dem bestehenden Operationsmodell. Verhindere doppelte Aktionen
  und Rennen mit lokalem Import, Restore oder Install.

Speichere Herkunftsinformationen wie Anbieter, externe Mod-ID,
Quellseite, Release-/Datei-ID und Version, soweit vorhanden.
Halte persistierte Daten rückwärtskompatibel. Verwende stabile IDs und
vorhandene Hash-Deduplizierung statt Modnamen als Identität.

Ordne einen Onlineeintrag nur dann einem lokalen Mod zu, wenn die
Zuordnung belegt ist. Bei bestehenden manuellen Imports ohne Herkunft
darf die UI keine sichere Zuordnung behaupten.

Neue Releases dürfen bestehende Profile nicht ungefragt verändern.
Automatische Updates sind für diese erste Version nicht erforderlich.

Cache Katalogdaten und Bilder sparsam. Zeige bei Offline-Nutzung den
Zeitpunkt des letzten erfolgreichen Abrufs. Ein Ausfall des Katalogs
darf die lokale Modverwaltung nicht blockieren.

7. Umfang begrenzen

Für diese Version erforderlich:
- Litterbox-Katalog mit Suche, Filtern und Details;
- funktionierende Game-Jolt-Dateiauflösung, soweit öffentlich möglich;
- Download, Fortschritt, Abbruch und Fehlerbehandlung;
- Import und Integration in die bestehende Profilinstallation;
- Herkunftsdaten, Duplikatbehandlung und sinnvolle Tests.

Nicht erforderlich:
- Login, Kommentare, Bewertungen schreiben, Favoriten oder Follows;
- eigene Downloadstatistiken oder Standorterfassung;
- eigener Backenddienst;
- Unterstützung beliebiger Downloadplattformen;
- automatische Hintergrundupdates;
- Änderungen am UndertaleModTool-Submodul.

8. Validierung und Abschluss

Ergänze deterministische Tests mit synthetischen Archiven,
kleinen Metadaten-Fixtures und gemocktem HTTP-Verkehr für:
- Katalogmapping und fehlende/optionale Felder;
- Preview ohne Download;
- Projektseite → Paket/Release → Datei;
- mehrere Dateien und nicht unterstützte Quellen;
- Redirects, HTML statt Mod-Datei, Größenlimits und Netzwerkfehler;
- Abbruch und Bereinigung;
- Duplikate und Herkunftszuordnung;
- Import → Profil → bestehender Installationsablauf;
- UI-Zustände und gegenseitige Sperrung schreibender Aktionen;
- Rückwärtskompatibilität bestehender gespeicherter Daten.

Keine echten Spiel- oder Moddateien ins Repository aufnehmen.
Live-Downloads nicht als Voraussetzung normaler Tests verwenden.
Prüfe den echten Litterbox-/Game-Jolt-Beispielpfad zusätzlich manuell,
soweit die Umgebung dies ermöglicht, und dokumentiere das Ergebnis
getrennt von automatisierten Tests.

Führe dotnet build -c Release und dotnet test aus. Unsere Projekte
müssen ohne Warnungen bauen. Aktualisiere README und Datenlayout-Doku.

Berichte am Ende:
- welche Dateien und Abläufe geändert wurden;
- welche Quellen tatsächlich direkt funktionieren;
- welche Tests bestanden haben;
- welche externen Einschränkungen oder manuellen Desktopprüfungen
  noch offen sind.
