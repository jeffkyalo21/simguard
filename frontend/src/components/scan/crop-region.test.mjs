// Run with `npm test`. The expected numbers are worked out by hand in the
// comments, not by calling the function being tested.
import { test } from "node:test";
import assert from "node:assert/strict";
import { GUIDE, guideFrameRect, printedNumberRect, viewRectToFrame } from "./crop-region.js";

function near(actual, expected, label) {
  assert.ok(Math.abs(actual - expected) <= 1, `${label}: ${actual} is not within 1 of ${expected}`);
}

test("the guide fractions stay inside the viewfinder and leave room under it", () => {
  assert.ok(GUIDE.left > 0 && GUIDE.top > 0);
  assert.ok(GUIDE.left + GUIDE.width < 1);
  assert.ok(GUIDE.top + GUIDE.height < 1);
});

test("a video with the same aspect as the viewfinder maps by a plain scale", () => {
  // 300x100 view, 1500x500 video: scale 0.2, nothing cut off.
  assert.deepEqual(guideFrameRect({ width: 300, height: 100 }, { width: 1500, height: 500 }), {
    x: 75, // 0.05 * 300 / 0.2
    y: 40, // 0.08 * 100 / 0.2
    width: 1350, // 0.9 * 300 / 0.2
    height: 360, // 0.72 * 100 / 0.2
  });
});

test("a video wider than the viewfinder is cut at the sides", () => {
  // 300x100 view, 4000x1000 video: scale 0.1 (height decides), shown 400 wide, 50 cut each side.
  assert.deepEqual(guideFrameRect({ width: 300, height: 100 }, { width: 4000, height: 1000 }), {
    x: 650, // (15 + 50) / 0.1
    y: 80, // 8 / 0.1
    width: 2700, // 270 / 0.1
    height: 720, // 72 / 0.1
  });
});

test("a video narrower than the viewfinder is cut at the top and bottom", () => {
  // 400x144 view, 1920x1080 video: scale 400/1920, shown 225 high, 40.5 cut top and bottom.
  const rect = guideFrameRect({ width: 400, height: 144 }, { width: 1920, height: 1080 });
  near(rect.x, 96, "x"); // 20 / (400/1920)
  near(rect.width, 1728, "width"); // 360 / (400/1920)
  near(rect.y, 249.7, "y"); // (11.52 + 40.5) / (400/1920)
  near(rect.height, 498.5, "height"); // 103.68 / (400/1920), rounded outwards
});

test("a portrait video in a wide viewfinder shows only a band across its middle", () => {
  // 360x144 view, 1080x1920 video: scale 1/3, shown 640 high, 248 cut top and bottom.
  const rect = guideFrameRect({ width: 360, height: 144 }, { width: 1080, height: 1920 });
  near(rect.x, 54, "x"); // 18 * 3
  near(rect.width, 972, "width"); // 324 * 3
  near(rect.y, 778.6, "y"); // (11.52 + 248) * 3
  near(rect.height, 311, "height"); // 103.68 * 3
});

test("the rectangle never leaves the camera frame, whatever the sizes", () => {
  for (const view of [{ width: 200, height: 144 }, { width: 640, height: 180 }, { width: 320, height: 320 }]) {
    for (const video of [{ width: 640, height: 480 }, { width: 1920, height: 1080 }, { width: 1080, height: 1920 }, { width: 3000, height: 1000 }]) {
      for (const rect of [guideFrameRect(view, video), printedNumberRect(view, video)]) {
        assert.ok(rect.x >= 0 && rect.y >= 0, JSON.stringify({ view, video, rect }));
        assert.ok(rect.width >= 1 && rect.height >= 1, JSON.stringify({ view, video, rect }));
        assert.ok(rect.x + rect.width <= video.width && rect.y + rect.height <= video.height, JSON.stringify({ view, video, rect }));
      }
    }
  }
});

test("the printed number area is the guide plus a margin below it, inside the viewfinder", () => {
  const view = { width: 300, height: 100 };
  const video = { width: 1500, height: 500 }; // scale 0.2, no cut
  const guide = guideFrameRect(view, video);
  const printed = printedNumberRect(view, video);

  assert.equal(printed.x, guide.x);
  assert.equal(printed.y, guide.y);
  assert.equal(printed.width, guide.width);
  // Bottom at (0.08 + 0.72 + 0.12) * 100 / 0.2 = 460, which is above the frame bottom (500).
  assert.equal(printed.y + printed.height, 460);
  assert.ok(printed.height > guide.height);
});

test("the whole viewfinder maps to the part of the frame that is visible", () => {
  // 300x100 view, 4000x1000 video: the visible part is x 500 to 3500, all 1000 high.
  assert.deepEqual(viewRectToFrame({ left: 0, top: 0, width: 1, height: 1 }, { width: 300, height: 100 }, { width: 4000, height: 1000 }), {
    x: 500,
    y: 0,
    width: 3000,
    height: 1000,
  });
});
