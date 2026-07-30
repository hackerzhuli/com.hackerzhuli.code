# Game View Automation Demo

`Resources/GameViewAutomationDemo.uxml` and its USS define the runtime UI used by
`GameViewAutomationPlayModeTests`.

Running the PlayMode test writes artifacts from the real protocol response to:

- `Temp/GameViewAutomationDemo/game-view-snapshot.yaml`
- `Temp/GameViewAutomationDemo/game-view-hierarchy.yaml`
- `Temp/GameViewAutomationDemo/game-view-inspection.yaml`
- `Temp/GameViewAutomationDemo/raw-visual-tree.txt`
- `Temp/GameViewScreenshots/game-view-*.png`

The raw tree is intentionally emitted beside the compact snapshot to make it easy to compare
which UI Toolkit implementation nodes were folded or retained.
