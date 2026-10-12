# Tweakora Safety System

Quality, safety, and system stability come before speed and features.

## 1. Safety Pillars

1. **System Restore Point Verification**
   - Query system restore points before creation.
   - Request creation via `SRSetRestorePoint`.
   - Query restore points again to confirm a new restore point actually exists.
   - If Windows blocks creation (e.g. 24-hour limit or disabled), **never proceed silently**. Display a dialog asking the user whether to continue with Registry backup only or cancel.

2. **Registry Backup (.reg)**
   - Before modifying any key, export the target key to `Backups/registry-[timestamp].reg`.

3. **Write-Ahead Rollback Journal**
   - The Rollback Journal records the *real* current system state before any action is executed.
   - **Write-ahead logging**: The journal entry is written to disk and flushed before executing the action.
   - **Baseline preservation**: If a tweak is applied again, the initial baseline value is preserved. It is never overwritten by a tweaked value.
   - If a value did not exist originally, `existedBefore: false` is recorded. Rollback deletes the created key/value.
   - The `undo` array in the tweak catalog is only a static fallback if the journal file is missing.

4. **Crash / Unfinished Session Recovery**
   - On application startup, Tweakora inspects the journal directory.
   - If an unfinished session is detected, a recovery dialog is displayed giving the user the option to rollback or resume.

5. **Safe Development Rules**
   - During development on the host PC, **no tweak or system modification may be run**.
   - Unit tests must use mock/fake registry and service layers (`FakeRegistry`, `FakeServiceManager`).
   - Real-system verification only takes place in dedicated test virtual machines.
