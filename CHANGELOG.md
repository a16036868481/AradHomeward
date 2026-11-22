# Changes

## v2.6.0-preview.28 — 2026-11-22

- The full Windows ZIP includes the startup repair and opens the compatibility account window by default. Each installation manages its own account window.
- A successful startup check displays the game entry. The detailed report is saved locally for troubleshooting.

## Startup repair for v2.6.0-preview.27 — 2026-11-22

- Each installation manages its account window separately. The compatibility window can open while an older launcher still holds the global account-window lock.
- Repeated starts bring the existing compatibility window forward. The repair ZIP selects that window directly and refreshes startup and language integrity checks.
- A Windows test reproduced the previous startup failure with a controlled launcher exit and the old mutex held. With the repair applied, the account window opened, diagnostics passed and the game reached character selection.

## v2.6.0-preview.27 — 2026-11-22

- Start a private LAN room from the saved-account screen. The room window shows the address, connected guests, and a copy-invitation button. Guests join by invitation and player name.
- Guest progress is saved on the host PC and can be resumed from the same guest installation. Room closure disconnects guests. Exiting the host game closes the room and saves a backup.
- Recover from an early original-launcher error by opening the compatibility account window. Startup diagnostics verify the archived runtime using the bundled file manifest.
- The complete Windows ZIP includes the launcher, LAN room features, current trainer, and bilingual illustrated instructions.

Local Windows tests verified room creation and closure, guest sign-in to character selection, and guest account reconnection. Party play on two physical PCs remains unverified. Part of the legacy text and voice content remains in the original language.

## v2.6.0-preview.26 — 2026-11-19

- Online edits use native dungeon loading and town-return events to determine when editing is available. Tested gold, Cera, level, skill points, fatigue, class changes, bulk grants, and inventory edits in Seria's room.
- All 31,474 catalogue entries display an icon. The 15 entries whose frames are absent from the installed client use an icon from the same item family or avatar set.
- Localized the PIN and guild menu labels, class advancement and awakening panels, and replay buttons. Added 227 text repairs across the Traditional Chinese, English, Japanese, and Korean packs.
- The editor's character status adjusts to the translated label width. Local accounts use name-only sign-in.

Some original game screens and dialogue still contain source-language text. The launcher shows text coverage for the selected language.

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
