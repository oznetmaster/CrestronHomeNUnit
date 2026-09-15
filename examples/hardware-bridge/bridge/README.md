# Automatic hardware checks

The controller runs on a GitHub-hosted machine in this private repository. Every ten minutes it looks for eligible source changes, selects one exact revision, creates a check in its source repository, invokes the existing Windows processor workflow, and reports its result. GitHub schedules are best-effort; this is not a ten-minute completion guarantee.

The Windows machine receives no GitHub App key or check-writing token. Processor credentials and raw results stay in its protected local folder. The hosted reporter writes only a fixed summary, source revisions and a link to the private orchestration run.

## Eligibility

- Default-branch revisions run only after the configured hosted test workflows pass for that exact SHA.
- Same-repository pull requests also need the `hardware-approved` label and an approving review from a configured `trustedReviewers` account for the exact head SHA. A later commit requires a fresh review. Requested changes or a dismissed approval block the request. The author cannot approve their own request.
- Fork pull requests are not executed on the development computer by this bridge. Review and integrate the changes into a trusted branch first. Do not put a self-hosted job in `pull_request_target` that checks out incoming code.
- Library changes are checked in the independent library repository. The collection's default-branch package definition must pass its own hosted gates. The worker changes only the selected library's source pin in its disposable checkout; all other dependencies remain pinned. The check records both revisions. No processor projects are added to independent library repositories.
- The collection also receives one check per package for its own exact revision and original locked dependencies. These `collection_<target>` checks pass no library override. They cover default-branch changes and approved same-repository PRs in the collection. A changed collection revision also makes independent library/package pairs eligible again.
- Successful and failed requests are remembered through this App's checks for the source/package pair. Another App's check cannot substitute for this one. Failures do not retry automatically. After checking private evidence, cleanup and any retained lease, explicitly dispatch with `retry_failed: true` and the target ID.

One controller is active at a time and selects one request per invocation. Hardware execution shares the existing `development-processor` concurrency group and the processor lease with manual runs. Pending hardware runs use GitHub's `queue: max`; incoming schedules may be coalesced, but cannot cancel an active test. Tests still remove their temporary instance and release their lease. An interrupted controller without a confirmed report is marked failed when next observed; it is never inferred to have passed.

## GitHub App provisioning

Create a private GitHub App owned by the repository owner. Webhooks, OAuth callbacks, user authorization and expiring user tokens are not needed. Use these repository permissions only:

| Permission | Access | Purpose |
|---|---|---|
| Metadata | Read | Repository identity and default branch |
| Contents | Read | Resolve exact source revisions |
| Actions | Read | Verify hosted tests on the candidate SHA |
| Pull requests | Read | Inspect label and exact-revision approvals |
| Checks | Read and write | Create and complete the hardware check |

Install the App only on the source repositories listed in `bridge-config.json` and the package-definition repository. It needs no access to this private orchestration repository: its ordinary `GITHUB_TOKEN` reads its own run status. The App needs no contents write, administration, issues write, secrets, workflow write or package publishing permissions.

In the private orchestration repository set:

- Variable `HARDWARE_BRIDGE_APP_ID`: the numeric App ID.
- Secret `HARDWARE_BRIDGE_PRIVATE_KEY`: its generated private key.
- Variable `HARDWARE_BRIDGE_ENABLED`: `true`, after provisioning and review.

The App action creates short-lived installation tokens independently in the hosted planning and reporting jobs and revokes them afterward. Never copy the key into ProgramData, a source repository, a workflow artifact or the self-hosted service profile.

First manually dispatch **Automatic processor tests** with `dry_run: true` and a target. Then dispatch with `dry_run: false` to validate its real check, exact source, hardware results and cleanup. No source/package release is triggered by this workflow.

After a successful real run, make `Processor tests / <target ID>` required in that source repository's branch protection or ruleset. Select this GitHub App as the expected source of the check. Preserve existing protections and required checks. An unavailable development machine, failed run, unapproved PR, or missing result must leave the gate unsatisfied. For single-maintainer development, normal-branch pushes can be tested automatically; a self-authored PR cannot satisfy the independent reviewer policy without another trusted reviewer.

Branch protection is a separate repository setting, not established merely by adding these files. Enable it only after the App check has been registered and validated. Do not claim pre-merge gating from a passing manual processor run.

## Validation and maintenance

`node --test bridge/*.test.mjs` tests exact revision identity, library/package pairing, hosted gates, stale/dismissed approvals, fork exclusion, duplicate suppression, interruption recovery and result reporting without network or processor access.

Review changes to `bridge-config.json`, this controller and the private workflow as infrastructure changes. Keep trusted reviewers current. The controller does not execute source scripts during planning or reporting. Its source API requests are read-only until it creates or updates its own checks; network failures stop the decision rather than yielding a partial pass.

Scheduled automation is disabled until `HARDWARE_BRIDGE_ENABLED` is true and the App is configured. Explicit manual validation is available once the App ID and key are provisioned. Adding a scheduled YAML file alone does not establish an operating cross-repository integration.

References: [GitHub App authentication](https://docs.github.com/en/actions/how-tos/security-for-github-actions/security-guides/making-authenticated-api-requests-with-a-github-app-in-a-github-actions-workflow), [check runs](https://docs.github.com/en/rest/checks/runs), [concurrency queues](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency).

## Validated installation - 2026-09-15

The private installation has completed App authentication, an exact-library-revision hardware run, and a successful cross-repository report on KasaTapoClient. The App is installed only on the approved 14 source/package repositories. Its sole working private key is stored as an encrypted repository secret, and the temporary local download was removed. The remaining target/check registrations and branch gates are still being validated; the scheduled switch remains off during rollout.
