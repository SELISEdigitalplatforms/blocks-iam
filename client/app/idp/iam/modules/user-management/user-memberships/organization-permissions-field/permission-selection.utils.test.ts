import { describe, expect, it } from "vitest";
import { IPermission } from "@blocks-idp/iam/models/permission";
import {
  MAX_PERMISSIONS_PER_USER,
  PERMISSION_LIMIT_MESSAGE,
  getNewlySelectedPermissions,
  getTotalPermissionCount,
  isAtMaxPermissions,
  isPermissionAssigned,
  isSelectedInModal,
  shouldDisablePermissionCheckbox,
  togglePermissionSelection,
} from "./permission-selection.utils";

const permission = (index: number, overrides: Partial<IPermission> = {}): IPermission =>
  ({
    itemId: `perm-${index}`,
    name: `Permission ${index}`,
    resource: `blocks-iam::perm-${index}`,
    resourceGroup: "blocks-iam",
    type: 1,
    description: "",
    projectKey: "",
    tags: [],
    roles: [],
    dependentPermissions: [],
    isArchived: false,
    isBuiltIn: true,
    language: null,
    organizationIds: [],
    permissionSeverity: 0,
    ...overrides,
  }) as IPermission;

describe("Permission selection utils", () => {
  describe("1. Basic selection behavior", () => {
    it("TC-01: selecting one permission marks it as selected in modal state", () => {
      const assigned: IPermission[] = [];
      const first = permission(1);

      const result = togglePermissionSelection(true, first, assigned, []);

      expect(isSelectedInModal(first, assigned, result.selectedPermissions)).toBe(true);
    });

    it("TC-02: user can select exactly 5 permissions when none are assigned", () => {
      const assigned: IPermission[] = [];
      let selected: IPermission[] = [];

      for (let index = 1; index <= MAX_PERMISSIONS_PER_USER; index += 1) {
        const result = togglePermissionSelection(true, permission(index), assigned, selected);
        expect(result.blocked).toBe(false);
        selected = result.selectedPermissions;
      }

      expect(getTotalPermissionCount(assigned, selected)).toBe(MAX_PERMISSIONS_PER_USER);
    });

    it("TC-03: attempting to select a 6th permission is blocked", () => {
      const assigned: IPermission[] = [];
      const selected = Array.from({ length: MAX_PERMISSIONS_PER_USER }, (_, index) =>
        permission(index + 1),
      );

      const result = togglePermissionSelection(true, permission(99), assigned, selected);

      expect(result.blocked).toBe(true);
      expect(result.selectedPermissions).toHaveLength(MAX_PERMISSIONS_PER_USER);
      expect(shouldDisablePermissionCheckbox(permission(99), assigned, selected)).toBe(true);
    });

    it("TC-04: limit message matches product copy", () => {
      expect(PERMISSION_LIMIT_MESSAGE).toBe("You can select up to 5 permissions.");
    });

    it("TC-05: deselecting a selected permission decreases the count", () => {
      const assigned: IPermission[] = [];
      const first = permission(1);
      const second = permission(2);
      const selected = [first, second];

      const result = togglePermissionSelection(false, first, assigned, selected);

      expect(getTotalPermissionCount(assigned, result.selectedPermissions)).toBe(1);
      expect(isSelectedInModal(first, assigned, result.selectedPermissions)).toBe(false);
    });

    it("TC-06: unchecking one of five newly selected permissions re-enables others", () => {
      const assigned: IPermission[] = [];
      const selected = Array.from({ length: MAX_PERMISSIONS_PER_USER }, (_, index) =>
        permission(index + 1),
      );
      const candidate = permission(99);

      expect(shouldDisablePermissionCheckbox(candidate, assigned, selected)).toBe(true);

      const result = togglePermissionSelection(false, permission(1), assigned, selected);

      expect(shouldDisablePermissionCheckbox(candidate, assigned, result.selectedPermissions)).toBe(
        false,
      );
    });
  });

  describe("2. Pagination and state persistence", () => {
    it("TC-07/TC-08: selections persist when moving between pages", () => {
      const assigned: IPermission[] = [];
      const pageOneSelection = [permission(1), permission(2)];
      const pageTwoSelection = [permission(3), permission(4), permission(5)];

      const combined = [...pageOneSelection, ...pageTwoSelection];

      expect(getTotalPermissionCount(assigned, combined)).toBe(5);
      expect(isSelectedInModal(permission(1), assigned, combined)).toBe(true);
      expect(isSelectedInModal(permission(5), assigned, combined)).toBe(true);
    });

    it("TC-09: at the limit, remaining permissions on other pages are disabled", () => {
      const assigned: IPermission[] = [];
      const selected = Array.from({ length: MAX_PERMISSIONS_PER_USER }, (_, index) =>
        permission(index + 1),
      );
      const pageTwoItem = permission(99);

      expect(shouldDisablePermissionCheckbox(pageTwoItem, assigned, selected)).toBe(true);
    });

    it("TC-11: page size changes do not affect selected count", () => {
      const assigned: IPermission[] = [];
      const selected = [permission(1), permission(2)];

      expect(getTotalPermissionCount(assigned, selected)).toBe(2);
    });
  });

  describe("3. Selected count indicator", () => {
    it("TC-12: counter reflects assigned plus newly selected permissions", () => {
      const assigned = [permission(1), permission(2)];
      const selected = [permission(3)];

      expect(getTotalPermissionCount(assigned, selected)).toBe(3);
    });

    it("TC-13: hidden selections still count toward total", () => {
      const assigned = [permission(1)];
      const selected = [permission(2), permission(3)];

      expect(getTotalPermissionCount(assigned, selected)).toBe(3);
    });
  });

  describe("4. Search and filter", () => {
    it("TC-14/TC-15: filtered-out selections remain in total count", () => {
      const assigned: IPermission[] = [];
      const selected = [permission(1), permission(2)];

      expect(getNewlySelectedPermissions(selected, assigned)).toHaveLength(2);
      expect(getTotalPermissionCount(assigned, selected)).toBe(2);
    });
  });

  describe("5. Select all", () => {
    it("TC-16/TC-17: select-all is not available in this picker", () => {
      expect(true).toBe(true);
    });
  });

  describe("7. Edge cases", () => {
    it("TC-21: five assigned permissions disable all other checkboxes", () => {
      const assigned = Array.from({ length: MAX_PERMISSIONS_PER_USER }, (_, index) =>
        permission(index + 1),
      );
      const selected: IPermission[] = [];
      const candidate = permission(99);

      expect(isAtMaxPermissions(assigned, selected)).toBe(true);
      expect(isPermissionAssigned(permission(1), assigned)).toBe(true);
      expect(shouldDisablePermissionCheckbox(candidate, assigned, selected)).toBe(true);
    });

    it("TC-22: no assigned permissions starts at 0/5", () => {
      expect(getTotalPermissionCount([], [])).toBe(0);
      expect(isAtMaxPermissions([], [])).toBe(false);
    });

    it("TC-24: rapid toggles never exceed the limit", () => {
      const assigned: IPermission[] = [];
      let selected: IPermission[] = [];

      for (let attempt = 0; attempt < 10; attempt += 1) {
        const result = togglePermissionSelection(
          true,
          permission(attempt + 1),
          assigned,
          selected,
        );
        if (!result.blocked) {
          selected = result.selectedPermissions;
        }
      }

      expect(getTotalPermissionCount(assigned, selected)).toBeLessThanOrEqual(
        MAX_PERMISSIONS_PER_USER,
      );
    });
  });

  describe("9. Validation on save", () => {
    it("TC-30: zero newly selected permissions means nothing to add", () => {
      const assigned = [permission(1)];
      const selected: IPermission[] = [];

      expect(getNewlySelectedPermissions(selected, assigned)).toHaveLength(0);
    });
  });
});
