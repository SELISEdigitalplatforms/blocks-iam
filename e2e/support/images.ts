import fs from "fs";
import path from "path";

const imagesDir = path.resolve(__dirname, "../fixtures/images");

/** Local fixture path. Images live in git, not in `.env.e2e`. */
export function e2eImage(filename: string): string {
  const filePath = path.join(imagesDir, filename);
  if (!fs.existsSync(filePath)) {
    throw new Error(
      `Missing e2e image fixture: ${filePath}. Drop the file in e2e/fixtures/images/.`,
    );
  }
  return filePath;
}

export const AVATAR_VALID = e2eImage("pikachu.png");
export const AVATAR_OVER_5MB = e2eImage("thumbnail-2.png");
