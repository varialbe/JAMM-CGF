# Guten Morgen — JAMM-CGF Nachtbericht (16.11.2026)

**Kurz:** Phasen 2, 3, 4, ein zusätzlicher Blocker-Fix 3b und Phase 5 (Avalonia-GUI + neues README) sind fertig,
von mir gegen die echten Dateien geprüft und auf `experimental` gepusht. `main` ist unberührt. Die Spieldateien in
~/cgf-files sind nachweislich unverändert (SHA-256 nach jeder Phase geprüft).

| Phase | Commit | Ergebnis meiner Prüfung |
|---|---|---|
| 2 Konflikterkennung | 6efed81 | `check` über alle 3 Mods: 50 CONFLICT / 7 AUTO / 13 INFO / 6 WARNING; Details funktionieren |
| 3 Build/Install | c741b36 | Backup/Install/Restore sicher (fremdes data.win wird abgelehnt, Restore = Original-Hash). **Aber Blocker gefunden** (s. 3b) |
| 4 Mod-Verwaltung | ccba099 | config/add/list/profile/apply/restore laufen durch, apply ~1 Min, keine Build-Reste |
| 3b Index-Fix | a4e0703 | Katalog-Check: alter Merge 171/234 Einträge falsch → jetzt 0 falsch (beide Reihenfolgen) |
| 5 GUI + README | 0e0cc50 | 216/216 Tests grün mit echten Dateien, 0 Warnungen; Screenshots nur mit Demo-Daten |

## Der wichtigste Fund der Nacht
GML referenziert viele Objekte per **Zahl** (z.B. Fischkatalog `object[2,58] = 641`). Cave entfernt 3 Basis-Objekte, und
alle Mods hängen neue Objekte auf denselben Indizes an. Im Phase-3-Merge zeigte deshalb fast der ganze Katalog auf falsche
Fische, obwohl `verify` grün war. Ich habe die Fix-Phase 3b geschrieben und laufen lassen: Basis-Objekte bleiben stehen,
typisierte Zahlen werden umgemappt, und eine neue semantische Prüfung vergleicht jede Referenz namentlich mit dem Mod.
Mit einem eigenen Tool habe ich unabhängig nachgemessen, dass der Fix wirkt.

## Für dich zum Testen (Desktop)
- Fertige Builds + Anleitung: `~/JAMM-CGF/out/test-builds/` (`TEST-ANLEITUNG.md`, SHA256SUMS; nicht im Git).
- Oder direkt: `dotnet run --project src/Jamm.Gui -c Release` bzw. den CLI-Quick-Start im README.
- Ungetestet (hier kein Display/Spiel): echtes Spielen, Proton-Start, Datei-Dialoge/Drag&Drop der GUI.

## Bekannte Grenzen
- Abyss und Cave belegen **dieselben Katalog-/Boot-Slots** (z.B. Boot 8, Katalog `[2,58]` ff.). Nur ein Mod gewinnt diese
  Slots, die Fische des anderen existieren dann ohne Katalogeintrag. Automatisches Umnummerieren wäre das nächste große Feature.
- Kleinere Follow-ups (Liste in docs/reports/overnight-2026-11-16.md): 1-basierte vs. 0-basierte Hunk-Nummer in der
  Ausgabe, Statusfarben in der GUI, agy-Review scheitert bei Prompts > 128 KB (Codex-Fallback griff).

## Nächste Schritte (Vorschlag)
1. In-Game-Test mit einem der Test-Builds → Feedback.
2. Phase 6: Slot-Renumbering für kollidierende Kataloge/Boote + Follow-ups.
3. Danach `experimental` → `main` mergen (deine Entscheidung) und ein Release mit gepackten GUI-Binaries.
