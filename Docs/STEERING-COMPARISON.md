# Steering investigation

The supplied APK reports Paper.io 2, package io.voodoo.paper2, version 4.37.2.
It was installed from the split APK in `Reference/Paper2` and observed in a
portrait BlueStacks session. The official support description matches the same
interaction: slide a finger anywhere and the character follows the swipe.

Paper.io 2 support describes sliding a finger anywhere on the screen:
https://support.paper.io/support/solutions/articles/202000095752-what-are-the-basic-rules-of-paper-io-2-

The live match confirmed three visual/control targets: the camera stays close
to the moving character, a held drag represents a stable direction instead of
successive micro-deltas, and the exposed trail is a rounded ribbon rather than
a visible grid path. The reference game uses portrait presentation, while its
mobile layout can be adapted to landscape without changing the swipe mapping.

PaperTrails now holds a floating swipe anchor for the gesture lifetime. The
direction is the normalized vector from that anchor to the current finger
position, with a 1.4% short-screen deadzone and a 1440 degrees/second host turn
limit. These are deliberately tuned controls, not values recovered from the
APK; the high turn rate gives the sharp opposite-swipe reversal seen in the
reference while retaining a short curved path instead of teleporting the body.

The trail renderer now uses a tensioned cubic resample, with the first point
overlapping the friendly surface at departure. Trail opacity is 0.40, below
the opaque claimed turf. Orientation remains AutoRotation for portrait and
landscape builds.
