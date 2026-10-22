# Archived manual Unity Editor self-tests

The nine scripts here were moved from `Editor/` at v0.7.4 to remove all default **Run ... Self-Test** menus and unnecessary Editor compilation overhead. They do **not** run automatically, and `Tests~` is ignored by Unity's Package Manager/AssetDatabase.

These source files are retained **only as development regression fixtures**, not part of the production API. If a maintainer intentionally needs to rerun a test, use a development clone of the package and copy the required `.cs` into its `Editor/` directory *within the same assembly* as the C&C SAGE source. Remove it afterwards. Do not copy these internal fixtures to an unrelated project assembly, since they use internal Editor helpers. Keep original project W3X/assets untouched.

The normal installed version has no Run Self-Test menu items. Main commands for model and animation export and the read-only round-trip audit remain under `Tools > C&C SAGE`.
