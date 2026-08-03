import { useScopedPath } from "@seliseblocks/genesis-os/hooks";

export { useScopedPath };

/** `/app/:itemId/iam` */
export const useIamBasePath = () => useScopedPath()("iam");
