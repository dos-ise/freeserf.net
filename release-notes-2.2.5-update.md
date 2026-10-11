## Version 2.2.5 (update, version code 20207)

### Store short version (delta to 20206)

- Fixed a crash on the second app start
- Fixed the version shown in the main menu (now 2.2.5)
- Fixed black screen / app going to background after using Exit and reopening
- Fixed OpenGL ES detection on desktop drivers
- Roads can now be ended on an existing road by placing a flag

### Full changelog

### Android fixes
- **Game speed:** the game no longer runs too fast on Android. The game speed is now independent of the display refresh rate, so it matches the intended speed on all devices.
- **Sound on startup:** music and sound effects now work right after starting the app. Previously they only worked after toggling them off and on once in the options.
- **Sound while paused:** the sound no longer stutters or loops when the game is paused.
- **Loading screen:** the app no longer gets stuck on the loading screen after it was closed and reopened.
- **Focus ring:** removed the focus ring that appeared around the buttons in the data import dialog.

### Game logic
- The game update rate is now independent of the frame rate, so the game speed is correct on any display (also on desktop).
- The sound font is freed on exit, so no sound issues remain after closing the app.
- Serf visuals are restored correctly after a full multiplayer game sync.

### Multiplayer
- **Votes:** players can now vote to pause the game or change the game speed.
- **Rejoin:** players can rejoin a running multiplayer game, and the other players get a notification when someone rejoins.

### Desktop / window
- The window is now resizable while keeping the aspect ratio.
- The default window size is based on the monitor size.
- Windows that are outside the monitor are centered again.
- Window options given on the command line no longer overwrite the saved configuration.
- The start scripts show title bars.
