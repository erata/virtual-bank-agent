# LEGACY SCHEMA - NO AUTOMATIC ADOPTION.
# Earlier VirtualBank releases created business tables from raw bootstrap SQL.
# This release uses EF Core Code First migrations with a separate, least-privilege migrator.
# Existing volume is preserved. Read docs/EF_CORE_AND_AI_PROVIDERS.md before any conversion.
throw 'Legacy-to-EF migration needs an explicit data migration. This script does NOT drop tables or volumes. For a fresh EF demo installation, run scripts/windows/RESET_TO_EFCORE_WINDOWS.ps1 -ConfirmReset after reviewing its backup behavior.'
