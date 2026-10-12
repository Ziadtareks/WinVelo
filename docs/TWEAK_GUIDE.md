# Tweak Authoring Guide

Every tweak in Tweakora is defined in JSON inside the `Catalog/Tweaks/` directory and validated against `Catalog/schema/tweak.schema.json`.

## Rules for Adding a Tweak

1. **Must be Documented**:
   Include a Microsoft Learn URL or official manufacturer specification in `source`.
2. **Reversibility**:
   Must define an `undo` list as a fallback. Engine records actual state in Rollback Journal automatically.
3. **Accuracy**:
   Set `windows.minBuild`, `windows.maxBuild`, and `windows.editions` accurately.
   If behavior differs by edition (e.g., Home vs Enterprise), explain it in `effectNote`.
4. **Detection Rule**:
   Define `detect` accurately using Registry, Service, ScheduledTask, Appx, or OptionalFeature.
5. **No Hallucinated Keys**:
   If a tweak cannot be verified against official documentation, place it in `docs/NEEDS_VERIFICATION.md`.
