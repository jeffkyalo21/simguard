// Run with `npm test`. The expected numbers are worked out by hand in the
// comments, not by calling the function being tested.
import { test } from "node:test";
import assert from "node:assert/strict";
import { GUIDE_MARGIN_PX, guideFractions, guideFrameRect, printedNumberRect, viewRectToFrame } from "./crop-region.js";

// Rounding the rectangle outwards to whole pixels can add up to 2 pixels (1 on each side).
function near(actual, expected, label) {
  assert.ok(Math.abs(actual - expected) <= 2, `${label}: ${actual} is not within 2 of ${expected}`);
}

test("the guide frame has the same gap on all four sides of the viewfinder", () => {
  for (const view of [{ width: 400, height: 144 }, { width: 288, height: 144 }, { width: 1000, height: 176 }]) {
    const g = guideFractions(view);
    const px = [g.left * view.width, g.top * view.height, (1 - g.left - g.width) * view.width, (1 - g.top - g.height) * view.height];
    for (const value of px) assert.ok(Math.abs(value - GUIDE_MARGIN_PX) < 1e-9, JSON.stringify({ view, px }));
  }
});

test("on a tiny viewfinder the gap shrinks so the guide frame stays open", () => {
  const g = guideFractions({ width: 40, height: 20 });
  assert.ok(g.width > 0 && g.height > 0);
  assert.ok(Math.abs(g.left * 40 - 5) < 1e-9 && Math.abs(g.top * 20 - 5) < 1e-9); // a quarter of the short side
});

test("a video with the same aspect as the viewfinder maps by a plain scale", () => {
  // 300x100 view, 1500x500 video: scale 0.2, nothing cut off. Gap 12 px.
  assert.deepEqual(guideFrameRect({ width: 300, height: 100 }, { width: 1500, height: 500 }), {
    x: 60, // 12 / 0.2
    y: 60, // 12 / 0.2
    width: 1380, // 276 / 0.2
    height: 380, // 76 / 0.2
  });
});

test("a video wider than the viewfinder is cut at the sides", () => {
  // 300x100 view, 4000x1000 video: scale 0.1 (height decides), shown 400 wide, 50 cut each side.
  assert.deepEqual(guideFrameRect({ width: 300, height: 100 }, { width: 4000, height: 1000 }), {
    x: 620, // (12 + 50) / 0.1
    y: 120, // 12 / 0.1
    width: 2760, // 276 / 0.1
    height: 760, // 76 / 0.1
  });
});

test("a video narrower than the viewfinder is cut at the top and bottom", () => {
  // 400x144 view, 1920x1080 video: scale 400/1920, shown 225 high, 40.5 cut top and bottom.
  const rect = guideFrameRect({ width: 400, height: 144 }, { width: 1920, height: 1080 });
  near(rect.x, 57.6, "x"); // 12 / (400/1920)
  near(rect.width, 1804.8, "width"); // 376 / (400/1920)
  near(rect.y, 252, "y"); // (12 + 40.5) / (400/1920)
  near(rect.height, 576, "height"); // 120 / (400/1920)
});

test("a portrait video in a wide viewfinder shows only a band across its middle", () => {
  // 360x144 view, 1080x1920 video: scale 1/3, shown 640 high, 248 cut top and bottom.
  const rect = guideFrameRect({ width: 360, height: 144 }, { width: 1080, height: 1920 });
  near(rect.x, 36, "x"); // 12 * 3
  near(rect.width, 1008, "width"); // 336 * 3
  near(rect.y, 780, "y"); // (12 + 248) * 3
  near(rect.height, 360, "height"); // 120 * 3
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

test("the printed number area is the guide plus the gap below it, ending at the viewfinder edge", () => {
  const view = { width: 300, height: 100 };
  const video = { width: 1500, height: 500 }; // scale 0.2, no cut
  const guide = guideFrameRect(view, video);
  const printed = printedNumberRect(view, video);

  assert.equal(printed.x, guide.x);
  assert.equal(printed.y, guide.y);
  assert.equal(printed.width, guide.width);
  // The viewfinder bottom is the frame bottom here: 100 / 0.2 = 500.
  assert.equal(printed.y + printed.height, 500);
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
