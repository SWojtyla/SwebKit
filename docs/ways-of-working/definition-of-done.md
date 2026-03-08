# Definition of Done

A task or feature is done only when the implementation, validation, and documentation are aligned.

## Minimum conditions

A task is not done until all applicable items below are satisfied:

- The requested behavior is implemented.
- The code follows current architecture and design constraints.
- Relevant tests are added, updated, or explicitly addressed.
- Existing tests still pass, or known failures are clearly called out.
- Related documentation is updated.
- Important technical decisions are recorded when needed.
- No known blockers are being hidden.
- The current status is reflected accurately in the feature docs.

## Code quality

Done means:

- no unnecessary complexity
- naming is clear
- the change is coherent
- dead code or temporary scaffolding is removed unless intentionally kept
- follow-up debt is explicit if it remains

## Validation

Validation may include:

- automated tests
- manual verification
- smoke testing
- contract checks
- UI verification
- integration checks

Validation should match the risk and scope of the change.

## Documentation

Documentation is done when:

- the active feature docs match the actual implementation
- test expectations are current
- major tradeoffs are recorded
- outdated statements are removed or corrected

## Not done

A task is not done if any of these are true:

- the code was changed but docs were not updated
- the docs claim work that was not completed
- tests were skipped without explanation
- assumptions were made without recording them
- blockers still exist but are not visible in `status.md`
- the change cannot be reasonably reviewed by another human

## Ready for archive

A completed feature is ready for archive when:

- the feature status is `Done`
- active implementation work has stopped
- the final state is documented clearly
- reusable lessons are preserved
- there is no expectation of immediate further iteration in the active folder
