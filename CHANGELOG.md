# Changes

## v2.6.0-preview.25 — 2026-11-19

- Exit snapshots are generated and verified as standalone SQLite databases. Missing saves are reported as errors.
- Disconnects close both relay directions, and a late trainer reply cannot acknowledge the next edit.
- The game uses an offline welcome page in all five display languages. Public chat lines are handled independently, with adjustments to town activity pacing.
- The English character creation screen shows descriptions for all seven classes. Character name confirmation and several tutorial controls use clearer English.
- An isolated playtest covered login, channel switching, starter quest acceptance, trainer actions, scheduled rewards, and save recovery.

Online trainer actions in Seria's room can still be blocked with a request to return to town. The catalogue has 15 sendable entries without a defined icon. Some original bitmap text remains.

## v2.6.0-preview.23 — 2026-10-24

- Public portable build with English-named entries: AradHomeward, AradTrainer, StopAll and CheckStartup.
- Empty initial accounts and characters; no personal saves, logs, backups or credentials.
- Per-installation loopback TLS identity generated on first run; existing identities are verified without silent replacement.
- Name-only account creation and remembered account selection in the original launcher, with a compatibility window option.
- Native trainer with character, item, gameplay, channel and backup management; automatic shutdown after exiting the game.
- Five display languages for the launcher, trainer and covered game resources. Includes the latest character-creation, dungeon tooltip, NPC popup, combat, map/loading/title/tutorial and selection interface corrections.
- Bilingual illustrated guides, bundled runtime, startup diagnostics, relay-port preflight and local component validation.

This remains a translation preview. Some original text, bitmap text and voices remain; automatic translations can be inaccurate. Target-PC gameplay is not established by component validation.
