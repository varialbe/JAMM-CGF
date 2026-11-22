# Nachtschicht 2026-11-21 → 2026-11-22

## Kurzfassung (Deutsch)

- **Übernahme 01:15**, Briefing `~/jamm-handover/2026-11-21/BRIEFING.md` abgearbeitet. Ab 13:00 galt die
  Modell-Regel des Users: Codex nur `gpt-6-luna` (reasoning high), Gemini (`gemini-4-hydrogen-high`) immer
  parallel, Claude nur Orchestrierung, Review, Tests, Merges, Deploy.
- **Gemergt nach `main` (alle mit Release-Build ohne Warnungen + voller Testsuite, Ingame-Läufe auf dem VPS):**
  weak-pc-preset, base-mod, maize-nav-fix, big-money, chunk-script-types, autoplay (Crash-Jäger v1),
  object-index-guard (Instanz-Crash-Schutz), fish-value-cap, silo-v3, fair-play-audit, upgrade-loop-bounds,
  worldgen-v1 (Endless Ocean 0.2.0), submarine (U-Boot 0.1.0), value-guard-review, instance-guard-review,
  harbor-town (Harbor Town 0.1.0, eigenständige Mod). Dazu Site v2 vom PC-Claude (origin) eingemergt.
- **Instanz-Crash-Schutz:** ingame validiert auf persönlichem Paket, public-5, all-7 und Stress-Szenen;
  insgesamt rund 4,5 h Autoplay (zufällige echte Eingaben) **ohne einen einzigen Crash**.
- **Seite** (`/cgf-mods/`) mehrfach deployed: Silo 2.3.0, Endless Ocean 0.2.0, U-Boot-Detailseite. Backups unter
  `~/jamm-night/site-backups/`. Keine TGE/PWB- oder all7-Inhalte auf der Seite.
- **Persönliches Paket (final)** (nicht lokal installiert, liegt nur auf dem VPS):
  `~/jamm-night/personal-tge/own-mods-2026-11-22c.win`, Reihenfolge perf, hd-lite, console, smooth, silo,
  weather-pack, endless-ocean. 54.670.636 Byte,
  SHA-256 `d2748c6a7f51575acca36926c7a248f76fbd8c9823975540380d49301f70cf9b`.
  Enthält den Sonar-Fix für alle JAMM-Fische. Ingame: dock, weather, silo, worldgen, money, stress RECORDED;
  Autoplay 4 × 20 min (Seeds 11002, 11005, 11006, 11007) ohne Crash. Das frühere Paket `own-mods-2026-11-22.win`
  (SHA `7f243150…710b`) ist überholt: es hat den Sonar-Crash bei Endless-Ocean- und Wetter-Fischen.
- **Offen / Hinweise:**
  - Endless-Ocean-Untergrund (Kachelraster/Nähte): Geminis Runde 1 nach Ingame-Panoramen abgelehnt, Runde 2
    siehe unten.
  - Harbor Town: saubere Dock-Aufnahme fehlt noch (Modusmenü verdeckt das Bild).
  - Codex sah einmal den Steam-Bildschirm "auf einem anderen Computer angemeldet". Wenn Steam auf dem PC
    läuft, können VPS-Ingame-Tests scheitern.
  - Codex-Abo-Limit zweimal erreicht (05:57, 11:06), danach Resume-Jobs; Gemini-Kontingent abends erschöpft
    (429), Retry-Wrapper `~/jamm-wt/q21/agy-retry.sh`.

## Wer hat was gemacht

| Arbeit | Modell |
| --- | --- |
| weak-pc, base-mod, Teile maize-nav/big-money (vor der Regel) | Codex (Sol) / Claude |
| object-index-guard, fish-value-cap, silo-v3 | Codex (Sol, Resume) |
| worldgen-v1, harbor-town, submarine | Codex (`gpt-6-luna`, high) |
| fair-play-audit, upgrade-loop-bounds, value-guard-review, instance-guard-review, ocean-underlay, autoplay-v2 | Gemini (`gemini-4-hydrogen-high`) |
| Specs, Reviews, Merges, Ingame-Matrizen, Seite, Report | Claude |

## Ingame-Ergebnisse (Auswahl)

| Build | Szenen | Ergebnis |
| --- | --- | --- |
| own-mods-2026-11-22 | dock, weather, silo, worldgen, money, stress | alle RECORDED |
| own-mods-2026-11-22 | Autoplay 20 min, Seed 10001 | PLAYED, 117 Aktionen |
| all-7 (guard) | Autoplay 30 min / stress | PLAYED 209 / RECORDED (1975 Fische) |
| public-5 (main) | Autoplay 20 min | PLAYED 131 |
| Submarine solo + persönlich | submarine | RECORDED (Tauchgänge bis 4022) |
| Endless Ocean solo / persönlich / all-7 | worldgen | RECORDED |

## Fair-Play-Audit

Alle öffentlichen Mods gegen die Legacy-Basis: keine False Positives. `cave-expansion` meldet nur zwei
`INFO [CGF-FAIR-002]` (hohe XP-Werte, unverändert gelassen). PWB zeigte die 19/18-Upgrade-Tabelle, behoben durch
`[CGF-UPGRADE-LOOP-001]`.

## Nachträge

### Abend 2026-11-21

- **Autoplay v2** (Gemini) gemergt: zufällige echte Eingaben inkl. Boot, Tauchen, Shop; fand den Sonar-Crash.
- **Endless Ocean 0.2.1** (Untergrund Runde 2, Gemini): kein Kachelraster mehr, Vanilla-Kopfsteinpflaster;
  Seite aktualisiert.
- **Fisch-Variablen-Paritätsaudit** (Gemini) gemergt: Liste aller Variablen, die Vanilla-Fische haben und
  generierte Fische brauchen.
- **Sonar-Crash Endless Ocean** (`Variable Dijon.s5 not set … SonarPanel_Draw_0`, gefunden von Autoplay v2 bei
  x≈152.600): Codex-Luna hat die Ursache gefunden (Fische entstehen nach dem Sonar-Init-Durchlauf), Gemini Runde 2
  ohne harte Asset-Indizes, mit `instance_exists`-Schutz, `base_lunge_mod`, ohne Torby/Ancient Kingfish im Stream.
  Ingame: worldgen `sonar_missing = 0`, Autoplay Seeds 11002/11004 PLAYED. Gemergt nach `main`.
- **PC-Claude Perf-Fix** (CGF-PERF-REGRESS-001) von origin eingemergt, 642 + 130 Tests grün.
- **Harbor Town auf der Seite**: eigene Karte und Detailseite (Gemini), Galerie von Claude kuratiert (Solo-Bilder
  entfernt, weil das Modusmenü sie verdeckt), deployed, Backup `cgf-mods-20261121-212349`.
- **Finale Matrix Kandidat 22b**: alle sechs Szenen RECORDED, aber Autoplay (Seeds 11002, 11005) fand einen
  zweiten Crash derselben Klasse: Weather-Pack-Fisch `lowbelly` ohne `s5`. Der Fehler existierte schon auf `main`.
  Fix: Codex-Luna baut ein gemeinsames `jamm_fish_sonar_init` für alle JAMM-Fisch-Spawner, Gemini parallel eine
  Sonar-Testszene für Wetterfische und ein Audit aller Fisch-Spawnpfade.

### Nacht 2026-11-22 (Abschnitte 10–16 des Briefings)

**Gemergt nach `main`:**
- `exp/fish-sonar-scene` (44a9129): gemeinsames `jamm_fish_sonar_init` für Wetter-, Katalog- und Endless-Ocean-Fische
  (Codex-Luna), Audit aller Fisch-Spawnpfade + Review + Review-Fixes (Gemini-Hydrogen), Wetter-Testszene: in allen
  8 Wetterlagen `sonar_missing = 0`; Stress-Szene läuft auch ohne Perf-Controller. 658 + 130 Tests grün.
- `exp/harbor-site`: Harbor Town mit eigener Karte und Detailseite auf der Mod-Seite (deployed).
- Forschung (nur Doku): Fisch-Art-Notiz (AA-Stil), Fisch-KI-Clustern (negativ, keine Art geeignet),
  Multicore per DLL (No-Go: `buffer_get_address` liefert im GM-1.4-Runner offenbar 0, Hotspot nur 4,2–4,9 %).

**Fertig, aber noch nicht gemergt:**
- Silo 2.3.1 (Hydrogen): Ferrex mit eigenem Look, grauer Block am Schacht entfernt; solo + persönlich RECORDED.
  Liegt auf `pending/silo-2.3.1`, wartet auf den Autoplay-Check (der Lauf auf Paket 22d wurde wegen RAM-Mangel
  von Claude Code abgebrochen).
- U-Boot 0.2 (Hydrogen): Oval-Rumpf mit 6 Düsen, Zoom 1,55x mit Vorhalt, drehbare Scheinwerfer + Peek,
  Bow Winch (250M), 0.1-Saves kompatibel; alle 22 Szenen-Stufen solo + persönlich. Runde 2 läuft: HUD sitzt im
  Zoom falsch.

**Laufend:** Fish-Sleep (Hydrogen, höchste Priorität), Harbor Town 0.2 und Crash-Jagd (Codex-Luna),
Fisch-Virtualisierung Schritt 2b, Engine-/Build-Optimierung Phase 3, Fish_Step-Mikro-Validierung (alle Codex-Luna).
Pausiert: Licht-Culling (Hydrogen, WIP-Commit `b579056`, wegen Gemini-Kontingent).

**Infrastruktur / Probleme:**
- agy-Token-Datei durch gleichzeitige Refreshes beschädigt (2 Byte Rest) → repariert, `agy-auto.sh` prüft sie jetzt
  vor jeder Runde.
- Gemini-Kontingent 429 gegen 04:00 (~20 min), Auto-Resume hat überbrückt.
- RAM-Engpass gegen 04:00 (all-7-Spiel 4,4 GB + Steam + Builds): Claude Code beendete zwei Hintergrund-Shells.
- Engpass ist das eine Spiel auf dem VPS (bis 9 Läufe in der Warteschlange); Steam trennt alle 5–6 min, daher
  ~30 % EXITED → `run.sh` wartet jetzt vor dem Start auf „Logged On“.
- Platte zweimal bei 92 % → aufgeräumt (gemergte Worktrees, Spielkopien), jetzt ~88 %.

<!-- NACHTRAEGE -->
