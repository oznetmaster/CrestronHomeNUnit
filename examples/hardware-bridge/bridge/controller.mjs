// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
import { readFile, appendFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { githubClient } from './github.mjs';
import { validateConfig, requestKey, checkName, latestWorkflowPassed, hasCurrentApproval, ownedChecks, parseReceipt, selectCandidate } from './policy.mjs';

export async function plan(config, app, orchestration, context) {
  validateConfig(config);
  const cache = new Map();
  const cached = (key, load) => {
    if (!cache.has(key)) cache.set(key, load());
    return cache.get(key);
  };
  const prefix = repo => `/repos/${config.owner}/${repo}`;
  const head = async repo => {
    const info = await cached(`repo:${repo}`, () => app.request('GET', prefix(repo)));
    const commit = await cached(`head:${repo}`, () => app.request('GET', `${prefix(repo)}/commits/${encodeURIComponent(info.default_branch)}`));
    return commit.sha;
  };
  const gatesPassed = async (repo, revision, workflows) => {
    for (const workflow of workflows) {
      const runs = await cached(`gate:${repo}:${revision}:${workflow}`, () => app.list(
        `${prefix(repo)}/actions/workflows/${workflow}/runs?head_sha=${revision}`, 'workflow_runs'));
      if (!latestWorkflowPassed(runs, revision, `${config.owner}/${repo}`)) return false;
    }
    return true;
  };
  const candidates = [];
  for (const target of config.targets) {
    if (context.target && target.id !== context.target) continue;
    const defaultRevision = await head(target.repository);
    const packageRevision = target.sourceName ? await head(target.packageRepository) : null;
    if (target.sourceName && !await gatesPassed(target.packageRepository, packageRevision, config.packageWorkflows)) continue;
    const revisions = [defaultRevision];
    const pulls = await cached(`pulls:${target.repository}`, () => app.list(`${prefix(target.repository)}/pulls?state=open`));
    for (const pull of pulls) {
      if (pull.draft || pull.head?.repo?.full_name !== `${config.owner}/${target.repository}` ||
          !pull.labels?.some(l => l.name === config.approvalLabel)) continue;
      const reviews = await app.list(`${prefix(target.repository)}/pulls/${pull.number}/reviews`);
      if (hasCurrentApproval(reviews, pull.head.sha, new Set(config.trustedReviewers), pull.user.login)) revisions.push(pull.head.sha);
    }
    for (const revision of new Set(revisions)) {
      if (!await gatesPassed(target.repository, revision, target.workflows)) continue;
      const checks = await app.list(`${prefix(target.repository)}/commits/${revision}/check-runs?filter=all`, 'check_runs');
      const ours = ownedChecks(checks, context.appId, target.id);
      for (const check of ours.filter(c => c.status !== 'completed')) {
        const receipt = parseReceipt(check);
        // GitHub limits external_id to 255 characters. Older truncated receipts can still
        // be failed conservatively using this App's exact private-run details URL.
        const expectedUrl = `https://github.com/${config.owner}/${config.orchestration}/actions/runs/`;
        const urlRunId = check.details_url?.startsWith(expectedUrl) ? check.details_url.slice(expectedUrl.length) : '';
        const previousRunId = receipt?.runId || (/^\d+$/.test(urlRunId) ? urlRunId : null);
        if (!previousRunId || String(previousRunId) === String(context.runId)) continue;
        const run = await orchestration.request('GET', `${prefix(config.orchestration)}/actions/runs/${previousRunId}`);
        if (run.status === 'completed' && !context.dryRun) {
          await app.request('PATCH', `${prefix(target.repository)}/check-runs/${check.id}`, {
            status: 'completed', conclusion: 'failure', completed_at: new Date().toISOString(),
            output: { title: 'Hardware result was not confirmed', summary: 'The controlling run ended without a confirmed result. Inspect private evidence and cleanup before deliberately retrying.' }
          });
          check.status = 'completed'; check.conclusion = 'failure';
        }
      }
      const lastTested = ours.reduce((max, c) => Math.max(max, Date.parse(c.completed_at || c.started_at) || 0), 0);
      candidates.push({ target, revision, packageRevision: packageRevision || revision, checks, lastTested });
    }
  }
  const selected = selectCandidate(candidates, context.appId, context.retryFailed);
  if (!selected) return null;
  const receipt = {
    schema: 1, key: requestKey(selected), runId: String(context.runId),
    target: selected.target.id, repository: selected.target.repository,
    revision: selected.revision, packageRevision: selected.packageRevision
  };
  if (context.dryRun) return { ...receipt, dryRun: true };
  const check = await app.request('POST', `${prefix(selected.target.repository)}/check-runs`, {
    name: checkName(selected.target.id), head_sha: selected.revision, status: 'in_progress',
    started_at: new Date().toISOString(), external_id: JSON.stringify({ schema: 1, key: receipt.key, runId: receipt.runId }),
    details_url: `https://github.com/${config.owner}/${config.orchestration}/actions/runs/${context.runId}`,
    output: { title: 'Waiting for the development processor', summary: 'Local tests, package activation, processor tests and temporary-instance cleanup must all pass. Raw logs and inputs remain private.' }
  });
  return { ...receipt, checkId: check.id, workflowTarget: selected.target.workflowTarget || selected.target.id,
    libraryRevision: selected.target.sourceName ? selected.revision : '' };
}

export async function report(config, app, context, receipt, result) {
  const target = config.targets.find(t => t.id === receipt.target);
  if (!target || !/^\d+$/.test(String(receipt.checkId)) || receipt.repository !== target.repository ||
      String(receipt.runId) !== String(context.runId)) throw new Error('Invalid reporting identity.');
  const route = `/repos/${config.owner}/${target.repository}/check-runs/${receipt.checkId}`;
  const check = await app.request('GET', route);
  const saved = parseReceipt(check);
  if (!ownedChecks([check], context.appId, target.id).length || check.head_sha !== receipt.revision ||
      saved?.key !== receipt.key || String(saved.runId) !== String(context.runId)) throw new Error('Check does not belong to this tested request.');
  const success = result === 'success';
  await app.request('PATCH', route, {
    status: 'completed', conclusion: success ? 'success' : 'failure', completed_at: new Date().toISOString(),
    output: {
      title: success ? 'Processor workflow passed' : 'Processor workflow failed or was incomplete',
      summary: success
        ? `Local tests, processor tests, temporary-instance removal and lease release passed. Tested source: ${receipt.revision}; package definition: ${receipt.packageRevision}.`
        : 'No passing hardware result is established. Inspect the private workflow evidence and processor lease before retrying.'
    }
  });
}

async function main() {
  const config = validateConfig(JSON.parse(await readFile(new URL('../bridge-config.json', import.meta.url), 'utf8')));
  if (process.env.GITHUB_REPOSITORY !== `${config.owner}/${config.orchestration}` || process.env.GITHUB_REF !== 'refs/heads/main') throw new Error('The bridge runs only from the private orchestration main branch.');
  const app = githubClient(process.env.BRIDGE_TOKEN);
  const context = {
    appId: process.env.BRIDGE_APP_ID, runId: process.env.GITHUB_RUN_ID,
    target: process.env.TARGET_ID || '', dryRun: process.env.DRY_RUN === 'true',
    retryFailed: process.env.GITHUB_EVENT_NAME === 'workflow_dispatch' && process.env.RETRY_FAILED === 'true'
  };
  if (!/^\d+$/.test(context.appId || '') || !/^\d+$/.test(context.runId || '')) throw new Error('Missing application/run identity.');
  if (context.target && !config.targets.some(t => t.id === context.target)) throw new Error('Unknown requested target.');
  if (process.argv[2] === 'report') {
    await report(config, app, context, JSON.parse(process.env.REQUEST_RECEIPT), process.env.HARDWARE_RESULT);
  } else if (process.argv[2] === 'plan') {
    const selected = await plan(config, app, githubClient(process.env.ORCHESTRATION_TOKEN), context);
    const outputs = { selected: selected && !selected.dryRun ? 'true' : 'false', receipt: JSON.stringify(selected || {}),
      target: selected?.workflowTarget || selected?.target || '', source_commit: selected?.packageRevision || '', library_commit: selected?.libraryRevision || '' };
    for (const [name, value] of Object.entries(outputs)) {
      if (/[\r\n]/.test(value)) throw new Error('Invalid workflow output.');
      await appendFile(process.env.GITHUB_OUTPUT, `${name}=${value}\n`);
    }
    console.log(selected ? `${context.dryRun ? 'Dry run selected' : 'Scheduled'} ${selected.target} at ${selected.revision}.` : 'No eligible source revisions need hardware testing.');
  } else throw new Error('Expected plan or report.');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
