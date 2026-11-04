import fs from "fs";
import path from "path";

const imagesDir = path.resolve(__dirname, "../fixtures/images");

/**
 * Known fixture filenames only. A dynamic path.join/resolve against caller input
 * is refused so fixture lookup cannot traverse out of e2e/fixtures/images.
 */
const FIXTURES: Readonly<Record<string, string>> = {
  "pikachu.png": path.resolve(imagesDir, "pikachu.png"),
  "thumbnail-2.png": path.resolve(imagesDir, "thumbnail-2.png"),
};

/** Local fixture path. Images live in git, not in `.env.e2e`. */
export function e2eImage(filename: string): string {
  const filePath = FIXTURES[filename];
  if (!filePath) {
    throw new Error(
      `Unknown e2e image fixture '${filename}'. Allowed: ${Object.keys(FIXTURES).join(", ")}`,
    );
  }
  if (!fs.existsSync(filePath)) {
    throw new Error(
      `Missing e2e image fixture: ${filePath}. Drop the file in e2e/fixtures/images/.`,
    );
  }
  return filePath;
}

export const AVATAR_VALID = e2eImage("pikachu.png");
export const AVATAR_OVER_5MB = e2eImage("thumbnail-2.png");
