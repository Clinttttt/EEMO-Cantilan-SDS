---
name: stalltrack-backend-review
description: Pre-commit checklist for every StallTrack backend commit in the Claude Backend lane. Use immediately before staging and committing.
---

# StallTrack backend pre-commit review

Answer each question explicitly (in the handoff checkpoint where useful):

1. What business rule supports this change, and where is it written?
2. What source owns this fact?
3. Could this duplicate money?
4. Could this issue or reuse a physical OR/CT incorrectly?
5. Can tenant A reach tenant B (read, mutate, FK, outcome replay)?
6. Can a retry create a second effect?
7. Did I mutate historical evidence (posted rows, issued documents, old policies, old migrations)?
8. Did I accidentally activate a source, change settlement authority, or switch a production report?
9. Does reporting remain derived from posted events?
10. Did I modify UI files (`*.razor`, `*.razor.css`, `wwwroot`, UI docs)? If yes, revert unless it is an unavoidable
    compile fix.
11. Is every migration additive, and does `has-pending-model-changes` report none?
12. Do the tests prove the dangerous paths, and did at least one fail before the fix?

Then:

```bash
git diff --check
git diff --stat
git status
git add <explicit paths>
git diff --cached --name-only
```

Commit one bounded gap with a conventional message (`fix(wcf): …`, `feat(reports): …`). Never push, merge or deploy.
