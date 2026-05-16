import { useLayoutEffect, useRef, useState } from "react";

export default function usePopoverWidth(): [React.RefObject<HTMLButtonElement>, number | undefined] {
  const ref = useRef<HTMLButtonElement>(null);
  const [width, setWidth] = useState<number | undefined>(undefined);

  useLayoutEffect(() => {
    if (ref.current) {
      setWidth(ref.current.offsetWidth);
    }
  }, []);

  return [ref, width];
}
