using Blocks.Genesis;

namespace Iam.DomainService.Resources.ResponseModel
{
    /// <summary>
    /// The result of creating a role, plus what a default-organization administrator needs to know
    /// before confirming a name that is already used elsewhere.
    /// </summary>
    /// <remarks>
    /// Counts only, never identities. Naming the organizations would hand one administrator another
    /// organization's role inventory, and the numbers are all a confirmation needs. The advisory is
    /// computed for default-organization callers only: a child-organization caller's collisions are
    /// either with its own roles, which its own uniqueness rule already refuses, or with sibling
    /// organizations, which it has no business learning about.
    /// <para>
    /// Nothing here blocks anything permanently. A same-name pair is a state propagation produces on
    /// its own, so the point is that the administrator is not surprised by it, not that it is
    /// prevented.
    /// </para>
    /// </remarks>
    public sealed class CreateRoleResponse : BaseMutationResponse
    {
        /// <summary>
        /// True on the one refusal that a second attempt can clear by setting
        /// <c>ConfirmDuplicateName</c>. False on success and on every other failure, so a client can
        /// tell "ask the user" apart from "show the error".
        /// </summary>
        public bool RequiresDuplicateNameConfirmation { get; set; }

        /// <summary>
        /// OTHER organizations owning a live role with this name, so the number reads as
        /// "N other organizations". Excludes the caller's own organization, default-derived copies,
        /// and archived roles.
        /// </summary>
        public int DuplicateNameOrganizationCount { get; set; }

        /// <summary>
        /// The subset of <see cref="DuplicateNameOrganizationCount"/> whose role also holds the slug
        /// being created, which is the only case where an organization will NOT receive the new
        /// role: the insert skips any organization already holding the slug. Normally zero, since a
        /// private slug carries an organization fragment and a default slug does not -- it is the
        /// bare-slugged private roles created before the create guard existed that land here.
        /// </summary>
        public int SlugConflictOrganizationCount { get; set; }
    }
}
