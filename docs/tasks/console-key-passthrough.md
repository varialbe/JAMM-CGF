# Task: console mod must not pass typed keys to the game

## Bug
While typing in the JAMM console (builtin console mod, `src/Jamm.Core/Builtins/Console*.cs`), letters still trigger gameplay key
events. Example: typing `money` presses M → vanilla `Girl_KeyPress_77` cycles background_music_volume (0.8→1.05→0 = music muted).
The current guard calls keyboard_clear(48..90 etc.) in the console object's code, but GameMaker 1.4 event order is
Begin Step → keyboard/keypress/keyrelease events → Step; if the clear runs in Step/Draw (or the console object's depth/order runs after
Girl's), KeyPress events have already fired that frame.

## Required
1. Verify where the clear runs and fix so NO gameplay KeyPress/Keyboard/KeyRelease event fires while the console is open, including
   the frame the console opens (backtick) and the frame Enter submits. Options: move clearing to Begin Step of a persistent controller;
   or add a guard `if (variable_global_exists("jamm_console_open") && global.jamm_console_open) exit;` at the start of all
   KeyPress/Keyboard/KeyRelease events of gameplay objects via the existing code-hook installer (LegacyCodeHooks). Choose the robust
   one (likely the guard hook; Begin Step clear alone can't stop keyboard_check in Step code — also cover keyboard_check/_pressed in
   Step code by clearing in Begin Step).
2. keyboard_string still must receive the typed text.
3. Unit test that the built archive contains the guard/clear in the right event; `dotnet test` passes.
4. Commit on branch, don't push.
