interface LogoProps {
  src?: string;
  alt?: string;
  width?: number;
  height?: number;
  className?: string;
}

import { useTheme } from "@/hooks/use-theme";

export function Logo({ src, alt, width, height, className }: LogoProps) {
  const { resolvedTheme } = useTheme();
  const imgSrc = src || (resolvedTheme === "dark" ? "/Logo_White.svg" : "/Logo.svg");
  return (
    <img
      src={imgSrc}
      alt={alt ?? "SELISE Logo"}
      width={width}
      height={height}
      className={className}
    />
  );
}
