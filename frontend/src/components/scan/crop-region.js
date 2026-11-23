// Where the scan guide frame sits on the camera picture. Pure maths, no
// browser APIs, so it can be tested.
//
// The video is shown with object-fit: cover inside the viewfinder: it is
// scaled until it fills the viewfinder and the overflow is cut off evenly on
// both sides. The guide frame is drawn on the viewfinder, so its rectangle has
// to be mapped back to the real camera frame before a frame is cropped.

/** The gap between the guide frame and each edge of the viewfinder, in CSS pixels. */
export const GUIDE_MARGIN_PX = 12;

/**
 * The guide frame as fractions of the viewfinder (0 to 1): the same gap on all
 * four sides. On a very small viewfinder the gap shrinks so the frame stays open.
 * @param {{ width: number, height: number }} view the viewfinder size in CSS pixels
 */
export function guideFractions(view) {
  const margin = Math.min(GUIDE_MARGIN_PX, Math.min(view.width, view.height) / 4);
  const left = margin / view.width;
  const top = margin / view.height;
  return { left, top, width: 1 - 2 * left, height: 1 - 2 * top };
}

/**
 * Maps a rectangle given as fractions of the viewfinder to whole pixels of
 * the camera frame, for a video shown with object-fit: cover.
 * @param {{ left: number, top: number, width: number, height: number }} rect fractions of the viewfinder
 * @param {{ width: number, height: number }} view the viewfinder size in CSS pixels
 * @param {{ width: number, height: number }} video the camera frame size in pixels
 * @returns {{ x: number, y: number, width: number, height: number }}
 */
export function viewRectToFrame(rect, view, video) {
  const scale = Math.max(view.width / video.width, view.height / video.height);
  // The part of the scaled video that sticks out of the viewfinder, on each side.
  const cutX = (video.width * scale - view.width) / 2;
  const cutY = (video.height * scale - view.height) / 2;

  const left = ((rect.left * view.width + cutX) / scale);
  const top = ((rect.top * view.height + cutY) / scale);
  const right = (((rect.left + rect.width) * view.width + cutX) / scale);
  const bottom = (((rect.top + rect.height) * view.height + cutY) / scale);

  const x = Math.min(Math.max(Math.floor(left), 0), video.width - 1);
  const y = Math.min(Math.max(Math.floor(top), 0), video.height - 1);
  const x2 = Math.min(Math.max(Math.ceil(right), x + 1), video.width);
  const y2 = Math.min(Math.max(Math.ceil(bottom), y + 1), video.height);
  return { x, y, width: x2 - x, height: y2 - y };
}

/** The guide frame in camera frame pixels: the only area the barcode is looked for in. */
export function guideFrameRect(view, video) {
  return viewRectToFrame(guideFractions(view), view, video);
}

/**
 * The guide frame plus the gap below it, in camera frame pixels, for reading
 * the printed number (the digits sit under the barcode). It ends at the bottom
 * edge of the viewfinder, so nothing outside what the person sees is read.
 */
export function printedNumberRect(view, video) {
  const guide = guideFractions(view);
  return viewRectToFrame({ ...guide, height: 1 - guide.top }, view, video);
}
