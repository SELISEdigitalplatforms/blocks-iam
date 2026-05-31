interface LogoProps {
  src?: string;
  alt?: string;
  width?: number;
  height?: number;
  className?: string;
}


export function Logo({ src, alt, width, height, className }: LogoProps) {

  if (src) {
    return <img src={src} alt={alt ?? "SELISE Logo"} width={width} height={height} className={className} />;
  }

  return (
    <>
      <img
        src="/blocks-logos/iam_light_mode.svg"
        alt={alt ?? "SELISE Logo"}
        width={width}
        height={height}
        className={`${className ?? ""} dark:hidden`}
      />
      <img
        src="/blocks-logos/iam_dark_mode.svg"
        alt={alt ?? "SELISE Logo"}
        width={width}
        height={height}
        className={`${className ?? ""} hidden dark:block`}
      />
    </>
  );
}
