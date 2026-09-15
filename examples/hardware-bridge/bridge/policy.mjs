// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
import { createHash } from 'node:crypto';

export const shaPattern = /^[0-9a-f]{40}$/;
export const checkName = target => `Processor tests / ${target}`;

export function validateConfig(config) {
  if (!/^[A-Za-z0-9-]+$/.test(config.owner) || !/^[A-Za-z0-9-]+$/.test(config.orchestration)) throw new Error('Invalid account or orchestration repository.');
  const ids = new Set();
  for (const target of config.targets) {
    if (!/^[a-z0-9_]+$/.test(target.id) || ids.has(target.id)) throw new Error('Invalid or duplicate target.');
    ids.add(target.id);
    for (const repo of [target.repository, target.packageRepository]) {
      if (!/^[A-Za-z0-9_.-]+$/.test(repo)) throw new Error('Invalid source repository.');
    }
    if (!target.workflows?.length || !target.workflows.every(w => /^[A-Za-z0-9_.-]+\.ya?ml$/.test(w))) throw new Error('Missing hosted test gate.');
    if (target.sourceName && !/^[A-Za-z][A-Za-z0-9]*$/.test(target.sourceName)) throw new Error('Invalid library source name.');
    if (!target.sourceName && target.repository !== target.packageRepository) throw new Error('Driver source/package mismatch.');
    if (target.workflowTarget && (target.sourceName || !config.targets.some(t => t.id === target.workflowTarget && !t.workflowTarget))) throw new Error('Invalid package-definition workflow mapping.');
  }
  return config;
}

export function requestKey(candidate) {
  for (const sha of [candidate.revision, candidate.packageRevision]) {
    if (!shaPattern.test(sha)) throw new Error('Expected full source revisions.');
  }
  return createHash('sha256').update(JSON.stringify([
    candidate.target.id, candidate.target.repository, candidate.revision, candidate.packageRevision
  ])).digest('hex');
}

export function latestWorkflowPassed(runs, revision, repository) {
  const matching = runs.filter(r => r.head_sha === revision &&
    r.repository?.full_name === repository && ['push', 'pull_request', 'workflow_dispatch'].includes(r.event));
  matching.sort((a, b) => b.id - a.id || b.run_attempt - a.run_attempt);
  return matching.length > 0 && matching[0].status === 'completed' && matching[0].conclusion === 'success';
}

export function hasCurrentApproval(reviews, revision, allowedReviewers, author) {
  // The latest decisive review for each reviewer wins, including dismissal or requested changes.
  const latest = new Map();
  for (const review of [...reviews].sort((a, b) => a.id - b.id)) {
    if (review.state !== 'COMMENTED' && review.user?.login !== author) latest.set(review.user?.login, review);
  }
  const trusted = [...latest.values()].filter(r => allowedReviewers.has(r.user?.login));
  if (trusted.some(r => r.state === 'CHANGES_REQUESTED')) return false;
  return trusted.some(r => r.state === 'APPROVED' && r.commit_id === revision);
}

export function ownedChecks(checks, appId, target) {
  return checks.filter(c => String(c.app?.id) === String(appId) && c.name === checkName(target));
}

export function parseReceipt(check) {
  try {
    const receipt = JSON.parse(check.external_id);
    if (receipt.schema !== 1 || !/^[0-9a-f]{64}$/.test(receipt.key) || !/^\d+$/.test(String(receipt.runId))) return null;
    return receipt;
  } catch { return null; }
}

export function selectCandidate(candidates, appId, retryFailed = false) {
  const eligible = candidates.filter(c => {
    const checks = ownedChecks(c.checks, appId, c.target.id);
    const same = checks.filter(check => parseReceipt(check)?.key === requestKey(c)).sort((a, b) => b.id - a.id);
    if (!same.length && checks.some(check => !parseReceipt(check))) {
      return retryFailed && checks.every(check => check.status === 'completed');
    }
    if (!same.length) return true;
    return retryFailed && same[0].status === 'completed' && same[0].conclusion !== 'success';
  });
  // A target that has never run precedes one recently tested. Old revisions are superseded before selection.
  eligible.sort((a, b) => a.lastTested - b.lastTested || a.target.id.localeCompare(b.target.id));
  return eligible[0] ?? null;
}
