# Contributing

Open an issue before larger changes.

Before a pull request:

1. Run the core and UI tests.
2. Build the app in Release without new warnings.
3. For capture or export changes, run the native tests and check the resulting clips.
4. Do not commit clips, settings, logs, tokens, build output or third-party binaries.

New P/Invoke calls must exist in the bundled DLLs (`MonoClip.NativeTests.exe --exports`).

Bug reports should include the Windows version, GPU and driver, game and window mode, MonoClip version and steps to reproduce. Remove personal data from logs before posting.
