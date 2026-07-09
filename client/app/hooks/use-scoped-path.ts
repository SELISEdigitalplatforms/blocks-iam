import { useScopedPath } from "@seliseblocks/blocks-kit";

export { useScopedPath };

/** `/app/:itemId/iam` */
export const useIamBasePath = () => useScopedPath()("iam");
