import { useEffect, useState } from "react";

/**
 * Mirrors a boolean flag but keeps it `true` for at least `minMs` once it turns
 * on, so a fast API response doesn't make a loading indicator flash imperceptibly.
 */
export const useMinDurationFlag = (flag: boolean, minMs = 400) => {
  const [visible, setVisible] = useState(flag);

  useEffect(() => {
    if (flag) {
      setVisible(true);
      return;
    }
    const timeout = setTimeout(() => setVisible(false), minMs);
    return () => clearTimeout(timeout);
  }, [flag, minMs]);

  return visible;
};
