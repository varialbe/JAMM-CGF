# Task: native bridge via function_get_address (exp/native-bridge) — items 12–15 of the 2026-11-22 idea list
Background: docs/design/native-offload.md on branch vps/exp/native-offload (merge it into your branch first; Phase 1 found runner is
PE32 i386, GM bytecode 16; external_define/external_call real/string only; live probes were never run → no-go by default).
New idea: YAL-GameMaker/function_get_address trick — `variable_global_set(n, variable_global_get(n)); return ptr(variable_global_get(n));`
style closure capture to obtain a callable pointer to a runner built-in, so a native DLL can call built-ins / get raw buffer memory.
## Phase A (measure, LOCAL in-runner, mandatory): build a 32-bit DLL with i686-w64-mingw32-gcc (sudo apt/pacman/dnf allowed to install).
Probe archive from legacy base (copy, never modify inputs): (1) DLL loads under Proton via external_define; per-call overhead for
1/100/10000 calls per tick; (2) does ptr()/function_get_address-style trick yield a usable pointer in GM 1.4 bytecode 16 (if `ptr` or
method-closure semantics don't exist in 1.4, prove it and say so); (3) does buffer_get_address exist and does the pointer cross
external_call (as real/double cast) — read/write round trip; (4) a worker thread in the DLL writing to a buffer while GML runs, read
back next tick (safety by double-buffer). Run via ~/jamm-bench/run.sh-like harness (serialized flock ~/jamm-bench/.run.lock, isolated
prefix compatdata/jamm-bench; never CGF-Personal/cgf-personal/343780; no xdotool). Results → docs/reports/native-bridge.md with numbers.
## Phase B (only if A works): one async kernel: FishLarge avoidance math or DecorSleep/FishSleep distance+wake decisions computed in the
DLL from a shared buffer filled in Begin Step, results applied in End Step; clean GML fallback when DLL missing. Measure on all7/pub5
dock vs main (2 runs each). Ship as optional builtin flag only. Commit on branch, no push.
