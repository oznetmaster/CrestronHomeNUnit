// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { validateConfig, latestWorkflowPassed, hasCurrentApproval, requestKey, selectCandidate, checkName } from './policy.mjs';
import { plan, report } from './controller.mjs';

const sha = 'a'.repeat(40), newer = 'b'.repeat(40), packageSha = 'c'.repeat(40);
const target = { id: 'sample_driver', repository: 'Driver', packageRepository: 'Driver', workflows: ['tests.yml'] };
const config = { owner: 'example', orchestration: 'HardwareCI', approvalLabel: 'hardware-approved', trustedReviewers: ['maintainer'], packageWorkflows: ['validate.yml'], targets: [target] };
const context = { appId: '17', runId: '123', dryRun: false, retryFailed: false };
const candidate = { target, revision: sha, packageRevision: sha, checks: [], lastTested: 0 };
const run = { id: 1, head_sha: sha, repository: { full_name: 'example/Driver' }, status: 'completed', conclusion: 'success', event: 'push', run_attempt: 1 };
const review = { id: 1, state: 'APPROVED', commit_id: sha, user: { login: 'maintainer' } };
function savedCheck(c = candidate, overrides = {}) {
  return { id: 5, name: checkName(c.target.id), app: { id: 17 }, head_sha: c.revision, status: 'completed', conclusion: 'success',
    external_id: JSON.stringify({ schema: 1, key: requestKey(c), runId: '111' }), ...overrides };
}

test('all concrete targets have valid, unique identities and upstream test gates', async () => {
  const concrete = JSON.parse(await readFile(new URL('../bridge-config.json', import.meta.url), 'utf8'));
  assert.ok(validateConfig(concrete).targets.length > 0);
});
test('invalid and duplicate identities are rejected', () => {
  assert.throws(() => validateConfig({ ...config, targets: [target, target] }));
  assert.throws(() => validateConfig({ ...config, targets: [{ ...target, repository: '../other' }] }));
  assert.throws(() => validateConfig({ ...config, targets: [{ ...target, workflows: [] }] }));
});
test('hosted gate requires success for the exact revision and repository', () => {
  assert.equal(latestWorkflowPassed([run], sha, 'example/Driver'), true);
  assert.equal(latestWorkflowPassed([run], newer, 'example/Driver'), false);
  assert.equal(latestWorkflowPassed([run], sha, 'attacker/Driver'), false);
  assert.equal(latestWorkflowPassed([{ ...run, conclusion: 'skipped' }], sha, 'example/Driver'), false);
});
test('a pending or failed newer hosted run supersedes an older success', () => {
  assert.equal(latestWorkflowPassed([run, { ...run, id: 2, status: 'in_progress' }], sha, 'example/Driver'), false);
  assert.equal(latestWorkflowPassed([run, { ...run, id: 2, conclusion: 'failure' }], sha, 'example/Driver'), false);
});
test('PR approval must be from a configured reviewer, not the author, for the exact head', () => {
  assert.equal(hasCurrentApproval([review], sha, new Set(['maintainer']), 'contributor'), true);
  assert.equal(hasCurrentApproval([review], newer, new Set(['maintainer']), 'contributor'), false);
  assert.equal(hasCurrentApproval([review], sha, new Set(['someone']), 'contributor'), false);
  assert.equal(hasCurrentApproval([review], sha, new Set(['maintainer']), 'maintainer'), false);
});
test('dismissed approvals and requested changes block execution', () => {
  for (const state of ['DISMISSED', 'CHANGES_REQUESTED']) {
    assert.equal(hasCurrentApproval([review, { ...review, id: 2, state }], sha, new Set(['maintainer']), 'contributor'), false);
  }
});
test('an untrusted review cannot override a trusted approval', () => {
  assert.equal(hasCurrentApproval([review, { ...review, id: 2, user: { login: 'outsider' }, state: 'CHANGES_REQUESTED' }], sha, new Set(['maintainer']), 'contributor'), true);
});
test('library/package revision pair is part of result identity', () => {
  assert.notEqual(requestKey(candidate), requestKey({ ...candidate, packageRevision: packageSha }));
  assert.notEqual(requestKey(candidate), requestKey({ ...candidate, revision: newer }));
  assert.throws(() => requestKey({ ...candidate, revision: 'main' }));
});
test('the same passing revision is not queued again', () => {
  assert.equal(selectCandidate([{ ...candidate, checks: [savedCheck()] }], 17), null);
});
test('a check from another App cannot satisfy our gate', () => {
  assert.ok(selectCandidate([{ ...candidate, checks: [savedCheck(candidate, { app: { id: 999 } })] }], 17));
});
test('failed results require a deliberate retry and active work is never retried', () => {
  const failed = { ...candidate, checks: [savedCheck(candidate, { conclusion: 'failure' })] };
  assert.equal(selectCandidate([failed], 17), null);
  assert.ok(selectCandidate([failed], 17, true));
  assert.equal(selectCandidate([{ ...candidate, checks: [savedCheck(candidate, { status: 'in_progress', conclusion: null })] }], 17, true), null);
});
test('targets with no previous execution have priority', () => {
  assert.equal(selectCandidate([{ ...candidate, lastTested: 100 }, { ...candidate, target: { ...target, id: 'other' } }], 17).target.id, 'other');
});

function fakeApi({ checks = [], pulls = [], reviews = [review], gatePasses = true, previousRunStatus = 'completed' } = {}) {
  const writes = [];
  return {
    writes,
    async request(method, path, body) {
      if (method !== 'GET') { writes.push({ method, path, body }); return { id: 100, ...body }; }
      if (path.includes('/actions/runs/')) return { status: previousRunStatus };
      if (path.includes('/commits/')) return { sha: path.includes('/Packages/') ? packageSha : sha };
      return { default_branch: 'main' };
    },
    async list(path) {
      if (path.includes('/check-runs')) return structuredClone(checks);
      if (path.includes('/reviews')) return reviews;
      if (path.includes('/pulls?')) return pulls;
      if (path.includes('/actions/workflows/')) {
        const revision = new URL(`https://example.invalid${path}`).searchParams.get('head_sha');
        const repository = path.split('/').slice(2, 4).join('/');
        return [{ ...run, head_sha: revision, repository: { full_name: repository }, conclusion: gatePasses ? 'success' : 'failure' }];
      }
      throw new Error(`Unexpected list ${path}`);
    }
  };
}
test('planner creates one check for an eligible exact default-branch revision', async () => {
  const api = fakeApi();
  const result = await plan(config, api, api, context);
  assert.equal(result.revision, sha);
  assert.equal(result.checkId, 100);
  assert.equal(api.writes.length, 1);
  assert.equal(api.writes[0].body.head_sha, sha);
  assert.ok(api.writes[0].body.external_id.length <= 255, 'GitHub truncates longer external IDs');
  assert.equal(JSON.parse(api.writes[0].body.external_id).key, result.key);
});
test('dry run makes no mutations and failed hosted gates prevent scheduling', async () => {
  const api = fakeApi();
  assert.equal((await plan(config, api, api, { ...context, dryRun: true })).dryRun, true);
  assert.equal(api.writes.length, 0);
  const failed = fakeApi({ gatePasses: false });
  assert.equal(await plan(config, failed, failed, context), null);
  assert.equal(failed.writes.length, 0);
});
test('library checks report library SHA and separately carry the package SHA', async () => {
  const library = { ...target, id: 'sample_library', packageRepository: 'Packages', sourceName: 'Client' };
  const api = fakeApi();
  const result = await plan({ ...config, targets: [library] }, api, api, context);
  assert.equal(result.revision, sha);
  assert.equal(result.libraryRevision, sha);
  assert.equal(result.packageRevision, packageSha);
});
test('collection checks exercise the exact package definition with its original pinned sources', async () => {
  const collection = { id: 'collection_sample', repository: 'Packages', packageRepository: 'Packages', workflows: ['validate.yml'], workflowTarget: target.id };
  const api = fakeApi();
  const result = await plan({ ...config, targets: [target, collection] }, api, api, { ...context, target: collection.id });
  assert.equal(result.repository, 'Packages');
  assert.equal(result.revision, packageSha);
  assert.equal(result.packageRevision, packageSha);
  assert.equal(result.libraryRevision, '');
  assert.equal(result.workflowTarget, target.id);
  assert.equal(result.target, collection.id);
});
test('unlabelled, fork and unapproved PRs cannot reach hardware', async () => {
  for (const pull of [
    { labels: [], head: { sha: newer, repo: { full_name: 'example/Driver' } } },
    { labels: [{ name: 'hardware-approved' }], head: { sha: newer, repo: { full_name: 'outsider/Driver' } } },
    { labels: [{ name: 'hardware-approved' }], head: { sha: newer, repo: { full_name: 'example/Driver' } } }
  ]) {
    const api = fakeApi({ checks: [savedCheck()], pulls: [{ number: 1, user: { login: 'contributor' }, ...pull }] });
    assert.equal(await plan(config, api, api, context), null);
  }
});
test('labelled same-repository PR with approval for its exact head is selected', async () => {
  const api = fakeApi({ checks: [savedCheck()], reviews: [{ ...review, commit_id: newer }], pulls: [
    { number: 1, user: { login: 'contributor' }, labels: [{ name: 'hardware-approved' }], head: { sha: newer, repo: { full_name: 'example/Driver' } } }
  ] });
  const result = await plan(config, api, api, context);
  assert.equal(result.revision, newer);
});
test('an interrupted controller marks its unconfirmed check failed without retrying', async () => {
  const api = fakeApi({ checks: [savedCheck(candidate, { status: 'in_progress', conclusion: null })] });
  assert.equal(await plan(config, api, api, context), null);
  assert.equal(api.writes[0].method, 'PATCH');
  assert.equal(api.writes[0].body.conclusion, 'failure');
});
test('a still-running controller is not interrupted', async () => {
  const api = fakeApi({ checks: [savedCheck(candidate, { status: 'in_progress', conclusion: null })], previousRunStatus: 'in_progress' });
  assert.equal(await plan(config, api, api, context), null);
  assert.equal(api.writes.length, 0);
});
test('truncated legacy receipts are failed conservatively and require an explicit retry', async () => {
  const broken = savedCheck(candidate, { status: 'in_progress', conclusion: null, external_id: '{"schema":1,"key":',
    details_url: 'https://github.com/example/HardwareCI/actions/runs/111' });
  const api = fakeApi({ checks: [broken] });
  assert.equal(await plan(config, api, api, context), null);
  assert.equal(api.writes[0].body.conclusion, 'failure');
  const retry = fakeApi({ checks: [broken] });
  assert.ok(await plan(config, retry, retry, { ...context, retryFailed: true }));
});
test('reporter binds check, App, source and controlling run before writing success', async () => {
  const receipt = { schema: 1, key: requestKey(candidate), checkId: 100, runId: '123', target: target.id, repository: target.repository, revision: sha, packageRevision: sha };
  const writes = [];
  const api = { async request(method, path, body) {
    if (method === 'GET') return savedCheck(candidate, { id: 100, external_id: JSON.stringify(receipt) });
    writes.push(body);
  } };
  await report(config, api, context, receipt, 'success');
  assert.equal(writes[0].conclusion, 'success');
  await assert.rejects(report(config, api, context, { ...receipt, revision: newer }, 'success'));
  await assert.rejects(report(config, api, context, { ...receipt, runId: '999' }, 'success'));
  assert.equal(writes.length, 1);
});
test('cancelled, skipped and failed hardware jobs never produce a passing check', async () => {
  const receipt = { schema: 1, key: requestKey(candidate), checkId: 100, runId: '123', target: target.id, repository: target.repository, revision: sha, packageRevision: sha };
  for (const result of ['cancelled', 'skipped', 'failure']) {
    const api = { async request(method, path, body) {
      if (method === 'GET') return savedCheck(candidate, { id: 100, external_id: JSON.stringify(receipt) });
      assert.equal(body.conclusion, 'failure');
    } };
    await report(config, api, context, receipt, result);
  }
});
