
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { useGetUserById } from "@blocks-idp/iam/hooks/use-user";
import { useGetOrganizations } from "@blocks-idp/iam/hooks/use-organization";
import { useGetPermissions } from "@blocks-idp/iam/hooks/use-permission";
import { UserMembershipsList } from "./user-memberships-list";
import { AssignOrganization } from "./assign-organization";
import { useMemo } from "react";

type UserMembershipsProps = {
    id: string;
    projectKey: string;
};

export const UserMemberships = ({ id, projectKey }: UserMembershipsProps) => {
    const { data: userData, isLoading: isUserLoading } = useGetUserById({ id, projectKey });
    const { data: orgsData, isLoading: isOrgsLoading } = useGetOrganizations({
        projectKey,
        page: 0,
        pageSize: 1000,
    });
    const { data: permissionsData } = useGetPermissions({
        projectKey,
        page: 0,
        pageSize: 1000,
        search: "",
        isBuiltIn: "",
        roles: [],
    });

    const memberships = useMemo(() => {
        const user = userData?.data;
        if (!user) return [];

        if (user.organizations?.length > 0) return user.organizations;

        const orgIds =
            user.organizationIds?.length > 0
                ? user.organizationIds
                : (user as { OrganizationIds?: string[] }).OrganizationIds ?? [];

        const { roles, permissions } = user;

        // Flat legacy roles/permissions arrays aren't attributable to a specific
        // organization once the user belongs to more than one, so only apply them
        // when there's a single org to avoid showing every org as if it had the
        // full combined set.
        const isSingleOrg = orgIds.length === 1;

        return orgIds.map((orgId) => ({
            organizationId: orgId,
            roles: Array.isArray(roles) ? (isSingleOrg ? roles : []) : (roles?.[orgId] ?? []),
            permissions: Array.isArray(permissions)
                ? isSingleOrg
                    ? permissions
                    : []
                : (permissions?.[orgId] ?? []),
        }));
    }, [userData?.data]);

    const permissionGroupMap = useMemo(() => {
        const map = new Map<string, string>();
        (permissionsData?.data || []).forEach((permission) => {
            map.set(permission.name, permission.resourceGroup || "Other");
        });
        return map;
    }, [permissionsData?.data]);
    const organizationIds = useMemo(() => {
        const user = userData?.data;
        if (!user) return [];

        if (user.organizationIds && user.organizationIds.length > 0) {
            return user.organizationIds;
        }

        return (user as { OrganizationIds?: string[] }).OrganizationIds ?? [];
    }, [userData?.data]);

    // Create a map of organizationId to organizationName
    const orgNameMap = useMemo(() => {
        const map = new Map<string, string>();
        const organizations = orgsData?.organizations || [];
        organizations.forEach((org) => {
            map.set(org.itemId, org.name);
        });
        return map;
    }, [orgsData?.organizations]);

    const isLoading = isUserLoading || isOrgsLoading;

    return (
        <Card>
            <CardHeader className="flex flex-row items-center justify-between">
                <CardTitle>Organization</CardTitle>
                <AssignOrganization
                    userId={id}
                    organizations={orgsData?.organizations ?? []}
                    isOrgsLoading={isOrgsLoading}
                />
            </CardHeader>
            <CardContent>
                <UserMembershipsList
                    memberships={memberships}
                    organizationIds={organizationIds}
                    orgNameMap={orgNameMap}
                    permissionGroupMap={permissionGroupMap}
                    isLoading={isLoading}
                    userId={id}
                    projectKey={projectKey}
                />
            </CardContent>
        </Card>
    );
};
